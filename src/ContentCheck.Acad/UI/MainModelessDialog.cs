using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using ContentCheck.Acad.Dwg;
using ContentCheck.Acad.Report;
using ContentCheck.Core.AI;
using ContentCheck.Core.Config;
using ContentCheck.Core.Models;
using ContentCheck.Core.Services;
using ContentCheck.Core.Storage;

namespace ContentCheck.Acad.UI
{
    /// <summary>
    /// 校核非模态对话框：专业下拉（单选）+ "…" 按钮选择要校核的条文，模型空间文字，结果表。
    /// 线程纪律：AutoCAD 对象只在本控件（UI 线程）访问；校核在 Task.Run 工作线程，只碰 Core 类型。
    /// </summary>
    public class MainModelessDialog : Form
    {
        readonly ComboBox _cbDisc = new ComboBox();
        Button _btnProv = new Button();
        Button _btnRun = new Button();
        Button _btnSelectArea = new Button();  // 框选区域按钮
        Button _btnReport = new Button();
        readonly Button _btnSettings = new Button();
        readonly Label _lblStatus = new Label();
        readonly ProgressBar _progress = new ProgressBar();
        readonly Label _lblPreviewTitle = new Label();
        readonly RichTextBox _txtPreview = new RichTextBox();
        readonly DataGridView _grid = new DataGridView();

        // 结论统计条
        readonly FlowLayoutPanel _flowSummary = new FlowLayoutPanel();
        readonly Label _lblTotal = new Label();
        Label _lblOk, _lblBad, _lblNa, _lblUnk;

        /// <summary>当前图纸的模型空间文字（校核数据源）。</summary>
        DrawingSheet _modelSheet;

        List<VerdictResult> _results = new List<VerdictResult>();
        string _resultSheetName = "";

        /// <summary>每个专业的条文勾选：由 ProvisionSelectionState 统一持有并落盘（null = 默认全选）。</summary>

        CancellationTokenSource _cts;
        bool _running;

        // 校核进行中的实时计时（每秒刷新状态栏"已用时间"，让用户能判断是否卡死）
        readonly System.Windows.Forms.Timer _elapsedTimer = new System.Windows.Forms.Timer();
        readonly System.Diagnostics.Stopwatch _runSw = new System.Diagnostics.Stopwatch();
        string _lastStatus = "";

        sealed class RowTag
        {
            public string SheetName;
            public string Evidence;
        }

        sealed class UiProgress : IProgress<string>
        {
            readonly Control _c;
            readonly Action<string> _cb;
            public UiProgress(Control c, Action<string> cb) { _c = c; _cb = cb; }
            public void Report(string value)
            {
                try { _c.BeginInvoke(new Action(() => _cb(value))); }
                catch { }
            }
        }

        public MainModelessDialog()
        {
            // 保证配置已初始化（NETLOAD 后立即输入 CHECK 时 Idle 可能尚未触发）
            if (!PluginEnv.InitOk)
            {
                try { PluginEnv.Init(); }
                catch { }
            }
            BuildUi();
            WireEvents();
            ReloadData();
            
            // 设置非模态对话框属性
            this.Text = "图纸总说明规范校核";
            this.Size = new Size(520, 800);
            this.MinimumSize = new Size(480, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.FormClosing += MainModelessDialog_FormClosing;
        }

        private void MainModelessDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 如果是用户点击关闭按钮，隐藏而不是关闭
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        public void ReloadData()
        {
            // 无条件先加载专业下拉（含默认「建筑」兜底），不依赖图纸文字提取结果
            LoadDisciplines();

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || doc.Database == null)
            {
                SetStatus("无打开的图纸。");
                return;
            }

            try
            {
                _modelSheet = DwgTextExtractor.ExtractModel(doc);
                if (_modelSheet == null || _modelSheet.TextLines.Count == 0)
                {
                    UpdatePreview(null);
                    SetStatus("未提取到模型空间文字。");
                    return;
                }
                UpdatePreview(_modelSheet);
                SetStatus($"已提取模型空间文字（{_modelSheet.TextLines.Count} 行）。");
            }
            catch (Exception ex)
            {
                SetStatus("提取模型空间文字失败：" + ex.Message);
            }
        }

        void LoadDisciplines()
        {
            _cbDisc.Items.Clear();
            string[] disciplines;
            try
            {
                if (!PluginEnv.InitOk || PluginEnv.Config == null)
                    throw new InvalidOperationException("配置未初始化");

                var store = new SqliteProvisionStore(PluginEnv.Config.DbPath);
                disciplines = store.GetDistinctDisciplines();
                if (disciplines.Length == 0)
                    disciplines = new[] { "建筑", "给排水", "暖通", "国网" };   // 库为空时兜底
            }
            catch (Exception ex)
            {
                disciplines = new[] { "建筑", "给排水", "暖通", "国网" };
                SetStatus("读取条文数据库失败：" + ex.Message + "（请先运行 Import 或 CC_IMPORT）");
            }

            foreach (var d in disciplines)
                _cbDisc.Items.Add(d);

            // 默认选「建筑」；库中没有建筑则选第一项
            int idx = Array.IndexOf(disciplines, "建筑");
            _cbDisc.SelectedIndex = idx >= 0 ? idx : 0;
        }

        // ---------- 条文选择 ----------

        void btnProv_Click(object sender, EventArgs e)
        {
            var discipline = _cbDisc.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(discipline))
            {
                SetStatus("请先选择专业。");
                return;
            }

            try
            {
                var store = new SqliteProvisionStore(PluginEnv.Config.DbPath);
                var all = store.QueryByDisciplines(new[] { discipline }, null);
                if (all.Count == 0)
                {
                    SetStatus("该专业没有条文。");
                    return;
                }

                var current = ProvisionSelectionState.Get(discipline);
                var res = ProvisionPickerDialog.Pick(this, discipline, all, current);
                if (!ReferenceEquals(res.SelectedIds, current))
                    ProvisionSelectionState.Set(discipline, res.SelectedIds);

                if (res.WriteProvision != null)
                {
                    SetStatus($"已选 {res.SelectedIds.Count}/{all.Count} 条条文。请在图纸中指定插入点…");
                    WriteProvisionToCad(res.WriteProvision);
                    return;
                }

                SetStatus($"已选择 {res.SelectedIds.Count}/{all.Count} 条条文（{discipline}）。");
            }
            catch (Exception ex)
            {
                SetStatus("读取条文失败：" + ex.Message);
            }
        }

        // ---------- 框选区域 ----------

        /// <summary>
        /// 点击「框选文字」：通过 SendStringToExecute 在 AutoCAD 命令循环中执行 CC_SELECTTEXT。
        /// 不能在 WinForms 事件里直接调 Editor.GetSelection()，那会阻塞 UI 线程导致无响应。
        /// </summary>
        void btnSelectArea_Click(object sender, EventArgs e)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                SetStatus("无打开的图纸。");
                return;
            }

            // 把焦点交还给 AutoCAD，让命令提示在绘图区显示
            AcadApp.MainWindow.Focus();
            doc.SendStringToExecute("CC_SELECTTEXT\n", true, false, false);
        }

        /// <summary>供 CC_SELECTTEXT 命令回调：把框选提取到的文字设为校核数据源。</summary>
        public void ApplySelectedSheet(DrawingSheet sheet)
        {
            if (sheet == null) return;
            try
            {
                // 命令在 AutoCAD 线程执行，回到面板需封送 UI 线程
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => ApplySelectedSheet(sheet)));
                    return;
                }
                _modelSheet = sheet;
                UpdatePreview(sheet);
                SetStatus($"已提取框选区域文字（{sheet.TextLines.Count} 行），可以开始校核。");
            }
            catch (Exception ex)
            {
                SetStatus("框选结果应用失败：" + ex.Message);
            }
        }

        // ---------- 写入 CAD ----------

        /// <summary>
        /// 把选中的条文交给 CC_WRITECLAUSE 命令写入图纸。
        /// 不能直接在模态对话框里取插入点，复用「框选文字」的模式：
        /// 焦点交还 AutoCAD，通过 SendStringToExecute 让命令提示用户点选插入点。
        /// </summary>
        void WriteProvisionToCad(Provision provision)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                SetStatus("无打开的图纸。");
                return;
            }
            Commands.PendingWriteProvision = provision;
            AcadApp.MainWindow.Focus();
            doc.SendStringToExecute("CC_WRITECLAUSE\n", true, false, false);
        }

        // ---------- 运行 ----------

        async void btnRun_Click(object sender, EventArgs e)
        {
            if (_running)
            {
                _cts?.Cancel();
                return;
            }

            var cfg = PluginEnv.Config;
            if (string.IsNullOrWhiteSpace(cfg.ApiKey) && AiProviderPresets.Find(cfg.Provider).RequiresKey)
            {
                MessageBox.Show(this, "未配置 API key，请在设置中填写。", "ContentCheck",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var discipline = _cbDisc.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(discipline))
            {
                SetStatus("请先选择专业。");
                return;
            }
            if (_modelSheet == null || string.IsNullOrWhiteSpace(_modelSheet.FullText))
            {
                SetStatus("未提取到模型空间文字，请刷新图纸。");
                return;
            }

            // 条文：默认该专业全部；若用户用 … 勾选过则按其选择
            List<Provision> provisions;
            try
            {
                var store = new SqliteProvisionStore(cfg.DbPath);
                var all = store.QueryByDisciplines(new[] { discipline }, null);
                provisions = all;
                var sel = ProvisionSelectionState.Get(discipline);
                if (sel != null)
                    provisions = all.Where(p => sel.Contains(p.Id)).ToList();
            }
            catch (Exception ex)
            {
                SetStatus("读取条文失败：" + ex.Message);
                return;
            }
            if (provisions.Count == 0)
            {
                SetStatus("未选择任何条文，请点击「…」勾选。");
                return;
            }

            string model = cfg.Model;

            _running = true;
            _btnRun.Text = "取消";
            _grid.Rows.Clear();
            _results.Clear();
            _resultSheetName = _modelSheet.Name;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _cts = new CancellationTokenSource();
            _lastStatus = "";
            _runSw.Restart();
            _elapsedTimer.Start();
            var progress = new UiProgress(this, s => { _lastStatus = s; _lblStatus.Text = s; });

            var sheetName = _modelSheet.Name;
            var sheetText = string.IsNullOrWhiteSpace(_modelSheet.SegmentedText)
                ? _modelSheet.FullText
                : _modelSheet.SegmentedText;
            CheckEngine.RunResult result = null;
            string error = null;
            try
            {
                result = await Task.Run(() => RunCore(cfg, provisions, sheetName, sheetText, model, progress, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                error = "已取消。";
            }
            catch (System.Exception ex)
            {
                error = "校核失败：" + ex.Message;
            }

            // 显式封送回 UI 线程（不依赖 AutoCAD 线程的 SynchronizationContext）
            var r = result;
            var err = error;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _elapsedTimer.Stop();
                    _runSw.Stop();
                    if (r != null) FillGrid(r);
                    SetStatus(err ?? $"校核完成（{r?.Results.Count ?? 0} 条）。");
                    _running = false;
                    _btnRun.Text = "开始校核";
                    _progress.Style = ProgressBarStyle.Blocks;
                    _progress.MarqueeAnimationSpeed = 0;
                }));
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>工作线程执行：分批 AI 校核。不触碰 AutoCAD 对象。</summary>
        static CheckEngine.RunResult RunCore(AppConfig cfg, List<Provision> provisions,
            string sheetName, string sheetText, string model, IProgress<string> progress, CancellationToken ct)
        {
            var engine = new CheckEngine();
            return engine.RunAsync(cfg, provisions, sheetName, sheetText, model, cfg.LogDir, progress, ct).GetAwaiter().GetResult();
        }

        void FillGrid(CheckEngine.RunResult result)
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            _results = result.Results;
            int no = 0;
            foreach (var v in result.Results)
            {
                no++;
                var idx = _grid.Rows.Add(
                    no.ToString(), v.Evidence, ResultGridSetup.FormatProvision(v), v.Analysis, v.Suggestion, v.Verdict);
                var row = _grid.Rows[idx];
                ResultGridSetup.ColorRow(row, v.Verdict);
                row.Tag = new RowTag { SheetName = result.SheetName, Evidence = v.Evidence };
            }
            _grid.ResumeLayout();

            int ok = result.Results.Count(x => x.Verdict == AiJsonParser.VERDICT_OK);
            int bad = result.Results.Count(x => x.Verdict == AiJsonParser.VERDICT_BAD);
            int na = result.Results.Count(x => x.Verdict == AiJsonParser.VERDICT_NA);
            int unk = result.Results.Count(x => x.Verdict == AiJsonParser.VERDICT_UNKNOWN);
            UpdateSummary(result.Results.Count, ok, bad, na, unk);
            string trunc = result.SheetTruncated ? "（总说明过长已截断）" : "";
            SetStatus($"校核完成：共 {result.Results.Count} 条。{trunc}");
        }

        // ---------- 双击定位原文 ----------

        void grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var tag = _grid.Rows[e.RowIndex].Tag as RowTag;
            if (tag == null) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || _modelSheet == null) return;

            var handles = FindEvidenceHandles(_modelSheet, tag.Evidence);
            if (handles.Count == 0)
            {
                SetStatus("未在模型空间找到与依据原文匹配的文字。");
                return;
            }
            Highlighter.HighlightHandles(doc, handles);
            SetStatus($"已高亮 {handles.Count} 处文字。");
        }

        static List<string> FindEvidenceHandles(DrawingSheet sheet, string evidence)
        {
            var handles = new List<string>();
            if (string.IsNullOrWhiteSpace(evidence) || evidence == "总说明未提及") return handles;

            // 拆出 ≥6 字的中文片段（引号/顿号/标点分隔），用于在布局文字中定位
            var frags = Regex.Split(evidence, @"[，。；、：:；,.()（）「」『』\s]+")
                .Select(f => f.Trim())
                .Where(f => f.Length >= 6)
                .Distinct()
                .OrderByDescending(f => f.Length)
                .ToList();
            if (frags.Count == 0) return handles;

            // 优先在聚合段落里找（段落文本更完整，命中率更高）；找不到再回退逐行
            if (sheet.Segments != null && sheet.Segments.Count > 0)
            {
                foreach (var seg in sheet.Segments)
                {
                    if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                    if (!frags.Any(f => seg.Text.Contains(f))) continue;

                    foreach (var line in seg.Lines)
                    {
                        if (string.IsNullOrWhiteSpace(line.Handle)) continue;
                        if (!handles.Contains(line.Handle))
                            handles.Add(line.Handle);
                    }
                }
            }

            if (handles.Count == 0)
            {
                foreach (var line in sheet.TextLines)
                {
                    if (string.IsNullOrWhiteSpace(line.Text) || string.IsNullOrWhiteSpace(line.Handle)) continue;
                    if (frags.Any(f => line.Text.Contains(f)))
                        if (!handles.Contains(line.Handle))
                            handles.Add(line.Handle);
                }
            }
            return handles;
        }

        // ---------- 导出报告 ----------

        void btnReport_Click(object sender, EventArgs e)
        {
            if (_results.Count == 0)
            {
                SetStatus("没有可导出的结果，请先运行校核。");
                return;
            }
            using (var dlg = new SaveFileDialog
            {
                Title = "另存校核报告",
                Filter = "Excel 报告 (*.xlsx)|*.xlsx|文本报告 (*.txt)|*.txt",
                FileName = $"图纸总说明校核报告_{_resultSheetName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = "xlsx",
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        ReportExporter.ExportTxt(dlg.FileName, _resultSheetName, _results);
                    else
                        ReportExporter.ExportXlsx(dlg.FileName, _resultSheetName, _results);
                    SetStatus("报告已导出：" + dlg.FileName);
                }
                catch (Exception ex)
                {
                    SetStatus("导出失败：" + ex.Message);
                }
            }
        }

        // ---------- UI 构建 ----------

        void BuildUi()
        {
            BackColor = UiTheme.Bg;
            Dock = DockStyle.Fill;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, BackColor = UiTheme.Bg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));  // 设置卡片
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // 结论统计条
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // 状态
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));   // 进度
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));   // 识别文字标题
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));  // 识别文字预览
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 结果表

            _progress.Dock = DockStyle.Fill;
            _grid.Dock = DockStyle.Fill;

            root.Controls.Add(BuildSettingsCard(), 0, 0);
            root.Controls.Add(BuildSummaryBar(), 0, 1);
            root.Controls.Add(BuildStatusRow(), 0, 2);
            root.Controls.Add(_progress, 0, 3);
            root.Controls.Add(BuildPreviewRow(), 0, 4);
            root.Controls.Add(_txtPreview, 0, 5);
            root.Controls.Add(_grid, 0, 6);

            ResultGridSetup.Configure(_grid);
            Controls.Add(root);
        }

        /// <summary>白卡片：专业 / 操作（设置放在操作行最前，不再占用顶栏一整行）。</summary>
        Control BuildSettingsCard()
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = UiTheme.Card,
                Padding = new Padding(16, 12, 16, 12),
                Margin = Padding.Empty,
            };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));  // 专业
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // 操作按钮一行

            // 校核专业：下拉 + …按钮
            var rowDisc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, BackColor = UiTheme.Card };
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            rowDisc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rowDisc.Controls.Add(MidLabel("校核专业"), 0, 0);
            var cbDisc = UiTheme.StyleCombo(_cbDisc);
            cbDisc.Dock = DockStyle.Fill;
            cbDisc.Margin = new Padding(0, 5, 6, 5);
            rowDisc.Controls.Add(cbDisc, 1, 0);
            _btnProv = UiTheme.StyleButton(_btnProv, UiTheme.ButtonKind.Secondary, "规范条文详细…");
            _btnProv.Dock = DockStyle.Fill;
            _btnProv.Margin = new Padding(0, 5, 0, 5);
            new ToolTip().SetToolTip(_btnProv, "选择要校核的条文（默认全勾）");
            rowDisc.Controls.Add(_btnProv, 2, 0);
            card.Controls.Add(rowDisc, 0, 0);

            // 操作：设置 / 框选文字 / 开始校核 / 另存报告 放在一行
            var rowOps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty, BackColor = UiTheme.Card };
            rowOps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
            rowOps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            rowOps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            rowOps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            rowOps.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            UiTheme.StyleButton(_btnSettings, UiTheme.ButtonKind.Secondary, "设置");
            _btnSelectArea = UiTheme.StyleButton(_btnSelectArea, UiTheme.ButtonKind.Primary, "框选文字");
            _btnRun = UiTheme.StyleButton(_btnRun, UiTheme.ButtonKind.Primary, "开始校核");
            _btnReport = UiTheme.StyleButton(_btnReport, UiTheme.ButtonKind.Secondary, "另存报告");
            _btnSettings.Dock = DockStyle.Fill;
            _btnSelectArea.Dock = DockStyle.Fill;
            _btnRun.Dock = DockStyle.Fill;
            _btnReport.Dock = DockStyle.Fill;
            _btnRun.Font = UiTheme.UiFontBold(9f);
            new ToolTip().SetToolTip(_btnSettings, "大模型设置");
            _btnSettings.Margin = new Padding(0, 6, 4, 0);
            _btnSelectArea.Margin = new Padding(2, 6, 2, 0);
            _btnRun.Margin = new Padding(2, 6, 2, 0);
            _btnReport.Margin = new Padding(4, 6, 0, 0);
            rowOps.Controls.Add(_btnSettings, 0, 0);
            rowOps.Controls.Add(_btnSelectArea, 1, 0);
            rowOps.Controls.Add(_btnRun, 2, 0);
            rowOps.Controls.Add(_btnReport, 3, 0);
            card.Controls.Add(rowOps, 0, 1);

            return card;
        }

        /// <summary>结论统计条：共N条 · 符合n · 不符合n · 未涉及n · 无法判断n。</summary>
        Control BuildSummaryBar()
        {
            _flowSummary.Dock = DockStyle.Fill;
            _flowSummary.FlowDirection = FlowDirection.LeftToRight;
            _flowSummary.WrapContents = false;
            _flowSummary.Padding = new Padding(2, 5, 0, 0);
            _flowSummary.BackColor = UiTheme.Bg;
            _flowSummary.Margin = Padding.Empty;

            _lblTotal.Font = UiTheme.UiFontBold(9f);
            _lblTotal.ForeColor = UiTheme.TextMain;
            _lblTotal.AutoSize = true;
            _lblTotal.Margin = new Padding(0, 2, 0, 0);
            _lblOk = SummaryLabel("符合 0", UiTheme.VerdictOk);
            _lblBad = SummaryLabel("不符合 0", UiTheme.VerdictBad);
            _lblNa = SummaryLabel("未涉及 0", UiTheme.VerdictNa);
            _lblUnk = SummaryLabel("无法判断 0", UiTheme.VerdictUnknown);

            _flowSummary.Controls.Add(_lblTotal);
            _flowSummary.Controls.Add(_lblOk);
            _flowSummary.Controls.Add(_lblBad);
            _flowSummary.Controls.Add(_lblNa);
            _flowSummary.Controls.Add(_lblUnk);
            _flowSummary.Visible = false;
            return _flowSummary;
        }

        static Label SummaryLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                ForeColor = color,
                Font = UiTheme.UiFontBold(9f),
                AutoSize = true,
                Margin = new Padding(8, 2, 0, 0),
            };
        }

        Control BuildStatusRow()
        {
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Text = "就绪。";
            _lblStatus.ForeColor = UiTheme.TextMuted;
            _lblStatus.Font = UiTheme.UiFont(9f);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Margin = new Padding(2, 3, 0, 0);
            return _lblStatus;
        }

        /// <summary>识别文字预览区标题（识别文字 · N 行）。</summary>
        Control BuildPreviewRow()
        {
            _lblPreviewTitle.Dock = DockStyle.Fill;
            _lblPreviewTitle.Text = "识别文字";
            _lblPreviewTitle.ForeColor = UiTheme.TextMuted;
            _lblPreviewTitle.Font = UiTheme.UiFontBold(9f);
            _lblPreviewTitle.TextAlign = ContentAlignment.MiddleLeft;
            _lblPreviewTitle.Margin = new Padding(2, 4, 0, 0);

            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.ReadOnly = true;
            _txtPreview.ScrollBars = RichTextBoxScrollBars.Vertical;
            _txtPreview.WordWrap = false;
            _txtPreview.DetectUrls = false;
            _txtPreview.BackColor = UiTheme.Card;
            _txtPreview.ForeColor = UiTheme.TextMain;
            _txtPreview.Font = UiTheme.UiFont(9f);
            _txtPreview.Margin = new Padding(0, 2, 0, 4);
            _txtPreview.Text = "（暂无识别文字。点击「框选文字」或刷新图纸后在此预览。）";
            return _lblPreviewTitle;
        }

        /// <summary>把提取到的文字填入预览区。</summary>
        void UpdatePreview(DrawingSheet sheet)
        {
            if (sheet == null || sheet.TextLines.Count == 0)
            {
                _lblPreviewTitle.Text = "识别文字";
                _txtPreview.Text = "（暂无识别文字。点击「框选文字」或刷新图纸后在此预览。）";
                return;
            }
            int segCount = sheet.Segments != null ? sheet.Segments.Count : 0;
            _lblPreviewTitle.Text = segCount > 0
                ? $"识别文字（{sheet.TextLines.Count} 行，{segCount} 段）"
                : $"识别文字（{sheet.TextLines.Count} 行）";
            // 有分段时用彩色背景高亮每个段落；否则回退到纯文本
            if (sheet.Segments != null && sheet.Segments.Count > 0)
            {
                _txtPreview.Clear();
                for (int i = 0; i < sheet.Segments.Count; i++)
                {
                    var seg = sheet.Segments[i];
                    var bg = UiTheme.SegmentBg(i);
                    string body = (seg.Text ?? "").Replace("\n", "\r\n");

                    _txtPreview.SelectionBackColor = bg;
                    _txtPreview.SelectionFont = UiTheme.UiFont(9f);
                    _txtPreview.AppendText(body);

                    // 段落之间仅加一个换行做分隔（不是空行）
                    if (i < sheet.Segments.Count - 1)
                    {
                        _txtPreview.SelectionBackColor = UiTheme.Card;
                        _txtPreview.AppendText("\n");
                    }
                }
            }
            else
            {
                _txtPreview.Text = (sheet.FullText ?? "").Replace("\n", "\r\n");
            }
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

        void UpdateSummary(int total, int ok, int bad, int na, int unk)
        {
            _lblTotal.Text = $"共 {total} 条";
            _lblOk.Text = $"符合 {ok}";
            _lblBad.Text = $"不符合 {bad}";
            _lblNa.Text = $"未涉及 {na}";
            _lblUnk.Text = $"无法判断 {unk}";
            _flowSummary.Visible = total > 0;
        }

        void WireEvents()
        {
            _btnProv.Click += btnProv_Click;
            _btnRun.Click += btnRun_Click;
            _btnSelectArea.Click += btnSelectArea_Click;
            _btnReport.Click += btnReport_Click;
            _btnSettings.Click += btnSettings_Click;
            _grid.CellDoubleClick += grid_CellDoubleClick;

            _elapsedTimer.Interval = 1000;
            _elapsedTimer.Tick += (s, e) =>
            {
                var baseMsg = string.IsNullOrEmpty(_lastStatus) ? "校核进行中…" : _lastStatus;
                _lblStatus.Text = $"{baseMsg}（已用 {FormatElapsed(_runSw.Elapsed)}）";
            };
        }

        static string FormatElapsed(System.TimeSpan t)
            => t.TotalMinutes >= 1 ? $"{t.Minutes}分{t.Seconds}秒" : $"{t.Seconds}秒";

        void btnSettings_Click(object sender, EventArgs e)
        {
            if (!PluginEnv.InitOk) return;
            var saved = SettingsDialog.Show(this, PluginEnv.ConfigPath, PluginEnv.Config);
            if (saved != null)
            {
                PluginEnv.Config = saved;
                SetStatus("设置已保存，将在下次校核时生效。");
            }
        }

        void SetStatus(string msg) => _lblStatus.Text = msg;
    }
}