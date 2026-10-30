<#
.SYNOPSIS
    Stratocraft tek komutla baslatma betigi: .env, C++20 DLL ve Otonom 3B Fabrika ciktilarini dogrulayip Unity 6 projesini acar.
#>
[CmdletBinding()]
param(
    [ValidateSet("HostLoopback", "DedicatedServer", "CheckOnly")]
    [string]$Mode = "HostLoopback"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

Write-Host "[Stratocraft] Baslatma on-kontrolleri calistiriliyor (Mod: $Mode)..." -ForegroundColor Cyan

# 1. .env kontrolu
& (Join-Path $RepoRoot "scripts\generate-secrets.ps1")

# 2. StratocraftNative.dll kontrolu
$NativeDll = Join-Path $RepoRoot "Assets\Plugins\x86_64\StratocraftNative.dll"
if (-not (Test-Path $NativeDll)) {
    Write-Host "[Stratocraft] StratocraftNative.dll bulunamadi, derleniyor..." -ForegroundColor Yellow
    & (Join-Path $RepoRoot "scripts\build-native.ps1")
}

# 3. Kuresel Uber-Shader dokulari kontrolu
$LutFile = Join-Path $RepoRoot "Assets\Art\Generated\Textures\T_Stratocraft_MaterialLUT_256x16.png"
if (-not (Test-Path $LutFile)) {
    Write-Host "[Stratocraft] Kuresel dokular bulunamadi, Otonom 3B Fabrika calistiriliyor..." -ForegroundColor Yellow
    & (Join-Path $RepoRoot "scripts\generate-assets.ps1") -Phase 0
}

if ($Mode -eq "CheckOnly") {
    Write-Host "[Stratocraft] Tum on-kontroller basarili (CheckOnly)." -ForegroundColor Green
    exit 0
}

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
if (-not (Test-Path $UnityExe)) {
    $found = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\6000*\Editor\Unity.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $UnityExe = $found.FullName }
}

if (Test-Path $UnityExe) {
    Write-Host "[Stratocraft] Unity 6 aciliyor: $UnityExe" -ForegroundColor Green
    Start-Process -FilePath $UnityExe -ArgumentList "-projectPath `"$RepoRoot`""
} else {
    Write-Host "[Stratocraft] UYARI: Unity 6000.6.7f1 henuz kurulu degil. Unity Hub girisi sonrasi kurulum tamamlanmalidir." -ForegroundColor Yellow
}
