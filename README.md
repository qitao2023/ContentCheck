# ContentCheck — 图纸总说明规范校核（AutoCAD 2020 插件）

基于规范条文，用 AI 语义对比校核 AutoCAD 图纸《总说明》是否满足要求。覆盖建筑/给排水/暖通/国网等多个专业（专业由 Excel sheet 驱动，可扩展）。

## 流程

1. **导入条文**（一次性）：把 `00-资料\电网工程土建设计规范条文.xlsx` 清洗进 SQLite `provisions.db`。
2. **提取图纸文字**：插件实时读取当前图纸所有布局（含模型空间）的 TEXT/MTEXT。
3. **AI 校核**：按 (专业, 规范) 分批调用 DeepSeek，判定每条条文 符合/不符合/未涉及/无法判断，附原文证据与修改建议。
4. **结果展示**：停靠面板表格，双击定位图纸原文，可导出 Excel 报告。

## 构建

- 环境：VS2022 Community、AutoCAD 2020、.NET Framework 4.7.2（已装）
- `build.bat`：还原 NuGet 并编译 → `out\ContentCheck.Acad.dll`（首次需联网）

## 使用

```
1. 导入条文
   ContentCheck.Import.exe                # 读取 config.json 中的 excel_path → provisions.db
   （或在 AutoCAD 内执行命令 CC_IMPORT）

2. AutoCAD 2020
   NETLOAD → out\ContentCheck.Acad.dll
   输入 CHECK → 停靠面板出现
   选择图纸/布局（自动识别总说明）、勾选专业、选择模型 → 开始校核
   双击结果行 → 高亮图纸原文；另存报告 → xlsx/txt
```

辅助命令：`CC_SELECTTEXT` 框选区域提取文字，`CC_WRITECLAUSE` 写入条文。

## 配置（config.json）

| 字段 | 说明 |
|---|---|
| `api_key` | DeepSeek key；为空时回退环境变量 `DEEPSEEK_API_KEY` → `ANTHROPIC_AUTH_TOKEN` |
| `base_url` | 默认 `https://api.deepseek.com/v1`（OpenAI 兼容接口） |
| `model` | 核对模型 |
| `max_sheet_chars` | 总说明送入 AI 的最大字符数（超长保留头尾） |
| `batch_size` | 一次 AI 调用校核的条文数 |

## 目录结构

```
src\ContentCheck.Core\      领域逻辑（无 AutoCAD 依赖，可离线测试）
src\ContentCheck.Acad\      AutoCAD 插件（DWG 提取 / 停靠面板 / 导出）
src\ContentCheck.Import\    一次性 Excel→SQLite 导入工具
tests\ContentCheck.Tests\   离线自测
```

## 版本维护

插件针对 AutoCAD 2020（net472）编译。升级 AutoCAD 版本时：改 `TargetFramework` 与 csproj 中的 AutoCAD 程序集 HintPath 后重新编译即可（.NET API 向后兼容性良好）。

## 目录说明

- `logs\`：AI 交互日志（每次调用一个 JSON，可审计，不含 API key）
- `provisions.db`：条文数据库（导入生成）
- `out\`：插件部署目录（需含 `SQLite.Interop.dll` x64）
