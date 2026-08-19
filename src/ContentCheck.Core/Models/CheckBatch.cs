using System.Collections.Generic;

namespace ContentCheck.Core.Models
{
    /// <summary>一次 DeepSeek 调用要校核的一组条文（按专业+规范名称分组）。</summary>
    public class CheckBatch
    {
        public string Discipline { get; set; }
        public string CodeName { get; set; }

        public List<BatchItem> Items { get; set; } = new List<BatchItem>();

        public class BatchItem
        {
            public long ProvisionId { get; set; }
            public string ClauseNumber { get; set; }
            public string ClauseText { get; set; }
            public string DrawingTypesRaw { get; set; }
        }
    }
}
