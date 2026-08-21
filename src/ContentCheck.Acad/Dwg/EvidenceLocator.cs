using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>
    /// 依据原文（evidence）→ 图纸文字行 / 实体 Handle 的定位。
    /// 供结果表双击定位共用（MainModelessDialog 使用）。
    /// </summary>
    public static class EvidenceLocator
    {
        /// <summary>依据原文中拆出的中文片段最小长度（过短片段易误匹配）。</summary>
        const int MinFragmentLength = 6;

        /// <summary>规范化文本用于匹配：去除换行符、多余空格。</summary>
        static string NormalizeForMatch(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r\n", "").Replace("\n", "").Replace("\r", "")
                       .Replace(" ", "").Replace("　", ""); // 全角空格
        }

        /// <summary>在图纸文字中定位与依据原文匹配的文字行（段落优先，回退逐行，按 Handle 去重）。</summary>
        public static List<TextLine> FindLines(DrawingSheet sheet, string evidence)
        {
            var lines = new List<TextLine>();
            if (sheet == null || string.IsNullOrWhiteSpace(evidence) || evidence == "总说明未提及") return lines;

            var frags = SplitFragments(evidence);
            if (frags.Count == 0) return lines;

            // 优先在聚合段落里找（段落文本更完整，命中率更高）；找不到再回退逐行
            if (sheet.Segments != null && sheet.Segments.Count > 0)
            {
                foreach (var seg in sheet.Segments)
                {
                    if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                    var segText = NormalizeForMatch(seg.Text);
                    if (!frags.Any(f => segText.Contains(f))) continue;

                    lines.AddRange(seg.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Handle)));
                }
            }

            if (lines.Count == 0)
            {
                lines.AddRange(sheet.TextLines.Where(l =>
                    !string.IsNullOrWhiteSpace(l.Text) && !string.IsNullOrWhiteSpace(l.Handle)
                    && frags.Any(f => NormalizeForMatch(l.Text).Contains(f))));
            }

            // 去重：同一实体拆出的多行只保留一次
            return lines
                .GroupBy(l => l.Handle)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>定位匹配文字行的实体 Handle（去重）。</summary>
        public static List<string> FindHandles(DrawingSheet sheet, string evidence)
            => FindLines(sheet, evidence)
                .Select(l => l.Handle)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .ToList();

        /// <summary>拆出 ≥6 字的中文片段（引号/顿号/标点分隔），用于在布局文字中定位。</summary>
        public static List<string> SplitFragments(string evidence)
            => Regex.Split(evidence ?? "", @"[，。；、：:；,.()（）「」『』\s]+")
                .Select(f => f.Trim())
                .Where(f => f.Length >= MinFragmentLength)
                .Distinct()
                .OrderByDescending(f => f.Length)
                .ToList();
    }
}
