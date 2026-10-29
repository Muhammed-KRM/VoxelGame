<#
.SYNOPSIS
    Dunya kaydini (SQLite WAL world_state.db + bolgesel .rgn dosyalari) atomik olarak yedekler ve SHA-256 dogrulama ozeti uretir.
#>
[CmdletBinding()]
param(
    [string]$WorldName = "default_world",
    [string]$OutputZip = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$WorldDir = Join-Path $RepoRoot "saves\$WorldName"

if (-not (Test-Path $WorldDir)) {
    throw "Yedeklenecek dunya klasoru bulunamadi: $WorldDir"
}

$BackupsDir = Join-Path $RepoRoot "saves\backups"
New-Item -ItemType Directory -Force -Path $BackupsDir | Out-Null

$Timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd_HHmmss")
if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path $BackupsDir "${WorldName}_${Timestamp}.zip"
} else {
    $OutParent = Split-Path -Parent $OutputZip
    if ($OutParent) { New-Item -ItemType Directory -Force -Path $OutParent | Out-Null }
}

$StagingDir = Join-Path $BackupsDir "_staging_${WorldName}_${Timestamp}"
New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null

try {
    $SqliteCmd = Get-Command sqlite3 -ErrorAction SilentlyContinue
    Get-ChildItem -Path $WorldDir -File | ForEach-Object {
        $src = $_.FullName
        $dst = Join-Path $StagingDir $_.Name
        if (($_.Extension -eq ".db") -and $SqliteCmd) {
            $escapedDst = $dst.Replace("'", "''")
            & $SqliteCmd $src "VACUUM INTO '$escapedDst';"
            if ($LASTEXITCODE -ne 0) {
                Copy-Item -Path $src -Destination $dst -Force
            }
        } elseif ($_.Name -notmatch '\.(db-wal|db-shm)$') {
            Copy-Item -Path $src -Destination $dst -Force
        }
    }

    $RegionsDir = Join-Path $WorldDir "regions"
    if (Test-Path $RegionsDir) {
        Copy-Item -Path $RegionsDir -Destination (Join-Path $StagingDir "regions") -Recurse -Force
    }

    # SHA-256 butunluk manifestosu
    $ManifestLines = @()
    Get-ChildItem -Path $StagingDir -File -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($StagingDir.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $ManifestLines += "$hash  $rel"
    }
    Set-Content -Path (Join-Path $StagingDir "SHA256SUMS.txt") -Value $ManifestLines -Encoding ASCII

    if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
    Compress-Archive -Path (Join-Path $StagingDir "*") -DestinationPath $OutputZip -Force
    $ZipHash = (Get-FileHash -Path $OutputZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$OutputZip.sha256" -Value "$ZipHash  $(Split-Path -Leaf $OutputZip)" -Encoding ASCII

    Write-Host "[Backup] Dunya yedegi alindi: $OutputZip (SHA-256: $ZipHash)" -ForegroundColor Green
}
finally {
    if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }
}
