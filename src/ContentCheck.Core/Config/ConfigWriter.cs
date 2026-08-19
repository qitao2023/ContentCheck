using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ContentCheck.Core.Config
{
    /// <summary>大模型设置保存用 DTO。</summary>
    public class AiSettings
    {
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public int MaxSheetChars { get; set; }
        public int BatchSize { get; set; }
    }

    /// <summary>
    /// 把 AiSettings 写回 config.json（只覆盖 AI 字段，保留 excel/db/log 相对路径）。
    /// </summary>
    public static class ConfigWriter
    {
        /// <summary>
        /// 写回 AI 相关字段到 config.json，返回重新 Load 的新 AppConfig（路径已解析为绝对路径）。
        /// </summary>
        public static AppConfig SaveAiSettings(string configPath, AiSettings s)
        {
            var file = Path.GetFullPath(configPath);
            JObject root;
            if (File.Exists(file))
                root = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
            else
                root = new JObject();

            // 只覆盖 AI 相关字段，保留 excel_path/db_path/log_dir 等相对路径
            root["provider"] = s.Provider ?? "";
            root["api_key"] = s.ApiKey ?? "";
            root["base_url"] = s.BaseUrl ?? "";
            root["model"] = s.Model ?? "";
            root.Remove("model_pro"); // 迁移：写入时清除旧字段
            root["temperature"] = new JValue(s.Temperature);
            root["max_tokens"] = s.MaxTokens;
            root["max_sheet_chars"] = s.MaxSheetChars;
            root["batch_size"] = s.BatchSize;

            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(file, root.ToString(Formatting.Indented), new UTF8Encoding(false));

            // 重新 Load 解析路径
            return ConfigLoader.Load(file);
        }

        /// <summary>读 config.json 里原始的 api_key（未做环境变量解析），供对话框预填。</summary>
        public static string ReadRawApiKey(string configPath)
        {
            try
            {
                var root = JObject.Parse(File.ReadAllText(Path.GetFullPath(configPath), Encoding.UTF8));
                return root["api_key"]?.Value<string>() ?? "";
            }
            catch { return ""; }
        }
    }
}
