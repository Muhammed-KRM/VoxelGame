<#
.SYNOPSIS
    C++20 StratocraftNative SIMD kutuphanesini (AVX2/FMA, LZ4, Zstd, XXH64, Ed25519, SQLite3 WAL) derler,
    GoogleTest (ctest) birim testlerini kosar ve StratocraftNative.dll dosyasini Assets/Plugins/x86_64/ altina kopyalar.
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$NativeDir = Join-Path $RepoRoot "NativePlugins\StratocraftNative"
$BuildDir = Join-Path $NativeDir "build"
$PluginOutDir = Join-Path $RepoRoot "Assets\Plugins\x86_64"

$PortableLlvmBin = Join-Path $RepoRoot "Tools\bin\llvm-mingw\bin"
$machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
$env:Path = "$PortableLlvmBin;$machinePath;$userPath;C:\Program Files\CMake\bin;C:\Program Files\LLVM\bin;$env:LOCALAPPDATA\Microsoft\WinGet\Links"

$cmakeCmd = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmakeCmd) {
    throw "cmake bulunamadi! Lutfen once scripts\setup-environment.ps1 calistirin."
}

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
New-Item -ItemType Directory -Force -Path $PluginOutDir | Out-Null

# MSVC veya Clang++ tespiti: vswhere uzerinden vcvars64 veya Ninja+clang++
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vcInstallDir = $null
if (Test-Path $vsWhere) {
    $vcInstallDir = & $vsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}

Write-Host "[StratocraftNative] CMake yapilandirmasi baslatiliyor ($Configuration)..." -ForegroundColor Cyan

if ($vcInstallDir) {
    $vcvars = Join-Path $vcInstallDir "VC\Auxiliary\Build\vcvars64.bat"
    if (Test-Path $vcvars) {
        $ninjaCmd = Get-Command ninja -ErrorAction SilentlyContinue
        $genArg = if ($ninjaCmd) { "-G Ninja" } else { "" }
        $cmdLine = "`"$vcvars`" && cmake -S `"$NativeDir`" -B `"$BuildDir`" $genArg -DCMAKE_BUILD_TYPE=$Configuration && cmake --build `"$BuildDir`" --config $Configuration --parallel && ctest --test-dir `"$BuildDir`" -C $Configuration --output-on-failure"
        cmd.exe /c $cmdLine
        if ($LASTEXITCODE -ne 0) { throw "StratocraftNative C++20 derleme veya ctest basarisiz oldu (ExitCode=$LASTEXITCODE)." }
    } else {
        & cmake -S $NativeDir -B $BuildDir "-DCMAKE_BUILD_TYPE=$Configuration"
        if ($LASTEXITCODE -ne 0) { throw "CMake configure basarisiz." }
        & cmake --build $BuildDir --config $Configuration --parallel
        if ($LASTEXITCODE -ne 0) { throw "CMake build basarisiz." }
        & ctest --test-dir $BuildDir -C $Configuration --output-on-failure
        if ($LASTEXITCODE -ne 0) { throw "ctest basarisiz." }
    }
}
elseif (Get-Command clang++ -ErrorAction SilentlyContinue) {
    $ninjaCmd = Get-Command ninja -ErrorAction SilentlyContinue
    $genArgs = if ($ninjaCmd) { @("-G", "Ninja") } else { @() }
    & cmake -S $NativeDir -B $BuildDir @genArgs "-DCMAKE_CXX_COMPILER=clang++" "-DCMAKE_C_COMPILER=clang" "-DCMAKE_BUILD_TYPE=$Configuration" -Wno-deprecated
    if ($LASTEXITCODE -ne 0) { throw "CMake Clang configure basarisiz." }
    & cmake --build $BuildDir --config $Configuration --parallel
    if ($LASTEXITCODE -ne 0) { throw "CMake Clang build basarisiz." }
    & ctest --test-dir $BuildDir -C $Configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw "ctest basarisiz." }
}
else {
    throw "C++20 derleyicisi (MSVC v143 veya Clang++) bulunamadi! Lutfen scripts\setup-environment.ps1 calistirin."
}

$dllCandidate = Get-ChildItem -Path $BuildDir -Filter "StratocraftNative.dll" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dllCandidate) {
    throw "Derleme tamamlandi ancak StratocraftNative.dll bulunamadi!"
}

$destDll = Join-Path $PluginOutDir "StratocraftNative.dll"
Copy-Item -Path $dllCandidate.FullName -Destination $destDll -Force
Write-Host "[StratocraftNative] Basarili! DLL kopyalandi -> $destDll ($([math]::Round($dllCandidate.Length / 1KB, 1)) KB)" -ForegroundColor Green
