# ContentCheck UI 修改变更日志

## 版本 2.4.1 - 识别文字预览行点击定位

### 主要变更

- **预览区每行文字也可关联定位**：识别文字预览中点击任意一行，即可在 CAD 中选中该行文字及其自动绑定的图面实体
  （与结果表双击定位同一套 Handle 解析与高亮逻辑），状态栏提示「已选中「…」所在文字及 N 个关联图面实体」。
- **字符区间映射**：`UpdatePreview` 重建预览时逐行记录字符区间 → `TextLine` 映射（`PreviewLineSpan`），
  点击通过 `GetCharIndexFromPosition` 反查所属文字行；分段模式与纯文本回退模式都支持。
- **修改文件**：`src/ContentCheck.Acad/UI/MainModelessDialog.cs`、`MainPaletteUserControl.cs`

## 版本 2.4.0 - 文字与图面实体自动绑定

### 主要变更

- **文字 ↔ 图面实体自动空间绑定**：提取文字时同步扫描每个文字周围的非文字实体（管线 / 图块 / 表格 / 尺寸 / 填充等），
  按「距文字距离 ≤ 容差（取行字高与全图中位字高的较大者 × 3）」筛选，距离用曲线最近点（`GetClosestPointTo`，
  支持 Line/Polyline/Circle/Arc/Spline 等真实几何），其余实体回退包围盒距离；每条文字最多绑定 6 个最近实体。
  全图提取（`ExtractModel`）与框选提取（`CC_SELECTTEXT`）两条路径都生效，框选场景只在选中实体内绑定。
- **双击结果同时选中文字与关联实体**：原来只高亮证据文字实体本身；现在匹配到的文字及其绑定的图面实体一起
  `SetImpliedSelection` 选中（夹点可见），状态栏提示「已高亮 N 处文字及 M 个关联图面实体」。
- **识别文字预览标题显示绑定数**：`识别文字（N 行，M 段，关联 K 个实体）`，状态栏同步提示。
- **修复 Handle 十六进制解析**：`Handle.ToString()` 返回十六进制（如 `2F`），原 `long.TryParse` 按十进制解析，
  含 A–F 的 Handle 会静默定位失败；改为 `NumberStyles.HexNumber`。
- **证据定位逻辑收敛**：两套 UI 重复的 `FindEvidenceHandles` 合并到 `EvidenceLocator`（段落优先 + 逐行回退规则不变）。
- **修复 build.bat 过期**：解决方案文件已改为 `ContentCheck.slnx`，脚本仍引用不存在的 `.sln`，已修复。
- **修改文件**：
  - `src/ContentCheck.Acad/Dwg/TextLine.cs`（新增 `BoundEntity` 数据模型与 `TextLine.BoundEntities`）
  - `src/ContentCheck.Acad/Dwg/DwgTextExtractor.cs`（候选实体收集 + 自动空间绑定）
  - `src/ContentCheck.Acad/Dwg/EvidenceLocator.cs`（新增，共享证据定位）
  - `src/ContentCheck.Acad/UI/Highlighter.cs`（Handle 十六进制解析修复）
  - `src/ContentCheck.Acad/UI/MainModelessDialog.cs`、`MainPaletteUserControl.cs`（双击定位 + 预览标题 + 状态提示）
  - `build.bat`（解决方案引用修复）

## 版本 2.3.1 - 按钮与下拉框去彩色

### 主要变更

- **按钮全部改为中性灰白**：主按钮（框选文字 / 开始校核 / 保存等）由蓝色底改为浅灰底 + 细边框，次按钮保持白底 + 细边框，悬停统一为中灰，不再有蓝 / 浅蓝配色。
- **「规范条文详细…」去掉浅蓝底**：移除 `AccentSoft` 覆盖，恢复为中性次按钮样式。
- **下拉框去彩色**：统一白底、灰边框、深灰文字（`StyleCombo` 补充 `ForeColor`）。
- **修改文件**：
  - `src/ContentCheck.Acad/UI/UiTheme.cs`（新增 `BtnPrimaryBg` / `BtnHoverBg`，重写 `StyleButton` / `StyleCombo`）
  - `src/ContentCheck.Acad/UI/MainModelessDialog.cs`
  - `src/ContentCheck.Acad/UI/MainPaletteUserControl.cs`

## 版本 2.3.0 - 设置并入操作行

### 主要变更

- **取消顶栏「设置」行**：移除顶栏一整行（深蓝条 + 右侧「设置」按钮），对话框顶部直接是设置卡片，结果表获得更多空间。
- **「设置」按钮并入操作行**：操作按钮由 3 个变为 4 个一行 ——「设置 / 框选文字 / 开始校核 / 另存报告」，设置位于最前（次要样式）。
- **修改文件**：
  - `src/ContentCheck.Acad/UI/MainModelessDialog.cs`
  - `src/ContentCheck.Acad/UI/MainPaletteUserControl.cs`
  - `tools/UiPreview/Program.cs`

## 版本 2.2.0 - 条文勾选状态持久化

### 主要变更

- **记住上次的条文勾选**：规范条文树形菜单的勾选状态持久化到 `provision_selections.json`（配置文件同目录），AutoCAD 重启、关闭并重新打开面板后，仍能恢复上次的勾选，不再每次打开都重置。
- **修复「只勾 1 条」重开丢失**：根因是 `SyncGroup` 程序化同步组复选框时触发 `AfterCheck` 级联 `SetGroup`，把组内已设好的部分勾选清空。现加重入保护（`_suppressSync`），并区分「组节点级联整组」与「叶节点刷新组状态」两类事件。
- **修复「全不选」无法记忆**：此前"空集合"与"从未勾选"无法区分，导致用户全不选后再次打开又变回全选。现在无历史勾选（`null`）才默认全选，用户明确保存的空集合表示全不选。
- **新增文件**：
  - `src/ContentCheck.Core/Storage/ProvisionSelectionsStore.cs`：勾选状态 JSON 读写。
  - `src/ContentCheck.Acad/UI/ProvisionSelectionState.cs`：进程级共享持有者，非模态对话框与停靠面板共用同一份勾选并落盘。
  - `tests/PickerRepro/`：条文选择框回归测试（勾选应用 / 用户操作 / 确定收集 / 持久化回读，10 项）。
- **修改文件**：
  - `src/ContentCheck.Acad/UI/ProvisionPickerDialog.cs`：`ApplyChecks` 支持 `null` 初始值（默认全勾），空集合按用户选择处理；`OnAfterCheck`/`SyncGroup` 重入保护与级联修复。
  - `src/ContentCheck.Acad/UI/MainModelessDialog.cs`、`MainPaletteUserControl.cs`：改用 `ProvisionSelectionState` 读写勾选。
  - `tools/UiPreview/Program.cs`：预览工具传 `null` 保持默认全勾效果。

## 版本 2.1.0 - 精简操作区

### 主要变更

- **移除「提取全部文字」按钮**：删除 `_btnExtract` 及相关事件绑定，同时移除 `CC_EXTRACT` 命令（`Commands.ExtractAllText`）。
- **操作按钮改为一行**：「框选文字」「开始校核」「另存报告」三个按钮放在同一行，取消「开始校核」全宽主按钮 + 次按钮两行的旧布局。
- **设置卡片高度**：由 150 调整为 112，配合一行操作区。

## 版本 2.0.0 - 非模态对话框改造

### 发布日期
2024年

### 主要变更

#### 1. 界面形式改变
- **原实现**：使用 `PaletteSet`（停靠栏），固定在 AutoCAD 右侧
- **新实现**：使用 `MainModelessDialog`（非模态对话框），浮动窗口

#### 2. 新增文件
- `src/ContentCheck.Acad/UI/MainModelessDialog.cs`
  - 新的非模态对话框类
  - 继承自 `System.Windows.Forms.Form`
  - 保持与原有 `MainPaletteUserControl` 相同的功能

#### 3. 修改的文件
- `src/ContentCheck.Acad/Commands.cs`
  - 移除 `PaletteSet` 相关代码
  - 更新 `CHECK` 命令实现
  - 使用 `MainModelessDialog` 替代 `MainPaletteUserControl`

- `src/ContentCheck.Acad/AppExtension.cs`
  - 更新清理逻辑以处理新的对话框类型

- `tools/UiPreview/Program.cs`
  - 更新 UI 预览工具以使用新的对话框样式

### 技术细节

#### 对话框属性
```csharp
FormBorderStyle = FormBorderStyle.SizableToolWindow
ShowInTaskbar = false
TopMost = true
StartPosition = FormStartPosition.CenterScreen
MinimumSize = new Size(480, 600)
Size = new Size(520, 800)
```

#### 关闭行为
- 点击关闭按钮（×）时，对话框隐藏而不是关闭
- 再次调用 `CHECK` 命令会重新显示对话框
- 在 AutoCAD 终止时正确清理资源

#### 焦点管理
- 当需要与 AutoCAD 交互时，通过 `AcadApp.MainWindow.Focus()` 将焦点交还给 AutoCAD
- 使用 `SendStringToExecute` 在 AutoCAD 命令循环中执行命令

### 功能保持不变
- 所有原有功能完全保留
- 命令接口不变：`CHECK`、`CC_SELECTTEXT`、`CC_WRITECLAUSE`
- 设置对话框、条文选择对话框等保持不变

### 用户界面改进
- 对话框可以自由移动和调整大小
- 不占用 AutoCAD 的停靠区域
- 更灵活的窗口管理

### 兼容性
- 向后兼容：所有原有功能保持不变
- 命令接口不变
- 配置文件格式不变

### 已知限制
- 对话框关闭时隐藏而非销毁，可能占用少量内存
- 需要手动管理焦点切换

### 未来计划
- 添加对话框位置记忆功能
- 考虑添加快捷键支持
- 可能添加系统托盘图标

## 版本 1.0.0 - 初始版本

### 发布日期
2024年

### 功能
- 使用 `PaletteSet` 停靠栏界面
- 支持 AutoCAD 停靠和自动隐藏
- 所有核心校核功能

---

## 升级指南

### 从版本 1.0 升级到 2.0

1. **备份配置**：确保 `config.json` 和数据库文件已备份
2. **重新编译**：使用新代码重新编译插件
3. **重新加载**：在 AutoCAD 中重新加载插件
4. **测试功能**：验证所有功能正常工作

### 注意事项
- 命令接口保持不变，无需修改工作流程
- 所有配置和数据完全兼容
- 界面操作习惯可能需要适应

---

## 技术支持

如有问题或建议，请参考：
- `UI_CHANGES.md`：详细的技术说明
- `README_UI.md`：用户使用指南
- 项目 Issues：报告问题和功能请求