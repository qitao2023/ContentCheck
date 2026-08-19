using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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

            sheet.FullText = string.Join("\n", sheet.TextLines.Select(l => l.Text));
        }

        static void AddMText(DrawingSheet sheet, MText mt)
        {
            // Text 为无格式文本（首选）；Contents 兜底并剥格式码
            var clean = mt.Text;
            if (string.IsNullOrEmpty(clean))
                clean = FormatCodeRx.Replace(mt.Contents, "");
            AddLines(sheet, clean, mt.Location, mt.Height, mt.Layer, mt.Handle.ToString());
        }

        static void AddLines(DrawingSheet sheet, string text, Autodesk.AutoCAD.Geometry.Point3d pos, double h, string layer, string handle)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ln in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = ln.TrimEnd();
                if (string.IsNullOrWhiteSpace(t)) continue;
                sheet.TextLines.Add(new TextLine(t, pos, h, layer, handle));
            }
        }
    }
}
