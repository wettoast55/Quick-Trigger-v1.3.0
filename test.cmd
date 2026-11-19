@echo off
setlocal
rem Use paths relative to this script so the extracted folder can live anywhere.
cd /d "%~dp0"

rem Build first if the executable is missing.
if not exist "QuickTrigger.exe" call build.cmd
if not exist "QuickTrigger.exe" exit /b 1

rem Wait for the windowless test process before reading its report.
start "" /wait "QuickTrigger.exe" --self-test "%CD%\test-results.txt"
set "TEST_EXIT=%ERRORLEVEL%"
type "test-results.txt"
exit /b %TEST_EXIT%
