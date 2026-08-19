namespace ContentCheck.Core.Models
{
    /// <summary>一条条文的校核结论（AI 返回，含原文证据）。</summary>
    public class VerdictResult
    {
        /// <summary>专业。</summary>
        public string Discipline { get; set; }

        /// <summary>规范名称。</summary>
        public string CodeName { get; set; }

        /// <summary>条文编号（可空）。</summary>
        public string ClauseNumber { get; set; }

        /// <summary>条文全文。</summary>
        public string ClauseText { get; set; }

        /// <summary>图纸类型。</summary>
        public string DrawingTypesRaw { get; set; }

        /// <summary>结论：符合 / 不符合 / 未涉及 / 无法判断。</summary>
        public string Verdict { get; set; }

        /// <summary>依据原文（总说明原文字句）。</summary>
        public string Evidence { get; set; }

        /// <summary>AI 分析说明。</summary>
        public string Analysis { get; set; }

        /// <summary>修改建议（不符合/无法判断时填）。</summary>
        public string Suggestion { get; set; }

        /// <summary>数据来源说明（如 deepseek-v4-flash；降级/失败时记录原因）。</summary>
        public string SourceNote { get; set; }
    }
}
