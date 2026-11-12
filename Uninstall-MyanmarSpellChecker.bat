@echo off
REM ============================================================================
REM 1-Click Friendly Uninstaller for Microsoft Word + Microsoft Excel
REM ============================================================================
setlocal
echo Uninstalling Myanmar Official Spell Checker from Microsoft Word and Excel...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0installer\Easy-Setup.ps1" -Uninstall
exit /b %ERRORLEVEL%

