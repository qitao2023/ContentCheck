using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ContentCheck.Acad.UI;   // 复用真实 UiTheme + ResultGridSetup + ProvisionPickerDialog
using ContentCheck.Core.Config;
using ContentCheck.Core.Models;

namespace UiPreview
{
    /// <summary>
    /// 离线渲染停靠面板布局与条文选择框，输出 PNG + 几何诊断（不加载 AutoCAD）。
    /// 用法：UiPreview.exe         渲染并保存截图
    ///       UiPreview.exe --show 实时显示面板
    /// </summary>
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main()
        {
            SetProcessDPIAware();   // 1:1 渲染，避免系统缩放导致截图错位
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool interactive = Environment.GetCommandLineArgs().Length > 1
                && Environment.GetCommandLineArgs()[1] == "--show";

            var form = BuildForm(interactive);
            if (interactive) { Application.Run(form); return 0; }

            form.Show();
            form.Refresh();
            Application.DoEvents();
            DumpLayout(form);
            Console.WriteLine();

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Capture(form, System.IO.Path.Combine(dir, "preview.png"));

            // 渲染条文选择框
            var picker = BuildPicker();
            picker.Show();
            picker.Refresh();
            Application.DoEvents();
            DumpLayout(picker, indent: "picker: ");
            Capture(picker, System.IO.Path.Combine(dir, "picker.png"));
            picker.Close();

            // 渲染设置对话框
            var settings = new SettingsDialog(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cc_prev.json"), new AppConfig());
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-10000, -10000);
            settings.Show();
            settings.Refresh();
            Application.DoEvents();
            DumpLayout(settings, indent: "settings: ");
            Capture(settings, System.IO.Path.Combine(dir, "settings.png"));
            settings.Close();

            Console.WriteLine("已保存 preview.png / picker.png / settings.png");
            return 0;
        }

        // ---------- 主面板（与 MainPaletteUserControl 一致） ----------

        static readonly ComboBox _cbDisc = new ComboBox();
        static readonly Button _btnProv = new Button();
        static readonly ComboBox _cbModel = new ComboBox();
        static readonly Button _btnRun = new Button();
        static readonly Button _btnExtract = new Button();
        static readonly Button _btnReport = new Button();
        static readonly Label _lblStatus = new Label();
        static readonly ProgressBar _progress = new ProgressBar();
        static readonly DataGridView _grid = new DataGridView();
        static readonly FlowLayoutPanel _flowSummary = new FlowLayoutPanel();
        static readonly Label _lblTotal = new Label();
        static Label _lblOk, _lblBad, _lblNa, _lblUnk;

        static Form BuildForm(bool interactive)
        {
            var form = new Form
            {
                Width = 480,
                Height = 760,
                StartPosition = FormStartPosition.Manual,
                Location = interactive ? new Point(60, 60) : new Point(-10000, -10000),
                FormBorderStyle = FormBorderStyle.Sizable,
                Text = "ContentCheck 校核面板（预览）",
            };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, BackColor = UiTheme.Bg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 194));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _progress.Dock = DockStyle.Fill;
            _grid.Dock = DockStyle.Fill;

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildSettingsCard(), 0, 1);
            root.Controls.Add(BuildSummaryBar(), 0, 2);
            root.Controls.Add(BuildStatusRow(), 0, 3);
            root.Controls.Add(_progress, 0, 4);
            root.Controls.Add(_grid, 0, 5);

            ResultGridSetup.Configure(_grid);
            FillGrid();
            form.Controls.Add(root);
            return form;
        }

        static Control BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.HeaderBg, Padding = new Padding(18, 8, 18, 8) };
            var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.HeaderBg };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            tbl.Controls.Add(new Label
            {
                Text = "图纸总说明 · 规范校核",
                ForeColor = Color.White,
                Font = UiTheme.UiFontBold(11.5f),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = Padding.Empty,
            }, 0, 0);
            tbl.Controls.Add(new Label
            {
                Text = "模型空间 · AI 语义校核",
                ForeColor = UiTheme.HeaderSub,
                Font = UiTheme.UiFont(8.5f),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = Padding.Empty,
            }, 1, 0);
            p.Controls.Add(tbl);
            return p;
        }

        static Control BuildSettingsCard()
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = UiTheme.Card,
                Padding = new Padding(16, 12, 16, 12),
                Margin = Padding.Empty,
            };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            card.Controls.Add(new Label
            {
                Text = "校核设置",
                ForeColor = UiTheme.TextMuted,
                Font = UiTheme.UiFontBold(9f),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            }, 0, 0);

            // 校核专业：下拉 + …按钮
            var rowDisc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, BackColor = UiTheme.Card };
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowDisc.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            rowDisc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rowDisc.Controls.Add(MidLabel("校核专业"), 0, 0);
            UiTheme.StyleCombo(_cbDisc);
            _cbDisc.Dock = DockStyle.Fill;
            _cbDisc.Margin = new Padding(0, 5, 6, 5);
            _cbDisc.Items.AddRange(new object[] { "建筑", "给排水", "暖通", "国网" });
            _cbDisc.SelectedIndex = 0;
            rowDisc.Controls.Add(_cbDisc, 1, 0);
            UiTheme.StyleButton(_btnProv, UiTheme.ButtonKind.Secondary, "…");
            _btnProv.Dock = DockStyle.Fill;
            _btnProv.Margin = new Padding(0, 5, 0, 5);
            _btnProv.Font = UiTheme.UiFontBold(12f);
            _btnProv.FlatAppearance.BorderSize = 0;
            _btnProv.BackColor = UiTheme.AccentSoft;
            rowDisc.Controls.Add(_btnProv, 2, 0);
            card.Controls.Add(rowDisc, 0, 1);

            // 校核模型
            var rowModel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, BackColor = UiTheme.Card };
            rowModel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            rowModel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowModel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rowModel.Controls.Add(MidLabel("校核模型"), 0, 0);
            UiTheme.StyleCombo(_cbModel);
            _cbModel.Dock = DockStyle.Fill;
            _cbModel.Margin = new Padding(0, 5, 0, 5);
            _cbModel.Items.Add("快速核对 · deepseek-v4-flash");
            _cbModel.Items.Add("深度核对 · deepseek-v4-pro");
            _cbModel.SelectedIndex = 0;
            rowModel.Controls.Add(_cbModel, 1, 0);
            card.Controls.Add(rowModel, 0, 2);

            // 操作
            UiTheme.StyleButton(_btnRun, UiTheme.ButtonKind.Primary, "开始校核");
            _btnRun.Dock = DockStyle.Fill;
            _btnRun.Font = UiTheme.UiFontBold(10f);
            _btnRun.Margin = new Padding(0, 6, 0, 4);
            card.Controls.Add(_btnRun, 0, 3);

            var rowOp2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, BackColor = UiTheme.Card };
            rowOp2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            rowOp2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            rowOp2.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            UiTheme.StyleButton(_btnExtract, UiTheme.ButtonKind.Secondary, "提取全部文字");
            UiTheme.StyleButton(_btnReport, UiTheme.ButtonKind.Secondary, "另存报告");
            _btnExtract.Dock = DockStyle.Fill;
            _btnReport.Dock = DockStyle.Fill;
            _btnExtract.Margin = new Padding(0, 4, 4, 0);
            _btnReport.Margin = new Padding(4, 4, 0, 0);
            rowOp2.Controls.Add(_btnExtract, 0, 0);
            rowOp2.Controls.Add(_btnReport, 1, 0);
            card.Controls.Add(rowOp2, 0, 4);

            return card;
        }

        static Control BuildSummaryBar()
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
            return _flowSummary;
        }

        static Label SummaryLabel(string text, Color color) => new Label
        {
            Text = text,
            ForeColor = color,
            Font = UiTheme.UiFontBold(9f),
            AutoSize = true,
            Margin = new Padding(8, 2, 0, 0),
        };

        static Control BuildStatusRow()
        {
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Text = "就绪。已提取模型空间文字（312 行）。";
            _lblStatus.ForeColor = UiTheme.TextMuted;
            _lblStatus.Font = UiTheme.UiFont(9f);
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Margin = new Padding(2, 3, 0, 0);
            return _lblStatus;
        }

        static Label MidLabel(string text) => new Label
        {
            Text = text,
            ForeColor = UiTheme.TextMain,
            Font = UiTheme.UiFont(),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
        };

        static void FillGrid()
        {
            var rows = new[]
            {
                new[] { "符合", "《建筑防火通用规范》(55037-2022)", "3.4.1", "工业与民用建筑周围、工厂厂区内应设置消防车道，消防车道应满足消防车安全通行要求。", "总图,设计说明", "厂区四周设消防车道，净宽净高满足要求。", "总说明明确设置消防车道，满足条文要求。", "" },
                new[] { "不符合", "《建筑防火通用规范》(55037-2022)", "6.3.4", "防火门、防火窗应具有自动关闭的功能，在关闭后应具有烟密闭性能。", "平面,设计说明", "总说明未提及", "总说明未对防火门自动关闭功能作出规定。", "建议补充防火门自动关闭功能要求。" },
                new[] { "未涉及", "《建筑防烟排烟系统技术标准》(GB51251-2017)", "8.2.1", "下列部位应采取防烟措施：封闭楼梯间、防烟楼梯间及其前室。", "平面", "总说明未提及", "该条文针对防烟部位设置，属于平面设计内容。", "" },
                new[] { "无法判断", "国网上海市电力公司电网工程土建设计标准化技术规范（2024 版）", "第九条", "标准220kV户内中心变电站围墙内占地面积不小于116m×70m标准矩形。", "总图,设计说明", "总说明未提及", "总说明未涉及站区占地尺寸，无法判断。", "建议核对总平面图。" },
            };
            foreach (var r in rows)
            {
                var idx = _grid.Rows.Add(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7]);
                ResultGridSetup.ColorRow(_grid.Rows[idx], r[0]);
            }
            _lblTotal.Text = "共 4 条";
            _lblOk.Text = "符合 1";
            _lblBad.Text = "不符合 1";
            _lblNa.Text = "未涉及 1";
            _lblUnk.Text = "无法判断 1";
            _flowSummary.Visible = true;
        }

        // ---------- 条文选择框 ----------

        static Form BuildPicker()
        {
            var provisions = new List<Provision>
            {
                Prov(1, "建筑", "《建筑防火通用规范》(55037-2022)", "3.4.1", "工业与民用建筑周围、工厂厂区内、仓库库区内应设置消防车道，消防车道应满足消防车安全通行要求。", "总图,设计说明"),
                Prov(2, "建筑", "《建筑防火通用规范》(55037-2022)", "3.4.2", "下列建筑应至少沿建筑的两条长边设置消防车道：高层厂房，占地面积大于3000㎡的单、多层厂房……", "总图"),
                Prov(3, "建筑", "《建筑防火通用规范》(55037-2022)", "6.3.4", "防火门、防火窗应具有自动关闭的功能，在关闭后应具有烟密闭性能。", "平面,设计说明"),
                Prov(4, "建筑", "《总图制图标准》(GB/T 50103-2010)", "3.1.1", "总平面图的绘制应符合本标准的规定，图纸上应注明比例、指北针。", "总图,设计说明"),
                Prov(5, "建筑", "《总图制图标准》(GB/T 50103-2010)", "4.2.3", "竖向设计宜采用等高线法或坡面表示法表达。", "总图"),
            };
            var dlg = new ProvisionPickerDialog("建筑", provisions, new HashSet<long>());
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(-10000, -10000);
            return dlg;
        }

        static Provision Prov(long id, string disc, string code, string num, string text, string types) => new Provision
        {
            Id = id,
            Discipline = disc,
            CodeName = code,
            ClauseNumber = num,
            ClauseText = text,
            DrawingTypesRaw = types,
        };

        // ---------- 诊断与截图 ----------

        static void DumpLayout(Control parent, string indent = "")
        {
            foreach (Control c in parent.Controls)
            {
                string text = (c is ComboBox cb) ? (cb.SelectedItem?.ToString() ?? cb.Text)
                            : (c is Button b) ? b.Text
                            : (c is Label || c is CheckBox) ? c.Text : "";
                if (text.Length > 24) text = text.Substring(0, 24) + "…";
                string clip = "";
                if (text.Length > 0 && c.Height > 0 && c.Width > 0 && c.IsHandleCreated)
                {
                    using (var g = c.CreateGraphics())
                    {
                        var sz = g.MeasureString(text, c.Font, c.Width);
                        if (sz.Width > c.Width + 1) clip += " [横排裁剪!]";
                        if (sz.Height > c.Height + 1) clip += " [纵向裁剪!]";
                    }
                }
                Console.WriteLine($"{indent}{c.GetType().Name} @({c.Left},{c.Top}) {c.Width}x{c.Height} '{text}'{clip}");
                if (c.Controls.Count > 0) DumpLayout(c, indent);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        static void Capture(Form form, string path)
        {
            form.Refresh();
            System.Threading.Thread.Sleep(300);
            Application.DoEvents();

            // 用 PrintWindow 直接把窗口内容渲染到位图（不依赖窗口是否可见）
            var bmp = new Bitmap(form.Width, form.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                PrintWindow(form.Handle, hdc, 2);   // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
            }

            // 裁掉非客户区（标题栏），只留客户区
            var clientScreen = form.RectangleToScreen(form.ClientRectangle);
            var windowScreen = form.RectangleToScreen(new Rectangle(0, 0, form.Width, form.Height));
            var crop = new Rectangle(
                clientScreen.Left - windowScreen.Left,
                clientScreen.Top - windowScreen.Top,
                form.ClientSize.Width,
                form.ClientSize.Height);
            using (var client = bmp.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                client.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
