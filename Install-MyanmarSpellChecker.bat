@echo off
REM ============================================================================
REM 1-Click Friendly Installer for Microsoft Word + Microsoft Excel
REM No Admin rights or PowerShell knowledge needed — just double-click!
REM ============================================================================
setlocal
echo Installing Myanmar Official Spell Checker for Microsoft Word and Excel...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0installer\Easy-Setup.ps1"
exit /b %ERRORLEVEL%

