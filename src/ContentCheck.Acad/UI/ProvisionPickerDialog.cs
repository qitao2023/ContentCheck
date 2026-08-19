using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ContentCheck.Core.Models;

namespace ContentCheck.Acad.UI
{
    /// <summary>
    /// 选择要校核的条文：按规范名称分组（TreeView 带复选框），默认全勾。
    /// 勾选/取消分组节点会级联到组内所有条文。
    /// </summary>
    public class ProvisionPickerDialog : Form
    {
        readonly TreeView _tree = new TreeView();
        readonly TextBox _detailBox = new TextBox();
        readonly List<Provision> _provisions;
        readonly HashSet<long> _initial;
        readonly Dictionary<long, Provision> _provisionMap;

        public HashSet<long> SelectedIds { get; private set; } = new HashSet<long>();

        /// <summary>用户点击「写入CAD」时选中的条文；未触发时为 null。</summary>
        public Provision WriteProvision { get; private set; }

        public ProvisionPickerDialog(string discipline, List<Provision> provisions, HashSet<long> initial)
        {
            _provisions = provisions;
            _initial = initial;
            _provisionMap = provisions.ToDictionary(p => p.Id);

            Text = $"选择校核条文 · {discipline}";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1060, 640);
            MinimumSize = new Size(800, 480);
            Font = UiTheme.UiFont();
            BackColor = UiTheme.Bg;

            BuildUi();
            PopulateTree();
            ApplyChecks();
        }

        /// <summary>弹出选择框的结果：勾选的条文 Id（取消时等于传入的 current）+ 需写入 CAD 的条文。</summary>
        public sealed class PickResult
        {
            public HashSet<long> SelectedIds;
            public Provision WriteProvision;
        }

        /// <summary>弹出选择框；取消时 SelectedIds = current。</summary>
        public static PickResult Pick(IWin32Window owner, string discipline, List<Provision> provisions, HashSet<long> current)
        {
            using (var dlg = new ProvisionPickerDialog(discipline, provisions, current))
            {
                var ok = dlg.ShowDialog(owner) == DialogResult.OK;
                return new PickResult
                {
                    SelectedIds = ok ? dlg.SelectedIds : current,
                    WriteProvision = dlg.WriteProvision,
                };
            }
        }

        void BuildUi()
        {
            // 底部按钮
            var btnOk = UiTheme.StyleButton(new Button(), UiTheme.ButtonKind.Primary, "确定");
            var btnCancel = UiTheme.StyleButton(new Button(), UiTheme.ButtonKind.Secondary, "取消");
            btnOk.DialogResult = DialogResult.OK;
            btnCancel.DialogResult = DialogResult.Cancel;
            var btnAll = UiTheme.StyleButton(new Button(), UiTheme.ButtonKind.Secondary, "全选");
            var btnNone = UiTheme.StyleButton(new Button(), UiTheme.ButtonKind.Secondary, "全不选");
            btnOk.Dock = btnCancel.Dock = btnAll.Dock = btnNone.Dock = DockStyle.Fill;

            var btnRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 60, ColumnCount = 5, Padding = new Padding(8, 8, 8, 8) };
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            btnAll.Margin = new Padding(0, 4, 8, 4);
            btnNone.Margin = new Padding(0, 4, 8, 4);
            btnOk.Margin = new Padding(0, 4, 8, 4);
            btnCancel.Margin = new Padding(0, 4, 0, 4);
            btnRow.Controls.Add(new Label(), 0, 0);
            btnRow.Controls.Add(btnAll, 1, 0);
            btnRow.Controls.Add(btnNone, 2, 0);
            btnRow.Controls.Add(btnOk, 3, 0);
            btnRow.Controls.Add(btnCancel, 4, 0);

            // 树
            _tree.Dock = DockStyle.Fill;
            _tree.CheckBoxes = true;
            _tree.HideSelection = false;
            _tree.Font = UiTheme.UiFont();
            _tree.BackColor = Color.White;
            _tree.BorderStyle = BorderStyle.FixedSingle;
            _tree.FullRowSelect = true;
            _tree.ShowLines = true;
            _tree.Padding = new Padding(0, 48, 0, 0);  // 往下移动约两行文字
            _tree.AfterCheck += OnAfterCheck;
            _tree.AfterSelect += OnAfterSelect;

            // 右侧详情面板
            var detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                BackColor = UiTheme.Bg,
            };

            // 详情头部：标题 + 「写入CAD」按钮
            var detailLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "条文内容",
                ForeColor = UiTheme.TextMuted,
                Font = UiTheme.UiFontBold(9f),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var btnWriteCad = UiTheme.StyleButton(new Button(), UiTheme.ButtonKind.Primary, "写入CAD");
            btnWriteCad.Dock = DockStyle.Fill;
            btnWriteCad.Margin = new Padding(8, 0, 0, 0);
            new ToolTip().SetToolTip(btnWriteCad, "把左侧选中的条文以多行文字写入图纸（字高 300，行宽 10000）");
            btnWriteCad.Click += (s, e) => OnWriteCad();

            var detailHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                ColumnCount = 2,
                Margin = Padding.Empty,
                BackColor = UiTheme.Bg,
            };
            detailHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detailHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            detailHeader.Controls.Add(detailLabel, 0, 0);
            detailHeader.Controls.Add(btnWriteCad, 1, 0);

            _detailBox.Dock = DockStyle.Fill;
            _detailBox.Multiline = true;
            _detailBox.ReadOnly = true;
            _detailBox.ScrollBars = ScrollBars.Vertical;
            _detailBox.Font = UiTheme.UiFont();
            _detailBox.BackColor = Color.White;
            _detailBox.BorderStyle = BorderStyle.FixedSingle;
            _detailBox.WordWrap = true;
            _detailBox.Text = "← 请在左侧选择一条条文";

            detailPanel.Controls.Add(_detailBox);
            detailPanel.Controls.Add(detailHeader);

            // 左右分栏
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 500,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Splitter,
            };
            split.Panel1.Controls.Add(_tree);
            split.Panel2.Controls.Add(detailPanel);

            // 左右等宽：初始及窗口缩放都保持 50/50
            split.SplitterMoved += (s, e) => BalanceSplitter(split);
            Shown += (s, e) => BalanceSplitter(split);
            Resize += (s, e) => BalanceSplitter(split);

            Controls.Add(btnRow);
            Controls.Add(split);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            btnAll.Click += (s, e) => SetAll(true);
            btnNone.Click += (s, e) => SetAll(false);
        }

        void PopulateTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            foreach (var g in _provisions.GroupBy(p => p.CodeName).OrderBy(g => g.Key))
            {
                var groupNode = new TreeNode(g.Key) { Tag = null };
                foreach (var p in g)
                {
                    // 跳过没有条文编号的记录（规范名称、章节标题、注释等）
                    if (string.IsNullOrWhiteSpace(p.ClauseNumber))
                        continue;

                    var num = p.ClauseNumber;
                    var text = OneLine(p.ClauseText, 30);
                    var types = string.IsNullOrWhiteSpace(p.DrawingTypesRaw) ? "" : $"（{p.DrawingTypesRaw}）";
                    groupNode.Nodes.Add(new TreeNode(OneLine($"{num}  {text}{types}", 30)) { Tag = p.Id });
                }
                // 只添加有子节点的分组
                if (groupNode.Nodes.Count > 0)
                    _tree.Nodes.Add(groupNode);
            }
            _tree.EndUpdate();
            _tree.ExpandAll();
        }

        void ApplyChecks()
        {
            _tree.BeginUpdate();
            foreach (TreeNode group in _tree.Nodes)
            {
                foreach (TreeNode leaf in group.Nodes)
                {
                    bool on = _initial.Count == 0 || _initial.Contains((long)leaf.Tag);
                    leaf.Checked = on;
                }
                SyncGroup(group);
            }
            _tree.EndUpdate();
        }

        void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (e.Action != TreeViewAction.Unknown) return;
            if (e.Node.Tag == null && e.Node.Nodes.Count > 0)
                SetGroup(e.Node, e.Node.Checked);
        }

        void OnAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is long id && _provisionMap.TryGetValue(id, out var p))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"【{p.CodeName}】");
                if (!string.IsNullOrWhiteSpace(p.ClauseNumber))
                    sb.AppendLine($"条文编号：{p.ClauseNumber}");
                if (!string.IsNullOrWhiteSpace(p.DrawingTypesRaw))
                    sb.AppendLine($"图纸类型：{p.DrawingTypesRaw}");
                sb.AppendLine();
                sb.AppendLine(p.ClauseText ?? "(无内容)");
                _detailBox.Text = sb.ToString();
                _detailBox.SelectionStart = 0;
                _detailBox.SelectionLength = 0;
            }
            else if (e.Node?.Tag == null && e.Node.Nodes.Count > 0)
            {
                // 点击分组节点，显示组内条文数量
                _detailBox.Text = $"【{e.Node.Text}】\n\n共 {e.Node.Nodes.Count} 条条文";
            }
        }

        /// <summary>「写入CAD」：把左侧当前选中的条文交给命令写入图纸，并关闭对话框。</summary>
        void OnWriteCad()
        {
            var node = _tree.SelectedNode;
            if (node?.Tag is long id && _provisionMap.TryGetValue(id, out var p))
            {
                WriteProvision = p;
                DialogResult = DialogResult.OK;   // 关闭并把当前勾选返回给面板
                Close();
            }
            else
            {
                _detailBox.Text = "请先在左侧选择一条条文，再点击「写入CAD」。";
            }
        }

        void SetGroup(TreeNode group, bool on)
        {
            _tree.BeginUpdate();
            foreach (TreeNode leaf in group.Nodes)
                leaf.Checked = on;
            _tree.EndUpdate();
        }

        void SetAll(bool on)
        {
            _tree.BeginUpdate();
            foreach (TreeNode group in _tree.Nodes)
            {
                foreach (TreeNode leaf in group.Nodes) leaf.Checked = on;
                SyncGroup(group);
            }
            _tree.EndUpdate();
        }

        void SyncGroup(TreeNode group)
        {
            bool allOn = group.Nodes.Count > 0 && group.Nodes.Cast<TreeNode>().All(n => n.Checked);
            group.Checked = allOn;
        }

        /// <summary>把左右分栏拉到 50% 等宽，并夹在合法范围内。</summary>
        void BalanceSplitter(SplitContainer split)
        {
            if (split.Width <= 0) return;
            int target = (split.Width - split.SplitterWidth) / 2;
            int max = split.Width - split.SplitterWidth - split.Panel2MinSize;
            if (max < split.Panel1MinSize) max = split.Panel1MinSize;
            target = Math.Max(split.Panel1MinSize, Math.Min(max, target));
            if (split.SplitterDistance != target)
                split.SplitterDistance = target;
        }

        static string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var line = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return line.Length <= max ? line : line.Substring(0, max) + "…";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                SelectedIds = _tree.Nodes
                    .Cast<TreeNode>()
                    .SelectMany(g => g.Nodes.Cast<TreeNode>())
                    .Where(n => n.Checked)
                    .Select(n => (long)n.Tag)
                    .ToHashSet();
            }
            base.OnFormClosing(e);
        }
    }
}
