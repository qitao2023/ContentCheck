using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ContentCheck.Acad.UI
{
    /// <summary>面板统一主题：配色、字体、控件样式。避免散落的魔法色值。</summary>
    public static class UiTheme
    {
        // 背景 / 卡片
        public static readonly Color Bg = Color.FromArgb(0xF5, 0xF7, 0xFA);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(0xDC, 0xE2, 0xEA);

        // 强调（标准选中蓝 #3378CE）
        public static readonly Color Accent = Color.FromArgb(51, 120, 206);
        public static readonly Color AccentDark = Color.FromArgb(38, 95, 170);
        public static readonly Color AccentSoft = Color.FromArgb(0xE8, 0xEF, 0xFB);

        // 按钮 / 下拉框中性色（无彩色）
        public static readonly Color BtnPrimaryBg = Color.FromArgb(0xF0, 0xF2, 0xF6);  // 主按钮浅灰底
        public static readonly Color BtnHoverBg = Color.FromArgb(0xE2, 0xE7, 0xEE);    // 悬停中灰底

        // 顶栏
        public static readonly Color HeaderBg = Color.FromArgb(0x1F, 0x38, 0x64);
        public static readonly Color HeaderSub = Color.FromArgb(0xB6, 0xC6, 0xDE);

        // 文字
        public static readonly Color TextMain = Color.FromArgb(0x30, 0x3A, 0x45);
        public static readonly Color TextMuted = Color.FromArgb(0x8A, 0x94, 0xA0);

        // 结论配色
        public static readonly Color VerdictOk = Color.FromArgb(0x1E, 0x8E, 0x3E);
        public static readonly Color VerdictBad = Color.FromArgb(0xD9, 0x30, 0x25);
        public static readonly Color VerdictNa = Color.FromArgb(0x80, 0x86, 0x8B);
        public static readonly Color VerdictUnknown = Color.FromArgb(0xE8, 0x74, 0x0A);

        // 段落背景色（交替使用，便于区分相邻段落）
        public static readonly Color[] SegmentBgs = new[]
        {
            Color.FromArgb(0xFF, 0xF8, 0xE1), // 浅黄
            Color.FromArgb(0xE8, 0xF5, 0xE9), // 浅绿
            Color.FromArgb(0xE3, 0xF2, 0xFD), // 浅蓝
            Color.FromArgb(0xF3, 0xE5, 0xF5), // 浅紫
            Color.FromArgb(0xFF, 0xEB, 0xEE), // 浅红
            Color.FromArgb(0xE0, 0xF7, 0xFA), // 浅青
        };
        public static Color SegmentBg(int index) => SegmentBgs[index % SegmentBgs.Length];

        public static readonly Color RowAlt = Color.FromArgb(0xF8, 0xFA, 0xFD);
        public static readonly Color GridHeaderBg = Color.FromArgb(0x34, 0x43, 0x58);
        public static readonly Color GridHeaderText = Color.White;
        public static readonly Color Splitter = Color.FromArgb(0xDC, 0xE2, 0xEA);

        public static Font UiFont(float size = 9f) => new Font("Microsoft YaHei UI", size);
        public static Font UiFontBold(float size = 9f) => new Font("Microsoft YaHei UI", size, FontStyle.Bold);
        public static Font UiFontBoldUnderline(float size = 9f) => new Font("Microsoft YaHei UI", size, FontStyle.Bold | FontStyle.Underline);

        /// <summary>卡片分区：白底 + 边框 + 内边距，可向其中添加行控件。</summary>
        public static Panel MakeCard()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0),
            };
        }

        /// <summary>分区标题。</summary>
        public static Label SectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                ForeColor = TextMuted,
                Font = UiFont(),
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        public enum ButtonKind { Primary, Secondary }

        public static Button StyleButton(Button b, ButtonKind kind, string text)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Border;
            b.Cursor = Cursors.Hand;
            b.Font = UiFont();
            b.UseVisualStyleBackColor = false;
            b.ForeColor = TextMain;
            b.BackColor = kind == ButtonKind.Primary ? BtnPrimaryBg : Card;
            b.MouseEnter += (s, e) => { if (b.Enabled) { b.BackColor = Accent; b.ForeColor = Color.White; } };
            b.MouseLeave += (s, e) => { if (b.Enabled) { b.BackColor = kind == ButtonKind.Primary ? BtnPrimaryBg : Card; b.ForeColor = TextMain; } };
            return b;
        }

        public static ComboBox StyleCombo(ComboBox cb, bool editable = false)
        {
            cb.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Font = UiFont();
            cb.BackColor = Color.White;
            cb.ForeColor = TextMain;
            return cb;
        }

        public static CheckBox StyleCheck(CheckBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
            cb.ForeColor = TextMain;
            cb.Font = UiFont();
            cb.AutoSize = true;
            cb.Cursor = Cursors.Hand;
            return cb;
        }

        /// <summary>
        /// 药丸按钮（Chip）：深蓝底白字圆角标签，用于筛选器。
        /// 激活态加粗，未激活态常规字重。
        /// </summary>
        public class VerdictChip : Label
        {
            public string Verdict { get; }
            static readonly Color ChipBg = Accent;             // #3378CE 选中蓝
            static readonly Color ChipBgInactive = Color.White;
            static readonly Color ChipTextInactive = TextMain; // #303A45
            static readonly Color ChipText = Color.White;
            bool _active;

            public bool Active
            {
                get => _active;
                set { _active = value; Invalidate(); }
            }

            public VerdictChip(string text, string verdict)
            {
                Verdict = verdict;
                Text = text;
                AutoSize = false;
                Height = 24;
                MinimumSize = new Size(0, 24);
                Padding = new Padding(10, 2, 10, 2);
                Cursor = Cursors.Hand;
                Font = UiTheme.UiFontBold(9f);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                int radius = Height - 2;

                // 背景
                using (var bg = new SolidBrush(_active ? ChipBg : ChipBgInactive))
                    FillPill(g, bg, rect, radius);

                // 边框
                using (var pen = new Pen(_active ? Accent : Border, _active ? 1.5f : 1f))
                    DrawPill(g, pen, rect, radius);

                // 文字
                using (var brush = new SolidBrush(_active ? ChipText : ChipTextInactive))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(Text, _active ? UiFontBold(9f) : UiFont(9f), brush, rect, sf);
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                Invalidate();
            }

            static void FillPill(Graphics g, Brush brush, Rectangle r, int d)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
                path.Dispose();
            }

            static void DrawPill(Graphics g, Pen pen, Rectangle r, int d)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
                path.Dispose();
            }
        }
    }
}
