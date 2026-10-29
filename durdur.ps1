<#
.SYNOPSIS
    Calisan Stratocraft yerel sunucu veya baslatilmis arka plan test sureclerini guvenli sekilde sonlandirir.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "SilentlyContinue"
$processes = Get-Process | Where-Object { $_.ProcessName -match '^Stratocraft' }
if ($processes) {
    foreach ($proc in $processes) {
        Write-Host "[Stratocraft] Kapatiliyor: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force
    }
    Write-Host "[Stratocraft] Tum calisan Stratocraft surecleri kapatildi." -ForegroundColor Green
} else {
    Write-Host "[Stratocraft] Aktif calisan Stratocraft sunucu sureci bulunamadi." -ForegroundColor Cyan
}
