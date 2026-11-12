@echo off
REM ============================================================================
REM 1-Click Friendly Uninstaller for Microsoft Word + Microsoft Excel
REM ============================================================================
setlocal

if not exist "%~dp0installer\Easy-Setup.ps1" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -Command "Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show('Please extract the .ZIP file first before running Uninstall-MyanmarSpellChecker.bat.', 'Myanmar Official Spell Checker - Extract ZIP First', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)"
    exit /b 1
)

echo Uninstalling Myanmar Official Spell Checker from Microsoft Word and Excel...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0installer\Easy-Setup.ps1" -Uninstall
exit /b %ERRORLEVEL%
