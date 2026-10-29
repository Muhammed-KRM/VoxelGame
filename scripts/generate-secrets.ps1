<#
.SYNOPSIS
    Yerel .env dosyasini .env.example sablonundan uretir ve kriptografik rastgele anahtarlar atar.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$EnvExamplePath = Join-Path $RepoRoot ".env.example"
$EnvPath = Join-Path $RepoRoot ".env"

if ((Test-Path $EnvPath) -and (-not $Force)) {
    Write-Host "[Stratocraft] .env zaten mevcut, degistirilmedi: $EnvPath" -ForegroundColor Cyan
    exit 0
}

if (-not (Test-Path $EnvExamplePath)) {
    throw ".env.example bulunamadi: $EnvExamplePath"
}

function New-RandomHexString([int]$ByteCount) {
    $bytes = New-Object byte[] $ByteCount
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    return ([System.BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
}

$adminKey = New-RandomHexString -ByteCount 32
$contractSecret = New-RandomHexString -ByteCount 32

$content = Get-Content -Path $EnvExamplePath -Raw -Encoding UTF8
$content = $content -replace 'DEGISTIR_BENI_en_az_32_karakter_uzun_gizli_anahtar', $adminKey
$content = $content -replace 'DEGISTIR_BENI_ed25519_sunucu_m.+r_anahtari', $contractSecret

[System.IO.File]::WriteAllText($EnvPath, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[Stratocraft] Yerel .env basariyla uretildi (Git disinda tutulur)." -ForegroundColor Green
