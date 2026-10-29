<#
.SYNOPSIS
    Sabah (Is PC) <-> Aksam (Ev PC) GitHub senkronizasyon, devir kontrolu ve yerel uretim betigi.
#>
[CmdletBinding()]
param(
    [switch]$Pull,
    [switch]$Push,
    [string]$Message = "wip(devir): bilgisayarlar arasi oturum devri [devir]"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    if ($Pull) {
        Write-Host "[Sync-PC] Uzak depo (origin/main) kontrol ediliyor..." -ForegroundColor Cyan
        git fetch origin
        git status -sb
        git pull --rebase origin main

        # Yerel .env kontrolu
        & (Join-Path $PSScriptRoot "generate-secrets.ps1")

        # C++20 StratocraftNative.dll yoksa veya kaynak degistiyse derle
        $DllPath = Join-Path $RepoRoot "Assets\Plugins\x86_64\StratocraftNative.dll"
        if (-not (Test-Path $DllPath)) {
            Write-Host "[Sync-PC] StratocraftNative.dll eksik, yerel olarak derleniyor..." -ForegroundColor Cyan
            & (Join-Path $PSScriptRoot "build-native.ps1")
        }

        # Kuresel dokular yoksa uret
        $LutPath = Join-Path $RepoRoot "Assets\Art\Generated\Textures\T_Stratocraft_MaterialLUT_256x16.png"
        $DdsPath = Join-Path $RepoRoot "Assets\Art\Generated\Textures\T_Stratocraft_MicroDetail_Array32.dds"
        if ((-not (Test-Path $LutPath)) -or (-not (Test-Path $DdsPath))) {
            Write-Host "[Sync-PC] Kuresel dokular eksik, yerel olarak firinlaniyor..." -ForegroundColor Cyan
            & (Join-Path $PSScriptRoot "generate-assets.ps1") -Phase 0
        }

        Write-Host "[Sync-PC] Pull ve yerel esitleme tamamlandi. DURUM_VE_DEVIR.md son durumu:" -ForegroundColor Green
        git log -3 --oneline --date=iso
    }
    elseif ($Push) {
        Write-Host "[Sync-PC] Hassas dosya kontrolu yapiliyor..." -ForegroundColor Cyan
        $statusLines = git status --porcelain
        if ($statusLines -match '(^|\s)\.env($|\s)' -or $statusLines -match 'saves/') {
            throw "GUVENLIK HATASI: .env veya saves/ sahnelenmis gorunuyor! Islem durduruldu."
        }

        git add -A
        $staged = git diff --cached --name-only
        if ($staged) {
            git commit -m $Message
            git push origin main
            Write-Host "[Sync-PC] Degisiklikler basariyla origin/main dalina gonderildi." -ForegroundColor Green
        } else {
            Write-Host "[Sync-PC] Gonderilecek yeni degisiklik yok." -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "Kullanim: .\scripts\sync-pc.ps1 -Pull  VEYA  .\scripts\sync-pc.ps1 -Push -Message '...'" -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
