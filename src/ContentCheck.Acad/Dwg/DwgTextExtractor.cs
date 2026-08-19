using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>
    /// 读取当前图纸所有布局（含 Model）的 TEXT/MTEXT 文字，按布局分组。
    /// 通过 LayoutDictionary → Layout → BlockTableRecordId 权威映射（每个布局对应唯一块表记录）。
    /// </summary>
    public static class DwgTextExtractor
    {
        /// <summary>MText.Contents 兜底时的格式码清洗。</summary>
        static readonly Regex FormatCodeRx = new Regex(@"{\\[A-Za-z][^}]*}|[{}]", RegexOptions.Compiled);

        /// <summary>
        /// 校核默认取模型空间文字；若模型空间为空则回退到文字最多的布局（通常为总说明）。
        /// </summary>
        public static DrawingSheet ExtractModel(Document doc)
        {
            var sheets = Extract(doc);
            var model = sheets.FirstOrDefault(s => s.Name == "Model");
            if (model != null && !string.IsNullOrWhiteSpace(model.FullText))
                return model;

            var best = sheets
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.TextLines.Count > 0)
                .OrderByDescending(s => s.Name.Contains("说明"))
                .ThenByDescending(s => s.TextLines.Count)
                .FirstOrDefault();
            return best ?? model ?? sheets.FirstOrDefault();
        }

        public static List<DrawingSheet> Extract(Document doc)
        {
            var sheets = new List<DrawingSheet>();
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var layoutDict = tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (layoutDict != null)
                {
                    foreach (DBDictionaryEntry entry in layoutDict)
                    {
                        if (tr.GetObject(entry.Value, OpenMode.ForRead, true) is Layout layout)
                        {
                            if (layout.ObjectId.IsErased || !layout.BlockTableRecordId.IsValid) continue;
                            var btr = tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead, true) as BlockTableRecord;
                            if (btr == null) continue;

                            var sheet = new DrawingSheet { Name = layout.LayoutName };
                            ExtractFromBlock(btr, tr, sheet);
                            sheets.Add(sheet);
                        }
                    }
                }

                // 兜底：若布局字典为空（异常情况），至少扫 ModelSpace
                if (sheets.Count == 0)
                {
                    var bt = tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead, true) as BlockTable;
                    if (bt != null && bt.Has(BlockTableRecord.ModelSpace))
                    {
                        var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead, true) as BlockTableRecord;
                        if (ms != null)
                        {
                            var sheet = new DrawingSheet { Name = "Model" };
                            ExtractFromBlock(ms, tr, sheet);
                            sheets.Add(sheet);
                        }
                    }
                }

                tr.Commit();
            }
            return sheets;
        }

        static void ExtractFromBlock(BlockTableRecord btr, Transaction tr, DrawingSheet sheet)
        {
            foreach (ObjectId id in btr)
            {
                if (!id.IsValid || id.IsErased) continue;
                try
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (ent == null) continue;

                    switch (ent)
                    {
                        case DBText t:
                            AddLines(sheet, t.TextString, t.Position, t.Height, t.Layer, t.Handle.ToString());
                            break;
                        case MText mt:
                            AddMText(sheet, mt);
                            break;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // 跳过无法读取的实体（ObjectErased / 跨系统等）
                }
                catch (System.Exception)
                {
                    // 单实体读取失败不影响整体
                }
            }

            // 按阅读顺序（先上后下、同行从左到右）重排
            SortByReadingOrder(sheet.TextLines);
            sheet.FullText = string.Join("\n", sheet.TextLines.Select(l => l.Text));
        }

        static void AddMText(DrawingSheet sheet, MText mt)
        {
            // Text 为无格式文本（首选）；Contents 兜底并剥格式码
            var clean = mt.Text;
            if (string.IsNullOrEmpty(clean))
                clean = FormatCodeRx.Replace(mt.Contents, "");
            // MText 多行文字从 Location（左上角）向下生长，行距 ≈ 字高 × 行距系数，
            // 必须给每行算独立坐标，否则整段 MText 的行会挤在同一位置导致排序错乱。
            // 注意：MText.TextHeight 才是字高，Height 是整个文字块的高度。
            var h = mt.TextHeight;
            if (h <= 0.01) h = mt.Height;
            AddLines(sheet, clean, mt.Location, h, mt.Layer, mt.Handle.ToString(), mt.LineSpacingFactor);
        }

        static void AddLines(DrawingSheet sheet, string text, Autodesk.AutoCAD.Geometry.Point3d pos, double h,
            string layer, string handle, double lineSpacing = 1.0)
        {
            if (string.IsNullOrEmpty(text)) return;
            var all = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i].TrimEnd();
                if (string.IsNullOrWhiteSpace(t)) continue;
                // 第 i 行的 Y：从插入点向下偏移 i×行距
                var p = new Autodesk.AutoCAD.Geometry.Point3d(pos.X, pos.Y - i * h * lineSpacing, pos.Z);
                sheet.TextLines.Add(new TextLine(t, p, h, layer, handle));
            }
        }

        /// <summary>
        /// 按图纸阅读顺序重排文字行：先按 Y 从大到小（上→下）聚类成行组，
        /// 组内按 X 从小到大（左→右）。同一视觉行的 Y 容差取字高中位数的 0.5 倍。
        /// </summary>
        public static void SortByReadingOrder(List<TextLine> lines)
        {
            if (lines == null || lines.Count < 2) return;

            // 字高中位数作为归一化尺度（抗个别超大/超小文字干扰）
            var heights = lines.Where(l => l.Height > 0.01).Select(l => l.Height).OrderBy(h => h).ToList();
            if (heights.Count == 0) return;
            double medH = heights[heights.Count / 2];
            if (medH <= 0) return;

            // 同一视觉行的 Y 抖动容差：取 0.2×字高。同行 DBText 基线一致（抖动≈0），
            // 而异行即使最紧凑压缩的行距（0.25×字高）也大于该值，保证不同行不被粘连。
            double tol = medH * 0.2;

            // 按 Y 降序聚类成视觉行：用【相邻行】比较而非与行组锚点比较，
            // 避免多行累积误差把整列文字吸进第一行。
            var sorted = lines.OrderByDescending(l => l.Position.Y).ToList();
            var rows = new List<List<TextLine>>();
            foreach (var l in sorted)
            {
                if (rows.Count == 0)
                {
                    rows.Add(new List<TextLine> { l });
                    continue;
                }
                var prev = rows.Last()[rows.Last().Count - 1];
                if (Math.Abs(prev.Position.Y - l.Position.Y) <= tol)
                    rows.Last().Add(l);
                else
                    rows.Add(new List<TextLine> { l });
            }

            lines.Clear();
            foreach (var row in rows)
                lines.AddRange(row.OrderBy(l => l.Position.X));
        }

        /// <summary>
        /// 框选区域提取文字：让用户在 AutoCAD 中框选一个区域，只提取该区域内的文字。
        /// </summary>
        /// <param name="doc">当前文档</param>
        /// <param name="promptMessage">提示用户框选的消息</param>
        /// <returns>提取到的文字，如果用户取消则返回 null</returns>
        public static DrawingSheet ExtractBySelection(Document doc, string promptMessage = "请框选要识别文字的区域")
        {
            var editor = doc.Editor;

            // 提示用户框选
            var selOpts = new PromptSelectionOptions
            {
                MessageForAdding = promptMessage,
                AllowDuplicates = false,
            };

            // 使用 CrossingPolygon 或 Window 选择
            var selResult = editor.GetSelection(selOpts);

            if (selResult.Status != PromptStatus.OK || selResult.Value == null)
            {
                editor.WriteMessage("\n已取消框选。");
                return null;
            }

            var selSet = selResult.Value;
            return ExtractFromSelectionSet(doc, selSet);
        }

        /// <summary>
        /// 从选择集中提取文字。
        /// </summary>
        public static DrawingSheet ExtractFromSelectionSet(Document doc, SelectionSet selSet)
        {
            var sheet = new DrawingSheet { Name = "框选区域" };

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selSet)
                {
                    if (selObj == null || !selObj.ObjectId.IsValid || selObj.ObjectId.IsErased) continue;

                    try
                    {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead, true) as Entity;
                        if (ent == null) continue;

                        switch (ent)
                        {
                            case DBText t:
                                AddLines(sheet, t.TextString, t.Position, t.Height, t.Layer, t.Handle.ToString());
                                break;
                            case MText mt:
                                AddMText(sheet, mt);
                                break;
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    catch (System.Exception) { }
                }

                tr.Commit();
            }

            // 与整图提取一致：按阅读顺序重排后再拼全文
            SortByReadingOrder(sheet.TextLines);
            sheet.FullText = string.Join("\n", sheet.TextLines.Select(l => l.Text));
            return sheet;
        }

        /// <summary>
        /// 让用户点选或框选区域，返回该区域内文字的包围盒（用于后续高亮等）。
        /// </summary>
        public static Extents3d? GetSelectionBounds(Document doc)
        {
            var editor = doc.Editor;
            var selResult = editor.GetSelection();

            if (selResult.Status != PromptStatus.OK || selResult.Value == null)
                return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool found = false;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selResult.Value)
                {
                    if (selObj == null || !selObj.ObjectId.IsValid || selObj.ObjectId.IsErased) continue;
                    try
                    {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead, true) as Entity;
                        if (ent == null) continue;
                        var b = ent.GeometricExtents;
                        minX = Math.Min(minX, b.MinPoint.X);
                        minY = Math.Min(minY, b.MinPoint.Y);
                        maxX = Math.Max(maxX, b.MaxPoint.X);
                        maxY = Math.Max(maxY, b.MaxPoint.Y);
                        found = true;
                    }
                    catch { }
                }
                tr.Commit();
            }

            if (!found) return null;
            return new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
        }
    }
}
