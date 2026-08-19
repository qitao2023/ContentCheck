@echo off
setlocal
rem ============================================================
rem  ContentCheck build script (VS2022 MSBuild)
rem  Usage: build.bat
rem  Output: out\ContentCheck.Acad.dll + dependencies (NETLOAD)
rem          plus Import / Tests executables
rem ============================================================

set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set "ROOT=%~dp0"
set "SLN=%ROOT%ContentCheck.sln"

if not exist "%MSBUILD%" (
  echo [ERROR] VS2022 MSBuild not found: %MSBUILD%
  exit /b 1
)

echo [1/2] Restoring NuGet and building ...
"%MSBUILD%" "%SLN%" -t:Build -p:Configuration=Release -m -restore -v:minimal
if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)

echo [2/2] Verifying plugin output in out\ ...
if not exist "%ROOT%out\ContentCheck.Acad.dll" (
  echo [ERROR] out\ContentCheck.Acad.dll not found.
  exit /b 1
)
if not exist "%ROOT%out\x64\SQLite.Interop.dll" (
  echo [WARN] out\x64\SQLite.Interop.dll missing - AutoCAD x64 will fail to load provisions.db.
)

echo.
echo Build OK.
echo   Plugin : %ROOT%out\ContentCheck.Acad.dll
echo   Usage  : In AutoCAD 2020, NETLOAD then run CHECK
echo   Import : %ROOT%src\ContentCheck.Import\bin\Release\net472\ContentCheck.Import.exe
echo   Tests  : %ROOT%tests\ContentCheck.Tests\bin\Release\net472\ContentCheck.Tests.exe
endlocal
