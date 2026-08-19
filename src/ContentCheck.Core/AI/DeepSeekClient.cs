using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContentCheck.Core.Config;
using ContentCheck.Core.Models;
using ContentCheck.Core.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ContentCheck.Core.AI
{
    /// <summary>连接测试的结构化结果。</summary>
    public class TestResult
    {
        public bool Success { get; set; }
        public string ModelName { get; set; }
        public long LatencyMs { get; set; }
        public string Reply { get; set; }
        public string Error { get; set; }
    }

    /// <summary>AI 调用失败（重试耗尽仍失败）。</summary>
    public class AiCallException : Exception
    {
        public AiCallException(string message) : base(message) { }
    }

    /// <summary>
    /// DeepSeek OpenAI 兼容接口客户端。JSON mode，批量校核，重试+退避，交互日志。
    /// 批量整体失败后由 CheckEngine 降级为逐条调用。
    /// </summary>
    public class DeepSeekClient
    {
        static readonly HttpClient Http = CreateHttp();

        readonly AppConfig _cfg;
        readonly string _logDir;

        /// <summary>模型名（默认取 cfg.Model，可覆盖为 cfg.ModelPro 等）。</summary>
        public string Model { get; set; }

        public DeepSeekClient(AppConfig cfg, string logDir = null)
        {
            _cfg = cfg;
            _logDir = logDir ?? cfg.LogDir;
            Model = cfg.Model;
        }

        static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            h.DefaultRequestHeaders.Add("User-Agent", "ContentCheck/1.0");
            return h;
        }

        /// <summary>校核一批条文，返回与条文一一对应的结论。</summary>
        public async Task<List<AiVerdict>> CheckBatchAsync(CheckBatch batch, string sheetName, string sheetText,
            CancellationToken ct)
        {
            var system = PromptBuilder.SystemPrompt();
            var user = PromptBuilder.UserPrompt(sheetName, sheetText, batch);
            var raw = await ChatJsonAsync(system, user, ct);

            var verdicts = AiJsonParser.ParseVerdicts(raw);
            if (verdicts.Count == 0)
            {
                JsonLog.WriteCall(_logDir, Model, sheetName, $"{batch.Discipline}|{batch.CodeName}",
                    batch.Items.Count, system, user, raw, "解析失败:空结果");
                throw new AiCallException("AI 返回为空或无法解析为 JSON");
            }

            // 补齐缺失条文（AI 漏答）
            var byNum = verdicts.Where(v => !string.IsNullOrWhiteSpace(v.ClauseNumber))
                .ToDictionary(v => v.ClauseNumber, v => v);
            foreach (var item in batch.Items)
            {
                var key = item.ClauseNumber ?? "";
                if (byNum.TryGetValue(key, out _)) continue;
                verdicts.Add(new AiVerdict
                {
                    ClauseNumber = item.ClauseNumber,
                    Verdict = AiJsonParser.VERDICT_UNKNOWN,
                    Evidence = "",
                    Analysis = "AI 未返回该条文结论，按无法判断处理。",
                    Suggestion = "",
                });
            }

            JsonLog.WriteCall(_logDir, Model, sheetName, $"{batch.Discipline}|{batch.CodeName}",
                batch.Items.Count, system, user, raw, $"成功:{verdicts.Count}条");
            return verdicts;
        }

        /// <summary>逐条校核（批量降级时用），期望返回单个 JSON 对象。</summary>
        public async Task<AiVerdict> CheckSingleAsync(CheckBatch.BatchItem item, string sheetName, string sheetText,
            CancellationToken ct)
        {
            var batch = new CheckBatch
            {
                Discipline = "",
                CodeName = "",
                Items = { item },
            };
            var system = PromptBuilder.SystemPrompt();
            var user = PromptBuilder.UserPrompt(sheetName, sheetText, batch);
            var raw = await ChatJsonAsync(system, user, ct);

            var verdicts = AiJsonParser.ParseVerdicts(raw);
            var verdict = verdicts.FirstOrDefault();
            JsonLog.WriteCall(_logDir, Model, sheetName, $"逐条:{item.ClauseNumber}",
                1, system, user, raw, verdict == null ? "解析失败" : $"成功:{verdict.Verdict}");
            return verdict
                ?? new AiVerdict
                {
                    ClauseNumber = item.ClauseNumber,
                    Verdict = AiJsonParser.VERDICT_UNKNOWN,
                    Analysis = "AI 未返回有效结论。",
                };
        }

        /// <summary>调用 chat/completions，返回 message 内容（JSON 文本）。失败重试 2 次后抛 AiCallException。</summary>
        async Task<string> ChatJsonAsync(string system, string user, CancellationToken ct)
        {
            int[] backoffs = { 3000, 9000 };
            Exception lastErr = null;

            for (int attempt = 0; attempt <= backoffs.Length; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var raw = await PostOnceAsync(system, user, ct);
                    // 空内容也视作失败重试
                    if (string.IsNullOrWhiteSpace(raw))
                        throw new AiCallException("AI 返回内容为空");
                    return raw;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    if (attempt < backoffs.Length)
                    {
                        await Task.Delay(backoffs[attempt], ct);
                    }
                }
            }

            throw new AiCallException("DeepSeek 调用失败：" + lastErr?.Message);
        }

        /// <summary>连通性测试：最小 payload（不要求 json_object，兼容本地服务）。成功返回回复文本。</summary>
        public async Task<string> TestConnectionAsync(CancellationToken ct)
        {
            var payload = new JObject
            {
                ["model"] = Model,
                ["max_tokens"] = 1,
                ["messages"] = new JArray(
                    new JObject { ["role"] = "user", ["content"] = "ping" }),
            };
            return await PostRawAsync(payload, ct);
        }

        /// <summary>连通性测试（结构化）：发送实际 prompt，返回延迟和响应内容。</summary>
        public async Task<TestResult> TestConnectionDetailedAsync(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var payload = new JObject
                {
                    ["model"] = Model,
                    ["max_tokens"] = 10,
                    ["messages"] = new JArray(
                        new JObject { ["role"] = "user", ["content"] = "请回复OK" }),
                };
                var reply = await PostRawAsync(payload, ct);
                sw.Stop();
                return new TestResult
                {
                    Success = true,
                    ModelName = Model,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Reply = reply?.Trim(),
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new TestResult
                {
                    Success = false,
                    ModelName = Model,
                    LatencyMs = sw.ElapsedMilliseconds,
                    Error = ex.Message,
                };
            }
        }

        async Task<string> PostOnceAsync(string system, string user, CancellationToken ct)
        {
            var payload = new JObject
            {
                ["model"] = Model,
                ["temperature"] = _cfg.Temperature,
                ["max_tokens"] = _cfg.MaxTokens,
                ["response_format"] = new JObject { ["type"] = "json_object" },
                ["messages"] = new JArray(
                    new JObject { ["role"] = "system", ["content"] = system },
                    new JObject { ["role"] = "user", ["content"] = user }),
            };

            return await PostRawAsync(payload, ct);
        }

        async Task<string> PostRawAsync(JObject payload, CancellationToken ct)
        {
            var baseUrl = _cfg.BaseUrl ?? AppConfig.DefaultBaseUrl;
            baseUrl = baseUrl.TrimEnd('/');
            var url = baseUrl + "/chat/completions";

            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg.ApiKey ?? "");
                req.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        var detail = TryExtractError(body);
                        throw new AiCallException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}：{detail}");
                    }

                    string content = null;
                    string reasoning = null;
                    try
                    {
                        var root = JObject.Parse(body);
                        var msg = root["choices"]?[0]?["message"];
                        content = msg?["content"]?.Value<string>();
                        reasoning = msg?["reasoning_content"]?.Value<string>();
                    }
                    catch (JsonException) { }

                    var result = !string.IsNullOrWhiteSpace(content) ? content : reasoning;
                    if (result == null)
                        throw new AiCallException("响应中无 content：HTTP 200 但消息为空");
                    return result;
                }
            }
        }

        static string TryExtractError(string body)
        {
            try
            {
                var root = JObject.Parse(body);
                return root["error"]?["message"]?.Value<string>() ?? body;
            }
            catch { return body != null && body.Length > 300 ? body.Substring(0, 300) : body; }
        }
    }
}
