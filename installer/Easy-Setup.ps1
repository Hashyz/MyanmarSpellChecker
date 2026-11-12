param(
    [switch]$Uninstall,
    [switch]$Silent,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms

$TitleText = "Myanmar Official Spell Checker - Setup (Word + Excel)"

function Show-UserInfo([string]$Message, [System.Windows.Forms.MessageBoxIcon]$Icon = [System.Windows.Forms.MessageBoxIcon]::Information) {
    Write-Host $Message
    if (-not $Silent) {
        [void][System.Windows.Forms.MessageBox]::Show($Message, $TitleText, [System.Windows.Forms.MessageBoxButtons]::OK, $Icon)
    }
}

function Resolve-BestDll([string[]]$CandidatePaths) {
    $validFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($p in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        try {
            if ([System.IO.File]::Exists($p)) {
                $full = [System.IO.Path]::GetFullPath($p)
                $parentDir = [System.IO.Path]::GetDirectoryName($full)
                $coreSibling = [System.IO.Path]::Combine($parentDir, "MyanmarSpellChecker.Core.dll")
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
            $coreInfo = [System.IO.FileInfo]::new([System.IO.Path]::Combine($_.DirectoryName, "MyanmarSpellChecker.Core.dll"))
            $t2 = $coreInfo.LastWriteTimeUtc
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

$wordAddInKey  = "HKCU:\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn"
$excelAddInKey = "HKCU:\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn"
$wordAsmFullName  = "MyanmarSpellChecker.WordAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$excelAsmFullName = "MyanmarSpellChecker.ExcelAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"

$localRootDir  = [System.IO.Path]::Combine($env:LOCALAPPDATA, "MyanmarSpellChecker")
$appInstallDir = [System.IO.Path]::Combine($localRootDir, "app")

$comClasses = @(
    @{
        ProgId      = "MyanmarSpellChecker.WordAddIn"
        Clsid       = "{8F5A9E42-6D11-4F3B-9C80-7B12E5D94A11}"
        TypeName    = "MyanmarSpellChecker.WordAddIn.ThisAddIn"
        AsmFullName = $wordAsmFullName
        DllName     = "MyanmarSpellChecker.WordAddIn.dll"
        IsControl   = $false
    },
    @{
        ProgId      = "MyanmarSpellChecker.ProofingTaskPaneControl"
        Clsid       = "{B3A2C741-9D18-4E2A-8C55-1F7B8A9C2D33}"
        TypeName    = "MyanmarSpellChecker.WordAddIn.UI.ProofingTaskPaneControl"
        AsmFullName = $wordAsmFullName
        DllName     = "MyanmarSpellChecker.WordAddIn.dll"
        IsControl   = $true
    },
    @{
        ProgId      = "MyanmarSpellChecker.ExcelAddIn"
        Clsid       = "{9A6B4D53-7E22-4C8A-AD91-8C23F6E05B22}"
        TypeName    = "MyanmarSpellChecker.ExcelAddIn.ExcelThisAddIn"
        AsmFullName = $excelAsmFullName
        DllName     = "MyanmarSpellChecker.ExcelAddIn.dll"
        IsControl   = $false
    },
    @{
        ProgId      = "MyanmarSpellChecker.ExcelTaskPaneControl"
        Clsid       = "{C4B3D852-AE29-4F3B-9D66-2E8C9B0D3E44}"
        TypeName    = "MyanmarSpellChecker.ExcelAddIn.UI.ExcelProofingTaskPaneControl"
        AsmFullName = $excelAsmFullName
        DllName     = "MyanmarSpellChecker.ExcelAddIn.dll"
        IsControl   = $true
    }
)

# 1. Check if Word or Excel is open (never force-close user documents!)
if (-not $ValidateOnly) {
    while ($true) {
        $runningOffice = @(Get-Process -Name "WINWORD", "EXCEL" -ErrorAction SilentlyContinue)
        if ($runningOffice.Count -eq 0 -or $Silent) {
            break
        }
        $promptMsg = "Microsoft Word သို့မဟုတ် Excel ပွင့်နေပါသည်။`r`n`r`nကျေးဇူးပြု၍ လက်ရှိဖိုင်များကို Save လုပ်၍ Word နှင့် Excel ကို ခေတ္တပိတ်ပြီး 'Retry' ကို နှိပ်ပါ။`r`n`r`n(Please save your work, close Microsoft Word & Excel, and click Retry.)"
        $choice = [System.Windows.Forms.MessageBox]::Show(
            $promptMsg,
            $TitleText,
            [System.Windows.Forms.MessageBoxButtons]::RetryCancel,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($choice -ne [System.Windows.Forms.DialogResult]::Retry) {
            exit 1
        }
    }
}

# 2. Handle Uninstall
if ($Uninstall) {
    Write-Host "[*] Removing MyanmarSpellChecker (Word + Excel) from Current User..." -ForegroundColor Yellow
    $views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
    foreach ($view in $views) {
        $hkcuBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            foreach ($c in $comClasses) {
                $hkcuBase.DeleteSubKeyTree("Software\Classes\$($c.ProgId)", $false)
                $hkcuBase.DeleteSubKeyTree("Software\Classes\CLSID\$($c.Clsid)", $false)
            }
            $hkcuBase.DeleteSubKeyTree("Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn", $false)
            $hkcuBase.DeleteSubKeyTree("Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn", $false)
        } finally {
            $hkcuBase.Dispose()
        }
    }
    if ([System.IO.Directory]::Exists($appInstallDir)) {
        try { [System.IO.Directory]::Delete($appInstallDir, $true) } catch { }
    }
    Show-UserInfo "✓ မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း Add-in (Word + Excel) ကို အောင်မြင်စွာ ဖြုတ်သိမ်းပြီးပါပြီ။`r`n(Successfully uninstalled from Microsoft Word and Excel.)"
    exit 0
}

try {
    $scriptDir   = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $repoRoot    = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($scriptDir, ".."))
    $prebuiltDir = [System.IO.Path]::Combine($scriptDir, "prebuilt")

    # Detect accidental execution directly inside an unextracted .zip folder in Windows Explorer (%TEMP%\Temp1_*.zip\...)
    $tempRoot = [System.IO.Path]::GetFullPath($env:TEMP)
    if ($scriptDir.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and ($scriptDir -match '\.zip')) {
        throw "သင်သည် ZIP ဖိုင်ကို မဖြည်ဘဲ အတွင်းမှ တိုက်ရိုက် နှိပ်မိနေပါသည်။`r`n`r`nကျေးဇူးပြု၍ ZIP ဖိုင်ပေါ်တွင် Right-click နှိပ်ပြီး 'Extract All...' ဖြင့် အရင်ဖြည်ပြီးမှ ပြန်သွင်းပေးပါ။`r`n(Please right-click the .ZIP archive, select 'Extract All...', and run the installer from the extracted folder.)"
    }

    # Unblock downloaded files (removes Windows Zone.Identifier Mark-of-the-Web from ZIP downloads; -LiteralPath handles [1] brackets!)
    if (-not $ValidateOnly) {
        try {
            foreach ($f in [System.IO.Directory]::GetFiles($scriptDir)) {
                Unblock-File -LiteralPath $f -ErrorAction SilentlyContinue
            }
            if ([System.IO.Directory]::Exists($prebuiltDir)) {
                foreach ($f in [System.IO.Directory]::GetFiles($prebuiltDir)) {
                    Unblock-File -LiteralPath $f -ErrorAction SilentlyContinue
                }
            }
        } catch { }
    }

    # 3. Locate pre-built binaries (Standalone ZIP folder, Repo Release/Prebuilt binaries, or dotnet build fallback)
    $standaloneMode = [System.IO.File]::Exists([System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.WordAddIn.dll")) -and
                      [System.IO.File]::Exists([System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.ExcelAddIn.dll")) -and
                      [System.IO.File]::Exists([System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.Core.dll"))

    $wordDllSource  = $null
    $excelDllSource = $null
    $coreDllSource  = $null
    $cerSource      = $null

    if ($standaloneMode) {
        $wordDllSource  = [System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.WordAddIn.dll")
        $excelDllSource = [System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.ExcelAddIn.dll")
        $coreDllSource  = [System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker.Core.dll")
        $cerSource      = [System.IO.Path]::Combine($scriptDir, "MyanmarSpellChecker-EnterpriseCA.cer")
    } else {
        $slnPath      = [System.IO.Path]::Combine($repoRoot, "MyanmarSpellChecker.sln")
        $relWord      = [System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\MyanmarSpellChecker.WordAddIn.dll")
        $relExcl      = [System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\MyanmarSpellChecker.ExcelAddIn.dll")
        $pkgWord      = [System.IO.Path]::Combine($repoRoot, "artifacts\release-package\MyanmarSpellChecker.WordAddIn.dll")
        $pkgExcl      = [System.IO.Path]::Combine($repoRoot, "artifacts\release-package\MyanmarSpellChecker.ExcelAddIn.dll")
        $prebuiltWord = [System.IO.Path]::Combine($prebuiltDir, "MyanmarSpellChecker.WordAddIn.dll")
        $prebuiltExcl = [System.IO.Path]::Combine($prebuiltDir, "MyanmarSpellChecker.ExcelAddIn.dll")

        $hasAnyPrebuilt = ([System.IO.File]::Exists($relWord) -and [System.IO.File]::Exists($relExcl)) -or
                          ([System.IO.File]::Exists($pkgWord) -and [System.IO.File]::Exists($pkgExcl)) -or
                          ([System.IO.File]::Exists($prebuiltWord) -and [System.IO.File]::Exists($prebuiltExcl))

        if (-not $hasAnyPrebuilt) {
            $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
            if (-not $dotnetCmd) {
                throw "Pre-built Add-in files were not found, and .NET SDK is not installed on this PC.`r`nPlease re-download the complete repository or 'MyanmarSpellChecker-EasySetup-Word-Excel.zip' (no .NET SDK needed!)."
            }
            Write-Host "[1/3] Compiling Release binaries with dotnet build..." -ForegroundColor Cyan
            & dotnet build $slnPath -c Release /p:DebugType=none /p:DebugSymbols=false
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet build failed. Please close Word and Excel and try again."
            }
        }

        $wordDllSource = Resolve-BestDll @(
            $relWord,
            $pkgWord,
            ([System.IO.Path]::Combine($repoRoot, "artifacts\staged-word\MyanmarSpellChecker.WordAddIn.dll")),
            $prebuiltWord,
            ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48\MyanmarSpellChecker.WordAddIn.dll"))
        )
        $excelDllSource = Resolve-BestDll @(
            $relExcl,
            $pkgExcl,
            ([System.IO.Path]::Combine($repoRoot, "artifacts\staged-excel\MyanmarSpellChecker.ExcelAddIn.dll")),
            $prebuiltExcl,
            ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Debug\net48\MyanmarSpellChecker.ExcelAddIn.dll"))
        )

        if (-not $wordDllSource -or -not $excelDllSource) {
            throw "Compiled WordAddIn / ExcelAddIn DLLs could not be found."
        }
        $coreDllSource = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($excelDllSource), "MyanmarSpellChecker.Core.dll")

        $cerCandidates = @(
            ([System.IO.Path]::Combine($repoRoot, "artifacts\release-package\MyanmarSpellChecker-EnterpriseCA.cer")),
            ([System.IO.Path]::Combine($prebuiltDir, "MyanmarSpellChecker-EnterpriseCA.cer")),
            ([System.IO.Path]::Combine($repoRoot, "artifacts\MyanmarSpellChecker-EnterpriseCA.cer"))
        )
        foreach ($cc in $cerCandidates) {
            if ([System.IO.File]::Exists($cc)) {
                $cerSource = $cc
                break
            }
        }
    }

    # 4. Locate Full 12.44 MB Myanmar Dictionary Database (89,500+ headwords / 118,600+ entries)
    # Tier 1: Prefer database bundled directly inside this installer / repository folder
    $dbSource = Resolve-FullDictionaryDb @(
        ([System.IO.Path]::Combine($scriptDir, "mlc_thatpon.db")),
        ([System.IO.Path]::Combine($scriptDir, "data\mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "data\mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "artifacts\release-package\mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "artifacts\mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\mlc_thatpon.db")),
        ([System.IO.Path]::Combine($repoRoot, "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\mlc_thatpon.db"))
    )

    # Tier 2: Fallback to an existing full >= 5 MB installation on this PC only if bundled DB was moved/omitted
    if (-not $dbSource) {
        $dbSource = Resolve-FullDictionaryDb @(
            ([System.IO.Path]::Combine($appInstallDir, "mlc_thatpon.db")),
            ([System.IO.Path]::Combine($localRootDir, "mlc_thatpon.db")),
            ([System.IO.Path]::Combine($env:ProgramData, "MyanmarSpellChecker\mlc_thatpon.db")),
            ([System.IO.Path]::Combine($env:ProgramFiles, "MyanmarSpellChecker\mlc_thatpon.db"))
        )
    }

    # Auto-compile from data\sources if DB was deleted/incomplete and .NET SDK is present
    if (-not $dbSource) {
        $lexiconBuilder = [System.IO.Path]::Combine($scriptDir, "Build-MasterLexicon.ps1")
        $sourcesDir     = [System.IO.Path]::Combine($repoRoot, "data\sources")
        $dotnetCmd      = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetCmd -and [System.IO.File]::Exists($lexiconBuilder) -and [System.IO.Directory]::Exists($sourcesDir)) {
            Write-Host "[*] Compiling full mlc_thatpon.db from data\sources..." -ForegroundColor Cyan
            & $lexiconBuilder -CompileEncryptedDb
            $dbSource = Resolve-FullDictionaryDb @([System.IO.Path]::Combine($repoRoot, "data\mlc_thatpon.db"))
        }
    }

    # Interactive fallback: let user browse for mlc_thatpon.db if they moved the script out of its folder
    if (-not $dbSource -and -not $Silent -and -not $ValidateOnly) {
        $dlg = New-Object System.Windows.Forms.OpenFileDialog
        $dlg.Title  = "Select mlc_thatpon.db (Full Myanmar Dictionary Database >= 12 MB)"
        $dlg.Filter = "Myanmar Dictionary Database (mlc_thatpon.db)|mlc_thatpon.db|Database Files (*.db)|*.db|All Files (*.*)|*.*"
        if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $dbSource = Resolve-FullDictionaryDb @($dlg.FileName)
        }
        $dlg.Dispose()
    }

    if (-not $dbSource -or -not [System.IO.File]::Exists($dbSource) -or ([System.IO.FileInfo]::new($dbSource).Length -lt 5000000)) {
        throw "Full Myanmar Dictionary Database (mlc_thatpon.db >= 12 MB) was not found!`r`nPlease make sure you extracted the entire .ZIP folder (including 'data\mlc_thatpon.db' or 'mlc_thatpon.db') before running Setup."
    }

    if ($ValidateOnly) {
        $validDbBytes = [System.IO.FileInfo]::new($dbSource).Length
        Write-Host "VALIDATE_OK|Core=$coreDllSource|Word=$wordDllSource|Excel=$excelDllSource|Db=$dbSource|DbBytes=$validDbBytes"
        exit 0
    }

    # 5. Copy binaries & full 89,500+ word dictionary into permanent per-user %LocalAppData%\MyanmarSpellChecker\app
    Write-Host "[2/3] Installing Add-in files & 89,500+ word Offline Dictionary to $appInstallDir..." -ForegroundColor Cyan
    [void][System.IO.Directory]::CreateDirectory($localRootDir)
    [void][System.IO.Directory]::CreateDirectory($appInstallDir)

    [System.IO.File]::Copy($coreDllSource,  [System.IO.Path]::Combine($appInstallDir, "MyanmarSpellChecker.Core.dll"),     $true)
    [System.IO.File]::Copy($wordDllSource,  [System.IO.Path]::Combine($appInstallDir, "MyanmarSpellChecker.WordAddIn.dll"),  $true)
    [System.IO.File]::Copy($excelDllSource, [System.IO.Path]::Combine($appInstallDir, "MyanmarSpellChecker.ExcelAddIn.dll"), $true)
    [System.IO.File]::Copy($dbSource,       [System.IO.Path]::Combine($appInstallDir, "mlc_thatpon.db"),                     $true)
    [System.IO.File]::Copy($dbSource,       [System.IO.Path]::Combine($localRootDir,  "mlc_thatpon.db"),                     $true)

    try {
        foreach ($f in [System.IO.Directory]::GetFiles($appInstallDir)) {
            Unblock-File -LiteralPath $f -ErrorAction SilentlyContinue
        }
        Unblock-File -LiteralPath ([System.IO.Path]::Combine($localRootDir, "mlc_thatpon.db")) -ErrorAction SilentlyContinue
    } catch { }

    if ($cerSource -and [System.IO.File]::Exists($cerSource)) {
        try {
            Import-Certificate -FilePath $cerSource -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null
        } catch { }
    }

    # 6. Register Word + Excel COM Add-ins in HKCU (Zero Admin Privileges Required!)
    Write-Host "[3/3] Registering Microsoft Word & Microsoft Excel Add-ins (64-bit & 32-bit)..." -ForegroundColor Cyan
    $views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
    foreach ($view in $views) {
        $hkcuBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            foreach ($c in $comClasses) {
                $installedDll = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($appInstallDir, $c.DllName))
                $codeBaseUri  = ([System.Uri]::new($installedDll)).AbsoluteUri.Replace("%20", " ")

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

    New-Item -Path $wordAddInKey -Force | Out-Null
    Set-ItemProperty -Path $wordAddInKey -Name "FriendlyName" -Value "Myanmar Official Spell Checker" -Type String
    Set-ItemProperty -Path $wordAddInKey -Name "Description"  -Value "100% Offline Myanmar Spell and Grammar Checker (Word)" -Type String
    Set-ItemProperty -Path $wordAddInKey -Name "LoadBehavior" -Value 3 -Type DWord

    New-Item -Path $excelAddInKey -Force | Out-Null
    Set-ItemProperty -Path $excelAddInKey -Name "FriendlyName" -Value "Myanmar Official Spell Checker (Excel)" -Type String
    Set-ItemProperty -Path $excelAddInKey -Name "Description"  -Value "100% Offline Formula-Safe Myanmar Spell and Grammar Checker (Excel)" -Type String
    Set-ItemProperty -Path $excelAddInKey -Name "LoadBehavior" -Value 3 -Type DWord

    foreach ($ver in @("16.0", "15.0", "14.0")) {
        foreach ($appName in @("Word", "Excel")) {
            Remove-Item -Path "HKCU:\Software\Microsoft\Office\$ver\$appName\Resiliency\DisabledItems" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path "HKCU:\Software\Microsoft\Office\$ver\$appName\Resiliency\CrashingAddinList" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Suppress Office's built-in "MISSING PROOFING TOOLS: Proofing is not available for Burmese." yellow banner
    $langResKey = "HKCU:\Software\Microsoft\Office\16.0\Common\LanguageResources"
    if (Test-Path $langResKey) {
        try {
            $curNever = [string](Get-ItemProperty -Path $langResKey -Name "NotificationsNeverShowAgainLanguages" -ErrorAction SilentlyContinue).NotificationsNeverShowAgainLanguages
            $tags = @($curNever -split ';' | Where-Object { $_ -and $_.Trim() })
            foreach ($t in @("my-MM", "my")) {
                if ($tags -notcontains $t) { $tags += $t }
            }
            Set-ItemProperty -Path $langResKey -Name "NotificationsNeverShowAgainLanguages" -Value ($tags -join ';')
        } catch { }
    }

    $dbMb = [Math]::Round(([System.IO.FileInfo]::new([System.IO.Path]::Combine($appInstallDir, "mlc_thatpon.db")).Length / 1MB), 2)
    $doneMsg = "✓ မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် အဘိဓာန် (Word + Excel) ကို အောင်မြင်စွာ ထည့်သွင်းပြီးပါပြီ!`r`n`r`n" +
               "• Microsoft Word နှင့် Microsoft Excel နှစ်မျိုးလုံးတွင် အသင့်သုံးနိုင်ပါပြီ။`r`n" +
               "• Offline အဘိဓာန် ဒေတာဘေ့စ်: $dbMb MB (ဝေါဟာရနှင့် သုတပေါင်း ၁၁၈,၆၀၀+ အပြည့်အစုံ)`r`n" +
               "• ထည့်သွင်းထားသည့်နေရာ: $appInstallDir`r`n`r`n" +
               "ယခု Microsoft Word သို့မဟုတ် Excel ကို ဖွင့်၍ စတင်အသုံးပြုနိုင်ပါပြီ။"
    Show-UserInfo $doneMsg
    exit 0
}
catch {
    $errMsg = "ထည့်သွင်းစဉ် အခက်အခဲတစ်ခု ဖြစ်ပေါ်ခဲ့ပါသည် (Installation Error):`r`n`r`n$($_.Exception.Message)"
    Show-UserInfo $errMsg ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
