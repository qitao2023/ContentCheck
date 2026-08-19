using System.Collections.Generic;
using System.Linq;

namespace ContentCheck.Acad.Dwg
{
    /// <summary>自动识别总说明布局：布局名含「总说明/设计说明/说明」，否则取第一个非 Model 布局。</summary>
    public static class SheetAutoDetect
    {
        public static string PickByHeuristic(List<DrawingSheet> sheets)
        {
            if (sheets == null || sheets.Count == 0) return null;

            var named = sheets
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .ToList();

            foreach (var kw in new[] { "总说明", "设计说明", "说明" })
            {
                var hit = named.FirstOrDefault(s => s.Name.Contains(kw));
                if (hit != null) return hit.Name;
            }

            var nonModel = named.FirstOrDefault(s => s.Name != "Model");
            if (nonModel != null) return nonModel.Name;

            return sheets[0].Name;
        }
    }
}
