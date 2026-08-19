using System;
using System.IO;
using Newtonsoft.Json;

namespace ContentCheck.Core.Config
{
    /// <summary>
    /// 加载 config.json，并把相对路径解析为绝对路径（相对配置文件所在目录）。
    /// API key 直接取自 config.json.api_key，不做环境变量解析。
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

        /// <summary>规范化 API key：直接取 config.json 里的值，仅去空白，不做环境变量解析。</summary>
        public static string ResolveApiKey(string apiKey)
        {
            return string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey.Trim();
        }

        private static string Resolve(string baseDir, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDir, path));
        }
    }
}
