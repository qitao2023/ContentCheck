@echo off
setlocal

echo.
echo ========================================
echo   Set DEEPSEEK_API_KEY Environment Variable
echo ========================================
echo.
echo Please enter your DeepSeek API Key (starts with sk-):
echo.

set /p "KEY=API Key: "

if "%KEY%"=="" (
    echo.
    echo [ERROR] No input. Cancelled.
    pause
    exit /b 1
)

echo.
echo Setting environment variable...

reg add "HKCU\Environment" /v DEEPSEEK_API_KEY /t REG_SZ /d "%KEY%" /f >nul 2>&1

if errorlevel 1 (
    echo [ERROR] Failed to set.
    pause
    exit /b 1
)

echo.
echo [SUCCESS] DEEPSEEK_API_KEY has been set!
echo Restart AutoCAD to take effect.
echo.
pause
