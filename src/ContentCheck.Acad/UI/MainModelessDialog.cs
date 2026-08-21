using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using AcadColor = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.Geometry;
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

        // 结论统计条（可点击筛选）
        readonly FlowLayoutPanel _flowSummary = new FlowLayoutPanel();
        UiTheme.VerdictChip _chipTotal, _chipOk, _chipBad, _chipNa, _chipUnk;
        string _activeFilter = null;  // 当前筛选的结论类型（null = 不筛选）

        /// <summary>当前图纸的模型空间文字（校核数据源）。</summary>
        DrawingSheet _modelSheet;

        /// <summary>当前红色标记框（下次点击时先删旧框再画新框）。</summary>
        ObjectId _highlightBoxId = ObjectId.Null;

        /// <summary>当前颜色高亮的实体 Handle 及其原始颜色（用于切换时恢复）。</summary>
        string _colorHighlightHandle;
        AcadColor _colorHighlightOrigColor;

        /// <summary>识别文字预览：字符区间 → 文字行的映射（点击预览行 → CAD 定位文字及关联实体）。</summary>
        readonly List<PreviewLineSpan> _previewSpans = new List<PreviewLineSpan>();

        /// <summary>当前预览区蓝色高亮的行及其原始背景色（切换时先恢复上一行）。</summary>
        PreviewLineSpan _previewHighlightSpan;
        Color _previewHighlightOrigBg = Color.White;

        List<VerdictResult> _results = new List<VerdictResult>();
        string _resultSheetName = "";

        /// <summary>每个专业的条文勾选：由 ProvisionSelectionState 统一持有并落盘（null = 默认全选）。</summary>

        CancellationTokenSource _cts;
        bool _running;

        // 校核进行中的实时计时（每秒刷新状态栏"已用时间"，让用户能判断是否卡死）
        readonly System.Windows.Forms.Timer _elapsedTimer = new System.Windows.Forms.Timer();
        readonly System.Diagnostics.Stopwatch _runSw = new System.Diagnostics.Stopwatch();
        string _lastStatus = "";

        /// <summary>预览区中一个文字行的字符区间及其对应的 TextLine。</summary>
        sealed class PreviewLineSpan
        {
            public readonly int Start;
            public readonly int Length;
            public readonly TextLine Line;
            public PreviewLineSpan(int start, int length, TextLine line)
            {
                Start = start;
                Length = length;
                Line = line;
            }
        }

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
            this.Size = new Size(1040, 800);
            this.MinimumSize = new Size(960, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.FormClosing += MainModelessDialog_FormClosing;
            this.Load += MainModelessDialog_Load;
        }

        private void MainModelessDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 如果是用户点击关闭按钮，隐藏而不是关闭
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            // 关闭时删掉残留的红色标记框
            EraseHighlightBox();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        const int GWL_HWNDPARENT = -8;

        /// <summary>AutoCAD 主窗口句柄，用于 ClampToOwner 计算边界。</summary>
        IntPtr _ownerHwnd;

        /// <summary>
        /// 设置 Owner 为 AutoCAD 主窗口，使对话框始终限制在 AutoCAD 范围内，
        /// 不会跑到其他软件上方。
        /// </summary>
        void MainModelessDialog_Load(object sender, EventArgs e)
        {
            try
            {
                _ownerHwnd = AcadApp.MainWindow.Handle;
                if (_ownerHwnd != IntPtr.Zero)
                    SetWindowLongPtr(this.Handle, GWL_HWNDPARENT, _ownerHwnd);
            }
            catch { /* 非致命，降级为普通浮动窗口 */ }
        }

        /// <summary>把本窗口限制在 AutoCAD 主窗口的客户区内。</summary>
        void ClampToOwner()
        {
            if (_ownerHwnd == IntPtr.Zero) return;
            var ownerForm = Form.FromHandle(_ownerHwnd);
            if (ownerForm == null) return;

            var r = ownerForm.ClientRectangle;
            var ownerScreen = ownerForm.PointToScreen(new Point(r.X, r.Y));
            var screenRect = new Rectangle(ownerScreen.X, ownerScreen.Y, r.Width, r.Height);

            // 如果本窗口完全在 Owner 外面，拉回来
            var winRect = this.DesktopBounds;
            if (!screenRect.Contains(winRect))
            {
                int x = Math.Max(screenRect.X, Math.Min(winRect.X, screenRect.Right - this.Width));
                int y = Math.Max(screenRect.Y, Math.Min(winRect.Y, screenRect.Bottom - this.Height));
                this.Location = new Point(x, y);
            }
        }

        void EraseHighlightBox()
        {
            if (_highlightBoxId.IsNull || !_highlightBoxId.IsValid) return;
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(_highlightBoxId, OpenMode.ForWrite, true) as Entity;
                    if (ent != null && !ent.IsErased) ent.Erase();
                    tr.Commit();
                }
            }
            catch { }
            _highlightBoxId = ObjectId.Null;
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
                int bound = _modelSheet.TextLines
                    .SelectMany(l => l.BoundEntities)
                    .Select(b => b.Handle)
                    .Distinct()
                    .Count();
                // 调试：显示关联实体的类型统计
                var boundTypes = _modelSheet.TextLines
                    .SelectMany(l => l.BoundEntities)
                    .GroupBy(b => b.DxfName)
                    .Select(g => $"{g.Key}:{g.Count()}")
                    .ToList();
                var typeInfo = boundTypes.Count > 0 ? $" [{string.Join(", ", boundTypes.Take(5))}]" : "";
                SetStatus($"已提取模型空间文字（{_modelSheet.TextLines.Count} 行，关联 {bound} 个图面实体{typeInfo}）。");
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
                int bound = sheet.TextLines
                    .SelectMany(l => l.BoundEntities)
                    .Select(b => b.Handle)
                    .Distinct()
                    .Count();
                SetStatus($"已提取框选区域文字（{sheet.TextLines.Count} 行，关联 {bound} 个实体），可以开始校核。");
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
            _activeFilter = null;   // 新一轮校核重置筛选
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
            UpdateSummaryHighlight();
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

            var lines = EvidenceLocator.FindLines(_modelSheet, tag.Evidence);
            if (lines.Count == 0)
            {
                SetStatus("未在模型空间找到与依据原文匹配的文字。");
                return;
            }

            // 文字实体 + 自动空间绑定的关联实体一起选中（文字 ↔ 图面实体绑定）
            var textHandles = lines
                .Select(l => l.Handle)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .ToList();
            var boundHandles = lines
                .SelectMany(l => l.BoundEntities)
                .Select(b => b.Handle)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .ToList();

            Highlighter.HighlightHandles(doc, textHandles.Concat(boundHandles));

            // 颜色高亮：恢复上一个 → 标红当前（第一条匹配文字）
            RestoreColorHighlight(doc);
            if (textHandles.Count > 0
                && Highlighter.SetHighlightColor(doc, textHandles[0], out var orig))
            {
                _colorHighlightHandle = textHandles[0];
                _colorHighlightOrigColor = orig;
            }

            // 焦点转到 CAD + 画红色框标记文字位置
            AcadApp.MainWindow.Focus();
            DrawHighlightBox(doc, lines[0]);

            SetStatus(boundHandles.Count > 0
                ? $"已高亮 {textHandles.Count} 处文字及 {boundHandles.Count} 个关联图面实体。"
                : $"已高亮 {textHandles.Count} 处文字。");
        }

        // ---------- 单击选中行 → CAD 红框定位原文 ----------

        void grid_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                if (_grid.CurrentRow == null || _grid.CurrentRow.Index < 0) return;
                var tag = _grid.CurrentRow.Tag as RowTag;
                if (tag == null) return;

                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null || _modelSheet == null) return;

                var lines = EvidenceLocator.FindLines(_modelSheet, tag.Evidence);
                if (lines.Count == 0)
                {
                    SetStatus("未找到匹配文字，请双击行查看详情。");
                    return;
                }

                // 焦点转到 CAD + 画红色框标记文字位置
                DrawHighlightBox(doc, lines[0]);

                // 颜色高亮：恢复上一个 → 标红当前（第一条匹配文字）
                RestoreColorHighlight(doc);
                var textHandles = lines
                    .Select(l => l.Handle)
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Distinct()
                    .ToList();
                if (textHandles.Count > 0
                    && Highlighter.SetHighlightColor(doc, textHandles[0], out var orig))
                {
                    _colorHighlightHandle = textHandles[0];
                    _colorHighlightOrigColor = orig;
                }
            }
            catch (System.Exception ex)
            {
                SetStatus($"选中行时出错: {ex.Message}");
            }
        }

        /// <summary>在 CAD 中画一个红色矩形框标记文字位置（先删旧框再画新框，不缩放不闪屏）。</summary>
        void DrawHighlightBox(Autodesk.AutoCAD.ApplicationServices.Document doc, TextLine line)
        {
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    // 删旧框
                    if (!_highlightBoxId.IsNull && _highlightBoxId.IsValid)
                    {
                        try
                        {
                            var old = tr.GetObject(_highlightBoxId, OpenMode.ForWrite, true) as Entity;
                            if (old != null && !old.IsErased) old.Erase();
                        }
                        catch { }
                        _highlightBoxId = ObjectId.Null;
                    }

                    // 拿文字实体的包围盒（有就用，没有就用 Position+Height 估算）
                    var pad = line.Height * 0.5;
                    double minX, minY, maxX, maxY;
                    if (!string.IsNullOrWhiteSpace(line.Handle))
                    {
                        try
                        {
                            if (long.TryParse(line.Handle, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out long h))
                            {
                                var id = doc.Database.GetObjectId(false, new Handle(h), 0);
                                if (!id.IsNull && !id.IsErased)
                                {
                                    var ent = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                                    if (ent != null)
                                    {
                                        var ext = ent.GeometricExtents;
                                        minX = ext.MinPoint.X - pad;
                                        minY = ext.MinPoint.Y - pad;
                                        maxX = ext.MaxPoint.X + pad;
                                        maxY = ext.MaxPoint.Y + pad;
                                        goto draw;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    // 估算：文字插入点往右 ~15字高，上下各留半个字高
                    minX = line.Position.X - pad;
                    minY = line.Position.Y - line.Height * 1.5 - pad;
                    maxX = line.Position.X + line.Height * 15 + pad;
                    maxY = line.Position.Y + line.Height * 0.5 + pad;

                draw:
                    // 检查目标图层是否锁定
                    var targetLayer = doc.Database.Clayer;
                    var layerTable = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                    if (layerTable.Has(targetLayer))
                    {
                        var layer = (LayerTableRecord)tr.GetObject(targetLayer, OpenMode.ForRead);
                        if (layer.IsLocked)
                        {
                            // 解锁图层
                            layer.UpgradeOpen();
                            layer.IsLocked = false;
                        }
                    }

                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
                    pl.Closed = true;
                    pl.ColorIndex = 1; // 红色

                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    _highlightBoxId = pl.ObjectId;

                    tr.Commit();
                }

                // 刷新显示
                doc.TransactionManager.QueueForGraphicsFlush();
                doc.Editor.Regen();
            }
            catch (System.Exception ex)
            {
                // 画框失败不影响主流程
                SetStatus($"画红框失败: {ex.Message}");
            }
        }

        /// <summary>恢复上一个颜色高亮实体的原始颜色。</summary>
        void RestoreColorHighlight(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            if (string.IsNullOrEmpty(_colorHighlightHandle)) return;
            Highlighter.RestoreEntityColor(doc, _colorHighlightHandle, _colorHighlightOrigColor);
            _colorHighlightHandle = null;
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // 状态
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));   // 进度
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));   // 识别文字标题
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));  // 识别文字预览
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 结果表
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // 结论统计条

            _progress.Dock = DockStyle.Fill;
            _grid.Dock = DockStyle.Fill;

            root.Controls.Add(BuildSettingsCard(), 0, 0);
            root.Controls.Add(BuildStatusRow(), 0, 1);
            root.Controls.Add(_progress, 0, 2);
            root.Controls.Add(BuildPreviewRow(), 0, 3);
            root.Controls.Add(_txtPreview, 0, 4);
            root.Controls.Add(_grid, 0, 5);
            root.Controls.Add(BuildSummaryBar(), 0, 6);

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

        /// <summary>结论统计条：[共N条] [符合] [不符合] [未涉及] [无法判断]（药丸按钮可筛选）。</summary>
        Control BuildSummaryBar()
        {
            _flowSummary.Dock = DockStyle.Fill;
            _flowSummary.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            _flowSummary.WrapContents = false;
            _flowSummary.Padding = new Padding(2, 2, 0, 0);
            _flowSummary.BackColor = UiTheme.Bg;
            _flowSummary.Margin = Padding.Empty;

            _chipTotal = new UiTheme.VerdictChip("共 0 条", "__all__");
            _chipTotal.Active = true;  // 默认选中态（显示全部）

            _chipOk = new UiTheme.VerdictChip("符合 0", AiJsonParser.VERDICT_OK);
            _chipBad = new UiTheme.VerdictChip("不符合 0", AiJsonParser.VERDICT_BAD);
            _chipNa = new UiTheme.VerdictChip("未涉及 0", AiJsonParser.VERDICT_NA);
            _chipUnk = new UiTheme.VerdictChip("无法判断 0", AiJsonParser.VERDICT_UNKNOWN);

            foreach (var chip in new UiTheme.VerdictChip[] { _chipTotal, _chipOk, _chipBad, _chipNa, _chipUnk })
            {
                chip.Margin = new Padding(4, 1, 0, 1);
                chip.Click += Chip_Click;
            }
            new ToolTip().SetToolTip(_chipTotal, "点击显示全部");
            new ToolTip().SetToolTip(_chipOk, "点击只显示「符合」，再点取消");
            new ToolTip().SetToolTip(_chipBad, "点击只显示「不符合」，再点取消");
            new ToolTip().SetToolTip(_chipNa, "点击只显示「未涉及」，再点取消");
            new ToolTip().SetToolTip(_chipUnk, "点击只显示「无法判断」，再点取消");

            _flowSummary.Controls.Add(_chipTotal);
            _flowSummary.Controls.Add(_chipOk);
            _flowSummary.Controls.Add(_chipBad);
            _flowSummary.Controls.Add(_chipNa);
            _flowSummary.Controls.Add(_chipUnk);
            _flowSummary.Visible = false;
            return _flowSummary;
        }

        void Chip_Click(object sender, EventArgs e)
        {
            var chip = sender as UiTheme.VerdictChip;
            if (chip == null) return;

            if (chip.Verdict == "__all__")
                _activeFilter = null;           // "共N条" 点击 = 显示全部
            else if (_activeFilter == chip.Verdict)
                _activeFilter = null;           // 再点一次取消
            else
                _activeFilter = chip.Verdict;

            ApplyFilter();
            UpdateSummaryHighlight();
        }

        /// <summary>按 _activeFilter 过滤结果表行（null = 全部显示）。</summary>
        void ApplyFilter()
        {
            _grid.SuspendLayout();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (_activeFilter == null)
                {
                    row.Visible = true;
                }
                else
                {
                    var cell = row.Cells["结论"];
                    row.Visible = cell != null && string.Equals(cell.Value?.ToString(), _activeFilter, StringComparison.Ordinal);
                }
            }
            _grid.ResumeLayout();

            int visible = _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
            if (_activeFilter != null)
                SetStatus($"筛选「{_activeFilter}」：显示 {visible} 条（共 {_results.Count} 条）。点击统计标签可取消。");
        }

        /// <summary>高亮当前激活的筛选芯片，"共N条" 在无筛选时激活。</summary>
        void UpdateSummaryHighlight()
        {
            _chipTotal.Active = _activeFilter == null;
            foreach (var chip in new[] { _chipOk, _chipBad, _chipNa, _chipUnk })
                chip.Active = chip.Verdict == _activeFilter;
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
            _txtPreview.HideSelection = false;
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

        /// <summary>把提取到的文字填入预览区，并记录每个文字行的字符区间（供点击定位）。</summary>
        void UpdatePreview(DrawingSheet sheet)
        {
            _previewSpans.Clear();
            if (sheet == null || sheet.TextLines.Count == 0)
            {
                _lblPreviewTitle.Text = "识别文字";
                _txtPreview.Text = "（暂无识别文字。点击「框选文字」或刷新图纸后在此预览。）";
                return;
            }
            int segCount = sheet.Segments != null ? sheet.Segments.Count : 0;
            int boundCount = sheet.TextLines
                .SelectMany(l => l.BoundEntities)
                .Select(b => b.Handle)
                .Distinct()
                .Count();
            _lblPreviewTitle.Text = segCount > 0
                ? $"识别文字（{sheet.TextLines.Count} 行，{segCount} 段，关联 {boundCount} 个实体）"
                : $"识别文字（{sheet.TextLines.Count} 行，关联 {boundCount} 个实体）";

            _txtPreview.Clear();
            _previewHighlightSpan = null;
            // 有分段时用彩色背景高亮每个段落；否则回退到纯文本
            if (sheet.Segments != null && sheet.Segments.Count > 0)
            {
                for (int i = 0; i < sheet.Segments.Count; i++)
                {
                    var seg = sheet.Segments[i];
                    var bg = UiTheme.SegmentBg(i);
                    _txtPreview.SelectionBackColor = bg;
                    _txtPreview.SelectionFont = UiTheme.UiFont(9f);

                    // 逐行追加并记录区间：行间用 \r\n，段间用单个 \n（与旧版拼接结果一致）
                    for (int j = 0; j < seg.Lines.Count; j++)
                    {
                        AppendPreviewLine(seg.Lines[j], j < seg.Lines.Count - 1);
                    }

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
                for (int i = 0; i < sheet.TextLines.Count; i++)
                {
                    AppendPreviewLine(sheet.TextLines[i], i < sheet.TextLines.Count - 1);
                }
            }
        }

        /// <summary>向预览区追加一行文字（记录其字符区间），行间以 \r\n 分隔。</summary>
        void AppendPreviewLine(TextLine line, bool withSeparator)
        {
            var text = line.Text ?? "";
            int start = _txtPreview.TextLength;
            _txtPreview.AppendText(text);
            _previewSpans.Add(new PreviewLineSpan(start, text.Length, line));
            if (withSeparator)
                _txtPreview.AppendText("\r\n");
        }

        /// <summary>根据点击的字符位置找到对应的文字行。</summary>
        TextLine FindLineAt(int charIndex)
        {
            if (charIndex < 0) return null;
            foreach (var s in _previewSpans)
            {
                if (charIndex >= s.Start && charIndex < s.Start + s.Length)
                    return s.Line;
            }
            return null;
        }

        /// <summary>根据点击的字符位置找到对应的行区间。</summary>
        PreviewLineSpan FindSpanAt(int charIndex)
        {
            if (charIndex < 0) return null;
            foreach (var s in _previewSpans)
            {
                if (charIndex >= s.Start && charIndex < s.Start + s.Length)
                    return s;
            }
            return null;
        }

        /// <summary>在预览区高亮指定行（蓝底白字），并清除上一行的高亮。</summary>
        void SetPreviewHighlight(PreviewLineSpan span)
        {
            if (span == null) return;
            // 清除上一行
            if (_previewHighlightSpan != null)
            {
                _txtPreview.SelectionStart = _previewHighlightSpan.Start;
                _txtPreview.SelectionLength = _previewHighlightSpan.Length;
                _txtPreview.SelectionBackColor = _previewHighlightOrigBg;
                _txtPreview.SelectionColor = UiTheme.TextMain;
            }
            // 保存当前行的原始背景色，再标蓝
            _txtPreview.SelectionStart = span.Start;
            _txtPreview.SelectionLength = 1;
            _previewHighlightOrigBg = _txtPreview.SelectionBackColor;
            _txtPreview.SelectionLength = span.Length;
            _txtPreview.SelectionBackColor = UiTheme.Accent;
            _txtPreview.SelectionColor = Color.White;
            _previewHighlightSpan = span;
            _txtPreview.SelectionLength = 0;
        }

        /// <summary>点击识别文字预览中的一行 → 在 CAD 中选中该行文字及其关联图面实体。</summary>
        void txtPreview_MouseClick(object sender, MouseEventArgs e)
        {
            if (_modelSheet == null) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var span = FindSpanAt(_txtPreview.GetCharIndexFromPosition(e.Location));
            if (span == null || string.IsNullOrWhiteSpace(span.Line.Text)) return;
            var line = span.Line;

            // 预览区蓝底白字高亮
            SetPreviewHighlight(span);

            var textHandles = string.IsNullOrWhiteSpace(line.Handle)
                ? new List<string>()
                : new List<string> { line.Handle };
            var boundHandles = line.BoundEntities
                .Select(b => b.Handle)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .ToList();

            Highlighter.HighlightHandles(doc, textHandles.Concat(boundHandles));

            // 颜色高亮：恢复上一个 → 标红当前
            RestoreColorHighlight(doc);
            if (!string.IsNullOrWhiteSpace(line.Handle)
                && Highlighter.SetHighlightColor(doc, line.Handle, out var orig))
            {
                _colorHighlightHandle = line.Handle;
                _colorHighlightOrigColor = orig;
            }

            // 焦点转到 CAD + 画红色框标记文字位置
            AcadApp.MainWindow.Focus();
            DrawHighlightBox(doc, line);

            var preview = line.Text.Length > 20 ? line.Text.Substring(0, 20) + "…" : line.Text;
            SetStatus(boundHandles.Count > 0
                ? $"已选中「{preview}」所在文字实体及 {boundHandles.Count} 个关联图面实体。"
                : $"已选中「{preview}」所在文字实体。");
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
            _chipTotal.Text = $"共 {total} 条";
            _chipOk.Text = $"符合 {ok}";
            _chipBad.Text = $"不符合 {bad}";
            _chipNa.Text = $"未涉及 {na}";
            _chipUnk.Text = $"无法判断 {unk}";
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
            _grid.SelectionChanged += grid_SelectionChanged;
            _txtPreview.MouseClick += txtPreview_MouseClick;
            new ToolTip().SetToolTip(_txtPreview, "点击预览中的文字行：在 CAD 中定位该行文字及其关联图面实体");

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

        /// <summary>窗口拖动/缩放结束后，确保不超出 AutoCAD 主窗口边界。</summary>
        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            ClampToOwner();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            ClampToOwner();
        }

        void SetStatus(string msg) => _lblStatus.Text = msg;
    }
}