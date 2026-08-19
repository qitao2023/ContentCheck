using System;

namespace ContentCheck.Core.Config
{
    /// <summary>大模型服务商预设配置。</summary>
    public sealed class AiProviderPreset
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        /// <summary>云端服务必须提供 API Key；本地服务为 false。</summary>
        public bool RequiresKey { get; set; }
        /// <summary>建议模型列表（UI 可编辑 ComboBox 预置）。</summary>
        public string[] SuggestedModels { get; set; }
        public override string ToString() => Name ?? Key ?? "";
    }

    /// <summary>内置服务商预设表 + 查找。</summary>
    public static class AiProviderPresets
    {
        public const string DefaultProvider = "deepseek";

        public static readonly AiProviderPreset[] All =
        {
            new AiProviderPreset
            {
                Key = "deepseek", Name = "DeepSeek",
                BaseUrl = "https://api.deepseek.com/v1",
                Model = "deepseek-v4-flash",
                RequiresKey = true,
                SuggestedModels = new[] { "deepseek-v4-flash", "deepseek-v4-pro", "deepseek-chat", "deepseek-reasoner" },
            },
            new AiProviderPreset
            {
                Key = "mimo", Name = "MiMo(小米)",
                BaseUrl = "https://api.xiaomimimo.com/v1",
                Model = "mimo-v2.5",
                RequiresKey = true,
                SuggestedModels = new[] { "mimo-v2.5", "mimo-v2.5-pro" },
            },
            new AiProviderPreset
            {
                Key = "moonshot", Name = "Kimi(月之暗面)",
                BaseUrl = "https://api.moonshot.cn/v1",
                Model = "moonshot-v1-32k",
                RequiresKey = true,
                SuggestedModels = new[] { "kimi-k2", "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k" },
            },
            new AiProviderPreset
            {
                Key = "zhipu", Name = "智谱AI",
                BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Model = "glm-4-flash",
                RequiresKey = true,
                SuggestedModels = new[] { "glm-4-flash", "glm-4-air", "glm-4-plus", "glm-4.5" },
            },
            new AiProviderPreset
            {
                Key = "ollama", Name = "Ollama(本地)",
                BaseUrl = "http://localhost:11434/v1",
                Model = "qwen2.5:7b",
                SuggestedModels = new[] { "qwen2.5:7b", "qwen2.5:14b", "qwen2.5:32b", "llama3.1:8b", "deepseek-r1:7b" },
            },
            new AiProviderPreset
            {
                Key = "lmstudio", Name = "LM Studio(本地)",
                BaseUrl = "http://localhost:1234/v1",
                Model = "qwen2.5-7b-instruct",
                SuggestedModels = new[] { "qwen2.5-7b-instruct", "qwen2.5-14b-instruct", "deepseek-r1-distill-qwen-7b" },
            },
            new AiProviderPreset
            {
                Key = "custom", Name = "自定义(OpenAI 兼容)",
                BaseUrl = "", Model = "",
                SuggestedModels = new string[0],
            },
        };

        /// <summary>按 Key 查找预设；未知 key 兜底 deepseek，永不返回 null。</summary>
        public static AiProviderPreset Find(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
                foreach (var p in All)
                    if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                        return p;
            return All[0]; // deepseek 兜底
        }

    }
}
