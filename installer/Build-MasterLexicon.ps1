param(
    [switch]$CompileEncryptedDb
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dataDir = Join-Path $repoRoot "data"
$outMasterPath = Join-Path $dataDir "mlc_thatpon_master.json"
$adminToolsProj = Join-Path $repoRoot "src\MyanmarSpellChecker.AdminTools\MyanmarSpellChecker.AdminTools.csproj"

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " Building Master Myanmar Lexicon (462-Page သတ်ပုံကျမ်း + Multi-Domain)" -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

& dotnet run --project $adminToolsProj -c Release -- build-lexicon $dataDir $outMasterPath
if ($LASTEXITCODE -ne 0) {
    throw "MasterLexiconCompiler failed with exit code $LASTEXITCODE."
}

if ($CompileEncryptedDb) {
    $outDbPath = Join-Path $dataDir "mlc_thatpon.db"
    Write-Host "[*] Compiling encrypted database -> $outDbPath..." -ForegroundColor Cyan
    & dotnet run --project $adminToolsProj -c Release --no-build -- compile-dict $outMasterPath $outDbPath
    if ($LASTEXITCODE -ne 0) {
        throw "compile-dict failed with exit code $LASTEXITCODE."
    }
}
