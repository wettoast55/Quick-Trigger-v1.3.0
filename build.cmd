@echo off
setlocal
cd /d "%~dp0"
set "COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%COMPILER%" (
  echo The .NET Framework C# compiler was not found. Install .NET Framework 4.8.
  exit /b 1
)
"%COMPILER%" /nologo /target:winexe /platform:x64 /optimize+ /unsafe+ /warn:4 /win32manifest:app.manifest /out:ColorTriggerLab.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\*.cs
if errorlevel 1 exit /b 1
echo Built ColorTriggerLab.exe
