using System;
using System.Collections.Generic;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad.UI
{
    /// <summary>
    /// 专业条文勾选状态的进程级持有者：MainModelessDialog 使用，
    /// 并落盘到 provision_selections.json，保证 AutoCAD 重启 / 面板重建后仍记住上次勾选。
    /// 约定：Get 返回 null 表示该专业从未勾选过（打开选择框时默认全勾）；
    /// 返回非 null（可为空集合）表示用户明确的选择。
    /// </summary>
    public static class ProvisionSelectionState
    {
        static readonly Dictionary<string, HashSet<long>> _map = new Dictionary<string, HashSet<long>>();
        static bool _loaded;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var saved = ProvisionSelectionsStore.Load(PluginEnv.ConfigPath);
                foreach (var kv in saved)
                    _map[kv.Key] = kv.Value;
            }
            catch
            {
                // 加载失败时保持空状态（默认全勾）
            }
        }

        /// <summary>取某专业已保存的勾选；null = 无历史（默认全勾）。</summary>
        public static HashSet<long> Get(string discipline)
        {
            EnsureLoaded();
            return string.IsNullOrWhiteSpace(discipline) ? null : _map.TryGetValue(discipline, out var s) ? s : null;
        }

        /// <summary>保存某专业的勾选并立即落盘。</summary>
        public static void Set(string discipline, HashSet<long> ids)
        {
            if (string.IsNullOrWhiteSpace(discipline)) return;
            EnsureLoaded();
            _map[discipline] = ids ?? new HashSet<long>();
            ProvisionSelectionsStore.Save(PluginEnv.ConfigPath, _map);
        }
    }
}
