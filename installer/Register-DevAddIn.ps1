param(
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

function Resolve-NewestAddInDll([string[]]$CandidatePaths) {
    $existing = @($CandidatePaths | Where-Object {
        (Test-Path $_) -and (Test-Path (Join-Path (Split-Path $_ -Parent) "MyanmarSpellChecker.Core.dll"))
    } | Sort-Object {
        $addInTime = (Get-Item $_).LastWriteTimeUtc
        $coreTime  = (Get-Item (Join-Path (Split-Path $_ -Parent) "MyanmarSpellChecker.Core.dll")).LastWriteTimeUtc
        if ($addInTime -lt $coreTime) { $addInTime } else { $coreTime }
    } -Descending)
    if ($existing.Count -eq 0) { return $null }
    return [System.IO.Path]::GetFullPath($existing[0])
}

$wordAddInDll = Resolve-NewestAddInDll @(
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\MyanmarSpellChecker.WordAddIn.dll"),
    (Join-Path $PSScriptRoot "..\artifacts\release-package\MyanmarSpellChecker.WordAddIn.dll"),
    (Join-Path $PSScriptRoot "..\artifacts\staged-word\MyanmarSpellChecker.WordAddIn.dll"),
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48\MyanmarSpellChecker.WordAddIn.dll")
)

$excelAddInDll = Resolve-NewestAddInDll @(
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\MyanmarSpellChecker.ExcelAddIn.dll"),
    (Join-Path $PSScriptRoot "..\artifacts\release-package\MyanmarSpellChecker.ExcelAddIn.dll"),
    (Join-Path $PSScriptRoot "..\artifacts\staged-excel\MyanmarSpellChecker.ExcelAddIn.dll"),
    (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.ExcelAddIn\bin\Debug\net48\MyanmarSpellChecker.ExcelAddIn.dll")
)

$wordAddInKey  = "HKCU:\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn"
$excelAddInKey = "HKCU:\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn"

$wordAsmFullName  = "MyanmarSpellChecker.WordAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$excelAsmFullName = "MyanmarSpellChecker.ExcelAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"

$comClasses = @(
    @{
        ProgId      = "MyanmarSpellChecker.WordAddIn"
        Clsid       = "{8F5A9E42-6D11-4F3B-9C80-7B12E5D94A11}"
        TypeName    = "MyanmarSpellChecker.WordAddIn.ThisAddIn"
        AsmFullName = $wordAsmFullName
        DllPath     = $wordAddInDll
        IsControl   = $false
    },
    @{
        ProgId      = "MyanmarSpellChecker.ProofingTaskPaneControl"
        Clsid       = "{B3A2C741-9D18-4E2A-8C55-1F7B8A9C2D33}"
        TypeName    = "MyanmarSpellChecker.WordAddIn.UI.ProofingTaskPaneControl"
        AsmFullName = $wordAsmFullName
        DllPath     = $wordAddInDll
        IsControl   = $true
    },
    @{
        ProgId      = "MyanmarSpellChecker.ExcelAddIn"
        Clsid       = "{9A6B4D53-7E22-4C8A-AD91-8C23F6E05B22}"
        TypeName    = "MyanmarSpellChecker.ExcelAddIn.ExcelThisAddIn"
        AsmFullName = $excelAsmFullName
        DllPath     = $excelAddInDll
        IsControl   = $false
    },
    @{
        ProgId      = "MyanmarSpellChecker.ExcelTaskPaneControl"
        Clsid       = "{C4B3D852-AE29-4F3B-9D66-2E8C9B0D3E44}"
        TypeName    = "MyanmarSpellChecker.ExcelAddIn.UI.ExcelProofingTaskPaneControl"
        AsmFullName = $excelAsmFullName
        DllPath     = $excelAddInDll
        IsControl   = $true
    }
)

if ($Unregister) {
    Write-Host "[*] Unregistering MyanmarSpellChecker (Word + Excel) from HKCU..." -ForegroundColor Yellow
    foreach ($c in $comClasses) {
        Remove-Item -Path "HKCU:\Software\Classes\$($c.ProgId)" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path "HKCU:\Software\Classes\CLSID\$($c.Clsid)" -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $wordAddInKey  -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $excelAddInKey -Recurse -Force -ErrorAction SilentlyContinue
    $localAppAppDir = Join-Path $env:LOCALAPPDATA "MyanmarSpellChecker\app"
    if (Test-Path $localAppAppDir) {
        Remove-Item -Path $localAppAppDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "[+] Unregistered Word + Excel Add-ins successfully." -ForegroundColor Green
    exit 0
}

if (-not $wordAddInDll -or -not (Test-Path $wordAddInDll)) {
    throw "WordAddIn DLL not found. Run 'dotnet build' first."
}
if (-not $excelAddInDll -or -not (Test-Path $excelAddInDll)) {
    throw "ExcelAddIn DLL not found. Run 'dotnet build' first."
}

# Copy freshest FULL (>= 5 MB, 89,500+ headwords) mlc_thatpon.db into Word bin, Excel bin, artifacts\, and %LocalAppData%\MyanmarSpellChecker
# Never allow a small (< 5 MB) 106-word seed fallback DB to overwrite the full dictionary!
$dataDb = Join-Path $PSScriptRoot "..\data\mlc_thatpon.db"
$releasePkgDb = Join-Path $PSScriptRoot "..\artifacts\release-package\mlc_thatpon.db"
$legacyArtifactDb = Join-Path $PSScriptRoot "..\artifacts\mlc_thatpon.db"
$candidateDbs = @($dataDb, $releasePkgDb, $legacyArtifactDb) |
    Where-Object { Test-Path $_ } |
    Sort-Object `
        @{ Expression = { if ((Get-Item $_).Length -ge 5000000) { 1 } else { 0 } }; Descending = $true }, `
        @{ Expression = { (Get-Item $_).LastWriteTimeUtc }; Descending = $true }, `
        @{ Expression = { (Get-Item $_).Length }; Descending = $true }
$artifactDb = if ($candidateDbs.Count -gt 0) { $candidateDbs[0] } else { $dataDb }

if ((Test-Path $artifactDb) -and ((Get-Item $artifactDb).Length -lt 5000000) -and (Test-Path $dataDb) -and ((Get-Item $dataDb).Length -ge 5000000)) {
    $artifactDb = $dataDb
}

if (Test-Path $artifactDb) {
    $dbSizeMb = [Math]::Round(((Get-Item $artifactDb).Length / 1MB), 2)
    Write-Host "[*] Syncing full dictionary database: $artifactDb ($dbSizeMb MB)" -ForegroundColor Cyan
    $srcFull = [System.IO.Path]::GetFullPath($artifactDb)
    $targetDirs = @(
        (Split-Path $legacyArtifactDb -Parent),
        (Split-Path $wordAddInDll -Parent),
        (Split-Path $excelAddInDll -Parent),
        (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.WordAddIn\bin\Release\net48"),
        (Join-Path $PSScriptRoot "..\src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48")
    )

    foreach ($dir in $targetDirs) {
        if (Test-Path $dir) {
            $dst = [System.IO.Path]::GetFullPath((Join-Path $dir "mlc_thatpon.db"))
            if ($srcFull -ne $dst) {
                try { Copy-Item $artifactDb $dst -Force -ErrorAction Stop } catch { }
            }
        }
    }

    $localAppDir = Join-Path $env:LOCALAPPDATA "MyanmarSpellChecker"
    New-Item -ItemType Directory -Path $localAppDir -Force | Out-Null
    Copy-Item $artifactDb (Join-Path $localAppDir "mlc_thatpon.db") -Force

    $progDataDb = Join-Path $env:ProgramData "MyanmarSpellChecker\mlc_thatpon.db"
    if (Test-Path (Split-Path $progDataDb -Parent)) {
        try { Copy-Item $artifactDb $progDataDb -Force -ErrorAction Stop } catch { }
    }

    $progFilesDir = Join-Path $env:ProgramFiles "MyanmarSpellChecker"
    if (Test-Path $progFilesDir) {
        try {
            Copy-Item $artifactDb (Join-Path $progFilesDir "mlc_thatpon.db") -Force -ErrorAction Stop
            $coreDll = Join-Path (Split-Path $excelAddInDll -Parent) "MyanmarSpellChecker.Core.dll"
            if (Test-Path $coreDll) { Copy-Item $coreDll (Join-Path $progFilesDir "MyanmarSpellChecker.Core.dll") -Force -ErrorAction Stop }
            Copy-Item $wordAddInDll  (Join-Path $progFilesDir "MyanmarSpellChecker.WordAddIn.dll")  -Force -ErrorAction Stop
            Copy-Item $excelAddInDll (Join-Path $progFilesDir "MyanmarSpellChecker.ExcelAddIn.dll") -Force -ErrorAction Stop
        } catch { }
    }
}

Write-Host "[*] Registering COM classes (Word + Excel) in HKCU:\Software\Classes (Both 64-bit & 32-bit RegistryViews)..." -ForegroundColor Cyan

$views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
foreach ($view in $views) {
    $hkcuBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        foreach ($c in $comClasses) {
            $codeBaseUri = "file:///" + ($c.DllPath -replace "\\", "/")

            $progKey = $hkcuBase.CreateSubKey("Software\Classes\$($c.ProgId)")
            $progKey.SetValue("", $c.TypeName)
            $progClsidKey = $hkcuBase.CreateSubKey("Software\Classes\$($c.ProgId)\CLSID")
            $progClsidKey.SetValue("", $c.Clsid)
            $progClsidKey.Dispose()
            $progKey.Dispose()

            $clsidKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)")
            $clsidKey.SetValue("", $c.TypeName)

            $inprocKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\InprocServer32")
            $inprocKey.SetValue("", "mscoree.dll")
            $inprocKey.SetValue("ThreadingModel", "Both")
            $inprocKey.SetValue("Class", $c.TypeName)
            $inprocKey.SetValue("Assembly", $c.AsmFullName)
            $inprocKey.SetValue("RuntimeVersion", "v4.0.30319")
            $inprocKey.SetValue("CodeBase", $codeBaseUri)
            $inprocKey.Dispose()

            $verKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\InprocServer32\1.0.0.0")
            $verKey.SetValue("Class", $c.TypeName)
            $verKey.SetValue("Assembly", $c.AsmFullName)
            $verKey.SetValue("RuntimeVersion", "v4.0.30319")
            $verKey.SetValue("CodeBase", $codeBaseUri)
            $verKey.Dispose()

            $pidKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\ProgId")
            $pidKey.SetValue("", $c.ProgId)
            $pidKey.Dispose()

            $catKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}")
            $catKey.Dispose()

            if ($c.IsControl) {
                $ctrlKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\Control")
                $ctrlKey.Dispose()
                $ctrlCatKey = $hkcuBase.CreateSubKey("Software\Classes\CLSID\$($c.Clsid)\Implemented Categories\{40FC6ED4-2438-11CF-A3DB-080036F12502}")
                $ctrlCatKey.Dispose()
            }

            $clsidKey.Dispose()
        }
    } finally {
        $hkcuBase.Dispose()
    }
}

# Clear any Word or Excel Resiliency / DisabledItems blocks
foreach ($appName in @("Word", "Excel")) {
    foreach ($ver in @("16.0", "15.0")) {
        foreach ($sub in @("DisabledItems", "CrashingAddinList")) {
            $rKey = "HKCU:\Software\Microsoft\Office\$ver\$appName\Resiliency\$sub"
            if (Test-Path $rKey) {
                Remove-Item -Path $rKey -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

# Suppress Office's built-in "MISSING PROOFING TOOLS: Proofing is not available for Burmese." yellow banner
$langResKey = "HKCU:\Software\Microsoft\Office\16.0\Common\LanguageResources"
if (Test-Path $langResKey) {
    $curNever = [string](Get-ItemProperty -Path $langResKey -Name "NotificationsNeverShowAgainLanguages" -ErrorAction SilentlyContinue).NotificationsNeverShowAgainLanguages
    $tags = @($curNever -split ';' | Where-Object { $_ -and $_.Trim() })
    foreach ($t in @("my-MM", "my")) {
        if ($tags -notcontains $t) { $tags += $t }
    }
    Set-ItemProperty -Path $langResKey -Name "NotificationsNeverShowAgainLanguages" -Value ($tags -join ';')
}

function Register-OfficeHostAddIn([string]$HostName, [string]$HkcuAddInKey, [string]$HklmAddInKey, [string]$FriendlyName, [string]$Desc, [string]$DllPath) {
    $hasActiveHklm = $false
    if (Test-Path $HklmAddInKey) {
        $hklmLb = (Get-ItemProperty -Path $HklmAddInKey -Name "LoadBehavior" -ErrorAction SilentlyContinue).LoadBehavior
        if ($hklmLb -eq 3) { $hasActiveHklm = $true }
    }

    if ($hasActiveHklm) {
        if (Test-Path $HkcuAddInKey) {
            Remove-Item -Path $HkcuAddInKey -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "[+] Verified active Microsoft $HostName Add-in via $HklmAddInKey (COM -> $DllPath)" -ForegroundColor Green
    } else {
        New-Item -Path $HkcuAddInKey -Force | Out-Null
        Set-ItemProperty -Path $HkcuAddInKey -Name "FriendlyName" -Value $FriendlyName
        Set-ItemProperty -Path $HkcuAddInKey -Name "Description"  -Value $Desc
        Set-ItemProperty -Path $HkcuAddInKey -Name "LoadBehavior" -Value 3 -Type DWord
        Write-Host "[+] Registered Microsoft $HostName Add-in at $HkcuAddInKey (LoadBehavior = 3 -> $DllPath)" -ForegroundColor Green
    }
}

Register-OfficeHostAddIn `
    -HostName "Word" `
    -HkcuAddInKey $wordAddInKey `
    -HklmAddInKey "HKLM:\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn" `
    -FriendlyName "Myanmar Official Spell & Grammar Checker" `
    -Desc "100% Offline Myanmar Spell & Grammar Checker (B2G Edition)" `
    -DllPath $wordAddInDll

Register-OfficeHostAddIn `
    -HostName "Excel" `
    -HkcuAddInKey $excelAddInKey `
    -HklmAddInKey "HKLM:\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn" `
    -FriendlyName "Myanmar Official Spell & Grammar Checker (Excel)" `
    -Desc "100% Offline Formula-Safe Myanmar Spell & Grammar Checker for Excel" `
    -DllPath $excelAddInDll
