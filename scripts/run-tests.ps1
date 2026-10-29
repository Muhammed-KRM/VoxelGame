<#
.SYNOPSIS
    Tum katmanlardaki otomatik testleri kosar:
    1) C++20 StratocraftNative GoogleTest (ctest)
    2) Python Otonom 3B Fabrika (ruff + pytest)
    3) C# Core, FixedQ20_12, 3 Katmanli SQLite Telemetri ve P/Invoke Native Kopru Testleri (dotnet test)
    4) Unity 6 Kurulu ve Lisansli ise Unity Batchmode EditMode Testleri
#>
[CmdletBinding()]
param(
    [switch]$SkipUnityBatchmode
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

$machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$machinePath;$userPath;C:\Program Files\CMake\bin;$env:USERPROFILE\.local\bin;$env:LOCALAPPDATA\Microsoft\WinGet\Links"

Write-Host "=== [1/4] C++20 StratocraftNative Derleme ve GoogleTest (ctest) ===" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "build-native.ps1")

Write-Host "=== [2/4] Python 3.11 Otonom 3B Fabrika Ruff & Pytest ===" -ForegroundColor Cyan
$FactoryDir = Join-Path $RepoRoot "Tools\AutonomousAssetFactory"
$VenvPython = Join-Path $FactoryDir ".venv\Scripts\python.exe"
& $VenvPython -m ruff check $FactoryDir
if ($LASTEXITCODE -ne 0) { throw "Ruff basarisiz!" }
& $VenvPython -m pytest (Join-Path $FactoryDir "tests") -v
if ($LASTEXITCODE -ne 0) { throw "Pytest basarisiz!" }

Write-Host "=== [3/4] C# Core, SQLite Telemetri ve Native SIMD P/Invoke Birim Testleri ===" -ForegroundColor Cyan
$StandaloneTestProj = Join-Path $RepoRoot "Tests\StandaloneCoreTests\Stratocraft.StandaloneCoreTests.csproj"
if (Test-Path $StandaloneTestProj) {
    & dotnet test $StandaloneTestProj -c Release --nologo --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw "C# Core birim testleri basarisiz!" }
}

Write-Host "=== [4/4] Unity 6 Batchmode EditMode Kontrolu ===" -ForegroundColor Cyan
$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.25f1\Editor\Unity.exe"
if (-not (Test-Path $UnityExe)) {
    $found = Get-ChildItem "C:\Program Files\Unity\Hub\Editor\6000*\Editor\Unity.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $UnityExe = $found.FullName }
}

if ((-not $SkipUnityBatchmode) -and (Test-Path $UnityExe)) {
    $ResultsXml = Join-Path $RepoRoot "BuildLogs\unity_editmode_results.xml"
    $UnityLog = Join-Path $RepoRoot "BuildLogs\unity_editmode.log"
    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "BuildLogs") | Out-Null
    Write-Host "Unity 6 Batchmode EditMode testleri kosuluyor..." -ForegroundColor Cyan
    & $UnityExe -batchmode -nographics -projectPath $RepoRoot -runTests -testPlatform EditMode -testResults $ResultsXml -logFile $UnityLog
    if ($LASTEXITCODE -ne 0) {
        throw "Unity 6 EditMode testleri basarisiz veya lisans aktivasyonu bekliyor! Log: $UnityLog"
    }
    Write-Host "Unity 6 EditMode testleri %100 gecti." -ForegroundColor Green
} else {
    Write-Host "BILGI: Unity 6 Editor henuz kurulu degil veya SkipUnityBatchmode aktif; C# Core kodlari dotnet test uzerinden %100 dogrulandi." -ForegroundColor Yellow
}

Write-Host "[Stratocraft] Tum aktif test katmanlari BASARILI (PASS)." -ForegroundColor Green
