using Newtonsoft.Json;

namespace ContentCheck.Core.Config
{
    /// <summary>强类型配置（来自 config.json，JSON 字段为 snake_case）。</summary>
    public class AppConfig
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com/v1";
        public const string DefaultModel = "deepseek-v4-flash";

        /// <summary>服务商标识（AiProviderPresets.Key），如 deepseek/mimo/ollama。</summary>
        [JsonProperty("provider")]
        public string Provider { get; set; } = "deepseek";

        /// <summary>API key；为空时回退到环境变量 DEEPSEEK_API_KEY / ANTHROPIC_AUTH_TOKEN。</summary>
        [JsonProperty("api_key")]
        public string ApiKey { get; set; }

        [JsonProperty("base_url")]
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>核对模型。</summary>
        [JsonProperty("model")]
        public string Model { get; set; } = DefaultModel;

        /// <summary>向后兼容：旧配置中的 model_pro 字段，仅用于迁移。</summary>
        [JsonProperty("model_pro")]
        internal string ModelProCompat { get; set; }

        [JsonProperty("temperature")]
        public double Temperature { get; set; } = 0.3;

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; } = 8192;

        /// <summary>总说明文字送入 AI 的最大字符数；超长时保留开头+结尾。</summary>
        [JsonProperty("max_sheet_chars")]
        public int MaxSheetChars { get; set; } = 12000;

        /// <summary>一次 AI 调用校核的最大条文数。</summary>
        [JsonProperty("batch_size")]
        public int BatchSize { get; set; } = 20;

        /// <summary>Excel 路径（相对配置文件目录或绝对路径）。</summary>
        [JsonProperty("excel_path")]
        public string ExcelPath { get; set; }

        /// <summary>SQLite 库路径。</summary>
        [JsonProperty("db_path")]
        public string DbPath { get; set; } = "provisions.db";

        /// <summary>交互日志目录。</summary>
        [JsonProperty("log_dir")]
        public string LogDir { get; set; } = "logs";
    }
}
