using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>
    /// 将按阅读顺序排好的 TextLine 列表聚合成文本段（Segment）。
    /// 规则优先级：空行分段 > 序号/标题分段 > 缩进变化分段。
    /// 聚合结果写入 DrawingSheet.Segments，并同步生成分段文本 DrawingSheet.SegmentedText。
    /// </summary>
    public static class TextSegmenter
    {
        // 序号开头：1. / 1、 / 1） / (1) / 一、 / 第一章 / 第1条 等
        static readonly Regex OrderedStart = new Regex(
            @"^\s*(?:\(?[0-9]{1,3}[.)、]\s*|[一二三四五六七八九十]{1,3}、\s*|第[0-9一二三四五六七八九十]{1,4}[章条节款]\s*)",
            RegexOptions.Compiled);

        // 段标题：以冒号/空格结尾的短标题行（≤20字），如 "设计依据：" / "材料要求"
        static readonly Regex TitleLike = new Regex(
            @"^\s*[\u4e00-\u9fa5A-Za-z0-9()（）]{2,20}\s*[:：]?\s*$",
            RegexOptions.Compiled);

        /// <summary>按规则将 TextLines 聚合为 Segments。</summary>
        public static void Segment(DrawingSheet sheet)
        {
            sheet.Segments.Clear();
            sheet.SegmentedText = "";

            var lines = sheet.TextLines;
            if (lines == null || lines.Count == 0) return;

            // 估算缩进基准：取全部行 X 的众数附近值（或最小值）作为"无缩进"基准
            double baseX = GuessBaseX(lines);
            double indentThreshold = GuessIndentThreshold(lines, baseX);

            var segs = new List<TextSegment>();
            var buf = new List<TextLine>();
            int idx = 1;

            void Flush(string title = null)
            {
                if (buf.Count == 0) return;
                var text = string.Join("\n", buf.Select(l => l.Text));
                var segment = new TextSegment
                {
                    Index = idx++,
                    Title = title ?? GuessTitleFromLines(buf),
                    Text = text,
                };
                segment.Lines.AddRange(buf);
                segs.Add(segment);
                buf.Clear();
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var cur = lines[i];
                bool isBlank = string.IsNullOrWhiteSpace(cur.Text);

                // 规则1：空行 → 分段（连续空行只触发一次）
                if (isBlank)
                {
                    Flush();
                    // 跳过连续空行
                    while (i + 1 < lines.Count && string.IsNullOrWhiteSpace(lines[i + 1].Text)) i++;
                    continue;
                }

                // 规则2：遇到序号/标题 → 新段（首行直接开始，不需要先 Flush 也能）
                if (OrderedStart.IsMatch(cur.Text))
                {
                    // 如果当前行就是新序号，先把之前积累的段落刷掉
                    Flush();
                    buf.Add(cur);
                    continue;
                }

                // 规则3：短标题行（独占一行且很短） → 分段
                if (IsTitleLike(cur.Text))
                {
                    Flush();
                    // 把标题行并入下一段首行（如果下一行不是序号/空行）
                    buf.Add(cur);
                    continue;
                }

                // 规则4：缩进变化（明显增大） → 分段
                if (buf.Count > 0 && cur.Position.X > baseX + indentThreshold * 1.5)
                {
                    Flush();
                    buf.Add(cur);
                    continue;
                }

                // 默认：归属当前段
                buf.Add(cur);
            }

            Flush();

            sheet.Segments.AddRange(segs);
            sheet.SegmentedText = BuildSegmentedText(segs);
        }

        static string BuildSegmentedText(List<TextSegment> segs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                sb.AppendLine($"【段落{s.Index}】{s.Title}");
                sb.AppendLine(s.Text);
                if (i < segs.Count - 1) sb.AppendLine();
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        static string GuessTitleFromLines(List<TextLine> lines)
        {
            // 取首行文本的前 20 字做标题
            var t = (lines[0].Text ?? "").Trim();
            if (t.Length > 20) t = t.Substring(0, 20) + "…";
            return t;
        }

        static bool IsTitleLike(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            // 短行（≤20字）且像标题
            return t.Length <= 20 && TitleLike.IsMatch(t);
        }

        static double GuessBaseX(List<TextLine> lines)
        {
            // 取所有非空行 X 的下四分位作为“无缩进”基线
            var xs = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text))
                          .Select(l => l.Position.X)
                          .OrderBy(x => x)
                          .ToList();
            if (xs.Count == 0) return 0;
            int idx = Math.Max(0, xs.Count / 4);
            return xs[idx];
        }

        static double GuessIndentThreshold(List<TextLine> lines, double baseX)
        {
            // 用字高中位数作为缩进阈值（1 字高 ≈ 1 个缩进级）
            var hs = lines.Where(l => l.Height > 0.01).Select(l => l.Height).OrderBy(h => h).ToList();
            if (hs.Count == 0) return 10;
            return hs[hs.Count / 2];
        }
    }
}
