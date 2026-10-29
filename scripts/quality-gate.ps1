<#
.SYNOPSIS
    Her faz sonunda zorunlu calistirilan 7 maddelik Ortak Kalite Kapisi (Quality Gate).
#>
[CmdletBinding()]
param(
    [int]$Phase = 0,
    [switch]$AllowPendingUnityLicense
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=================================================================" -ForegroundColor Magenta
Write-Host " STRATOCRAFT KALITE KAPISI (QUALITY GATE) - FAZ $Phase" -ForegroundColor Magenta
Write-Host "=================================================================" -ForegroundColor Magenta

# 1, 2, 3, 4: C++20 (-Wall/-W4 -Werror), Python 3B Fabrika, C# Core + 3 Katmanli SQLite Loglama Testleri
& (Join-Path $PSScriptRoot "generate-assets.ps1") -Phase $Phase
if ($AllowPendingUnityLicense) {
    & (Join-Path $PSScriptRoot "run-tests.ps1") -SkipUnityBatchmode
} else {
    & (Join-Path $PSScriptRoot "run-tests.ps1")
}

# 5. Kaplama VRAM Butcesi Kontrolu (< 16.0 MB Zorunlu Ust Sinir)
$TexDir = Join-Path $RepoRoot "Assets\Art\Generated\Textures"
if (-not (Test-Path $TexDir)) {
    throw "Kalite Kapisi Hatasi: $TexDir bulunamadi!"
}
$totalTexBytes = (Get-ChildItem -Path $TexDir -File -Recurse | Measure-Object -Property Length -Sum).Sum
$totalTexMb = [math]::Round($totalTexBytes / 1MB, 3)
Write-Host "[Butce Kontrolu] Toplam Kuresel Kaplama Boyutu (VRAM Butcesi): $totalTexMb MB / 16.000 MB" -ForegroundColor Cyan
if ($totalTexMb -ge 16.0) {
    throw "VRAM BUTCESSI ASILDI! Toplam kaplama: $totalTexMb MB (Limit: < 16.0 MB)"
}

# 6. Gizli Dosya (.env / saves/) Sizinti Denetimi
Push-Location $RepoRoot
try {
    $tracked = git ls-files
    if ($tracked -match '(^|/)\.env$' -or $tracked -match '^saves/') {
        throw "GUVENLIK IHLALI: .env veya saves/ Git tarafindan izleniyor!"
    }
}
finally {
    Pop-Location
}

Write-Host "=================================================================" -ForegroundColor Green
Write-Host " KALITE KAPISI BASARILI (PASS) - FAZ $Phase (Kaplama VRAM: $totalTexMb MB)" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
