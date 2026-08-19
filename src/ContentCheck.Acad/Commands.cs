using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using ContentCheck.Acad.UI;
using ContentCheck.Core.Excel;
using ContentCheck.Core.Models;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad
{
    public static class Commands
    {
        public static PaletteSet Palette;
        public static MainPaletteUserControl Control;

        /// <summary>待写入图纸的条文（由条文选择对话框「写入CAD」按钮设置，供 CC_WRITECLAUSE 使用）。</summary>
        public static Provision PendingWriteProvision;

        /// <summary>打开（或重新显示）校核停靠面板。</summary>
        [CommandMethod("CHECK")]
        public static void ShowPalette()
        {
            if (Palette == null || Palette.IsDisposed)
            {
                Control = new MainPaletteUserControl();
                Palette = new PaletteSet("图纸总说明规范校核", new Guid("D3E9A2B1-5C4D-4E8A-9B1C-2F3A4B5C6D7E"))
                {
                    Style = PaletteSetStyles.ShowAutoHideButton
                          | PaletteSetStyles.ShowCloseButton
                          | PaletteSetStyles.ShowPropertiesMenu,
                    Dock = DockSides.Right,
                    MinimumSize = new System.Drawing.Size(480, 560),
                };
                Palette.Size = new System.Drawing.Size(480, 760);
                Palette.Add("总说明校核", Control);
            }
            if (!Palette.Visible) Palette.Visible = true;
            Palette.Activate(0);
            Control.ReloadData();
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
                // 把框选结果回传给停靠面板
                Control?.ApplySelectedSheet(sheet);
                doc.Editor.WriteMessage($"\n已提取框选区域文字（{sheet.TextLines.Count} 行）。");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n框选提取失败：" + ex.Message);
            }
        }

        /// <summary>把所有布局文字导出为 txt（便于离线检查总说明在哪个布局）。</summary>
        [CommandMethod("CC_EXTRACT")]
        public static void ExtractAllText()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (!EnsureEnv(doc)) return;
            try
            {
                var sheets = Dwg.DwgTextExtractor.Extract(doc);
                if (sheets.Count == 0)
                {
                    doc.Editor.WriteMessage("\n未提取到任何布局文字。");
                    return;
                }

                Directory.CreateDirectory(PluginEnv.Config.LogDir);
                var name = Path.GetFileNameWithoutExtension(doc.Name);
                var file = Path.Combine(PluginEnv.Config.LogDir, $"extract_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                foreach (var s in sheets)
                {
                    sb.AppendLine($"==== {s.Name} ====");
                    sb.AppendLine(s.FullText);
                    sb.AppendLine();
                }
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                doc.Editor.WriteMessage($"\n已导出 {sheets.Count} 个布局文字 → {file}");
                Process.Start("explorer.exe", "/select,\"" + file + "\"");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n导出失败：" + ex.Message);
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
        /// 把选中的规范条文以多行文字（MText）写入模型空间：
        /// 字高 300、行宽 10000（约每行 50 字自动换行）。插入点由用户在图纸中点选。
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

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    using (var mtext = new MText())
                    {
                        mtext.SetDatabaseDefaults();
                        mtext.Location = pr.Value;
                        mtext.Height = 300;
                        mtext.Width = 10000;
                        mtext.Contents = BuildMTextContent(prov);
                        ms.AppendEntity(mtext);
                        tr.AddNewlyCreatedDBObject(mtext, true);
                    }
                    tr.Commit();
                }
                PendingWriteProvision = null;
                doc.Editor.WriteMessage($"\n条文已写入图纸（{prov.CodeName} {prov.ClauseNumber}）。");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n写入失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 组装 MText 内容：标题行（规范名称 + 条文编号）+ 条文全文。
        /// 换行转 MText 段落符 \P；反斜杠、花括号先转义避免被当成格式码。
        /// </summary>
        static string BuildMTextContent(Provision p)
        {
            var head = "【" + (p.CodeName ?? "") + "】";
            if (!string.IsNullOrWhiteSpace(p.ClauseNumber))
                head += " " + p.ClauseNumber;

            var text = head + "\n" + (p.ClauseText ?? "(无内容)");
            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\\", "\\\\")   // 先转义原文中的反斜杠
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("\n", "\\P");   // 最后统一换行 → 段落符
        }
    }
}
