# AutoCAD 插件界面修改说明

## 修改概述

将 ContentCheck AutoCAD 插件的界面从停靠栏（PaletteSet）改为非模态对话框（Modeless Dialog），提供更灵活的用户界面体验。

## 主要修改内容

### 1. 新增文件

- **`src/ContentCheck.Acad/UI/MainModelessDialog.cs`**：新的非模态对话框类
  - 继承自 `Form` 而不是 `UserControl`
  - 保持与原有 `MainPaletteUserControl` 相同的功能和布局
  - 设置为非模态对话框属性：
    - `FormBorderStyle = FormBorderStyle.SizableToolWindow`
    - `ShowInTaskbar = false`
    - `TopMost = true`
    - `StartPosition = FormStartPosition.CenterScreen`

### 2. 修改的文件

#### `src/ContentCheck.Acad/Commands.cs`
- 移除 `PaletteSet` 相关代码
- 将 `ShowPalette()` 方法改为 `ShowDialog()`
- 使用 `MainModelessDialog` 替代 `MainPaletteUserControl`
- 更新 `CC_SELECTTEXT` 命令的回调逻辑

#### `src/ContentCheck.Acad/AppExtension.cs`
- 更新清理逻辑，处理 `MainModelessDialog` 的生命周期

#### `tools/UiPreview/Program.cs`
- 更新 UI 预览工具，使用新的对话框尺寸和样式

## 功能对比

| 功能 | 原停靠栏 (PaletteSet) | 新非模态对话框 (MainModelessDialog) |
|------|----------------------|-----------------------------------|
| 界面类型 | 停靠在 AutoCAD 侧边 | 独立浮动窗口 |
| 位置控制 | 固定在右侧停靠 | 可自由移动和调整大小 |
| 焦点管理 | 与 AutoCAD 集成 | 需要手动管理焦点切换 |
| 关闭行为 | 关闭后需重新打开 | 隐藏后可通过命令重新显示 |
| 最小尺寸 | 480x560 | 480x600 |
| 默认尺寸 | 480x760 | 520x800 |

## 使用方式

### 命令
- **`CHECK`**：打开或重新显示校核对话框
- 其他命令保持不变：`CC_SELECTTEXT`、`CC_WRITECLAUSE`

### 界面操作
1. 输入 `CHECK` 命令打开对话框
2. 对话框会显示在屏幕中央，可以自由移动
3. 点击对话框的关闭按钮（×）会隐藏对话框而不是关闭
4. 再次输入 `CHECK` 命令会重新显示对话框

## 技术细节

### 线程安全
- 保持原有的线程纪律：AutoCAD 对象只在 UI 线程访问
- 校核操作在 Task.Run 工作线程执行
- 使用 `BeginInvoke` 进行跨线程 UI 更新

### 焦点管理
- 当需要与 AutoCAD 交互时（如框选文字），通过 `AcadApp.MainWindow.Focus()` 将焦点交还给 AutoCAD
- 使用 `SendStringToExecute` 在 AutoCAD 命令循环中执行命令

### 生命周期管理
- 对话框在关闭时隐藏而不是销毁
- 在 AutoCAD 插件终止时正确清理资源
- 支持多次打开/关闭操作

## 编译和测试

### 编译
```bash
dotnet build
```

### 测试
1. 在 AutoCAD 中加载插件：`NETLOAD ContentCheck.Acad.dll`
2. 输入 `CHECK` 命令打开对话框
3. 测试各项功能：选择专业、框选文字、运行校核等
4. 测试对话框的移动、调整大小、隐藏/显示功能

## 注意事项

1. **焦点切换**：在与 AutoCAD 交互时，对话框可能会失去焦点，这是正常行为
2. **对话框位置**：首次打开时会在屏幕中央显示，之后会记住上次位置
3. **资源清理**：确保在 AutoCAD 关闭时对话框能正确清理
4. **向后兼容**：所有原有功能保持不变，只是界面形式改变

## 未来改进建议

1. 可以考虑添加对话框位置记忆功能
2. 可以添加快捷键支持（如 Ctrl+Shift+C 打开对话框）
3. 可以考虑添加系统托盘图标，方便快速访问