using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ContentCheck.Core.Models;

namespace ContentCheck.Acad.UI
{
    /// <summary>结果表列定义、样式与结论配色（使用 UiTheme 统一主题）。</summary>
    public static class ResultGridSetup
    {
        public static void Configure(DataGridView grid)
        {
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 30;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;

            // 主题化
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = UiTheme.Border;
            grid.EnableHeadersVisualStyles = false;

            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiTheme.GridHeaderBg,
                ForeColor = UiTheme.GridHeaderText,
                Font = UiTheme.UiFontBold(),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = UiTheme.GridHeaderBg,
                SelectionForeColor = UiTheme.GridHeaderText,
                Padding = new Padding(2, 2, 2, 2),
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = UiTheme.UiFont(),
                ForeColor = UiTheme.TextMain,
                BackColor = Color.White,
                SelectionBackColor = UiTheme.AccentSoft,
                SelectionForeColor = UiTheme.TextMain,
                Padding = new Padding(4, 2, 4, 2),
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.RowAlt;
            grid.ColumnHeadersHeight = 30;

            grid.Columns.Clear();
            AddCol(grid, "序号", 44, false, DataGridViewContentAlignment.MiddleCenter);
            AddCol(grid, "识别原文", 200, true);
            AddCol(grid, "规范条文", 300, true);
            AddCol(grid, "AI分析", 200, true);
            AddCol(grid, "修改建议", 160, true);
            AddCol(grid, "结论", 56, false, DataGridViewContentAlignment.MiddleCenter);
        }

        /// <summary>
        /// 组装「规范条文」单元格文本：规范名称（含编号）+ 条文编号 + 条文全文，各占一行。
        /// </summary>
        public static string FormatProvision(VerdictResult v)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(v.CodeName))
                sb.Append(v.CodeName.Trim());
            if (!string.IsNullOrWhiteSpace(v.ClauseNumber))
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(v.ClauseNumber.Trim());
            }
            if (sb.Length > 0) sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(v.ClauseText))
                sb.Append(v.ClauseText.Trim());
            return sb.ToString();
        }

        static void AddCol(DataGridView grid, string header, int weight, bool wrap,
            DataGridViewContentAlignment align = DataGridViewContentAlignment.MiddleLeft)
        {
            var c = new DataGridViewTextBoxColumn
            {
                Name = header,
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight,
                MinimumWidth = 48,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = align },
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            if (wrap)
            {
                c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            }
            grid.Columns.Add(c);
        }

        /// <summary>给整行结论列配色（主题色）。</summary>
        public static void ColorRow(DataGridViewRow row, string verdict)
        {
            var col = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.OwningColumn.Name == "结论");
            if (col == null) return;
            var c = col;
            switch (verdict)
            {
                case "符合":
                    c.Style.ForeColor = UiTheme.VerdictOk;
                    c.Style.Font = UiTheme.UiFontBold();
                    break;
                case "不符合":
                    c.Style.ForeColor = UiTheme.VerdictBad;
                    c.Style.Font = UiTheme.UiFontBold();
                    break;
                case "未涉及":
                    c.Style.ForeColor = UiTheme.VerdictNa;
                    break;
                default:
                    c.Style.ForeColor = UiTheme.VerdictUnknown;
                    c.Style.Font = UiTheme.UiFontBold();
                    break;
            }
        }
    }
}
