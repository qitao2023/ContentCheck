using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad
{
    /// <summary>
    /// 插件入口：NETLOAD 加载时由 AutoCAD 调用。
    /// 1) 注册 AssemblyResolve（NETLOAD 不探测插件目录，需手动解析同目录依赖 DLL）；
    /// 2) 初始化配置并按需自检（数据库已导入？完整性？API key 已解析？）。
    /// </summary>
    public class AppExtension : IExtensionApplication
    {
        public void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // 延迟到空闲时自检，避免 Initialize 阶段 AutoCAD 未完全就绪
            Application.Idle += OnFirstIdle;
        }

        public void Terminate()
        {
            Application.Idle -= OnFirstIdle;
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            if (Commands.ModelessDialog != null)
            {
                try { Commands.ModelessDialog.Dispose(); } catch { }
                Commands.ModelessDialog = null;
            }
        }

        void OnFirstIdle(object sender, EventArgs e)
        {
            Application.Idle -= OnFirstIdle;
            try { PluginEnv.Init(); }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("ContentCheck 配置初始化失败：\n" + ex.Message);
                return;
            }

            var cfg = PluginEnv.Config;

            // 数据库自检
            try
            {
                var store = new SqliteProvisionStore(cfg.DbPath);
                if (!File.Exists(cfg.DbPath))
                {
                    Application.ShowAlertDialog(
                        "未找到规范条文数据库 provisions.db。\n\n" +
                        "请先运行导入：ContentCheck.Import.exe（或 AutoCAD 内执行 CC_IMPORT）。");
                }
                else if (store.GetLastImport() == null)
                {
                    Application.ShowAlertDialog("条文数据库已存在但无导入记录，请重新导入。");
                }
                else if (!string.Equals(store.CheckIntegrity(), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    Application.ShowAlertDialog(
                        "条文数据库完整性检查失败。\n请确认 out\\ 目录下存在 SQLite.Interop.dll（x64）。");
                }
            }
            catch (System.Exception ex)
            {
                Application.ShowAlertDialog("条文数据库访问失败：\n" + ex.Message +
                    "\n\n请确认 out\\ 目录下存在 SQLite.Interop.dll（x64）。");
            }

            // API key 自检
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                Application.ShowAlertDialog(
                    "未配置 DeepSeek API key。\n\n" +
                    "请在 config.json 中填写 api_key，或设置环境变量 DEEPSEEK_API_KEY / ANTHROPIC_AUTH_TOKEN。");
            }
        }

        /// <summary>NETLOAD 不会把插件目录加入默认探测路径，这里手动解析同目录依赖。</summary>
        static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var dir = PluginEnv.PluginDir ?? Path.GetDirectoryName(typeof(AppExtension).Assembly.Location);
            var p = Path.Combine(dir, name);
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        }
    }
}
