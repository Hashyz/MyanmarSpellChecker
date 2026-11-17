param(
    [string]$CertSubject = "CN=Myanmar Official Spell Checker (B2G Enterprise)"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$storeScope = if ($isAdmin) { "LocalMachine" } else { "CurrentUser" }

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " Enterprise Code-Signing & Office Suite Installer ($storeScope)" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Find or create Code Signing Certificate
$myStorePath = "Cert:\$storeScope\My"
$cert = Get-ChildItem $myStorePath | Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey } | Select-Object -First 1

if (-not $cert) {
    Write-Host "[*] Generating RSA-2048 Code-Signing Certificate: $CertSubject..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation $myStorePath `
        -NotAfter (Get-Date).AddYears(5)
}

# 2. Trust the certificate in Root and TrustedPublisher stores when running as Administrator
if ($isAdmin) {
    Write-Host "[*] Installing certificate into LocalMachine Root & TrustedPublisher stores..." -ForegroundColor Cyan
    $rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $rootStore.Open("ReadWrite")
    $rootStore.Add($cert)
    $rootStore.Close()

    $pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPublisher", "LocalMachine")
    $pubStore.Open("ReadWrite")
    $pubStore.Add($cert)
    $pubStore.Close()
}

# 3. Export public .cer file to artifacts/ for Active Directory GPO deployment
$artifactsDir = Join-Path $PSScriptRoot "..\artifacts"
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
$cerPath = Join-Path $artifactsDir "MyanmarSpellChecker-EnterpriseCA.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "[+] Exported GPO Public Certificate: $cerPath" -ForegroundColor Green

# 4. Digitally sign our self-contained 3-in-1 Office Suite build outputs (Word + Excel + PowerPoint)
$wordRelDir  = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Release\net48"
$wordDbgDir  = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48"
$wordBinDir  = if (Test-Path (Join-Path $wordRelDir "MyanmarSpellChecker.WordAddIn.dll")) { $wordRelDir } else { $wordDbgDir }
$cfg         = if ($wordBinDir -eq $wordRelDir) { "Release" } else { "Debug" }
$excelBinDir = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.ExcelAddIn\bin\$cfg\net48"
$pptBinDir   = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.PowerPointAddIn\bin\$cfg\net48"

$filesToSign = @(
    (Join-Path $wordBinDir "MyanmarSpellChecker.Core.dll"),
    (Join-Path $wordBinDir "MyanmarSpellChecker.WordAddIn.dll"),
    (Join-Path $excelBinDir "MyanmarSpellChecker.Core.dll"),
    (Join-Path $excelBinDir "MyanmarSpellChecker.ExcelAddIn.dll"),
    (Join-Path $pptBinDir "MyanmarSpellChecker.Core.dll"),
    (Join-Path $pptBinDir "MyanmarSpellChecker.PowerPointAddIn.dll"),
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.AdminTools\bin\$cfg\net48\MyanmarSpellChecker.AdminTools.exe"),
    (Join-Path $PSScriptRoot "..\tests\MyanmarSpellChecker.Core.Tests\bin\$cfg\net48\MyanmarSpellChecker.Core.Tests.dll")
)

foreach ($file in $filesToSign) {
    if (Test-Path $file) {
        try {
            $sig = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop
            Write-Host "[+] Signed: $(Split-Path $file -Leaf) (Status: $($sig.Status))" -ForegroundColor Green
        } catch {
            Write-Host "    [Note] Skipped locked binary: $file" -ForegroundColor Yellow
        }
    }
}

# 5. If running as Administrator, copy the signed DLLs to C:\Program Files\MyanmarSpellChecker
if ($isAdmin) {
    $progFilesDir = "C:\Program Files\MyanmarSpellChecker"
    Write-Host "[*] Installing signed binaries to trusted system path: $progFilesDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $progFilesDir -Force | Out-Null
    Copy-Item (Join-Path $pptBinDir "MyanmarSpellChecker.Core.dll") $progFilesDir -Force
    Copy-Item (Join-Path $wordBinDir "MyanmarSpellChecker.WordAddIn.dll") $progFilesDir -Force
    if (Test-Path (Join-Path $excelBinDir "MyanmarSpellChecker.ExcelAddIn.dll")) {
        Copy-Item (Join-Path $excelBinDir "MyanmarSpellChecker.ExcelAddIn.dll") $progFilesDir -Force
    }
    if (Test-Path (Join-Path $pptBinDir "MyanmarSpellChecker.PowerPointAddIn.dll")) {
        Copy-Item (Join-Path $pptBinDir "MyanmarSpellChecker.PowerPointAddIn.dll") $progFilesDir -Force
    }

    $regAsm64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    & $regAsm64 /codebase (Join-Path $progFilesDir "MyanmarSpellChecker.WordAddIn.dll")
    if (Test-Path (Join-Path $progFilesDir "MyanmarSpellChecker.ExcelAddIn.dll")) {
        & $regAsm64 /codebase (Join-Path $progFilesDir "MyanmarSpellChecker.ExcelAddIn.dll")
    }
    if (Test-Path (Join-Path $progFilesDir "MyanmarSpellChecker.PowerPointAddIn.dll")) {
        & $regAsm64 /codebase (Join-Path $progFilesDir "MyanmarSpellChecker.PowerPointAddIn.dll")
    }
}

# 6. Register the Add-in entries for Microsoft Word, Excel & PowerPoint (resets LoadBehavior = 3)
& (Join-Path $PSScriptRoot "Register-DevAddIn.ps1")
