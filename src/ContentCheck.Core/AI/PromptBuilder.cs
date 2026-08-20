using System.Linq;
using System.Text;
using ContentCheck.Core.Models;

namespace ContentCheck.Core.AI
{
    /// <summary>构造 DeepSeek 校核提示词（中文）。</summary>
    public static class PromptBuilder
    {
        public static string SystemPrompt() => @"
你是一位资深电力工程土建设计与施工图审查专家，精通电力工程设计规程与建筑、给排水、暖通等专业的设计规范，负责校核施工图《总说明》是否满足规范条文要求。

你将收到一份《总说明》文字和若干规范条文。总说明是设计单位在图纸中给出的总体设计说明，涵盖设计依据、设计范围、材料与构造做法、防火与疏散、给排水、暖通等要求。

判定规则：
1.【符合】总说明中明确写出与条文要求一致的内容，或能依据总说明内容明确推断设计满足该条要求。
2.【不符合】该条文与总说明同专业、同适用范围，但总说明完全未提及且无法由其他内容推断满足；或总说明内容与条文要求明显矛盾。
3.【未涉及】该条文所述内容不属于本总说明所属设计范围或专业，本图纸/本专业无需涉及。
4.【无法判断】总说明内容不足以支撑上述任一结论，信息不足或表述含糊。

注意事项：
- 只依据给定的总说明文字作出判断，不得臆造总说明中不存在的内容。
- 证据 evidence 必须引用总说明中的原文字句（只引用关键原句，60 字以内，中间可省略；若未提及，写""总说明未提及""）。
- 分析 analysis 简明扼要，2 句以内、80 字以内。
- 修改建议 suggestion 仅在 verdict 为""不符合""或""无法判断""时填写具体、可落地的修改措辞建议（60 字以内）；其余情况填空字符串。
- 每条条文的结果尽量精简，整段 JSON 输出控制在 300 token 以内，不要冗余展开。
- 严格只输出一个 JSON 对象，不要输出 JSON 以外的任何文字、注释或 Markdown 代码块。
".TrimStart();

        public static string UserPrompt(string sheetName, string sheetText, CheckBatch batch)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【任务】");
            sb.AppendLine("请核对下列《总说明》是否满足给定规范条文的要求，逐条给出结论。");
            sb.AppendLine();
            sb.AppendLine("【总说明】");
            sb.AppendLine("图纸/布局名称：" + sheetName);
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine(sheetText);
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine($"【待核对规范条文】（共 {batch.Items.Count} 条，来自规范《{batch.CodeName}》，专业：{batch.Discipline}）");
            int i = 1;
            foreach (var item in batch.Items)
            {
                var num = string.IsNullOrWhiteSpace(item.ClauseNumber) ? "(未编号)" : item.ClauseNumber;
                sb.AppendLine($"{i}. 条文 {num}：{item.ClauseText}");
                i++;
            }
            sb.AppendLine();
            sb.AppendLine("【输出格式】");
            sb.AppendLine("只输出一个 JSON 对象，形如：");
            sb.AppendLine("{");
            sb.AppendLine("  \"results\": [");
            sb.AppendLine("    {\"clause_number\":\"3.4.1\",\"verdict\":\"符合\",\"evidence\":\"…\",\"analysis\":\"…\",\"suggestion\":\"\"},");
            sb.AppendLine("    …");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("其中 verdict 只能取值为：\"符合\"、\"不符合\"、\"未涉及\"、\"无法判断\"。");
            sb.AppendLine("results 数组必须与上述条文一一对应，每一条都要有记录。");
            return sb.ToString();
        }

        /// <summary>总说明过长时截断：保留开头+结尾（设计依据/材料/说明段通常承载关键要求）。</summary>
        public static string TruncateSheetText(string sheetText, int maxChars)
        {
            if (string.IsNullOrEmpty(sheetText) || sheetText.Length <= maxChars) return sheetText ?? "";
            int head = maxChars * 2 / 3;
            int tail = maxChars - head;
            return sheetText.Substring(0, head)
                 + "\n\n……（总说明过长，已截断。中间部分省略）……\n\n"
                 + sheetText.Substring(sheetText.Length - tail);
        }

        /// <summary>折叠连续空行，便于送入提示词。</summary>
        public static string NormalizeSheetText(string sheetText)
        {
            if (string.IsNullOrEmpty(sheetText)) return "";
            var lines = sheetText.Split('\n');
            var sb = new StringBuilder();
            bool prevBlank = false;
            foreach (var raw in lines)
            {
                var ln = raw.TrimEnd();
                bool blank = string.IsNullOrWhiteSpace(ln);
                if (blank && prevBlank) continue;
                sb.AppendLine(ln);
                prevBlank = blank;
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }
    }
}
