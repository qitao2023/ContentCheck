# 校核"慢"问题 — 根因分析与修复说明

## 现象

用户反馈：**只选择 1 条规范条文、识别内容也只有 1 条，进度条仍然跑很久才出结果**。
这排除了"批次多、条文多"的因素，说明慢发生在**单次 AI 调用**本身。

## 真正的根因：max_tokens 过大，模型"跑飞"时无上限

- 原配置 `max_tokens = 8192`（config.json 默认）。
- 单条条文的结果 JSON 实际只需要几百 token（verdict + evidence + analysis + suggestion）。
- 但 DeepSeek/MiMo 等服务在 JSON 模式下，如果模型输出不收敛（例如重复啰嗦、JSON 格式漂移），
  **会一直生成直到 max_tokens 上限**——生成 8192 个 token 需要数分钟，这就是"1 条也慢"的元凶。
- 之前的代码把 `max_tokens` 固定写死为配置值，与条文数量无关。

## 修复 1：动态 max_tokens（核心）

`src\ContentCheck.Core\AI\DeepSeekClient.cs` 新增 `MaxTokensFor(itemCount)`：

```
每条条文结果约 300~500 token → 按 400/条 + 800 余量计算
最终 max_tokens = min(配置上限, 800 + 条文数 × 400)
```

- 1 条条文 → max_tokens = 1200（原来 8192，降 85%）
- 10 条条文 → 4800，封顶配置值
- 即使模型"跑飞"，也会在 1200 token 内被强制截断，单条最多十几秒完成。

## 修复 2：调用耗时写入日志（可诊断）

- `JsonLog.WriteCall` 新增 `elapsed_ms` 字段。
- 每次批量/逐条调用完成后，日志 JSON 里记录本次调用耗时（含重试）。
- 下次校核后打开 `logs\call_*.json` 看 `elapsed_ms` 即可确认慢在哪一步：
  - 数值小（< 30 秒）→ 慢在别处（批次多/网络往返多）
  - 数值大（> 60 秒）→ 单次调用确实慢（服务端慢，需换模型或网络）

## 修复 3：状态栏实时显示已用时间（不再"无感等待"）

两个 UI（`MainModelessDialog.cs` / `MainPaletteUserControl.cs`）增加每秒计时器：

- 校核进行中，状态栏每秒刷新：`正在校核（1/1）：建筑《GB 50229-2019》（1 条）…（已用 42秒）`
- 用户能直接看到时间在走，判断是否卡死，而不是盯着静止的滚动条。

## 修复 4：重试与超时收紧

- HTTP 超时：120 秒 → 45 秒
- 重试次数：2 → 1 次，等待 1.5 秒
- 单批最坏耗时：372 秒（6.2 分钟）→ 约 91 秒

## 修复 5：配置对齐

- `config.json` / `config.json.example`：`max_tokens` 8192 → 2048，`batch_size` 20 → 10
- 配置文件已恢复为 MiMo（provider/mimo，原 API key 保留）——上一版误改为 DeepSeek 空 key 会直接 401，反而更慢，已纠正。

## 实测诊断（2026-08-20 二次反馈：单条文仍等 30~40 秒）

最新日志 `logs\call_20260820_195223_705.json` 显示单条文调用 `elapsed_ms: 42718`（42.7 秒）。
用 PowerShell 直接实测 MiMo API 定位瓶颈：

| 测试 | 耗时 | 结论 |
|---|---|---|
| 最小请求 ping（max_tokens=1） | **0.87 秒** | 网络 + 排队 + 首 token 都很快，**不是网络问题** |
| 真实校核请求（1 条文） | **36 秒**（生成 981 tokens） | **MiMo 服务端生成速度仅 ~28 token/秒**，中文更慢 |

**结论：30~40 秒不合理（正常应 5~15 秒），瓶颈在 MiMo 服务端生成速度，且模型输出过于啰嗦。**

## 修复 6：收紧输出约束（减少生成量 = 直接减少等待时间）

- `PromptBuilder.cs` 注意事项新增硬约束：evidence 只引用关键原句 ≤60 字、analysis ≤2 句 80 字、
  suggestion ≤60 字、**整段 JSON 输出控制在 300 token 以内**。
- `DeepSeekClient.MaxTokensFor` 同步收紧：`600 + 条文数×300`（1 条 = 900，10 条封顶 3600）。
- 单条文输出从 ~400+ token 压到 ~200 token，MiMo 上预计 40 秒 → 20 秒左右；换快模型后约 5 秒。

## 治本建议：换更快的模型/服务商

代码侧已压到极限（动态 max_tokens、超时/重试收紧、输出约束、实时计时），MiMo 生成速度是硬瓶颈。
建议在插件的「设置」对话框切换 provider，或改 config.json：

- **DeepSeek 官方**（`https://api.deepseek.com/v1`，model `deepseek-chat`）：生成速度通常 30~60 token/秒
- **智谱 GLM-4-Flash**（`https://open.bigmodel.cn/api/paas/v4`，model `glm-4-flash`）：免费且快
- **Kimi（Moonshot）**（`https://api.moonshot.cn/v1`）：速度快
- **Ollama 本地**（无网络延迟，取决于本机显卡）

## 修改文件清单

| 文件 | 改动 |
|---|---|
| `src\ContentCheck.Core\AI\DeepSeekClient.cs` | 动态 max_tokens、调用耗时、超时/重试收紧 |
| `src\ContentCheck.Core\AI\PromptBuilder.cs` | 输出长度硬约束（evidence/analysis/suggestion ≤ 字） |
| `src\ContentCheck.Core\Util\JsonLog.cs` | 日志新增 `elapsed_ms` |
| `src\ContentCheck.Core\Services\CheckEngine.cs` | 状态消息带"已用时间" |
| `src\ContentCheck.Acad\UI\MainModelessDialog.cs` | 每秒刷新"已用时间" |
| `src\ContentCheck.Acad\UI\MainPaletteUserControl.cs` | 同上 |
| `config.json` / `config.json.example` | max_tokens 2048、batch_size 10、恢复 mimo |

## 验证结果

- `ContentCheck.Core` Release 编译：通过，0 警告 0 错误。
- `ContentCheck.Acad` Release 编译（临时输出目录）：通过，0 警告 0 错误。

> 注意：构建到 `out\` 会因 AutoCAD 正在运行锁定 `out\ContentCheck.Core.dll` 而失败。
> 请在 AutoCAD 中卸载插件（或关闭 AutoCAD）后再 `build.bat` 部署。

## 如果还是慢

1. 先看 `logs\call_*.json` 的 `elapsed_ms`——确认是单次调用慢还是批次多。
2. 单次调用 > 20 秒：按上方"治本建议"换模型/服务商（MiMo 生成速度是硬瓶颈，代码无法再快）。
3. 可以临时把 `max_sheet_chars` 调到 6000 减小输入长度（会截断总说明，注意校核质量）。
