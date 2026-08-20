// ContentCheck 非模态对话框演示脚本
// 此脚本展示如何在 AutoCAD 中使用新的非模态对话框

using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

namespace ContentCheck.Demo
{
    public class ModelessDialogDemo
    {
        // 演示1：基本对话框操作
        [CommandMethod("DEMO_CHECK")]
        public static void DemoCheck()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n=== ContentCheck 非模态对话框演示 ===");
            doc.Editor.WriteMessage("\n1. 正在打开校核对话框...");

            // 调用 CHECK 命令打开对话框
            doc.SendStringToExecute("CHECK\n", true, false, false);

            doc.Editor.WriteMessage("\n2. 对话框已打开！");
            doc.Editor.WriteMessage("\n   - 对话框现在是浮动窗口，可以自由移动");
            doc.Editor.WriteMessage("\n   - 点击关闭按钮(×)会隐藏对话框");
            doc.Editor.WriteMessage("\n   - 再次输入 CHECK 命令会重新显示");
            doc.Editor.WriteMessage("\n\n演示完成！");
        }

        // 演示2：对话框交互
        [CommandMethod("DEMO_INTERACT")]
        public static void DemoInteract()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n=== 对话框交互演示 ===");
            doc.Editor.WriteMessage("\n1. 打开对话框...");
            doc.SendStringToExecute("CHECK\n", true, false, false);

            doc.Editor.WriteMessage("\n2. 现在您可以：");
            doc.Editor.WriteMessage("\n   - 移动对话框到任意位置");
            doc.Editor.WriteMessage("\n   - 调整对话框大小");
            doc.Editor.WriteMessage("\n   - 同时操作 AutoCAD 图纸");
            doc.Editor.WriteMessage("\n   - 点击'框选文字'按钮进行区域选择");

            doc.Editor.WriteMessage("\n\n3. 尝试以下操作：");
            doc.Editor.WriteMessage("\n   a) 在对话框中选择专业");
            doc.Editor.WriteMessage("\n   b) 点击'框选文字'按钮");
            doc.Editor.WriteMessage("\n   c) 在图纸中框选一个区域");
            doc.Editor.WriteMessage("\n   d) 返回对话框查看结果");

            doc.Editor.WriteMessage("\n\n演示提示：对话框会保持在AutoCAD窗口上方，");
            doc.Editor.WriteMessage("\n但您仍然可以正常操作AutoCAD。");
        }

        // 演示3：对话框状态管理
        [CommandMethod("DEMO_STATE")]
        public static void DemoState()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n=== 对话框状态管理演示 ===");

            // 检查对话框状态
            if (Commands.ModelessDialog != null && Commands.ModelessDialog.Visible)
            {
                doc.Editor.WriteMessage("\n对话框当前状态：可见");
                doc.Editor.WriteMessage("\n正在隐藏对话框...");
                Commands.ModelessDialog.Hide();
                doc.Editor.WriteMessage("\n对话框已隐藏。");
            }
            else
            {
                doc.Editor.WriteMessage("\n对话框当前状态：隐藏或未创建");
                doc.Editor.WriteMessage("\n正在显示对话框...");
                doc.SendStringToExecute("CHECK\n", true, false, false);
                doc.Editor.WriteMessage("\n对话框已显示。");
            }

            doc.Editor.WriteMessage("\n\n状态管理说明：");
            doc.Editor.WriteMessage("\n- 对话框使用隐藏/显示模式，而不是创建/销毁");
            doc.Editor.WriteMessage("\n- 这样可以保持对话框的状态和位置");
            doc.Editor.WriteMessage("\n- 在AutoCAD关闭时，对话框会自动清理");
        }

        // 演示4：与AutoCAD的交互
        [CommandMethod("DEMO_CAD_INTERACT")]
        public static void DemoCadInteract()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n=== AutoCAD 交互演示 ===");
            doc.Editor.WriteMessage("\n此演示展示对话框如何与AutoCAD交互。");

            // 打开对话框
            doc.SendStringToExecute("CHECK\n", true, false, false);

            doc.Editor.WriteMessage("\n\n交互流程说明：");
            doc.Editor.WriteMessage("\n1. 当您点击'框选文字'按钮时：");
            doc.Editor.WriteMessage("\n   - 对话框会将焦点交给AutoCAD");
            doc.Editor.WriteMessage("\n   - AutoCAD会显示选择提示");
            doc.Editor.WriteMessage("\n   - 您可以在图纸中框选区域");
            doc.Editor.WriteMessage("\n   - 选择完成后，结果会返回到对话框");

            doc.Editor.WriteMessage("\n\n2. 当您点击'规范条文详细…'按钮时：");
            doc.Editor.WriteMessage("\n   - 会打开条文选择对话框");
            doc.Editor.WriteMessage("\n   - 您可以选择要校核的条文");
            doc.Editor.WriteMessage("\n   - 选择完成后，可以写入CAD图纸");

            doc.Editor.WriteMessage("\n\n3. 焦点管理：");
            doc.Editor.WriteMessage("\n   - 对话框和AutoCAD共享焦点");
            doc.Editor.WriteMessage("\n   - 需要时会自动切换焦点");
            doc.Editor.WriteMessage("\n   - 确保用户体验流畅");

            doc.Editor.WriteMessage("\n\n演示完成！请尝试在对话框中操作。");
        }

        // 演示5：高级功能
        [CommandMethod("DEMO_ADVANCED")]
        public static void DemoAdvanced()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.Editor.WriteMessage("\n=== 高级功能演示 ===");

            // 打开对话框
            doc.SendStringToExecute("CHECK\n", true, false, false);

            doc.Editor.WriteMessage("\n\n高级功能说明：");

            doc.Editor.WriteMessage("\n1. 多文档支持：");
            doc.Editor.WriteMessage("\n   - 对话框可以处理多个打开的图纸");
            doc.Editor.WriteMessage("\n   - 每个图纸的状态独立管理");
            doc.Editor.WriteMessage("\n   - 切换图纸时会自动更新内容");

            doc.Editor.WriteMessage("\n\n2. 后台处理：");
            doc.Editor.WriteMessage("\n   - AI校核在后台线程执行");
            doc.Editor.WriteMessage("\n   - 不会阻塞AutoCAD操作");
            doc.Editor.WriteMessage("\n   - 进度条显示处理状态");

            doc.Editor.WriteMessage("\n\n3. 结果管理：");
            doc.Editor.WriteMessage("\n   - 校核结果可以导出为Excel或文本");
            doc.Editor.WriteMessage("\n   - 支持双击定位到图纸中的相关文字");
            doc.Editor.WriteMessage("\n   - 结果可以高亮显示在图纸中");

            doc.Editor.WriteMessage("\n\n4. 配置管理：");
            doc.Editor.WriteMessage("\n   - 支持多种AI模型配置");
            doc.Editor.WriteMessage("\n   - 可以自定义校核参数");
            doc.Editor.WriteMessage("\n   - 配置可以保存和加载");

            doc.Editor.WriteMessage("\n\n请尝试使用对话框中的各种功能。");
            doc.Editor.WriteMessage("\n如有问题，请查看 README_UI.md 文档。");
        }
    }
}

// 使用说明：
// 1. 将此文件添加到 ContentCheck.Acad 项目中
// 2. 重新编译项目
// 3. 在 AutoCAD 中加载插件
// 4. 使用以下命令进行演示：
//    - DEMO_CHECK: 基本对话框操作
//    - DEMO_INTERACT: 对话框交互演示
//    - DEMO_STATE: 状态管理演示
//    - DEMO_CAD_INTERACT: AutoCAD交互演示
//    - DEMO_ADVANCED: 高级功能演示

// 注意：此文件仅用于演示目的，实际使用时请参考主项目代码