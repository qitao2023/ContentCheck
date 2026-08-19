using System.Collections.Generic;

namespace ContentCheck.Core.Models
{
    /// <summary>一条规范条文（来自 Excel 导入，存于 SQLite）。</summary>
    public class Provision
    {
        public long Id { get; set; }

        /// <summary>专业：建筑 / 给排水 / 暖通 / 国网（随 Excel sheet 扩展）。</summary>
        public string Discipline { get; set; }

        /// <summary>规范名称，如《建筑防火通用规范》(55037-2022)。</summary>
        public string CodeName { get; set; }

        /// <summary>条文编号，如 3.4.1；无法解析（如国网的"第九条"）时为 null。</summary>
        public string ClauseNumber { get; set; }

        /// <summary>条文全文（含多行子条）。</summary>
        public string ClauseText { get; set; }

        /// <summary>图纸类型（顿号连接），如 设计说明、平面。</summary>
        public string DrawingTypesRaw { get; set; }

        public List<string> DrawingTypes { get; set; } = new List<string>();
    }
}
