using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ContentCheck.Core.AI
{
    /// <summary>一次条文校核的 AI 原始结论（尚未与条文关联）。</summary>
    public class AiVerdict
    {
        public string ClauseNumber { get; set; }
        public string Verdict { get; set; }
        public string Evidence { get; set; }
        public string Analysis { get; set; }
        public string Suggestion { get; set; }
    }

    /// <summary>
    /// 防御式 JSON 解析：DeepSeek 可能返回 Markdown 围栏、前缀说明、括号失配等脏输出。
    /// 纯函数，无 I/O，可离线单测。
    /// </summary>
    public static class AiJsonParser
    {
        public const string VERDICT_OK = "符合";
        public const string VERDICT_BAD = "不符合";
        public const string VERDICT_NA = "未涉及";
        public const string VERDICT_UNKNOWN = "无法判断";

        /// <summary>从 LLM 原始输出中提取最外层 JSON 对象/数组片段。</summary>
        public static string CleanJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim();

            // 剥 Markdown 围栏
            if (s.StartsWith("```"))
            {
                int idx = s.IndexOf('\n');
                if (idx >= 0) s = s.Substring(idx + 1);
                int end = s.LastIndexOf("```");
                if (end >= 0) s = s.Substring(0, end);
                s = s.Trim();
            }

            char start = s.Length > 0 ? s[0] : '\0';
            if (start != '{' && start != '[')
            {
                // 找到首个 { 或 [，做括号匹配截取
                int open = FindFirstOpen(s);
                if (open < 0) return "";
                s = BraceExtract(s, open);
            }
            return s.Trim();
        }

        static int FindFirstOpen(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '{' || s[i] == '[') return i;
            return -1;
        }

        /// <summary>从 open 处做括号匹配（跳过字符串字面量），返回闭合的片段。</summary>
        static string BraceExtract(string s, int open)
        {
            char openCh = s[open];
            char closeCh = openCh == '{' ? '}' : ']';
            int depth = 0;
            bool inStr = false;
            bool escaped = false;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == openCh) depth++;
                else if (c == closeCh)
                {
                    depth--;
                    if (depth == 0) return s.Substring(open, i - open + 1);
                }
            }
            return s.Substring(open); // 括号失配：尽量返回剩余部分，交给 ParseVerdicts 兜底
        }

        /// <summary>解析 LLM 返回的 verdict 列表。兼容 {"results":[...]} / 裸数组 / 单对象。</summary>
        public static List<AiVerdict> ParseVerdicts(string json)
        {
            var result = new List<AiVerdict>();
            var cleaned = CleanJson(json);
            if (cleaned.Length == 0) return result;

            JToken root;
            try { root = JToken.Parse(cleaned); }
            catch (JsonException) { return result; }

            JToken arr;
            switch (root)
            {
                case JObject obj when obj["results"] is JArray ra:
                    arr = ra;
                    break;
                case JArray a:
                    arr = a;
                    break;
                case JObject single when single["clause_number"] != null:
                    arr = new JArray(single);
                    break;
                default:
                    return result;
            }

            foreach (var item in ((JArray)arr).OfType<JObject>())
            {
                result.Add(new AiVerdict
                {
                    ClauseNumber = (item["clause_number"]?.Value<string>() ?? "").Trim(),
                    Verdict = CoerceVerdict(item["verdict"]?.Value<string>()),
                    Evidence = item["evidence"]?.Value<string>() ?? "",
                    Analysis = item["analysis"]?.Value<string>() ?? "",
                    Suggestion = item["suggestion"]?.Value<string>() ?? "",
                });
            }
            return result;
        }

        /// <summary>未知/空结论强制归为「无法判断」。</summary>
        public static string CoerceVerdict(string v)
        {
            if (v == null) return VERDICT_UNKNOWN;
            v = v.Trim();
            if (v == VERDICT_OK || v == VERDICT_BAD || v == VERDICT_NA || v == VERDICT_UNKNOWN) return v;
            // 容错常见变体
            if (v.Contains("符合") && v.Contains("不")) return VERDICT_BAD;
            if (v.Contains("符合")) return VERDICT_OK;
            if (v.Contains("未涉及") || v.Contains("不涉及") || v.Contains("无关")) return VERDICT_NA;
            return VERDICT_UNKNOWN;
        }
    }
}
