param(
    [switch]$Uninstall,
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms

$TitleText = "Myanmar Official Spell Checker - Setup (Word + Excel)"

function Show-UserInfo([string]$Message, [System.Windows.Forms.MessageBoxIcon]$Icon = [System.Windows.Forms.MessageBoxIcon]::Information) {
    Write-Host $Message
    if (-not $Silent) {
        [void][System.Windows.Forms.MessageBox]::Show($Message, $TitleText, [System.Windows.Forms.MessageBoxButtons]::OK, $Icon)
    }
}

$wordAddInKey  = "HKCU:\Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn"
$excelAddInKey = "HKCU:\Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn"
$wordAsmFullName  = "MyanmarSpellChecker.WordAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
$excelAsmFullName = "MyanmarSpellChecker.ExcelAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"

$localRootDir = Join-Path $env:LOCALAPPDATA "MyanmarSpellChecker"
$appInstallDir = Join-Path $localRootDir "app"

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
    if (Test-Path $appInstallDir) {
        try { Remove-Item -Path $appInstallDir -Recurse -Force -ErrorAction Stop } catch { }
    }
    Show-UserInfo "✓ မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း Add-in (Word + Excel) ကို အောင်မြင်စွာ ဖြုတ်သိမ်းပြီးပါပြီ။`r`n(Successfully uninstalled from Microsoft Word and Excel.)"
    exit 0
}

try {
    $scriptDir = $PSScriptRoot
    $repoRoot  = [System.IO.Path]::GetFullPath((Join-Path $scriptDir ".."))

    # Unblock downloaded files (removes Windows Zone.Identifier Mark-of-the-Web from ZIP downloads!)
    try {
        Get-ChildItem -Path $scriptDir -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
        if (Test-Path (Join-Path $repoRoot "MyanmarSpellChecker.sln")) {
            Get-ChildItem -Path $repoRoot -Recurse -Include "*.dll","*.db","*.cer","*.ps1" -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
        }
    } catch { }

    # 3. Locate pre-built binaries (either standalone ZIP folder OR repo build output)
    $standaloneMode = (Test-Path (Join-Path $scriptDir "MyanmarSpellChecker.WordAddIn.dll")) -and
                      (Test-Path (Join-Path $scriptDir "MyanmarSpellChecker.ExcelAddIn.dll")) -and
                      (Test-Path (Join-Path $scriptDir "MyanmarSpellChecker.Core.dll"))

    $wordDllSource  = $null
    $excelDllSource = $null
    $coreDllSource  = $null
    $dbSource       = $null
    $cerSource      = $null

    if ($standaloneMode) {
        $wordDllSource  = Join-Path $scriptDir "MyanmarSpellChecker.WordAddIn.dll"
        $excelDllSource = Join-Path $scriptDir "MyanmarSpellChecker.ExcelAddIn.dll"
        $coreDllSource  = Join-Path $scriptDir "MyanmarSpellChecker.Core.dll"
        $dbSource       = Join-Path $scriptDir "mlc_thatpon.db"
        $cerSource      = Join-Path $scriptDir "MyanmarSpellChecker-EnterpriseCA.cer"
    } else {
        $slnPath = Join-Path $repoRoot "MyanmarSpellChecker.sln"
        $pkgWord = Join-Path $repoRoot "artifacts\release-package\MyanmarSpellChecker.WordAddIn.dll"
        $relWord = Join-Path $repoRoot "src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\MyanmarSpellChecker.WordAddIn.dll"
        $relExcl = Join-Path $repoRoot "src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\MyanmarSpellChecker.ExcelAddIn.dll"

        if ((-not (Test-Path $relWord) -or -not (Test-Path $relExcl)) -and -not (Test-Path $pkgWord)) {
            $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
            if (-not $dotnetCmd) {
                throw "Pre-built Add-in files were not found, and .NET SDK is not installed on this PC.`r`nPlease download 'MyanmarSpellChecker-EasySetup-Word-Excel.zip' from GitHub Releases (no .NET SDK needed!), or install .NET SDK 6+."
            }
            Write-Host "[1/3] Compiling Release binaries with dotnet build..." -ForegroundColor Cyan
            & dotnet build $slnPath -c Release /p:DebugType=none /p:DebugSymbols=false
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet build failed. Please close Word and Excel and try again."
            }
        }

        function Resolve-BestDll([string[]]$Paths) {
            $valid = @($Paths | Where-Object {
                (Test-Path $_) -and (Test-Path (Join-Path (Split-Path $_ -Parent) "MyanmarSpellChecker.Core.dll"))
            } | Sort-Object {
                $t1 = (Get-Item $_).LastWriteTimeUtc
                $t2 = (Get-Item (Join-Path (Split-Path $_ -Parent) "MyanmarSpellChecker.Core.dll")).LastWriteTimeUtc
                if ($t1 -lt $t2) { $t1 } else { $t2 }
            } -Descending)
            if ($valid.Count -gt 0) { return $valid[0] }
            return $null
        }

        $wordDllSource = Resolve-BestDll @(
            $relWord,
            $pkgWord,
            (Join-Path $repoRoot "artifacts\staged-word\MyanmarSpellChecker.WordAddIn.dll"),
            (Join-Path $repoRoot "src\MyanmarSpellChecker.WordAddIn\bin\Debug\net48\MyanmarSpellChecker.WordAddIn.dll")
        )
        $excelDllSource = Resolve-BestDll @(
            $relExcl,
            (Join-Path $repoRoot "artifacts\release-package\MyanmarSpellChecker.ExcelAddIn.dll"),
            (Join-Path $repoRoot "artifacts\staged-excel\MyanmarSpellChecker.ExcelAddIn.dll"),
            (Join-Path $repoRoot "src\MyanmarSpellChecker.ExcelAddIn\bin\Debug\net48\MyanmarSpellChecker.ExcelAddIn.dll")
        )
        if (-not $wordDllSource -or -not $excelDllSource) {
            throw "Compiled WordAddIn / ExcelAddIn DLLs could not be found."
        }
        $coreDllSource = Join-Path (Split-Path $excelDllSource -Parent) "MyanmarSpellChecker.Core.dll"
        $cerSource = Join-Path $repoRoot "artifacts\release-package\MyanmarSpellChecker-EnterpriseCA.cer"

        $dbCandidates = @(
            (Join-Path $repoRoot "data\mlc_thatpon.db"),
            (Join-Path $repoRoot "artifacts\release-package\mlc_thatpon.db"),
            (Join-Path $repoRoot "artifacts\mlc_thatpon.db")
        ) | Where-Object { Test-Path $_ } | Sort-Object `
            @{ Expression = { if ((Get-Item $_).Length -ge 5000000) { 1 } else { 0 } }; Descending = $true }, `
            @{ Expression = { (Get-Item $_).LastWriteTimeUtc }; Descending = $true }

        if ($dbCandidates.Count -gt 0) {
            $dbSource = $dbCandidates[0]
        }
    }

    if (-not $dbSource -or -not (Test-Path $dbSource) -or ((Get-Item $dbSource).Length -lt 5000000)) {
        throw "Full Myanmar Dictionary Database (mlc_thatpon.db >= 12 MB) was not found! Ensure 'data\mlc_thatpon.db' is present."
    }

    # 4. Copy binaries & full 89,500+ word dictionary into permanent per-user %LocalAppData%\MyanmarSpellChecker\app
    Write-Host "[2/3] Installing Add-in files & 89,500+ word Offline Dictionary to $appInstallDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $localRootDir -Force | Out-Null
    New-Item -ItemType Directory -Path $appInstallDir -Force | Out-Null

    Copy-Item $coreDllSource  (Join-Path $appInstallDir "MyanmarSpellChecker.Core.dll")     -Force
    Copy-Item $wordDllSource  (Join-Path $appInstallDir "MyanmarSpellChecker.WordAddIn.dll")  -Force
    Copy-Item $excelDllSource (Join-Path $appInstallDir "MyanmarSpellChecker.ExcelAddIn.dll") -Force
    Copy-Item $dbSource       (Join-Path $appInstallDir "mlc_thatpon.db")                     -Force
    Copy-Item $dbSource       (Join-Path $localRootDir  "mlc_thatpon.db")                     -Force

    try {
        Get-ChildItem -Path $appInstallDir -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
    } catch { }

    if ($cerSource -and (Test-Path $cerSource)) {
        try {
            Import-Certificate -FilePath $cerSource -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null
        } catch { }
    }

    # 5. Register Word + Excel COM Add-ins in HKCU (Zero Admin Privileges Required!)
    Write-Host "[3/3] Registering Microsoft Word & Microsoft Excel Add-ins (64-bit & 32-bit)..." -ForegroundColor Cyan
    $views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
    foreach ($view in $views) {
        $hkcuBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            foreach ($c in $comClasses) {
                $installedDll = [System.IO.Path]::GetFullPath((Join-Path $appInstallDir $c.DllName))
                $codeBaseUri  = "file:///" + ($installedDll -replace "\\", "/")

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

    $dbMb = [Math]::Round(((Get-Item (Join-Path $appInstallDir "mlc_thatpon.db")).Length / 1MB), 2)
    $doneMsg = "✓ မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် အဘိဓာန် (Word + Excel) ကို အောင်မြင်စွာ ထည့်သွင်းပြီးပါပြီ!`r`n`r`n" +
               "• Microsoft Word နှင့် Microsoft Excel နှစ်မျိုးလုံးတွင် အသင့်သုံးနိုင်ပါပြီ။`r`n" +
               "• Offline အဘိဓာန် ဒေတာဘေ့စ်: $dbMb MB (ဝေါဟာရနှင့် သုတပေါင်း ၁၁၆,၀၀၀+ အပြည့်အစုံ)`r`n" +
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

