using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ContentCheck.Core.Util
{
    /// <summary>AI 交互日志：每条调用一个 JSON 文件，便于审计坏输出。不含 API key。</summary>
    public static class JsonLog
    {
        static readonly object Gate = new object();

        public static void WriteCall(string logDir, string model, string sheetName, string batchKey,
            int itemCount, string systemPrompt, string userPrompt, string rawResponse, string parsedSummary,
            long elapsedMs = 0)
        {
            if (string.IsNullOrWhiteSpace(logDir)) return;
            try
            {
                Directory.CreateDirectory(logDir);
                var entry = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    model,
                    sheetName,
                    batchKey,
                    itemCount,
                    elapsed_ms = elapsedMs,
                    system_prompt = systemPrompt,
                    user_prompt = userPrompt,
                    response_raw = rawResponse,
                    parsed = parsedSummary,
                };
                string json = JsonConvert.SerializeObject(entry, Formatting.Indented);

                lock (Gate)
                {
                    string file = Path.Combine(logDir, $"call_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json");
                    File.WriteAllText(file, json, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志失败不应影响主流程
            }
        }
    }
}
