using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using ContentCheck.Acad.UI;
using ContentCheck.Core.Excel;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad
{
    public static class Commands
    {
        public static PaletteSet Palette;
        public static MainPaletteUserControl Control;

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
    }
}
