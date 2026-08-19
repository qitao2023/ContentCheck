using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ContentCheck.Core.Models;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ContentCheck.Core.Excel
{
    /// <summary>
    /// 解析《电网工程土建设计规范条文.xlsx》，把 4 个专业 sheet 清洗为规范条文列表。
    /// 仅用于一次性导入（Excel → SQLite），插件运行时不再读 Excel。
    ///
    /// 表格布局兼容两种：
    ///  - Sheet 1-3（建筑/给排水/暖通）：规范名称在块首行（col1 以《 开头），
    ///    数据行 [序号, 条文号+全文, 图纸类型]。
    ///  - Sheet 4（国网2024版）：每行自带规范名称，[序号, 规范名称, 检查内容, 图纸类型]，
    ///    且表末有大量空白行必须跳过。
    /// </summary>
    public static class ExcelParser
    {
        // 条文号正则：容错 "3.4.1 "、"3. 1. 4自建"、"6.3.4. …" 等脏格式，只对首行生效
        // 编号后面不能紧跟数字（防止 "8.4.10.500千伏" 被整体当作编号）
        static readonly Regex ClauseNumRx = new Regex(
            @"^\s*(\d+(?:\s*[\.．]\s*\d+)*)\s*[\.．]?\s*[:：]?\s*(?!\d)(.*)$",
            RegexOptions.Compiled);

        // 中文条文号正则：匹配 "第九条"、"第十条"、"第三十三条" 等
        static readonly Regex ChineseClauseNumRx = new Regex(
            @"^\s*(第[一二三四五六七八九十百千零\d]+条)\s*(.*)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // 去掉规范名称末尾拖尾的条文号垃圾（如 "\n3．1"、" 3.4.2"）
        // 注意：不能去掉年份（如 "-2019"），所以只匹配独立的数字或以点分隔的编号
        static readonly Regex CodeNameJunkRx = new Regex(
            @"[\s\n]+(?:\d+(?:[\.．]\d+)*)\s*$", RegexOptions.Compiled);

        public static List<Provision> ParseAll(string xlsxPath)
        {
            var result = new List<Provision>();
            using (var fs = File.OpenRead(xlsxPath))
            {
                var wb = new XSSFWorkbook(fs);
                for (int s = 0; s < wb.NumberOfSheets; s++)
                {
                    var sheet = wb.GetSheetAt(s);
                    if (sheet == null) continue;

                    string name = sheet.SheetName.Trim();
                    string disc = MapDiscipline(name);
                    if (disc == "国网")
                        ParseSheetStateGrid(sheet, disc, result);
                    else
                        ParseSheet123(sheet, disc, result);
                }
            }
            return result;
        }

        /// <summary>Sheet 名 → 专业名：建筑/给排水/暖通原样；国网前缀→"国网"；其余（未来新专业如"结构"）用 sheet 名。</summary>
        public static string MapDiscipline(string sheetName)
        {
            if (sheetName == "建筑" || sheetName == "给排水" || sheetName == "暖通") return sheetName;
            if (sheetName.StartsWith("国网")) return "国网";
            return sheetName;
        }

        // ---------- Sheet 1-3：块首规范名称 ----------

        static void ParseSheet123(ISheet sheet, string discipline, List<Provision> result)
        {
            var fmt = new DataFormatter();
            string curCode = null;

            for (int r = 0; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                string c1 = CellText(row, 1, fmt).Trim();
                if (c1.Length == 0) continue;

                if (c1.StartsWith("《"))
                {
                    // 块首：规范名称（取首行，去掉换行后拖尾的条文号垃圾）
                    curCode = CleanCodeName(c1);
                    continue;
                }

                // 数据行
                var prov = new Provision
                {
                    Discipline = discipline,
                    CodeName = curCode ?? "",
                    ClauseText = c1,
                    DrawingTypesRaw = NormalizeTypes(CellText(row, 2, fmt)),
                };
                var split = SplitClause(c1);
                prov.ClauseNumber = split.num;
                if (split.text != null) prov.ClauseText = split.text;
                prov.DrawingTypes = SplitTypes(prov.DrawingTypesRaw);
                result.Add(prov);
            }
        }

        // ---------- Sheet 4：每行自带规范名称 ----------

        static void ParseSheetStateGrid(ISheet sheet, string discipline, List<Provision> result)
        {
            var fmt = new DataFormatter();
            for (int r = 0; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                string c0 = CellText(row, 0, fmt).Trim();
                string c1 = CellText(row, 1, fmt).Trim();
                string c2 = CellText(row, 2, fmt).Trim();
                string c3 = CellText(row, 3, fmt).Trim();

                // 跳过表头（第0行）与全空行
                if (c0.Length == 0 && c1.Length == 0 && c2.Length == 0 && c3.Length == 0) continue;
                if (c1 == "规范名称") continue;

                var types = NormalizeTypes(c3);

                // 尝试提取中文条文号（如 "第九条"、"第十条"）
                string clauseNum = null;
                string clauseText = c2;
                var chineseMatch = ChineseClauseNumRx.Match(c2);
                if (chineseMatch.Success)
                {
                    clauseNum = chineseMatch.Groups[1].Value;
                    clauseText = chineseMatch.Groups[2].Value.Trim();
                }

                result.Add(new Provision
                {
                    Discipline = discipline,
                    CodeName = c1,
                    ClauseNumber = clauseNum,
                    ClauseText = clauseText,
                    DrawingTypesRaw = types,
                    DrawingTypes = SplitTypes(types),
                });
            }
        }

        // ---------- 工具 ----------

        static string CellText(IRow row, int col, DataFormatter fmt)
        {
            var cell = row.GetCell(col);
            return cell == null ? "" : (fmt.FormatCellValue(cell) ?? "");
        }

        /// <summary>条文号从首行提取；条文正文保留多行子条全文。返回 (编号, 条文正文)。</summary>
        public static (string num, string text) SplitClause(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell)) return (null, null);
            var lines = cell.Replace("\r", "").Split('\n');
            var line0 = lines[0].Trim();
            var m = ClauseNumRx.Match(line0);
            if (m.Success && m.Groups[2].Value.Length > 0)
            {
                // 规范化编号里的空格与全角点：3. 1. 4 → 3.1.4
                var num = Regex.Replace(m.Groups[1].Value, @"[\s．]+", ".");
                num = Regex.Replace(num, @"\.+", ".");
                var rest = m.Groups[2].Value;
                // 保留后续子条行（如 "3.4.2 …：\n1 高层厂房…\n2 …"）
                if (lines.Length > 1)
                    rest += "\n" + string.Join("\n", lines, 1, lines.Length - 1);
                return (num, rest);
            }
            return (null, cell);
        }

        /// <summary>块首规范名称清洗：取首行，并去掉末尾拖尾的条文号垃圾（如 "\n3．1"）。</summary>
        public static string CleanCodeName(string cell)
        {
            var line0 = cell.Replace("\r", "").Split('\n')[0].Trim();
            var cleaned = CodeNameJunkRx.Replace(line0, "");
            return string.IsNullOrWhiteSpace(cleaned) ? line0 : cleaned.Trim();
        }

        /// <summary>图纸类型规范化：按 ,  ，、空白切分去重，顿号连接。</summary>
        public static string NormalizeTypes(string raw)
        {
            return string.Join("、", SplitTypes(raw));
        }

        public static List<string> SplitTypes(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            var seen = new HashSet<string>();
            foreach (var part in raw.Split(new[] { '，', ',', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length > 0 && seen.Add(t)) list.Add(t);
            }
            return list;
        }
    }
}
