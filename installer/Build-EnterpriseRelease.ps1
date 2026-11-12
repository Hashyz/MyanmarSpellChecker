param(
    [string]$CertSubject = "CN=Myanmar Official Spell Checker (B2G Enterprise)"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsDir = Join-Path $repoRoot "artifacts"
$releasePkgDir = Join-Path $artifactsDir "release-package"

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " Building MyanmarSpellChecker B2G Enterprise Suite (Word + Excel)" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

# 1. Build Solution in Release Mode (AnyCPU, .NET Framework 4.8)
Write-Host "[1/5] Compiling Release binaries (Core + WordAddIn + ExcelAddIn)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $releasePkgDir -Force | Out-Null

$wordReleaseBinDir  = Join-Path $repoRoot "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48"
$excelReleaseBinDir = Join-Path $repoRoot "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48"

function Test-FileLocked([string]$PathToTest) {
    if (-not (Test-Path $PathToTest)) { return $false }
    try {
        $fs = [System.IO.File]::Open($PathToTest, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $fs.Close()
        return $false
    } catch {
        return $true
    }
}

$wordBinLocked  = Test-FileLocked (Join-Path $wordReleaseBinDir "MyanmarSpellChecker.Core.dll")
$excelBinLocked = Test-FileLocked (Join-Path $excelReleaseBinDir "MyanmarSpellChecker.Core.dll")

if (-not $wordBinLocked -and -not $excelBinLocked) {
    & dotnet build (Join-Path $repoRoot "MyanmarSpellChecker.sln") -c Release /p:DebugType=none /p:DebugSymbols=false /p:CopyRetryCount=1
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
    $wordBuildOutDir  = $wordReleaseBinDir
    $excelBuildOutDir = $excelReleaseBinDir
} else {
    Write-Host "    [Note] Active Office process detected; building unlocked Release outputs cleanly..." -ForegroundColor Yellow
    & dotnet build (Join-Path $repoRoot "tests\MyanmarSpellChecker.Core.Tests\MyanmarSpellChecker.Core.Tests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Core/Tests build failed." }

    if (-not $wordBinLocked) {
        & dotnet build (Join-Path $repoRoot "src\MyanmarSpellChecker.WordAddIn\MyanmarSpellChecker.WordAddIn.csproj") -c Release /p:DebugType=none /p:DebugSymbols=false /p:CopyRetryCount=1
        if ($LASTEXITCODE -ne 0) { throw "WordAddIn build failed." }
        $wordBuildOutDir = $wordReleaseBinDir
    } else {
        $pkgWordLocked = (Test-FileLocked (Join-Path $releasePkgDir "MyanmarSpellChecker.WordAddIn.dll")) -or (Test-FileLocked (Join-Path $releasePkgDir "MyanmarSpellChecker.Core.dll"))
        $wordFallbackDir = if (-not $pkgWordLocked) { $releasePkgDir } else { Join-Path $artifactsDir "staged-word" }
        New-Item -ItemType Directory -Path $wordFallbackDir -Force | Out-Null
        & dotnet build (Join-Path $repoRoot "src\MyanmarSpellChecker.WordAddIn\MyanmarSpellChecker.WordAddIn.csproj") -c Release -o $wordFallbackDir /p:DebugType=none /p:DebugSymbols=false /p:CopyRetryCount=1
        if ($LASTEXITCODE -ne 0) { throw "WordAddIn fallback build failed." }
        $wordBuildOutDir = $wordFallbackDir
    }

    if (-not $excelBinLocked) {
        & dotnet build (Join-Path $repoRoot "src\MyanmarSpellChecker.ExcelAddIn\MyanmarSpellChecker.ExcelAddIn.csproj") -c Release /p:DebugType=none /p:DebugSymbols=false /p:CopyRetryCount=1
        if ($LASTEXITCODE -ne 0) { throw "ExcelAddIn build failed." }
        $excelBuildOutDir = $excelReleaseBinDir
    } else {
        $pkgExcelLocked = (Test-FileLocked (Join-Path $releasePkgDir "MyanmarSpellChecker.ExcelAddIn.dll")) -or (Test-FileLocked (Join-Path $releasePkgDir "MyanmarSpellChecker.Core.dll"))
        $excelFallbackDir = if (-not $pkgExcelLocked) { $releasePkgDir } else { Join-Path $artifactsDir "staged-excel" }
        New-Item -ItemType Directory -Path $excelFallbackDir -Force | Out-Null
        & dotnet build (Join-Path $repoRoot "src\MyanmarSpellChecker.ExcelAddIn\MyanmarSpellChecker.ExcelAddIn.csproj") -c Release -o $excelFallbackDir /p:DebugType=none /p:DebugSymbols=false /p:CopyRetryCount=1
        if ($LASTEXITCODE -ne 0) { throw "ExcelAddIn fallback build failed." }
        $excelBuildOutDir = $excelFallbackDir
    }
}

# Locate or create B2G Enterprise Code-Signing Certificate
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$storeScope = if ($isAdmin) { "LocalMachine" } else { "CurrentUser" }
$myStorePath = "Cert:\$storeScope\My"
$cert = Get-ChildItem $myStorePath | Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation $myStorePath `
        -NotAfter (Get-Date).AddYears(5)
}

# Sign Core, WordAddIn, and ExcelAddIn in build dirs and copy to release-package
$signTargets = @(
    @{ Dir = $wordBuildOutDir;  Dll = "MyanmarSpellChecker.Core.dll" },
    @{ Dir = $wordBuildOutDir;  Dll = "MyanmarSpellChecker.WordAddIn.dll" },
    @{ Dir = $excelBuildOutDir; Dll = "MyanmarSpellChecker.Core.dll" },
    @{ Dir = $excelBuildOutDir; Dll = "MyanmarSpellChecker.ExcelAddIn.dll" }
)

foreach ($t in $signTargets) {
    $p = Join-Path $t.Dir $t.Dll
    if ((Test-Path $p) -and -not (Test-FileLocked $p)) {
        $sig = Set-AuthenticodeSignature -FilePath $p -Certificate $cert -HashAlgorithm SHA256
        Write-Host "    Signed $($t.Dll) ($($t.Dir)) -> Status: $($sig.Status)" -ForegroundColor Green
    }
    $pkgTarget = Join-Path $releasePkgDir $t.Dll
    if ([System.IO.Path]::GetFullPath($p) -ne [System.IO.Path]::GetFullPath($pkgTarget)) {
        try {
            Copy-Item $p $pkgTarget -Force -ErrorAction Stop
        } catch {
            Write-Host "    [Note] $pkgTarget is currently loaded by MS Office; keeping signed output at $p" -ForegroundColor Yellow
        }
    }
}

# 2. Compile or Verify Encrypted AES-256 + HMAC-SHA256 Dictionary (mlc_thatpon.db)
Write-Host "[2/5] Preparing AES-256 + HMAC-SHA256 Encrypted 'မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း' Database..." -ForegroundColor Cyan

$masterJsonPath = Join-Path $repoRoot "data\mlc_thatpon_master.json"
$sourceDataDb   = Join-Path $repoRoot "data\mlc_thatpon.db"
$sampleCsvPath  = Join-Path $repoRoot "data\mlc_thatpon_sample_import.csv"
$outDbPath      = Join-Path $releasePkgDir "mlc_thatpon.db"
$password       = "MM-GOV-THATPON-SQLCIPHER-2026!"

$coreLoadDir = if (-not (Test-FileLocked (Join-Path $excelBuildOutDir "MyanmarSpellChecker.Core.dll"))) { $excelBuildOutDir } else { $wordBuildOutDir }
$coreAsm  = [System.Reflection.Assembly]::LoadFrom((Join-Path $coreLoadDir "MyanmarSpellChecker.Core.dll"))
$repoType = $coreAsm.GetType("MyanmarSpellChecker.Core.Dictionary.EncryptedDictionaryRepository")
$seedType = $coreAsm.GetType("MyanmarSpellChecker.Core.Dictionary.SeedDictionaryProvider")

if ((Test-Path $masterJsonPath) -and ((Get-Item $masterJsonPath).Length -gt 5000000)) {
    $masterModel = $repoType::ParseFromJsonFile($masterJsonPath)
    $seedEntries = $seedType::GetOfficialSeedEntries()
    $seedRules   = $seedType::GetOfficialOrthographyRules()

    foreach ($se in $seedEntries) { $masterModel.Entries.Add($se) }
    foreach ($sr in $seedRules)   { $masterModel.OrthographyRules.Add($sr) }

    if (Test-Path $sampleCsvPath) {
        $csvEntries = $repoType::ParseEntriesFromCsv($sampleCsvPath)
        foreach ($ce in $csvEntries) { $masterModel.Entries.Add($ce) }
    }

    $repoInstance = New-Object MyanmarSpellChecker.Core.Dictionary.EncryptedDictionaryRepository($outDbPath)
    $repoInstance.CompileEncryptedDatabase($password, $masterModel)
} elseif ((Test-Path $sourceDataDb) -and ((Get-Item $sourceDataDb).Length -gt 5000000)) {
    Write-Host "    [Note] Master JSON not present (fresh git clone); using tracked full encrypted data\mlc_thatpon.db..." -ForegroundColor Cyan
    Copy-Item $sourceDataDb $outDbPath -Force
    $repoInstance = New-Object MyanmarSpellChecker.Core.Dictionary.EncryptedDictionaryRepository($outDbPath)
} else {
    throw "Full dictionary source not found! Expected data\mlc_thatpon.db (>5 MB) or data\mlc_thatpon_master.json."
}

$verifiedTrie = $repoInstance.LoadIntoTrie($password)
if ($verifiedTrie.WordCount -lt 50000) {
    throw "Refusing to package incomplete seed database ($($verifiedTrie.WordCount) words < 50,000 minimum)!"
}

Copy-Item $outDbPath (Join-Path $artifactsDir "mlc_thatpon.db") -Force
Write-Host "[+] Encrypted Database verified: $outDbPath ($($verifiedTrie.WordCount) unique headwords, $($verifiedTrie.RuleCount) rules, $($verifiedTrie.ContextualRuleCount) context rules, $($verifiedTrie.BigramCount) bigrams)" -ForegroundColor Green

# 3. Export Enterprise CA Certificate & Sync mlc_thatpon.db / license.lic + Sign Package DLLs
Write-Host "[3/5] Verifying Release Package Signatures & Syncing Dictionary..." -ForegroundColor Cyan
$cerPath = Join-Path $releasePkgDir "MyanmarSpellChecker-EnterpriseCA.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

foreach ($legalFile in @("LICENSE", "NOTICE", "CITATION.cff")) {
    $srcLegal = Join-Path $repoRoot $legalFile
    if (Test-Path $srcLegal) {
        Copy-Item $srcLegal (Join-Path $releasePkgDir $legalFile) -Force
    }
}

foreach ($binDir in @($wordReleaseBinDir, $excelReleaseBinDir)) {
    if (Test-Path $binDir) {
        try { Copy-Item $outDbPath (Join-Path $binDir "mlc_thatpon.db") -Force -ErrorAction Stop } catch { }
        if (Test-Path (Join-Path $releasePkgDir "license.lic")) {
            try { Copy-Item (Join-Path $releasePkgDir "license.lic") (Join-Path $binDir "license.lic") -Force -ErrorAction Stop } catch { }
        }
    }
}

foreach ($dll in @("MyanmarSpellChecker.Core.dll", "MyanmarSpellChecker.WordAddIn.dll", "MyanmarSpellChecker.ExcelAddIn.dll")) {
    $dllPath = Join-Path $releasePkgDir $dll
    if ((Test-Path $dllPath) -and -not (Test-FileLocked $dllPath)) {
        try {
            $sig = Set-AuthenticodeSignature -FilePath $dllPath -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop
            Write-Host "    Signed package $dll -> Status: $($sig.Status)" -ForegroundColor Green
        } catch { }
    }
}

# 4. Generate Silent GPO Deployment Script (Install-MinistryGPO.cmd) for BOTH Word AND Excel!
Write-Host "[4/5] Generating Unified Word + Excel Silent Active Directory GPO Deployment Script..." -ForegroundColor Cyan

$installCmd = @'
@echo off
REM ============================================================================
REM Silent Active Directory GPO Deployment Script (Runs as SYSTEM / Admin)
REM Target: C:\Program Files\MyanmarSpellChecker (Microsoft Word + Microsoft Excel)
REM ============================================================================
setlocal
set "INSTALL_DIR=%ProgramFiles%\MyanmarSpellChecker"
set "DATA_DIR=%ProgramData%\MyanmarSpellChecker"

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"

REM 1. Trust Enterprise Publisher Certificate in LocalMachine Root & TrustedPublisher
certutil -addstore -f "Root" "%~dp0MyanmarSpellChecker-EnterpriseCA.cer" >nul 2>&1
certutil -addstore -f "TrustedPublisher" "%~dp0MyanmarSpellChecker-EnterpriseCA.cer" >nul 2>&1

REM 2. Copy Signed Binaries (Core + WordAddIn + ExcelAddIn) & Encrypted Dictionary
copy /Y "%~dp0MyanmarSpellChecker.Core.dll" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0MyanmarSpellChecker.WordAddIn.dll" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0MyanmarSpellChecker.ExcelAddIn.dll" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0mlc_thatpon.db" "%INSTALL_DIR%\" >nul
copy /Y "%~dp0mlc_thatpon.db" "%DATA_DIR%\" >nul
if exist "%~dp0license.lic" copy /Y "%~dp0license.lic" "%DATA_DIR%\" >nul

REM 3. Register COM Add-ins (Word + Excel) for both 64-bit and 32-bit MS Office
if exist "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" (
    "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /codebase "%INSTALL_DIR%\MyanmarSpellChecker.WordAddIn.dll" /silent
    "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /codebase "%INSTALL_DIR%\MyanmarSpellChecker.ExcelAddIn.dll" /silent
)
if exist "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe" (
    "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe" /codebase "%INSTALL_DIR%\MyanmarSpellChecker.WordAddIn.dll" /silent
    "%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe" /codebase "%INSTALL_DIR%\MyanmarSpellChecker.ExcelAddIn.dll" /silent
)

REM 4. Register Office Word Addins Registry Keys (64-bit & 32-bit WOW6432Node)
reg add "HKLM\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "FriendlyName" /t REG_SZ /d "Myanmar Official Spell Checker" /f >nul
reg add "HKLM\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "Description" /t REG_SZ /d "100% Offline Myanmar Spell and Grammar Checker (Word)" /f >nul
reg add "HKLM\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "LoadBehavior" /t REG_DWORD /d 3 /f >nul

reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "FriendlyName" /t REG_SZ /d "Myanmar Official Spell Checker" /f >nul
reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "Description" /t REG_SZ /d "100% Offline Myanmar Spell and Grammar Checker (Word)" /f >nul
reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /v "LoadBehavior" /t REG_DWORD /d 3 /f >nul
reg delete "HKCU\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" /f >nul 2>&1

REM 5. Register Office Excel Addins Registry Keys (64-bit & 32-bit WOW6432Node)
reg add "HKLM\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "FriendlyName" /t REG_SZ /d "Myanmar Official Spell Checker (Excel)" /f >nul
reg add "HKLM\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "Description" /t REG_SZ /d "100% Offline Formula-Safe Myanmar Spell and Grammar Checker (Excel)" /f >nul
reg add "HKLM\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "LoadBehavior" /t REG_DWORD /d 3 /f >nul

reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "FriendlyName" /t REG_SZ /d "Myanmar Official Spell Checker (Excel)" /f >nul
reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "Description" /t REG_SZ /d "100% Offline Formula-Safe Myanmar Spell and Grammar Checker (Excel)" /f >nul
reg add "HKLM\Software\WOW6432Node\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /v "LoadBehavior" /t REG_DWORD /d 3 /f >nul
reg delete "HKCU\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" /f >nul 2>&1

exit /b 0
'@

Set-Content -Path (Join-Path $releasePkgDir "Install-MinistryGPO.cmd") -Value $installCmd -Encoding ASCII

# Also bundle 1-Click Non-Technical User Installer (Easy-Setup.ps1 + Install-EasyUser.bat + Uninstall-EasyUser.bat)
$easySetupSrc = Join-Path $PSScriptRoot "Easy-Setup.ps1"
if (Test-Path $easySetupSrc) {
    Copy-Item $easySetupSrc (Join-Path $releasePkgDir "Easy-Setup.ps1") -Force
}

$easyBat = @'
@echo off
REM ============================================================================
REM 1-Click Friendly Setup (No Admin, No Git, No .NET SDK Needed!)
REM Double-click this file to install for Microsoft Word & Microsoft Excel
REM ============================================================================
setlocal

if not exist "%~dp0Easy-Setup.ps1" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -Command "Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show('Please extract the .ZIP file first!`r`n`r`nRight-click the downloaded .zip file -> click ''Extract All...'' -> then double-click Install-EasyUser.bat inside the extracted folder.', 'Myanmar Official Spell Checker - Extract ZIP First', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)"
    exit /b 1
)

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Easy-Setup.ps1"
exit /b %ERRORLEVEL%
'@
Set-Content -Path (Join-Path $releasePkgDir "Install-EasyUser.bat") -Value $easyBat -Encoding ASCII

$easyUninstBat = @'
@echo off
setlocal
if not exist "%~dp0Easy-Setup.ps1" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -Command "Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show('Please extract the .ZIP file first before running Uninstall-EasyUser.bat.', 'Myanmar Official Spell Checker - Extract ZIP First', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)"
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Easy-Setup.ps1" -Uninstall
exit /b %ERRORLEVEL%
'@
Set-Content -Path (Join-Path $releasePkgDir "Uninstall-EasyUser.bat") -Value $easyUninstBat -Encoding ASCII

# Sync signed pre-built AnyCPU binaries (~1.07 MB) into installer\prebuilt so fresh git clone / GitHub ZIP works on PCs without .NET SDK!
$prebuiltDir = Join-Path $PSScriptRoot "prebuilt"
New-Item -ItemType Directory -Path $prebuiltDir -Force | Out-Null
$unlockedCoreSrc = Join-Path $excelBuildOutDir "MyanmarSpellChecker.Core.dll"
if (-not (Test-Path $unlockedCoreSrc)) { $unlockedCoreSrc = Join-Path $wordBuildOutDir "MyanmarSpellChecker.Core.dll" }
[System.IO.File]::Copy($unlockedCoreSrc, (Join-Path $prebuiltDir "MyanmarSpellChecker.Core.dll"), $true)
[System.IO.File]::Copy((Join-Path $wordBuildOutDir "MyanmarSpellChecker.WordAddIn.dll"), (Join-Path $prebuiltDir "MyanmarSpellChecker.WordAddIn.dll"), $true)
[System.IO.File]::Copy((Join-Path $excelBuildOutDir "MyanmarSpellChecker.ExcelAddIn.dll"), (Join-Path $prebuiltDir "MyanmarSpellChecker.ExcelAddIn.dll"), $true)
[System.IO.File]::Copy($cerPath, (Join-Path $prebuiltDir "MyanmarSpellChecker-EnterpriseCA.cer"), $true)

# Build lock-safe standalone EasySetup .zip archive even if MS Word or Excel is currently open!
$zipOutPath = Join-Path $artifactsDir "MyanmarSpellChecker-EasySetup-Word-Excel.zip"
$zipStaging = Join-Path $artifactsDir "zip-staging"
if (Test-Path $zipStaging) { Remove-Item -Path $zipStaging -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $zipStaging -Force | Out-Null
[System.IO.File]::Copy($unlockedCoreSrc, (Join-Path $zipStaging "MyanmarSpellChecker.Core.dll"), $true)
[System.IO.File]::Copy((Join-Path $wordBuildOutDir "MyanmarSpellChecker.WordAddIn.dll"), (Join-Path $zipStaging "MyanmarSpellChecker.WordAddIn.dll"), $true)
[System.IO.File]::Copy((Join-Path $excelBuildOutDir "MyanmarSpellChecker.ExcelAddIn.dll"), (Join-Path $zipStaging "MyanmarSpellChecker.ExcelAddIn.dll"), $true)
[System.IO.File]::Copy($outDbPath, (Join-Path $zipStaging "mlc_thatpon.db"), $true)
[System.IO.File]::Copy($cerPath, (Join-Path $zipStaging "MyanmarSpellChecker-EnterpriseCA.cer"), $true)
foreach ($extra in @("Easy-Setup.ps1", "Install-EasyUser.bat", "Uninstall-EasyUser.bat", "Install-MinistryGPO.cmd", "LICENSE", "NOTICE", "CITATION.cff")) {
    $extraSrc = Join-Path $releasePkgDir $extra
    if (Test-Path $extraSrc) { [System.IO.File]::Copy($extraSrc, (Join-Path $zipStaging $extra), $true) }
}
if (Test-Path $zipOutPath) { Remove-Item -Path $zipOutPath -Force }
Compress-Archive -Path (Join-Path $zipStaging "*") -DestinationPath $zipOutPath -CompressionLevel Optimal -Force
Remove-Item -Path $zipStaging -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "[5/5] Unified Office Suite Release Package & Standalone ZIP Ready:" -ForegroundColor Green
Write-Host "      Package Dir : $releasePkgDir" -ForegroundColor Green
Write-Host "      Prebuilt Dir: $prebuiltDir" -ForegroundColor Green
Write-Host "      EasySetup   : $zipOutPath ($([Math]::Round((Get-Item $zipOutPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
Get-ChildItem $releasePkgDir | Select-Object Name, Length
