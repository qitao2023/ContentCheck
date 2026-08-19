using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ContentCheck.Core.AI;
using ContentCheck.Core.Config;

namespace ContentCheck.Acad.UI
{
    /// <summary>
    /// 大模型设置对话框：服务商 / Base URL / API Key / 快速模型 / 深度模型。
    /// 仿 ProvisionPickerDialog 模式：CenterParent、UiTheme 配色、底部按钮行、静态入口。
    /// </summary>
    public class SettingsDialog : Form
    {
        readonly string _configPath;
        AppConfig _saved;

        // 控件
        readonly ComboBox _cbProvider = new ComboBox();
        readonly TextBox _txtBaseUrl = new TextBox();
        readonly TextBox _txtApiKey = new TextBox();
        readonly CheckBox _chkShowKey = new CheckBox();
        readonly ComboBox _cbModel = new ComboBox();
        readonly Button _btnTest = new Button();
        readonly Button _btnSave = new Button();
        readonly Button _btnCancel = new Button();

        // 测试结果面板
        readonly Panel _pnlTestResult = new Panel();
        readonly FlowLayoutPanel _flpResults = new FlowLayoutPanel();
        CancellationTokenSource _testCts;

        public SettingsDialog(string configPath, AppConfig current)
        {
            _configPath = configPath;

            Text = "大模型设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 400);
            MinimumSize = new Size(480, 360);
            Font = UiTheme.UiFont();
            BackColor = UiTheme.Bg;

            BuildUi();
            InitValues(current);

            // 关闭窗口时取消进行中的测试
            FormClosing += (s, e) => _testCts?.Cancel();
        }

        /// <summary>弹出设置对话框，保存成功返回新 AppConfig，取消返回 null。</summary>
        public static AppConfig Show(IWin32Window owner, string configPath, AppConfig current)
        {
            using (var dlg = new SettingsDialog(configPath, current))
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._saved : null;
        }

        // ---------- UI 构建 ----------

        void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(20, 16, 20, 8),
                BackColor = UiTheme.Bg,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 服务商
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // Base URL
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // API Key
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // 模型
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 测试结果面板
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 弹性空间
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // 按钮行

            int row = 0;

            // 服务商
            root.Controls.Add(MidLabel("服务商"), 0, row);
            var cbProvider = UiTheme.StyleCombo(_cbProvider);
            cbProvider.Dock = DockStyle.Fill;
            cbProvider.Margin = new Padding(0, 4, 0, 4);
            root.Controls.Add(cbProvider, 1, row++);

            // Base URL
            root.Controls.Add(MidLabel("接口地址"), 0, row);
            _txtBaseUrl.Font = UiTheme.UiFont();
            _txtBaseUrl.Dock = DockStyle.Fill;
            _txtBaseUrl.Margin = new Padding(0, 4, 0, 4);
            root.Controls.Add(_txtBaseUrl, 1, row++);

            // API Key
            root.Controls.Add(MidLabel("API Key"), 0, row);
            _txtApiKey.Font = UiTheme.UiFont();
            _txtApiKey.Dock = DockStyle.Fill;
            _txtApiKey.PasswordChar = '●';
            _txtApiKey.Margin = new Padding(0, 4, 0, 4);

            _chkShowKey.Text = "显示";
            _chkShowKey.Font = UiTheme.UiFont();
            _chkShowKey.AutoSize = true;
            _chkShowKey.Margin = new Padding(6, 8, 0, 0);
            _chkShowKey.Cursor = Cursors.Hand;
            _chkShowKey.CheckedChanged += (s, e) => _txtApiKey.PasswordChar = _chkShowKey.Checked ? '\0' : '●';

            // 编辑框占满整行，"显示" 复选框跟右侧
            var keyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, BackColor = UiTheme.Bg };
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            keyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyPanel.Controls.Add(_txtApiKey, 0, 0);
            keyPanel.Controls.Add(_chkShowKey, 1, 0);
            root.Controls.Add(keyPanel, 1, row++);

            // 模型
            root.Controls.Add(MidLabel("模型"), 0, row);
            var cbModel = UiTheme.StyleCombo(_cbModel, editable: true);
            cbModel.Dock = DockStyle.Fill;
            cbModel.Margin = new Padding(0, 4, 0, 4);
            cbModel.AutoCompleteSource = AutoCompleteSource.ListItems;
            cbModel.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            root.Controls.Add(cbModel, 1, row++);

            // 测试结果面板
            BuildTestResultPanel();
            root.Controls.Add(_pnlTestResult, 0, row);
            root.SetColumnSpan(_pnlTestResult, 2);
            row++;

            // 弹性行（占位控件，让 RowStyle.Percent 100 生效）
            var spacer = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty };
            root.Controls.Add(spacer, 0, row++);
            root.SetColumnSpan(spacer, 2);

            // 按钮行
            var btnRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 4, 0, 0) };
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            _btnTest.Text = "测试连接";
            _btnTest.FlatStyle = FlatStyle.Flat;
            _btnTest.FlatAppearance.BorderColor = UiTheme.Border;
            _btnTest.Cursor = Cursors.Hand;
            _btnTest.Font = UiTheme.UiFont();
            _btnTest.Dock = DockStyle.Fill;
            _btnTest.Margin = new Padding(0, 4, 0, 4);
            _btnTest.Click += OnTest;

            UiTheme.StyleButton(_btnSave, UiTheme.ButtonKind.Primary, "保存");
            _btnSave.Dock = DockStyle.Fill;
            _btnSave.Margin = new Padding(4, 4, 4, 4);
            UiTheme.StyleButton(_btnCancel, UiTheme.ButtonKind.Secondary, "取消");
            _btnCancel.Dock = DockStyle.Fill;
            _btnCancel.Margin = new Padding(4, 4, 0, 4);

            btnRow.Controls.Add(_btnTest, 0, 0);
            btnRow.Controls.Add(new Label(), 1, 0);
            btnRow.Controls.Add(_btnSave, 2, 0);
            btnRow.Controls.Add(_btnCancel, 3, 0);
            root.Controls.Add(btnRow, 0, row);
            root.SetColumnSpan(btnRow, 2);

            Controls.Add(root);

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;

            _cbProvider.SelectedIndexChanged += (s, e) =>
            {
                if (_cbProvider.SelectedItem is AiProviderPreset p)
                    ApplyPreset(p);
            };
        }

        void BuildTestResultPanel()
        {
            _pnlTestResult.Dock = DockStyle.Top;
            _pnlTestResult.AutoSize = true;
            _pnlTestResult.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _pnlTestResult.BackColor = UiTheme.Card;
            _pnlTestResult.Padding = new Padding(12, 8, 12, 8);
            _pnlTestResult.Margin = new Padding(0, 4, 0, 4);
            _pnlTestResult.Visible = false;

            _flpResults.Dock = DockStyle.Top;
            _flpResults.AutoSize = true;
            _flpResults.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _flpResults.FlowDirection = FlowDirection.TopDown;
            _flpResults.WrapContents = false;
            _flpResults.Margin = Padding.Empty;

            _pnlTestResult.Controls.Add(_flpResults);
        }

        /// <summary>向结果面板追加一行结果。</summary>
        void AddResultLine(string label, TestResult result)
        {
            var line = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
            };

            var icon = new Label
            {
                Text = result.Success ? "✓" : "✗",
                ForeColor = result.Success ? UiTheme.VerdictOk : UiTheme.VerdictBad,
                Font = UiTheme.UiFontBold(),
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0),
            };

            var text = new Label
            {
                Text = result.Success
                    ? $"{label}  延迟 {result.LatencyMs}ms  响应: \"{Truncate(result.Reply, 30)}\""
                    : $"{label}  失败: {Truncate(result.Error, 60)}",
                ForeColor = result.Success ? UiTheme.VerdictOk : UiTheme.VerdictBad,
                Font = UiTheme.UiFont(),
                AutoSize = true,
                Margin = Padding.Empty,
            };

            line.Controls.Add(icon);
            line.Controls.Add(text);
            _flpResults.Controls.Add(line);
        }

        /// <summary>在结果面板显示一条"测试中"占位行，返回该 Label 供后续替换。</summary>
        Label AddPendingLine(string label)
        {
            var line = new FlowLayoutPanel
            {
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Tag = label,
            };

            var icon = new Label
            {
                Text = "⏳",
                Font = UiTheme.UiFont(),
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0),
            };

            var text = new Label
            {
                Text = $"{label}  测试中…",
                ForeColor = UiTheme.TextMuted,
                Font = UiTheme.UiFont(),
                AutoSize = true,
                Margin = Padding.Empty,
            };

            line.Controls.Add(icon);
            line.Controls.Add(text);
            _flpResults.Controls.Add(line);
            return text;
        }

        /// <summary>替换"测试中"占位行为最终结果。</summary>
        void ReplacePendingLine(Label pendingLabel, string label, TestResult result)
        {
            pendingLabel.Text = result.Success
                ? $"{label}  延迟 {result.LatencyMs}ms  响应: \"{Truncate(result.Reply, 30)}\""
                : $"{label}  失败: {Truncate(result.Error, 60)}";
            pendingLabel.ForeColor = result.Success ? UiTheme.VerdictOk : UiTheme.VerdictBad;

            // 替换图标
            var line = pendingLabel.Parent as FlowLayoutPanel;
            if (line?.Controls.Count > 0 && line.Controls[0] is Label icon)
            {
                icon.Text = result.Success ? "✓" : "✗";
                icon.ForeColor = result.Success ? UiTheme.VerdictOk : UiTheme.VerdictBad;
                icon.Font = UiTheme.UiFontBold();
            }
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        static Label MidLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = UiTheme.TextMain,
                Font = UiTheme.UiFont(),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            };
        }

        // ---------- 初始化 ----------

        void InitValues(AppConfig current)
        {
            // 服务商下拉
            foreach (var preset in AiProviderPresets.All)
                _cbProvider.Items.Add(preset);

            // 匹配当前 provider
            int selIdx = -1;
            for (int i = 0; i < AiProviderPresets.All.Length; i++)
            {
                if (string.Equals(AiProviderPresets.All[i].Key, current.Provider, StringComparison.OrdinalIgnoreCase))
                { selIdx = i; break; }
            }
            if (selIdx < 0) selIdx = AiProviderPresets.All.Length - 1; // custom
            _cbProvider.SelectedIndex = selIdx;

            // 用当前值填
            var currentPreset = AiProviderPresets.Find(current.Provider);
            bool isCustom = selIdx == AiProviderPresets.All.Length - 1
                && !string.Equals(currentPreset.Key, current.Provider, StringComparison.OrdinalIgnoreCase);
            if (isCustom)
            {
                _txtBaseUrl.Text = current.BaseUrl ?? "";
                _txtApiKey.Text = ConfigWriter.ReadRawApiKey(_configPath);
                SetModelCombo(_cbModel, null, current.Model);
            }
            else
            {
                _txtBaseUrl.Text = currentPreset.BaseUrl ?? "";
                _txtApiKey.Text = ConfigWriter.ReadRawApiKey(_configPath);
                SetModelCombo(_cbModel, currentPreset.SuggestedModels, current.Model);
            }
        }

        void ApplyPreset(AiProviderPreset preset)
        {
            if (preset == null) return;
            _txtBaseUrl.Text = preset.BaseUrl ?? "";
            SetModelCombo(_cbModel, preset.SuggestedModels, preset.Model);
        }

        static void SetModelCombo(ComboBox cb, string[] suggested, string current)
        {
            cb.Items.Clear();
            if (suggested != null)
                foreach (var m in suggested)
                    if (!string.IsNullOrWhiteSpace(m) && !cb.Items.Contains(m))
                        cb.Items.Add(m);
            if (!string.IsNullOrWhiteSpace(current) && !cb.Items.Contains(current))
                cb.Items.Add(current);
            cb.Text = current ?? "";
        }

        // ---------- 测试连接 ----------

        async void OnTest(object sender, EventArgs e)
        {
            var providerKey = SelectedProviderKey();
            var apiKey = ConfigLoader.ResolveApiKey(_txtApiKey.Text.Trim());
            var baseUrl = _txtBaseUrl.Text.Trim();
            var model = _cbModel.Text.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                MessageBox.Show(this, "请先填写接口地址。", "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show(this, "请先填写模型名称。", "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 准备结果面板
            _flpResults.Controls.Clear();
            _pnlTestResult.Visible = true;
            _btnTest.Enabled = false;
            _btnTest.Text = "测试中…";

            // 取消上次测试
            _testCts?.Cancel();
            _testCts = new CancellationTokenSource();
            var ct = _testCts.Token;

            var temp = new AppConfig
            {
                Provider = providerKey,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                Model = model,
            };

            var pendingLabel = AddPendingLine(model);

            try
            {
                var client = new DeepSeekClient(temp) { Model = model };
                var result = await client.TestConnectionDetailedAsync(ct);
                ReplacePendingLine(pendingLabel, model, result);
            }
            catch (OperationCanceledException)
            {
                pendingLabel.Text = $"{model}  已取消";
                pendingLabel.ForeColor = UiTheme.TextMuted;
            }
            catch (Exception ex)
            {
                ReplacePendingLine(pendingLabel, model, new TestResult
                {
                    Success = false,
                    ModelName = model,
                    Error = ex.Message,
                });
            }
            finally
            {
                _btnTest.Enabled = true;
                _btnTest.Text = "测试连接";
            }
        }

        // ---------- 保存 ----------

        void btnSave_Click(object sender, EventArgs e)
        {
            var baseUrl = _txtBaseUrl.Text.Trim();
            var model = _cbModel.Text.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                MessageBox.Show(this, "请填写接口地址。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtBaseUrl.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show(this, "请填写模型名称。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _cbModel.Focus();
                return;
            }

            try
            {
                var settings = new AiSettings
                {
                    Provider = SelectedProviderKey(),
                    ApiKey = _txtApiKey.Text.Trim(),
                    BaseUrl = baseUrl,
                    Model = model,
                };
                _saved = ConfigWriter.SaveAiSettings(_configPath, settings);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：\n" + ex.Message, "保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        string SelectedProviderKey()
        {
            if (_cbProvider.SelectedItem is AiProviderPreset p) return p.Key;
            return AiProviderPresets.DefaultProvider;
        }
    }
}
