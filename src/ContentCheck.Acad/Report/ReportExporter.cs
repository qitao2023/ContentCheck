using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ContentCheck.Acad.UI;
using ContentCheck.Core.Models;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ContentCheck.Acad.Report
{
    /// <summary>校核结果导出 xlsx / txt（NPOI，纯数据写入，不触碰 AutoCAD 对象）。</summary>
    public static class ReportExporter
    {
        public static void ExportXlsx(string path, string sheetName, List<VerdictResult> results)
        {
            using (var wb = new XSSFWorkbook())
            {
                // 校核结果
                var sheet = wb.CreateSheet("校核结果");
                var wrapStyle = wb.CreateCellStyle();
                wrapStyle.WrapText = true;
                var headerStyle = wb.CreateCellStyle();
                headerStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
                headerStyle.FillPattern = FillPattern.SolidForeground;
                headerStyle.Alignment = HorizontalAlignment.Center;
                WriteHeader(sheet, headerStyle);
                for (int i = 0; i < results.Count; i++)
                    WriteRow(sheet, i + 1, results[i], wrapStyle);

                // 按 UI 列宽权重设置 Excel 列宽（单位：1/256 字符宽）
                int[] weights = { 44, 200, 300, 200, 160, 56 }; // 序号、识别原文、规范条文、AI分析、修改建议、结论
                int totalWeight = 960;
                int baseWidth = 120 * 256; // 基准列宽 ≈120字符宽，按权重分配
                for (int c = 0; c < 6; c++)
                    sheet.SetColumnWidth(c, baseWidth * weights[c] / totalWeight);

                // 统计
                var stat = wb.CreateSheet("统计");
                var hr = stat.CreateRow(0);
                hr.CreateCell(0).SetCellValue("专业");
                hr.CreateCell(1).SetCellValue("总数");
                hr.CreateCell(2).SetCellValue("符合");
                hr.CreateCell(3).SetCellValue("不符合");
                hr.CreateCell(4).SetCellValue("未涉及");
                hr.CreateCell(5).SetCellValue("无法判断");

                int r = 1;
                foreach (var g in results.GroupBy(x => x.Discipline).OrderBy(x => x.Key))
                {
                    var row = stat.CreateRow(r++);
                    row.CreateCell(0).SetCellValue(g.Key);
                    row.CreateCell(1).SetCellValue(g.Count());
                    row.CreateCell(2).SetCellValue(g.Count(x => x.Verdict == "符合"));
                    row.CreateCell(3).SetCellValue(g.Count(x => x.Verdict == "不符合"));
                    row.CreateCell(4).SetCellValue(g.Count(x => x.Verdict == "未涉及"));
                    row.CreateCell(5).SetCellValue(g.Count(x => x.Verdict == "无法判断"));
                }
                var total = stat.CreateRow(r);
                total.CreateCell(0).SetCellValue("合计");
                total.CreateCell(1).SetCellValue(results.Count);
                total.CreateCell(2).SetCellValue(results.Count(x => x.Verdict == "符合"));
                total.CreateCell(3).SetCellValue(results.Count(x => x.Verdict == "不符合"));
                total.CreateCell(4).SetCellValue(results.Count(x => x.Verdict == "未涉及"));
                total.CreateCell(5).SetCellValue(results.Count(x => x.Verdict == "无法判断"));
                // 统计页列宽：自动适应内容宽度
                for (int c = 0; c < 6; c++) stat.AutoSizeColumn(c);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    wb.Write(fs);
            }
        }

        public static void ExportTxt(string path, string sheetName, List<VerdictResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"图纸/布局：{sheetName}\t导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("序号\t识别原文\t规范条文\tAI分析\t修改建议\t结论");
            for (int i = 0; i < results.Count; i++)
            {
                var x = results[i];
                var provision = ResultGridSetup.FormatProvision(x).Replace("\r\n", " ").Replace("\n", " ");
                sb.AppendLine($"{i + 1}\t{x.Evidence}\t{provision}\t{x.Analysis}\t{x.Suggestion}\t{x.Verdict}");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static void WriteHeader(ISheet sheet, ICellStyle headerStyle)
        {
            var row = sheet.CreateRow(0);
            string[] cols = { "序号", "识别原文", "规范条文", "AI分析", "修改建议", "结论" };
            for (int i = 0; i < cols.Length; i++)
            {
                var cell = row.CreateCell(i);
                cell.SetCellValue(cols[i]);
                cell.CellStyle = headerStyle;
            }
        }

        static void WriteRow(ISheet sheet, int r, VerdictResult x, ICellStyle wrapStyle)
        {
            var row = sheet.CreateRow(r);
            // 序号、结论不需要自动换行
            row.CreateCell(0).SetCellValue(r.ToString());
            SetWrapCell(row, 1, x.Evidence ?? "", wrapStyle);
            SetWrapCell(row, 2, ResultGridSetup.FormatProvision(x), wrapStyle);
            SetWrapCell(row, 3, x.Analysis ?? "", wrapStyle);
            SetWrapCell(row, 4, x.Suggestion ?? "", wrapStyle);
            row.CreateCell(5).SetCellValue(x.Verdict ?? "");
            row.ZeroHeight = false; // 行高不锁定，Excel 打开时按内容自适应
        }

        static void SetWrapCell(IRow row, int col, string value, ICellStyle wrapStyle)
        {
            var cell = row.CreateCell(col);
            cell.SetCellValue(value);
            cell.CellStyle = wrapStyle;
        }
    }
}
