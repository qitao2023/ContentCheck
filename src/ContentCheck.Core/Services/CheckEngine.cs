using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContentCheck.Core.AI;
using ContentCheck.Core.Config;
using ContentCheck.Core.Models;

namespace ContentCheck.Core.Services
{
    /// <summary>
    /// 校核引擎：条文按 (专业, 规范) 分批 → DeepSeek 语义校核。
    /// 批量失败后降级为逐条调用；仍失败的条目填「无法判断」+ 错误说明，不丢行。
    /// 不触碰 AutoCAD 对象，可安全地在工作线程运行。
    /// </summary>
    public class CheckEngine
    {
        public class RunResult
        {
            public List<VerdictResult> Results { get; set; } = new List<VerdictResult>();
            public string SheetName { get; set; }
            public string SheetTextTruncated { get; set; }
            public bool SheetTruncated { get; set; }
            public int BatchCount { get; set; }
            public string Model { get; set; }
        }

        public async Task<RunResult> RunAsync(AppConfig cfg, List<Provision> provisions,
            string sheetName, string sheetText, string model, string logDir,
            IProgress<string> status, CancellationToken ct)
        {
            var result = new RunResult
            {
                SheetName = sheetName,
                Model = model,
            };

            // 总说明：折叠空行 + 按上限截断
            var norm = PromptBuilder.NormalizeSheetText(sheetText);
            result.SheetTruncated = norm.Length > cfg.MaxSheetChars;
            result.SheetTextTruncated = PromptBuilder.TruncateSheetText(norm, cfg.MaxSheetChars);
            string effectiveText = result.SheetTextTruncated;

            // 分批
            var batches = GroupBatches(provisions, cfg.BatchSize);
            result.BatchCount = batches.Count;

            var client = new DeepSeekClient(cfg, logDir) { Model = model };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                status?.Report($"正在校核（{done + 1}/{batches.Count}）：{batch.Discipline}《{batch.CodeName}》（{batch.Items.Count} 条）… 已用 {FormatElapsed(sw.Elapsed)}");
                var merged = await CheckBatchWithFallback(client, batch, sheetName, effectiveText, ct);

                // 关联条文信息，产出 VerdictResult
                foreach (var m in merged)
                {
                    var p = m.Item;
                    result.Results.Add(new VerdictResult
                    {
                        Discipline = batch.Discipline,
                        CodeName = batch.CodeName,
                        ClauseNumber = p.ClauseNumber,
                        ClauseText = p.ClauseText,
                        DrawingTypesRaw = p.DrawingTypesRaw,
                        Verdict = m.Verdict.Verdict,
                        Evidence = m.Verdict.Evidence,
                        Analysis = m.Verdict.Analysis,
                        Suggestion = m.Verdict.Suggestion,
                        SourceNote = m.SourceNote,
                    });
                }

                done++;
                status?.Report($"已完成 {done}/{batches.Count} 批，共 {result.Results.Count} 条，已用 {FormatElapsed(sw.Elapsed)}");
            }

            sw.Stop();
            status?.Report($"校核完成：共 {result.Results.Count} 条，总用时 {FormatElapsed(sw.Elapsed)}");
            return result;
        }

        static string FormatElapsed(TimeSpan t)
            => t.TotalMinutes >= 1 ? $"{t.Minutes}分{t.Seconds}秒" : $"{t.Seconds}秒";

        // ---------- 分批与降级 ----------

        internal static List<CheckBatch> GroupBatches(List<Provision> provisions, int batchSize)
        {
            var batches = new List<CheckBatch>();
            var groups = provisions
                .GroupBy(p => new { p.Discipline, p.CodeName })
                .OrderBy(g => g.Key.Discipline)
                .ThenBy(g => g.Key.CodeName);

            foreach (var g in groups)
            {
                var items = g.Select(p => new CheckBatch.BatchItem
                {
                    ProvisionId = p.Id,
                    ClauseNumber = p.ClauseNumber,
                    ClauseText = p.ClauseText,
                    DrawingTypesRaw = p.DrawingTypesRaw,
                }).ToList();

                for (int i = 0; i < items.Count; i += Math.Max(1, batchSize))
                {
                    batches.Add(new CheckBatch
                    {
                        Discipline = g.Key.Discipline,
                        CodeName = g.Key.CodeName,
                        Items = items.Skip(i).Take(batchSize).ToList(),
                    });
                }
            }
            return batches;
        }

        sealed class Merged
        {
            public CheckBatch.BatchItem Item;
            public AiVerdict Verdict;
            public string SourceNote;
        }

        static async Task<List<Merged>> CheckBatchWithFallback(DeepSeekClient client, CheckBatch batch,
            string sheetName, string sheetText, CancellationToken ct)
        {
            try
            {
                var verdicts = await client.CheckBatchAsync(batch, sheetName, sheetText, ct);
                var byNum = verdicts
                    .GroupBy(v => v.ClauseNumber ?? "")
                    .ToDictionary(g => g.Key, g => g.First());
                return batch.Items.Select(item =>
                {
                    byNum.TryGetValue(item.ClauseNumber ?? "", out var v);
                    return new Merged
                    {
                        Item = item,
                        Verdict = v ?? Unknown(item),
                        SourceNote = v == null ? "批量结果缺失该条文，按无法判断处理" : client.Model,
                    };
                }).ToList();
            }
            catch (AiCallException)
            {
                // 批量整体失败 → 降级为逐条
                var merged = new List<Merged>();
                foreach (var item in batch.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var v = await client.CheckSingleAsync(item, sheetName, sheetText, ct);
                        merged.Add(new Merged { Item = item, Verdict = v, SourceNote = client.Model + "(逐条降级)" });
                    }
                    catch (Exception ex)
                    {
                        merged.Add(new Merged
                        {
                            Item = item,
                            Verdict = new AiVerdict
                            {
                                ClauseNumber = item.ClauseNumber,
                                Verdict = AiJsonParser.VERDICT_UNKNOWN,
                                Analysis = "AI 调用失败：" + ex.Message,
                            },
                            SourceNote = "调用失败",
                        });
                    }
                }
                return merged;
            }
        }

        static AiVerdict Unknown(CheckBatch.BatchItem item) => new AiVerdict
        {
            ClauseNumber = item.ClauseNumber,
            Verdict = AiJsonParser.VERDICT_UNKNOWN,
            Analysis = "批量结果中缺少该条文。",
        };
    }
}
