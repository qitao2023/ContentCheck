@echo off
setlocal
rem Offline self-tests: Excel parser / SQLite / AI JSON / prompts (no AutoCAD needed)
set "ROOT=%~dp0"
set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
set "TESTEXE=%ROOT%tests\ContentCheck.Tests\bin\Release\net472\ContentCheck.Tests.exe"

if not exist "%TESTEXE%" (
  "%MSBUILD%" "%ROOT%ContentCheck.slnx" -t:Build -p:Configuration=Release -m -restore -v:minimal
  if errorlevel 1 exit /b 1
)

"%TESTEXE%" %*
exit /b %errorlevel%
