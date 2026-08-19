using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                WriteHeader(sheet, wb);
                for (int i = 0; i < results.Count; i++)
                    WriteRow(sheet, i + 1, results[i]);

                for (int c = 0; c < 8; c++) sheet.AutoSizeColumn(c);

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
                for (int c = 0; c < 6; c++) stat.AutoSizeColumn(c);

                // 信息（图纸/布局 + 导出时间）
                var info = wb.CreateSheet("信息");
                info.CreateRow(0).CreateCell(0).SetCellValue("图纸/布局");
                info.CreateRow(0).CreateCell(1).SetCellValue(sheetName);
                info.CreateRow(1).CreateCell(0).SetCellValue("导出时间");
                info.CreateRow(1).CreateCell(1).SetCellValue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                info.CreateRow(2).CreateCell(0).SetCellValue("条文总数");
                info.CreateRow(2).CreateCell(1).SetCellValue(results.Count);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    wb.Write(fs);
            }
        }

        public static void ExportTxt(string path, string sheetName, List<VerdictResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"图纸/布局：{sheetName}\t导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("结论\t规范名称\t条文编号\t条文内容\t图纸类型\t依据原文\tAI分析\t修改建议");
            foreach (var x in results)
                sb.AppendLine($"{x.Verdict}\t{x.CodeName}\t{x.ClauseNumber}\t{x.ClauseText}\t{x.DrawingTypesRaw}\t{x.Evidence}\t{x.Analysis}\t{x.Suggestion}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static void WriteHeader(ISheet sheet, XSSFWorkbook wb)
        {
            var row = sheet.CreateRow(0);
            var style = wb.CreateCellStyle();
            style.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey25Percent.Index;
            style.FillPattern = FillPattern.SolidForeground;
            style.Alignment = HorizontalAlignment.Center;

            string[] cols = { "结论", "规范名称", "条文编号", "条文内容", "图纸类型", "依据原文", "AI分析", "修改建议" };
            for (int i = 0; i < cols.Length; i++)
            {
                var cell = row.CreateCell(i);
                cell.SetCellValue(cols[i]);
                cell.CellStyle = style;
            }
        }

        static void WriteRow(ISheet sheet, int r, VerdictResult x)
        {
            var row = sheet.CreateRow(r);
            row.CreateCell(0).SetCellValue(x.Verdict ?? "");
            row.CreateCell(1).SetCellValue(x.CodeName ?? "");
            row.CreateCell(2).SetCellValue(x.ClauseNumber ?? "");
            row.CreateCell(3).SetCellValue(x.ClauseText ?? "");
            row.CreateCell(4).SetCellValue(x.DrawingTypesRaw ?? "");
            row.CreateCell(5).SetCellValue(x.Evidence ?? "");
            row.CreateCell(6).SetCellValue(x.Analysis ?? "");
            row.CreateCell(7).SetCellValue(x.Suggestion ?? "");
        }
    }
}
