param(
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Resolve-NewestAddInDll([string[]]$CandidatePaths) {
    $validFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($p in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        try {
            if ([System.IO.File]::Exists($p)) {
                $full = [System.IO.Path]::GetFullPath($p)
                $coreSibling = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($full), "MyanmarSpellChecker.Core.dll")
                if ([System.IO.File]::Exists($coreSibling)) {
                    $validFiles.Add([System.IO.FileInfo]::new($full))
                }
            }
        } catch { }
    }
    if ($validFiles.Count -eq 0) { return $null }

    $best = $validFiles | Sort-Object -Property @{
        Expression = {
            $t1 = $_.LastWriteTimeUtc
            $t2 = [System.IO.FileInfo]::new([System.IO.Path]::Combine($_.DirectoryName, "MyanmarSpellChecker.Core.dll")).LastWriteTimeUtc
            if ($t1 -lt $t2) { $t1 } else { $t2 }
        }
        Descending = $true
    } | Select-Object -First 1

    return $best.FullName
}

function Resolve-FullDictionaryDb([string[]]$CandidatePaths) {
    $validFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($p in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        try {
            if ([System.IO.File]::Exists($p)) {
                $fi = [System.IO.FileInfo]::new([System.IO.Path]::GetFullPath($p))
                if ($fi.Length -ge 5000000) {
                    $validFiles.Add($fi)
                }
            }
        } catch { }
    }
    if ($validFiles.Count -eq 0) { return $null }

    $best = $validFiles | Sort-Object -Property LastWriteTimeUtc, Length -Descending | Select-Object -First 1
    return $best.FullName
}

$scriptDir   = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot    = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($scriptDir, ".."))
$prebuiltDir = [System.IO.Path]::Combine($scriptDir, "prebuilt")

$wordAddInDll = Resolve-NewestAddInDll @(
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\MyanmarSpellChecker.WordAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "artifacts\release-package\MyanmarSpellChecker.WordAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "artifacts\staged-word\MyanmarSpellChecker.WordAddIn.dll")),
    ([System.IO.Path]::Combine($prebuiltDir, "MyanmarSpellChecker.WordAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48\MyanmarSpellChecker.WordAddIn.dll"))
)

$excelAddInDll = Resolve-NewestAddInDll @(
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\MyanmarSpellChecker.ExcelAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "artifacts\release-package\MyanmarSpellChecker.ExcelAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "artifacts\staged-excel\MyanmarSpellChecker.ExcelAddIn.dll")),
    ([System.IO.Path]::Combine($prebuiltDir, "MyanmarSpellChecker.ExcelAddIn.dll")),
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Debug\net48\MyanmarSpellChecker.ExcelAddIn.dll"))
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
    $localAppAppDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "MyanmarSpellChecker\app")
    if ([System.IO.Directory]::Exists($localAppAppDir)) {
        try { [System.IO.Directory]::Delete($localAppAppDir, $true) } catch { }
    }
    Write-Host "[+] Unregistered Word + Excel Add-ins successfully." -ForegroundColor Green
    exit 0
}

if (-not $wordAddInDll -or -not [System.IO.File]::Exists($wordAddInDll)) {
    throw "WordAddIn DLL not found. Run 'dotnet build -c Release' first."
}
if (-not $excelAddInDll -or -not [System.IO.File]::Exists($excelAddInDll)) {
    throw "ExcelAddIn DLL not found. Run 'dotnet build -c Release' first."
}

# Copy freshest FULL (>= 5 MB, 89,500+ headwords) mlc_thatpon.db into Word bin, Excel bin, artifacts\, and %LocalAppData%\MyanmarSpellChecker
# Uses explicit FileInfo list so single-item result on a fresh git clone NEVER unrolls to a [string] scalar!
$dataDb           = [System.IO.Path]::Combine($repoRoot, "data\mlc_thatpon.db")
$releasePkgDb     = [System.IO.Path]::Combine($repoRoot, "artifacts\release-package\mlc_thatpon.db")
$legacyArtifactDb = [System.IO.Path]::Combine($repoRoot, "artifacts\mlc_thatpon.db")
$localExistingDb  = [System.IO.Path]::Combine($env:LOCALAPPDATA, "MyanmarSpellChecker\mlc_thatpon.db")

$artifactDb = Resolve-FullDictionaryDb @($dataDb, $releasePkgDb, $legacyArtifactDb, $localExistingDb)
if (-not $artifactDb) {
    throw "Full Myanmar Dictionary Database (mlc_thatpon.db >= 12 MB) was not found! Ensure 'data\mlc_thatpon.db' is present."
}

$dbSizeMb = [Math]::Round(([System.IO.FileInfo]::new($artifactDb).Length / 1MB), 2)
Write-Host "[*] Syncing full dictionary database: $artifactDb ($dbSizeMb MB)" -ForegroundColor Cyan
$srcFull = [System.IO.Path]::GetFullPath($artifactDb)
$targetDirs = @(
    ([System.IO.Path]::GetDirectoryName($legacyArtifactDb)),
    ([System.IO.Path]::GetDirectoryName($wordAddInDll)),
    ([System.IO.Path]::GetDirectoryName($excelAddInDll)),
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48")),
    ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48"))
)

foreach ($dir in $targetDirs) {
    if ($dir -and [System.IO.Directory]::Exists($dir)) {
        $dst = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($dir, "mlc_thatpon.db"))
        if (-not [string]::Equals($srcFull, $dst, [System.StringComparison]::OrdinalIgnoreCase)) {
            try { [System.IO.File]::Copy($srcFull, $dst, $true) } catch { }
        }
    }
}

$localAppDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "MyanmarSpellChecker")
[void][System.IO.Directory]::CreateDirectory($localAppDir)
try { [System.IO.File]::Copy($srcFull, [System.IO.Path]::Combine($localAppDir, "mlc_thatpon.db"), $true) } catch { }

$localAppSubDir = [System.IO.Path]::Combine($localAppDir, "app")
if ([System.IO.Directory]::Exists($localAppSubDir)) {
    try { [System.IO.File]::Copy($srcFull, [System.IO.Path]::Combine($localAppSubDir, "mlc_thatpon.db"), $true) } catch { }
}

$progDataDir = [System.IO.Path]::Combine($env:ProgramData, "MyanmarSpellChecker")
if ([System.IO.Directory]::Exists($progDataDir)) {
    try { [System.IO.File]::Copy($srcFull, [System.IO.Path]::Combine($progDataDir, "mlc_thatpon.db"), $true) } catch { }
}

$progFilesDir = [System.IO.Path]::Combine($env:ProgramFiles, "MyanmarSpellChecker")
if ([System.IO.Directory]::Exists($progFilesDir)) {
    try {
        [System.IO.File]::Copy($srcFull, [System.IO.Path]::Combine($progFilesDir, "mlc_thatpon.db"), $true)
        $coreDll = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($excelAddInDll), "MyanmarSpellChecker.Core.dll")
        if ([System.IO.File]::Exists($coreDll)) {
            [System.IO.File]::Copy($coreDll, [System.IO.Path]::Combine($progFilesDir, "MyanmarSpellChecker.Core.dll"), $true)
        }
        [System.IO.File]::Copy($wordAddInDll,  [System.IO.Path]::Combine($progFilesDir, "MyanmarSpellChecker.WordAddIn.dll"),  $true)
        [System.IO.File]::Copy($excelAddInDll, [System.IO.Path]::Combine($progFilesDir, "MyanmarSpellChecker.ExcelAddIn.dll"), $true)
    } catch { }
}

Write-Host "[*] Registering COM classes (Word + Excel) in HKCU:\Software\Classes (Both 64-bit & 32-bit RegistryViews)..." -ForegroundColor Cyan

$views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
foreach ($view in $views) {
    $hkcuBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        foreach ($c in $comClasses) {
            $codeBaseUri = ([System.Uri]::new($c.DllPath)).AbsoluteUri.Replace("%20", " ")

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
    foreach ($ver in @("16.0", "15.0", "14.0")) {
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
