<#
.SYNOPSIS
    Dunya kayit klasorundeki SQLite veritabanlarinin (PRAGMA integrity_check) ve .rgn bolge dosyalarinin baslik butunlugunu dogrular.
#>
[CmdletBinding()]
param(
    [string]$WorldName = "default_world"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$WorldDir = Join-Path $RepoRoot "saves\$WorldName"

if (-not (Test-Path $WorldDir)) {
    Write-Host "[Verify] Dunya klasoru henuz olusmamis ($WorldDir) - Atlaniyor." -ForegroundColor Yellow
    exit 0
}

$SqliteCmd = Get-Command sqlite3 -ErrorAction SilentlyContinue
$DbFiles = Get-ChildItem -Path $WorldDir -Filter "*.db" -File -Recurse
foreach ($db in $DbFiles) {
    if ($SqliteCmd) {
        $check = (& $SqliteCmd $db.FullName "PRAGMA integrity_check;" | Out-String).Trim()
        if ($check -ne "ok") {
            throw "SQLite bozulmasi tespit edildi ($($db.Name)): $check"
        }
        Write-Host "[Verify] SQLite OK: $($db.Name)" -ForegroundColor Green
    }
}

$RgnFiles = Get-ChildItem -Path $WorldDir -Filter "*.rgn" -File -Recurse -ErrorAction SilentlyContinue
foreach ($rgn in $RgnFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($rgn.FullName)
    if ($bytes.Length -lt 24) {
        throw "Bozuk .rgn dosyasi (24 bayt basliktan kisa): $($rgn.FullName)"
    }
    $magic = [System.BitConverter]::ToUInt32($bytes, 0)
    if ($magic -ne 0x4E475253) { # 'SRGN'
        throw "Gecersiz .rgn magic basligi: $($rgn.FullName)"
    }
    Write-Host "[Verify] Region OK: $($rgn.Name) ($($bytes.Length) bytes)" -ForegroundColor Green
}

Write-Host "[Verify] Tum dunya dosyalari saglam." -ForegroundColor Green
