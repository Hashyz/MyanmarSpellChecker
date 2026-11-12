@echo off
REM ============================================================================
REM 1-Click Friendly Installer for Microsoft Word + Microsoft Excel
REM No Admin rights, no .NET SDK, and no PowerShell knowledge needed!
REM ============================================================================
setlocal

if not exist "%~dp0installer\Easy-Setup.ps1" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -Command "Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show('Please extract the .ZIP file first!`r`n`r`nRight-click the downloaded .zip file -> click ''Extract All...'' -> then double-click Install-MyanmarSpellChecker.bat inside the extracted folder.', 'Myanmar Official Spell Checker - Extract ZIP First', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)"
    exit /b 1
)

echo Installing Myanmar Official Spell Checker for Microsoft Word and Excel...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0installer\Easy-Setup.ps1"
exit /b %ERRORLEVEL%
