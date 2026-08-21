using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using ContentCheck.Acad.UI;
using ContentCheck.Core.Excel;
using ContentCheck.Core.Models;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad
{
    public static class Commands
    {
        public static MainModelessDialog ModelessDialog;

        /// <summary>待写入图纸的条文（由条文选择对话框「写入CAD」按钮设置，供 CC_WRITECLAUSE 使用）。</summary>
        public static Provision PendingWriteProvision;

        /// <summary>打开（或重新显示）校核非模态对话框。</summary>
        [CommandMethod("CHECK")]
        public static void ShowDialog()
        {
            if (ModelessDialog == null || ModelessDialog.IsDisposed)
            {
                ModelessDialog = new MainModelessDialog();
            }
            if (!ModelessDialog.Visible)
            {
                ModelessDialog.Show();
            }
            else
            {
                ModelessDialog.BringToFront();
            }
            ModelessDialog.ReloadData();
        }

        /// <summary>在插件内重新导入条文（主路径是 Import 控制台）。</summary>
        [CommandMethod("CC_IMPORT")]
        public static void ImportProvisions()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (!EnsureEnv(doc)) return;
            try
            {
                if (string.IsNullOrEmpty(PluginEnv.Config.ExcelPath) || !File.Exists(PluginEnv.Config.ExcelPath))
                {
                    doc.Editor.WriteMessage("\n错误：config.json 中未配置有效的 excel_path。");
                    return;
                }
                var provisions = ExcelParser.ParseAll(PluginEnv.Config.ExcelPath);
                var store = new SqliteProvisionStore(PluginEnv.Config.DbPath);
                store.Init();
                store.ReplaceAll(provisions, PluginEnv.Config.ExcelPath);
                var design = provisions.Count(p => p.DrawingTypesRaw.Contains("设计说明"));
                doc.Editor.WriteMessage($"\n已导入 {provisions.Count} 条条文（设计说明适用 {design} 条），来源：{PluginEnv.Config.ExcelPath}");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n导入失败：" + ex.Message);
            }
        }

        /// <summary>框选区域提取文字：让用户框选区域，仅提取选中文字。</summary>
        [CommandMethod("CC_SELECTTEXT")]
        public static void SelectTextArea()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (!EnsureEnv(doc)) return;
            try
            {
                var sheet = Dwg.DwgTextExtractor.ExtractBySelection(doc);
                if (sheet == null)
                {
                    doc.Editor.WriteMessage("\n已取消框选。");
                    return;
                }
                if (sheet.TextLines.Count == 0)
                {
                    doc.Editor.WriteMessage("\n框选区域内未提取到文字。");
                    return;
                }
                // 把框选结果回传给非模态对话框
                ModelessDialog?.ApplySelectedSheet(sheet);
                doc.Editor.WriteMessage($"\n已提取框选区域文字（{sheet.TextLines.Count} 行）。");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n框选提取失败：" + ex.Message);
            }
        }

        internal static bool EnsureEnv(Document doc)
        {
            if (PluginEnv.InitOk) return true;
            try
            {
                PluginEnv.Init();
                return true;
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n初始化配置失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 把选中的规范条文以单行文字（DBText）写入模型空间：
        /// 字高 300，每行一个 DBText 实体，行间距 = 字高 × 1.8。
        /// 内容按每行约 25 字硬断行。插入点由用户在图纸中点选。
        /// </summary>
        [CommandMethod("CC_WRITECLAUSE")]
        public static void WriteClauseToCad()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (!EnsureEnv(doc)) return;

            var prov = PendingWriteProvision;
            if (prov == null)
            {
                doc.Editor.WriteMessage("\n没有待写入的条文。");
                return;
            }

            try
            {
                var pr = doc.Editor.GetPoint("\n指定条文插入点：");
                if (pr.Status != PromptStatus.OK)
                {
                    PendingWriteProvision = null;
                    doc.Editor.WriteMessage("\n已取消写入。");
                    return;
                }

                var lines = BuildTextLines(prov);
                double textHeight = 300;
                double lineSpacing = textHeight * 1.8;

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    for (int i = 0; i < lines.Count; i++)
                    {
                        var dbText = new DBText();
                        dbText.SetDatabaseDefaults();
                        dbText.Position = new Point3d(
                            pr.Value.X,
                            pr.Value.Y - i * lineSpacing,
                            pr.Value.Z);
                        dbText.Height = textHeight;
                        dbText.TextString = lines[i];
                        ms.AppendEntity(dbText);
                        tr.AddNewlyCreatedDBObject(dbText, true);
                    }

                    tr.Commit();
                }
                PendingWriteProvision = null;
                doc.Editor.WriteMessage($"\n条文已写入图纸（{prov.CodeName} {prov.ClauseNumber}，共 {lines.Count} 行）。");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n写入失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 将条文拆分为单行文字列表：仅条文全文（不含规范名称与编号）。
        /// 按每行约 25 字硬断行，返回每行字符串。
        /// </summary>
        static List<string> BuildTextLines(Provision p)
        {
            var paragraphs = (p.ClauseText ?? "(无内容)")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            var lines = new List<string>();
            foreach (var para in paragraphs)
            {
                var text = para.Trim();
                if (text.Length == 0) continue;
                WrapIntoLines(lines, text, 25);
            }
            return lines;
        }

        /// <summary>把一段文字按每行最多 maxLen 个字符硬断行，逐行加入列表。</summary>
        static void WrapIntoLines(List<string> lines, string text, int maxLen)
        {
            for (int i = 0; i < text.Length; i += maxLen)
            {
                int len = Math.Min(maxLen, text.Length - i);
                lines.Add(text.Substring(i, len));
            }
        }
    }
}
