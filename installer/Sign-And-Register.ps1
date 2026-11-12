param(
    [string]$CertSubject = "CN=Myanmar Official Spell Checker (B2G Enterprise)"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$storeScope = if ($isAdmin) { "LocalMachine" } else { "CurrentUser" }

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " Enterprise Code-Signing & Word Add-in Installer ($storeScope)" -ForegroundColor Cyan
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

# 4. Digitally sign our self-contained 2-DLL build outputs (prefer Release, fallback to Debug)
$releaseBinDir = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Release\net48"
$debugBinDir   = Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48"
$binDir = if (Test-Path (Join-Path $releaseBinDir "MyanmarSpellChecker.WordAddIn.dll")) { $releaseBinDir } else { $debugBinDir }
$cfg = if ($binDir -eq $releaseBinDir) { "Release" } else { "Debug" }
$filesToSign = @(
    (Join-Path $binDir "MyanmarSpellChecker.Core.dll"),
    (Join-Path $binDir "MyanmarSpellChecker.WordAddIn.dll"),
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.AdminTools\bin\$cfg\net48\MyanmarSpellChecker.AdminTools.exe"),
    (Join-Path $PSScriptRoot "..\tests\MyanmarSpellChecker.Core.Tests\bin\$cfg\net48\MyanmarSpellChecker.Core.Tests.dll")
)

foreach ($file in $filesToSign) {
    if (Test-Path $file) {
        $sig = Set-AuthenticodeSignature -FilePath $file -Certificate $cert -HashAlgorithm SHA256
        Write-Host "[+] Signed: $(Split-Path $file -Leaf) (Status: $($sig.Status))" -ForegroundColor Green
    }
}

# 5. If running as Administrator, copy the signed 2 DLLs to C:\Program Files\MyanmarSpellChecker (trusted system path)
if ($isAdmin) {
    $progFilesDir = "C:\Program Files\MyanmarSpellChecker"
    Write-Host "[*] Installing signed binaries to trusted system path: $progFilesDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $progFilesDir -Force | Out-Null
    Copy-Item (Join-Path $binDir "MyanmarSpellChecker.Core.dll") $progFilesDir -Force
    Copy-Item (Join-Path $binDir "MyanmarSpellChecker.WordAddIn.dll") $progFilesDir -Force

    $regAsm64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
    & $regAsm64 /codebase (Join-Path $progFilesDir "MyanmarSpellChecker.WordAddIn.dll")
}

# 6. Register the Add-in entry for Microsoft Word (resets LoadBehavior = 3)
& (Join-Path $PSScriptRoot "Register-DevAddIn.ps1")
