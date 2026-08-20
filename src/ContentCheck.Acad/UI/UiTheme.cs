using System.Drawing;
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

        // 强调
        public static readonly Color Accent = Color.FromArgb(0x2D, 0x6C, 0xDF);
        public static readonly Color AccentDark = Color.FromArgb(0x1E, 0x50, 0xB8);
        public static readonly Color AccentSoft = Color.FromArgb(0xE8, 0xEF, 0xFB);

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
            b.FlatAppearance.BorderSize = kind == ButtonKind.Primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Border;
            b.Cursor = Cursors.Hand;
            b.Font = UiFont();
            b.UseVisualStyleBackColor = false;
            if (kind == ButtonKind.Primary)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = Card;
                b.ForeColor = TextMain;
            }
            b.MouseEnter += (s, e) => { if (b.Enabled) b.BackColor = kind == ButtonKind.Primary ? AccentDark : AccentSoft; };
            b.MouseLeave += (s, e) => { if (b.Enabled) b.BackColor = kind == ButtonKind.Primary ? Accent : Card; };
            return b;
        }

        public static ComboBox StyleCombo(ComboBox cb, bool editable = false)
        {
            cb.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Font = UiFont();
            cb.BackColor = Color.White;
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
    }
}
