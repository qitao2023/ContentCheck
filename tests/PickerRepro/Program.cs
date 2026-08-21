using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ContentCheck.Acad.UI;
using ContentCheck.Core.Models;
using ContentCheck.Core.Storage;

namespace PickerRepro
{
    /// <summary>
    /// 复现工程：验证 ProvisionPickerDialog 的勾选恢复链路
    /// （初始勾选应用 → 用户操作 → 确定收集 → 持久化 → 重开恢复）。
    /// </summary>
    static class Program
    {
        static int _passed, _failed;

        [STAThread]
        static int Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var dir = @"C:\Program Files\Autodesk\AutoCAD 2020";
                var path = Path.Combine(dir, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            var provisions = new List<Provision>
            {
                Prov(1, "《规范A》", "1.1", "条文A1"),
                Prov(2, "《规范A》", "1.2", "条文A2"),
                Prov(3, "《规范A》", "1.3", "条文A3"),
                Prov(4, "《规范B》", "2.1", "条文B1"),
                Prov(5, "《规范B》", "2.2", "条文B2"),
            };

            Test("S1 初始 null → 全部勾选", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.SetEquals(new long[] { 1, 2, 3, 4, 5 }), $"期望全勾 [1..5]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S2 初始 {1} → 只勾 id=1", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, new HashSet<long> { 1 }))
                {
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.SetEquals(new long[] { 1 }), $"期望 [1]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S2b 初始 {1} + 真实显示窗口 → 只勾 id=1", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, new HashSet<long> { 1 }))
                {
                    dlg.Show();
                    Application.DoEvents();
                    var ids = CheckedLeafIds(dlg);
                    dlg.Close();
                    Assert(ids.SetEquals(new long[] { 1 }), $"期望 [1]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S3 初始 空集合 → 全不勾", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, new HashSet<long>()))
                {
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.Count == 0, $"期望空，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S4 全不选后勾1个 → 确定收集 {1}", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    Invoke(dlg, "SetAll", false);
                    SetLeafChecked(dlg, 1, true);
                    FireFormClosing(dlg);
                    var ids = dlg.SelectedIds;
                    Assert(ids.SetEquals(new long[] { 1 }), $"期望 [1]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S5 全不选后勾1个 → 确定 → 用结果重开 → 只勾 id=1", () =>
            {
                HashSet<long> saved;
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    Invoke(dlg, "SetAll", false);
                    SetLeafChecked(dlg, 1, true);
                    FireFormClosing(dlg);
                    saved = dlg.SelectedIds;
                }
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, saved))
                {
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.SetEquals(new long[] { 1 }), $"重开期望 [1]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S6 只取消1个叶节点 → 组内其余叶不应被级联取消", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    SetLeafChecked(dlg, 2, false);   // 模拟用户取消 id=2
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.SetEquals(new long[] { 1, 3, 4, 5 }), $"期望 [1,3,4,5]，实际 [{string.Join(",", ids)}]（疑似组级联误伤）");
                }
            });

            Test("S7 只取消组内1个 → 确定收集应保留其余勾选", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    SetLeafChecked(dlg, 2, false);
                    FireFormClosing(dlg);
                    var ids = dlg.SelectedIds;
                    Assert(ids.SetEquals(new long[] { 1, 3, 4, 5 }), $"期望 [1,3,4,5]，实际 [{string.Join(",", ids)}]");
                }
            });

            Test("S8 ProvisionSelectionsStore 落盘回读", () =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "cc_picker_repro_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    var configPath = Path.Combine(dir, "config.json");
                    var map = new Dictionary<string, HashSet<long>> { ["建筑"] = new HashSet<long> { 1 } };
                    ProvisionSelectionsStore.Save(configPath, map);
                    var loaded = ProvisionSelectionsStore.Load(configPath);
                    Assert(loaded.ContainsKey("建筑") && loaded["建筑"].SetEquals(new long[] { 1 }), "落盘回读不一致");
                    // 空集合也要保留
                    map["给排水"] = new HashSet<long>();
                    ProvisionSelectionsStore.Save(configPath, map);
                    loaded = ProvisionSelectionsStore.Load(configPath);
                    Assert(loaded.ContainsKey("给排水") && loaded["给排水"].Count == 0, "空集合未保留");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            });

            Test("S9 组复选框程序化置反会级联整组（组点击级联是否生效）", () =>
            {
                using (var dlg = new ProvisionPickerDialog("建筑", provisions, null))
                {
                    var tree = Tree(dlg);
                    var g1 = tree.Nodes.Cast<TreeNode>().First(n => n.Text == "《规范A》");
                    g1.Checked = false;   // 模拟点组复选框取消
                    var ids = CheckedLeafIds(dlg);
                    Assert(ids.SetEquals(new long[] { 4, 5 }), $"期望组A整组取消 [4,5]，实际 [{string.Join(",", ids)}]");
                }
            });

            Console.WriteLine();
            Console.WriteLine($"通过 {_passed} 项，失败 {_failed} 项。");
            return _failed == 0 ? 0 : 1;
        }

        // ---------- helpers ----------

        static Provision Prov(long id, string code, string num, string text) => new Provision
        {
            Id = id,
            Discipline = "建筑",
            CodeName = code,
            ClauseNumber = num,
            ClauseText = text,
            DrawingTypesRaw = "",
        };

        static TreeView Tree(ProvisionPickerDialog dlg) =>
            (TreeView)typeof(ProvisionPickerDialog).GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dlg);

        static HashSet<long> CheckedLeafIds(ProvisionPickerDialog dlg)
        {
            var tree = Tree(dlg);
            var set = new HashSet<long>();
            foreach (TreeNode g in tree.Nodes)
                foreach (TreeNode leaf in g.Nodes)
                    if (leaf.Checked)
                        set.Add((long)leaf.Tag);
            return set;
        }

        static void SetLeafChecked(ProvisionPickerDialog dlg, long id, bool on)
        {
            var tree = Tree(dlg);
            foreach (TreeNode g in tree.Nodes)
                foreach (TreeNode leaf in g.Nodes)
                    if ((long)leaf.Tag == id)
                        leaf.Checked = on;
        }

        static object Invoke(ProvisionPickerDialog dlg, string method, params object[] args) =>
            typeof(ProvisionPickerDialog).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dlg, args);

        static void FireFormClosing(ProvisionPickerDialog dlg)
        {
            dlg.DialogResult = DialogResult.OK;
            typeof(ProvisionPickerDialog)
                .GetMethod("OnFormClosing", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(dlg, new object[] { new FormClosingEventArgs(CloseReason.UserClosing, false) });
        }

        static void Test(string name, Action body)
        {
            try
            {
                body();
                Console.WriteLine($"  [通过] {name}");
                _passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [失败] {name}  →  {ex.Message}");
                _failed++;
            }
        }

        static void Assert(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }
    }
}
