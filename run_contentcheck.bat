@echo off
chcp 936 >nul 2>&1
setlocal
REM ============================================================
REM  ContentCheck 一键测试
REM  自动完成：编译插件 -> 启动 AutoCAD 2020 -> 自动加载插件 -> 执行 CHECK
REM  用法：双击本文件（或在命令行运行）
REM  注意：本文件必须为 ANSI/GBK（代码页 936）+ CRLF 行尾，
REM        不要保存为 UTF-8，否则 cmd 会按 GBK 误读中文导致解析错误。
REM  警告：若 AutoCAD 正在运行会被直接强制关闭（未保存的图纸不会保存）。
REM ============================================================

REM --- 可修改配置（如 AutoCAD 安装位置不同请改这里） ---
set "ACADEXE=C:\Program Files\Autodesk\AutoCAD 2020\acad.exe"
set "ROOT=%~dp0"
set "CSPROJ=%ROOT%src\ContentCheck.Acad\ContentCheck.Acad.csproj"
set "DLL=%ROOT%out\ContentCheck.Acad.dll"
set "SCRIPT=%TEMP%\cc_autoload.scr"
REM -----------------------------------------------------------

REM 1) 检查 acad.exe
if not exist "%ACADEXE%" (
    echo [错误] 未找到 AutoCAD 2020：%ACADEXE%
    echo        请修改本文件顶部 ACADEXE 变量为实际路径。
    pause
    exit /b 1
)

REM 2) 若 AutoCAD 正在运行，插件 DLL 会被锁定，直接强制关闭后重新编译
tasklist /FI "IMAGENAME eq acad.exe" 2>nul | findstr /I "acad.exe" >nul
if not errorlevel 1 (
    echo [提示] 检测到 AutoCAD 正在运行，自动强制关闭后继续...
    taskkill /IM acad.exe /F >nul 2>nul
    %SystemRoot%\System32\timeout.exe /t 3 /nobreak >nul
)

REM 3) 重新编译插件到 out\
echo [1/3] 正在编译插件...
dotnet build "%CSPROJ%" -v q -nologo
if errorlevel 1 (
    echo [错误] 编译失败，请查看上方错误信息。
    pause
    exit /b 1
)
if not exist "%DLL%" (
    echo [错误] 未找到编译产物：%DLL%
    pause
    exit /b 1
)

REM 4) 生成自动加载脚本：关文件对话框 -> NETLOAD -> 恢复 -> 执行 CHECK
echo [2/3] 正在生成自动加载脚本...
>  "%SCRIPT%" echo _FILEDIA 0
>> "%SCRIPT%" echo _NETLOAD "%DLL%"
>> "%SCRIPT%" echo _FILEDIA 1
>> "%SCRIPT%" echo CHECK

REM 5) 启动 AutoCAD
echo [3/3] 正在启动 AutoCAD 2020...
start "" "%ACADEXE%" /b "%SCRIPT%"
echo 完成。AutoCAD 启动后将自动加载插件并打开校核面板（CHECK）。
endlocal
