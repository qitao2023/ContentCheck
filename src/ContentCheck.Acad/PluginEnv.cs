using System;
using System.IO;
using ContentCheck.Core.Config;

namespace ContentCheck.Acad
{
    /// <summary>插件级环境：定位配置文件并按插件 DLL 目录解析出绝对路径。</summary>
    public static class PluginEnv
    {
        public static string PluginDir { get; private set; }
        public static string ConfigPath { get; private set; }
        public static AppConfig Config { get; internal set; }
        public static bool InitOk { get; private set; }

        public static void Init()
        {
            PluginDir = Path.GetDirectoryName(typeof(PluginEnv).Assembly.Location);
            ConfigPath = FindConfigUpwards();
            Config = ConfigLoader.Load(ConfigPath);
            InitOk = true;
        }

        /// <summary>插件目录 → 向上找 config.json（部署在 out\ 时配置文件在上级目录）。</summary>
        static string FindConfigUpwards()
        {
            var dir = new DirectoryInfo(PluginDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "config.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(PluginDir, "config.json");
        }
    }
}
