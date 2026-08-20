using System;
using System.IO;
using Newtonsoft.Json;

namespace ContentCheck.Core.Config
{
    /// <summary>
    /// 加载 config.json，并把相对路径解析为绝对路径（相对配置文件所在目录）。
    /// API key 优先取 config.json.api_key，为空时回退到环境变量 DEEPSEEK_API_KEY / ANTHROPIC_AUTH_TOKEN。
    /// </summary>
    public static class ConfigLoader
    {
        public static AppConfig Load(string configPath)
        {
            var file = Path.GetFullPath(configPath);
            var dir = Path.GetDirectoryName(file);

            AppConfig cfg;
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file, System.Text.Encoding.UTF8);
                cfg = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            else
            {
                cfg = new AppConfig();
            }

            // provider 兜底
            if (string.IsNullOrWhiteSpace(cfg.Provider))
                cfg.Provider = AiProviderPresets.DefaultProvider;

            // 解析 API key
            cfg.ApiKey = ResolveApiKey(cfg.ApiKey);

            // 解析路径（相对 → 绝对，基于配置文件目录）
            cfg.ExcelPath = Resolve(dir, cfg.ExcelPath);
            cfg.DbPath = Resolve(dir, cfg.DbPath);
            cfg.LogDir = Resolve(dir, cfg.LogDir);

            // 默认值：provider 预设 → 常量兜底
            var preset = AiProviderPresets.Find(cfg.Provider);
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
                cfg.BaseUrl = preset.BaseUrl ?? AppConfig.DefaultBaseUrl;
            if (string.IsNullOrWhiteSpace(cfg.Model))
                cfg.Model = preset.Model ?? AppConfig.DefaultModel;
            // 向后兼容：旧配置只有 model_pro 时迁移到 model
            if (string.IsNullOrWhiteSpace(cfg.Model) && !string.IsNullOrWhiteSpace(cfg.ModelProCompat))
                cfg.Model = cfg.ModelProCompat;
            // 兜底到常量（custom 等预设可能为空）
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
                cfg.BaseUrl = AppConfig.DefaultBaseUrl;
            if (string.IsNullOrWhiteSpace(cfg.Model))
                cfg.Model = AppConfig.DefaultModel;

            return cfg;
        }

        /// <summary>
        /// 规范化 API key：config.json 值优先；为空时依次回退环境变量
        /// DEEPSEEK_API_KEY → ANTHROPIC_AUTH_TOKEN。
        /// </summary>
        public static string ResolveApiKey(string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();

            // 回退 1：DEEPSEEK_API_KEY
            var env = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

            // 回退 2：ANTHROPIC_AUTH_TOKEN（兼容）
            env = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

            return "";
        }

        private static string Resolve(string baseDir, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDir, path));
        }
    }
}
