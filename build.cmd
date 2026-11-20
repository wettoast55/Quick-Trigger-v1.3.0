@echo off
setlocal
rem Build from this folder so source paths are portable.
cd /d "%~dp0"

rem The .NET Framework compiler is sufficient; no package downloads are needed.
set "COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%COMPILER%" (
  echo Install .NET Framework 4.8 to build PopQuiz.
  exit /b 1
)

rem Compile a 64-bit desktop application without debug symbols or embedded source paths.
"%COMPILER%" /nologo /target:winexe /platform:x64 /optimize+ /debug- /unsafe+ /warn:4 /warnaserror+ /win32manifest:app.manifest /out:PopQuiz.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\*.cs
if errorlevel 1 exit /b 1
echo Built PopQuiz.exe
