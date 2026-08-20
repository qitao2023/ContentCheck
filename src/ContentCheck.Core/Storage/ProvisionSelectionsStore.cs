using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ContentCheck.Core.Storage
{
    /// <summary>
    /// 规范条文勾选状态的持久化：按专业保存勾选的条文 Id。
    /// 文件为 JSON（{ 专业: [id, ...] }），放在配置文件同目录 provision_selections.json，
    /// 使 AutoCAD 重启 / 面板重建后仍能记住上次的勾选。
    /// 约定：字典中「没有该专业条目」= 从未勾选过（打开选择框时默认全勾）；
    /// 「有条目（可为空集合）」= 用户明确的选择，空集合表示全不选。
    /// </summary>
    public static class ProvisionSelectionsStore
    {
        public const string DefaultFileName = "provision_selections.json";

        /// <summary>加载勾选状态；文件不存在或损坏时返回空字典（不抛异常）。</summary>
        public static Dictionary<string, HashSet<long>> Load(string configPath)
        {
            var file = GetFilePath(configPath);
            var result = new Dictionary<string, HashSet<long>>();
            try
            {
                if (!File.Exists(file)) return result;

                var json = File.ReadAllText(file, Encoding.UTF8);
                var obj = JsonConvert.DeserializeObject<Dictionary<string, List<long>>>(json);
                if (obj == null) return result;

                foreach (var kv in obj)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                    result[kv.Key] = new HashSet<long>(kv.Value);
                }
            }
            catch
            {
                // 文件损坏等：忽略，当作无历史勾选
            }
            return result;
        }

        /// <summary>保存勾选状态到磁盘（写入失败时静默忽略，不打断主流程）。</summary>
        public static void Save(string configPath, Dictionary<string, HashSet<long>> selections)
        {
            try
            {
                var file = GetFilePath(configPath);
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var obj = new Dictionary<string, List<long>>();
                foreach (var kv in selections)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                    // 空集合也要保留：代表用户明确「全不选」
                    obj[kv.Key] = kv.Value.OrderBy(x => x).ToList();
                }

                File.WriteAllText(file, JsonConvert.SerializeObject(obj, Formatting.Indented), new UTF8Encoding(false));
            }
            catch
            {
                // 磁盘只读 / 权限不足等：本次勾选无法持久化，但不影响本次运行
            }
        }

        static string GetFilePath(string configPath)
        {
            var full = Path.GetFullPath(configPath ?? "");
            var dir = Path.GetDirectoryName(full);
            return string.IsNullOrEmpty(dir)
                ? Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName)
                : Path.Combine(dir, DefaultFileName);
        }
    }
}
