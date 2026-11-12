param(
    [ValidateSet("all", "show-machine-id", "sign-license", "compile-dict")]
    [string]$Action = "all",
    [string]$MinistryName = "ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံး ဝန်ကြီးဌာန",
    [ValidateSet("SingleMachine", "MinistryEnterprise")]
    [string]$LicenseMode = "SingleMachine"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsDir = Join-Path $repoRoot "artifacts"
$releasePkgDir = Join-Path $artifactsDir "release-package"
New-Item -ItemType Directory -Path $releasePkgDir -Force | Out-Null

function Get-HardwareMachineId {
    $mbUuid = ""
    $cpuId  = ""
    $machineGuid = ""

    try {
        $mbUuid = (Get-CimInstance Win32_ComputerSystemProduct -ErrorAction SilentlyContinue | Select-Object -First 1).UUID
    } catch { }
    try {
        $cpuId = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1).ProcessorId
    } catch { }
    try {
        $machineGuid = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Cryptography" -ErrorAction SilentlyContinue).MachineGuid
    } catch { }

    if (-not $mbUuid) { $mbUuid = "FALLBACK-" + $env:COMPUTERNAME }
    if (-not $cpuId)  { $cpuId  = "FALLBACK-" + $env:COMPUTERNAME }
    if (-not $machineGuid) { $machineGuid = $env:COMPUTERNAME }

    $rawComposite = "$mbUuid|$cpuId|$machineGuid".ToUpperInvariant()
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($rawComposite))
    $hex = ([BitConverter]::ToString($hash) -replace "-", "")
    return "MMGOV-$($hex.Substring(0,4))-$($hex.Substring(4,4))-$($hex.Substring(8,4))"
}

function Protect-DatabaseKey([string]$plainKey, [string]$licenseId) {
    $salt = [System.Text.Encoding]::UTF8.GetBytes("MM-GOV-THATPON-2026-SALT")
    $derive = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($licenseId, $salt, 10000, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Key = $derive.GetBytes(32)
    $aes.IV  = $derive.GetBytes(16)

    $ms = New-Object System.IO.MemoryStream
    $cs = New-Object System.Security.Cryptography.CryptoStream($ms, $aes.CreateEncryptor(), [System.Security.Cryptography.CryptoStreamMode]::Write)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($plainKey)
    $cs.Write($bytes, 0, $bytes.Length)
    $cs.FlushFinalBlock()
    $res = [Convert]::ToBase64String($ms.ToArray())
    $cs.Dispose()
    $ms.Dispose()
    return $res
}

$machineId = Get-HardwareMachineId
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " MyanmarSpellChecker B2G Offline License & Dictionary Manager" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host "[*] Current PC Hardware Machine ID : $machineId" -ForegroundColor Yellow
Write-Host "[*] Current Domain / Workgroup     : $env:USERDOMAIN" -ForegroundColor Yellow

if ($Action -eq "show-machine-id") {
    exit 0
}

if ($Action -eq "all" -or $Action -eq "sign-license") {
    Write-Host "`n[*] Generating RSA-2048 KeyPair and Signing Offline License (.lic)..." -ForegroundColor Cyan
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048)
    $rsa.PersistKeyInCsp = $false

    $privKeyPath = Join-Path $artifactsDir "vendor_private_key.xml"
    $pubKeyPath  = Join-Path $artifactsDir "vendor_public_key.xml"

    if (Test-Path $privKeyPath) {
        $privXml = Get-Content $privKeyPath -Raw -Encoding UTF8
        $pubXml  = Get-Content $pubKeyPath -Raw -Encoding UTF8
        $rsa.FromXmlString($privXml)
    } else {
        $privXml = $rsa.ToXmlString($true)
        $pubXml  = $rsa.ToXmlString($false)
        Set-Content -Path $privKeyPath -Value $privXml -Encoding UTF8
        Set-Content -Path $pubKeyPath  -Value $pubXml  -Encoding UTF8
    }

    $licenseId = [Guid]::NewGuid().ToString("N")
    $dbPassword = "MM-GOV-THATPON-SQLCIPHER-2026!"
    $encDbKey = Protect-DatabaseKey -plainKey $dbPassword -licenseId $licenseId

    $modeInt = if ($LicenseMode -eq "SingleMachine") { 0 } else { 1 }
    $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $expMs = [DateTimeOffset]::UtcNow.AddYears(5).ToUnixTimeMilliseconds()

    $payloadObj = [ordered]@{
        AllowedDomain              = "*"
        AllowedMachineIds          = @($machineId)
        EncryptedDatabaseKeyBase64 = $encDbKey
        ExpiresUtc                 = "/Date($expMs)/"
        IssuedUtc                  = "/Date($nowMs)/"
        LicenseId                  = $licenseId
        MachineId                  = $machineId
        MinistryName               = $MinistryName
        Mode                       = $modeInt
    }

    $payloadJson = $payloadObj | ConvertTo-Json -Compress
    $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payloadJson)
    $sigBytes = $rsa.SignData($payloadBytes, [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))

    $signedLicObj = [ordered]@{
        PayloadJsonBase64  = [Convert]::ToBase64String($payloadBytes)
        RsaSignatureBase64 = [Convert]::ToBase64String($sigBytes)
    }

    $signedLicJson = $signedLicObj | ConvertTo-Json
    $licOutPath = Join-Path $releasePkgDir "license.lic"
    Set-Content -Path $licOutPath -Value $signedLicJson -Encoding UTF8
    Copy-Item $licOutPath (Join-Path $artifactsDir "license.lic") -Force

    # Verify RSA-2048 signature immediately using public key
    $rsaVerify = New-Object System.Security.Cryptography.RSACryptoServiceProvider(2048)
    $rsaVerify.PersistKeyInCsp = $false
    $rsaVerify.FromXmlString($pubXml)
    $isVerified = $rsaVerify.VerifyData($payloadBytes, [System.Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"), $sigBytes)

    Write-Host "[+] Signed Offline License (.lic)  : $licOutPath" -ForegroundColor Green
    Write-Host "[+] RSA-2048 Signature Verified    : $isVerified (Bound to Machine ID: $machineId)" -ForegroundColor Green
}

if ($Action -eq "all" -or $Action -eq "compile-dict") {
    & (Join-Path $PSScriptRoot "Build-EnterpriseRelease.ps1")
}

