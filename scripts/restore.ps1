<#
.SYNOPSIS
    Yedek arsivini (.zip) SHA-256 kontrolunden gecirerek saves/<WorldName> altina atomik olarak geri yukler.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArchivePath,
    [string]$WorldName = "default_world"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $ArchivePath)) {
    throw "Yedek arsivi bulunamadi: $ArchivePath"
}

$ShaFile = "$ArchivePath.sha256"
if (Test-Path $ShaFile) {
    $expected = ((Get-Content $ShaFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actual = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "SHA-256 DOGRULAMA HATASI! Beklenen: $expected, Gercek: $actual"
    }
}

$SavesDir = Join-Path $RepoRoot "saves"
New-Item -ItemType Directory -Force -Path $SavesDir | Out-Null
$TargetDir = Join-Path $SavesDir $WorldName
$TempExtract = Join-Path $SavesDir "_restore_${WorldName}_$([Guid]::NewGuid().ToString('N').Substring(0,8))"

try {
    Expand-Archive -Path $ArchivePath -DestinationPath $TempExtract -Force
    $InnerSums = Join-Path $TempExtract "SHA256SUMS.txt"
    if (Test-Path $InnerSums) {
        foreach ($line in (Get-Content $InnerSums)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = $line -split '\s+', 2
            $filePath = Join-Path $TempExtract ($parts[1].Replace('/', '\'))
            if (-not (Test-Path $filePath)) { throw "Arsiv icinde eksik dosya: $($parts[1])" }
            $fileHash = (Get-FileHash -Path $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($fileHash -ne $parts[0].ToLowerInvariant()) {
                throw "Dosya SHA-256 uyusmazligi: $($parts[1])"
            }
        }
        Remove-Item $InnerSums -Force
    }

    if (Test-Path $TargetDir) {
        $PrevBackup = "${TargetDir}_pre_restore_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Move-Item -Path $TargetDir -Destination $PrevBackup
    }
    Move-Item -Path $TempExtract -Destination $TargetDir
    Write-Host "[Restore] Dunya basariyla geri yuklendi: $TargetDir" -ForegroundColor Green
}
finally {
    if (Test-Path $TempExtract) { Remove-Item $TempExtract -Recurse -Force }
}
