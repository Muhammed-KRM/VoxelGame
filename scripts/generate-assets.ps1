<#
.SYNOPSIS
    Otonom 3B Varlik ve Doku Fabrikasini (Tools/AutonomousAssetFactory) faz bazli calistirir ve pytest ile dogrular.
#>
[CmdletBinding()]
param(
    [int]$Phase = 0
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$FactoryDir = Join-Path $RepoRoot "Tools\AutonomousAssetFactory"
$VenvPython = Join-Path $FactoryDir ".venv\Scripts\python.exe"

$machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$machinePath;$userPath;$env:USERPROFILE\.local\bin;$env:LOCALAPPDATA\Microsoft\WinGet\Links"

if (-not (Test-Path $VenvPython)) {
    $uvCmd = Get-Command uv -ErrorAction SilentlyContinue
    if (-not $uvCmd) { throw "Python .venv ve uv bulunamadi! Once scripts\setup-environment.ps1 calistirin." }
    Write-Host "[AssetFactory] Python 3.11 .venv olusturuluyor..." -ForegroundColor Cyan
    & $uvCmd venv (Join-Path $FactoryDir ".venv") --python 3.11 --allow-existing
    & $uvCmd pip install --python $VenvPython -r (Join-Path $FactoryDir "requirements.txt")
}

Write-Host "[AssetFactory] Faz $Phase kuresel dokulari ve 3B varliklari uretiliyor..." -ForegroundColor Cyan
& $VenvPython (Join-Path $FactoryDir "agent_generate_asset.py") --phase $Phase
if ($LASTEXITCODE -ne 0) { throw "agent_generate_asset.py hata ile sonlandi (Phase=$Phase)." }

Write-Host "[AssetFactory] Ruff lint ve Pytest kalite kapisi kosuluyor..." -ForegroundColor Cyan
& $VenvPython -m ruff check $FactoryDir
if ($LASTEXITCODE -ne 0) { throw "Ruff Python kod kalitesi kontrolu basarisiz!" }

& $VenvPython -m pytest (Join-Path $FactoryDir "tests") -v
if ($LASTEXITCODE -ne 0) { throw "Pytest 3B varlik ve manifesto testleri basarisiz!" }

Write-Host "[AssetFactory] Faz $Phase uretim ve dogrulamasi %100 basarili." -ForegroundColor Green
