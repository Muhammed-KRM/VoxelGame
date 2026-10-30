<#
.SYNOPSIS
    Antigravity SessionStart Yasam Dongusu Kancasi (Lifecycle Hook).
    Yeni bir sohbet oturumu basladiginda son Git commit'lerini ve DURUM_VE_DEVIR.md aktif kimlik satirlarini otomatik enjekte eder.
#>
$ErrorActionPreference = "SilentlyContinue"
$RepoRoot = Split-Path -Parent $PSScriptRoot

# stdin tuket (Antigravity hook sozlesmesi)
try { [void][Console]::In.ReadToEnd() } catch {}

$gitLines = ""
try {
    $gitLines = (& git -C $RepoRoot log -3 --oneline --date=iso 2>$null | Out-String).Trim()
} catch {}

$devirSummary = ""
$devirFile = Join-Path $RepoRoot "DURUM_VE_DEVIR.md"
if (Test-Path $devirFile) {
    $matched = Select-String -Path $devirFile -Pattern "^\|\s*\*\*(Devir Numar|Son G|Referans Son|Son Tamamlanan Faz|Aktif Faz|Faz Durumu)" -Encoding UTF8 | ForEach-Object { $_.Line }
    if ($matched) {
        $devirSummary = ($matched -join " | ")
    }
}

$msg = "[Stratocraft Otomatik Oturum Ozeti] Son Git Gecmisi: " + ($gitLines -replace "\r?\n", " ; ") + " || Devir Raporu: " + $devirSummary

$payload = @{
    injectSteps = @(
        @{
            systemMessage = @{
                systemMessage = $msg
            }
        }
    )
} | ConvertTo-Json -Depth 6 -Compress

[Console]::Out.Write($payload)
