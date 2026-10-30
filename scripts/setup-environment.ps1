<#
.SYNOPSIS
    Stratocraft Iki Bilgisayarli (Is <-> Ev) Surum Kilitlemeli Ortam Kurulum Betigi (Faz 0).
    Yeniden baslatma (restart) gerektirmeden tum C++20, Python 3.11, CLI araclari ve Unity Hub/Blender kontrollerini yapar.
#>
[CmdletBinding()]
param(
    [switch]$SkipHeavyVisualStudio,
    [switch]$SkipGuiInstallers,
    [switch]$SkipUnityEditorInstall
)

$ErrorActionPreference = "Continue"
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Refresh-SessionPath {
    $machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $portableClang = Join-Path $RepoRoot "Tools\bin\llvm-mingw\bin"
    $portableBlender = Join-Path $RepoRoot "Tools\bin\blender-4.2"
    $env:Path = "$portableClang;$portableBlender;$machinePath;$userPath;C:\Program Files\CMake\bin;$env:USERPROFILE\.local\bin;$env:LOCALAPPDATA\Microsoft\WinGet\Links"
}

function Install-WingetPackage([string]$PackageId, [string]$DisplayName, [string]$CommandName = "", [string]$OverrideArgs = "") {
    Write-Host "[Kurulum] Kontrol ediliyor: $DisplayName ($PackageId)..." -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($CommandName)) {
        if (Get-Command $CommandName -ErrorAction SilentlyContinue) {
            Write-Host "  -> Komut zaten mevcut ($CommandName): $DisplayName" -ForegroundColor DarkGreen
            return
        }
    }
    $listOut = winget list --id $PackageId -e --source winget --accept-source-agreements 2>$null | Out-String
    if ($listOut -match [regex]::Escape($PackageId)) {
        Write-Host "  -> Zaten kurulu: $DisplayName" -ForegroundColor DarkGreen
        return
    }
    if ([string]::IsNullOrWhiteSpace($OverrideArgs)) {
        winget install --id $PackageId -e --source winget --silent --accept-package-agreements --accept-source-agreements
    } else {
        winget install --id $PackageId -e --source winget --override $OverrideArgs --accept-package-agreements --accept-source-agreements
    }
    Refresh-SessionPath
}

Refresh-SessionPath

Write-Host "=================================================================" -ForegroundColor Yellow
Write-Host " Stratocraft Faz 0 - Surum Kilitlemeli Ortam Kurulumu Basliyor" -ForegroundColor Yellow
Write-Host "=================================================================" -ForegroundColor Yellow

# 1. Hafif CLI Arac Zinciri (Once Get-Command ile kontrol edilir, UAC tetiklemez)
Install-WingetPackage -PackageId "GitHub.GitLFS" -DisplayName "Git LFS" -CommandName "git-lfs"
Install-WingetPackage -PackageId "Kitware.CMake" -DisplayName "CMake 3.28+" -CommandName "cmake"
Install-WingetPackage -PackageId "Ninja-build.Ninja" -DisplayName "Ninja Build" -CommandName "ninja"
Install-WingetPackage -PackageId "astral-sh.uv" -DisplayName "Astral uv (Python 3.11 Yoneticisi)" -CommandName "uv"
Install-WingetPackage -PackageId "SQLite.SQLite" -DisplayName "SQLite CLI" -CommandName "sqlite3"
Install-WingetPackage -PackageId "OpenJS.NodeJS.LTS" -DisplayName "Node.js LTS" -CommandName "node"

# 2. C++20 Derleyici Arac Zinciri (MSVC v143 / Sistem Clang / Sifir-UAC Tasinabilir llvm-mingw)
$hasCpp20 = $false
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $vcPath = & $vsWhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vcPath) { $hasCpp20 = $true }
}
if ((-not $hasCpp20) -and (Get-Command clang++ -ErrorAction SilentlyContinue)) {
    $hasCpp20 = $true
}

if (-not $hasCpp20) {
    if (-not $SkipHeavyVisualStudio) {
        Write-Host "[C++20] Sifir-UAC tasinabilir llvm-mingw (Clang C++20 UCRT x86_64) indiriliyor..." -ForegroundColor Cyan
        $ToolsBin = Join-Path $RepoRoot "Tools\bin"
        $PortableTarget = Join-Path $ToolsBin "llvm-mingw"
        New-Item -ItemType Directory -Force -Path $ToolsBin | Out-Null
        $ZipFile = Join-Path $ToolsBin "llvm-mingw-ucrt-x86_64.zip"
        $ReleaseJson = Invoke-RestMethod -Uri "https://api.github.com/repos/mstorsjo/llvm-mingw/releases/latest" -UseBasicParsing
        $Asset = $ReleaseJson.assets | Where-Object { $_.name -match "ucrt-x86_64\.zip$" } | Select-Object -First 1
        if ($Asset) {
            & curl.exe -L -o $ZipFile $Asset.browser_download_url
            Expand-Archive -Path $ZipFile -DestinationPath $ToolsBin -Force
            Remove-Item $ZipFile -Force
            $Extracted = Get-ChildItem -Path $ToolsBin -Directory -Filter "llvm-mingw-*-ucrt-x86_64" | Select-Object -First 1
            if ($Extracted) {
                if (Test-Path $PortableTarget) { Remove-Item $PortableTarget -Recurse -Force }
                Rename-Item -Path $Extracted.FullName -NewName "llvm-mingw"
            }
            Refresh-SessionPath
            Write-Host "  -> Tasinabilir C++20 derleyici hazir: $PortableTarget" -ForegroundColor Green
        }
    }
} else {
    Write-Host "  -> C++20 derleyici hazir." -ForegroundColor DarkGreen
}

# 3. Opsiyonel Masaustu GUI Araclari (Tasinabilir Blender 4.2 LTS, Blockbench, Unity Hub - Sifir-UAC)
if (-not $SkipGuiInstallers) {
    if (-not (Get-Command blender -ErrorAction SilentlyContinue)) {
        $ToolsBin = Join-Path $RepoRoot "Tools\bin"
        $BlenderTarget = Join-Path $ToolsBin "blender-4.2"
        if (-not (Test-Path (Join-Path $BlenderTarget "blender.exe"))) {
            Write-Host "[Blender] Sifir-UAC tasinabilir Blender 4.2.16 LTS indiriliyor..." -ForegroundColor Cyan
            New-Item -ItemType Directory -Force -Path $ToolsBin | Out-Null
            $BlenderZip = Join-Path $ToolsBin "blender-4.2.16.zip"
            & curl.exe -L -o $BlenderZip "https://download.blender.org/release/Blender4.2/blender-4.2.16-windows-x64.zip"
            Expand-Archive -Path $BlenderZip -DestinationPath $ToolsBin -Force
            Remove-Item $BlenderZip -Force
            $ExBlender = Get-ChildItem -Path $ToolsBin -Directory -Filter "blender-4.2*-windows-x64" | Select-Object -First 1
            if ($ExBlender) {
                Rename-Item -Path $ExBlender.FullName -NewName "blender-4.2"
            }
            Refresh-SessionPath
        }
    } else {
        Write-Host "  -> Blender 4.2 LTS zaten hazir." -ForegroundColor DarkGreen
    }
    Install-WingetPackage -PackageId "JannisX11.Blockbench" -DisplayName "Blockbench"
    Install-WingetPackage -PackageId "Unity.UnityHub" -DisplayName "Unity Hub"
}

Refresh-SessionPath

# 4. Git LFS Etkinlestirme ve Yerel .env Uretimi
if (Get-Command git-lfs -ErrorAction SilentlyContinue) {
    Push-Location $RepoRoot
    git lfs install --local | Out-Null
    Pop-Location
}
& (Join-Path $PSScriptRoot "generate-secrets.ps1")

# 5. Python 3.11 Sanal Ortami (.venv) ve Otonom 3B Fabrika Paketleri
$FactoryDir = Join-Path $RepoRoot "Tools\AutonomousAssetFactory"
$VenvDir = Join-Path $FactoryDir ".venv"
$ReqFile = Join-Path $FactoryDir "requirements.txt"
$uvCmd = Get-Command uv -ErrorAction SilentlyContinue
if (-not $uvCmd) {
    $uvFallback = Join-Path $env:USERPROFILE ".local\bin\uv.exe"
    if (Test-Path $uvFallback) { $uvCmd = Get-Item $uvFallback }
}

if ($uvCmd -and (Test-Path $ReqFile)) {
    Write-Host "[Python] Python 3.11 sanal ortami ve paketler kuruluyor..." -ForegroundColor Cyan
    & $uvCmd venv $VenvDir --python 3.11 --allow-existing
    & $uvCmd pip install --python (Join-Path $VenvDir "Scripts\python.exe") -r $ReqFile
}

# 6. Resmi Unity CLI, Blender MCP, Material Maker 1.8 ve Antigravity MCP Kopruleri
$UnityCliExe = Join-Path $env:LOCALAPPDATA "Unity\bin\unity.exe"
if (-not (Test-Path $UnityCliExe)) {
    Write-Host "[Unity CLI] Resmi Unity CLI kuruluyor..." -ForegroundColor Cyan
    $env:UNITY_NON_INTERACTIVE = "1"
    $env:UNITY_NO_CONSENT_PROMPT = "1"
    Invoke-RestMethod -Uri "https://unity.com/install.ps1" | Invoke-Expression
}

$McpConfigDir = Join-Path $env:USERPROFILE ".gemini\config"
$McpConfigPath = Join-Path $McpConfigDir "mcp_config.json"
New-Item -ItemType Directory -Force -Path $McpConfigDir | Out-Null
if ((-not (Test-Path $McpConfigPath)) -or ((Get-Item $McpConfigPath).Length -eq 0)) {
    [System.IO.File]::WriteAllText($McpConfigPath, "{}", (New-Object System.Text.UTF8Encoding($false)))
}

if (Test-Path $UnityCliExe) {
    & $UnityCliExe pipeline install --project-path $RepoRoot --non-interactive | Out-Null
    & $UnityCliExe mcp configure antigravity --project-path $RepoRoot --yes --non-interactive | Out-Null
}

if ($uvCmd) {
    Write-Host "[Blender MCP] blender-mcp kurulumu dogrulaniyor..." -ForegroundColor Cyan
    & $uvCmd tool install blender-mcp | Out-Null
    $BlenderMcpBin = Join-Path $env:USERPROFILE ".local\bin\blender-mcp.exe"
    if (Test-Path $BlenderMcpBin) {
        $mcpObj = Get-Content $McpConfigPath -Raw | ConvertFrom-Json
        if (-not $mcpObj.mcpServers) {
            $mcpObj | Add-Member -NotePropertyName "mcpServers" -NotePropertyValue ([PSCustomObject]@{}) -Force
        }
        $mcpObj.mcpServers | Add-Member -NotePropertyName "blender" -NotePropertyValue ([PSCustomObject]@{
            command = $BlenderMcpBin
            args = @()
        }) -Force
        [System.IO.File]::WriteAllText($McpConfigPath, ($mcpObj | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    }
}

$BlenderExeLocal = Join-Path $RepoRoot "Tools\bin\blender-4.2\blender.exe"
if (Test-Path $BlenderExeLocal) {
    $BlenderAddonsDir = Join-Path $env:APPDATA "Blender Foundation\Blender\4.2\scripts\addons"
    New-Item -ItemType Directory -Force -Path $BlenderAddonsDir | Out-Null
    $AddonDest = Join-Path $BlenderAddonsDir "blender_mcp_addon.py"
    if (-not (Test-Path $AddonDest)) {
        & curl.exe -L -sS -o $AddonDest "https://raw.githubusercontent.com/ahujasid/blender-mcp/main/addon.py"
        & $BlenderExeLocal --background --python-expr "import bpy; bpy.ops.preferences.addon_enable(module='blender_mcp_addon'); bpy.ops.wm.save_userpref()" | Out-Null
    }
}

$MmTargetDir = Join-Path $RepoRoot "Tools\bin\material_maker"
if ((-not $SkipGuiInstallers) -and (-not (Test-Path (Join-Path $MmTargetDir "material_maker.exe")))) {
    Write-Host "[Material Maker] Tasinabilir Material Maker 1.8 indiriliyor..." -ForegroundColor Cyan
    $MmZip = Join-Path $RepoRoot "Tools\bin\mm_1_8.zip"
    & curl.exe -L -sS -o $MmZip "https://github.com/RodZill4/material-maker/releases/download/1.8/material_maker_1_8_windows.zip"
    New-Item -ItemType Directory -Force -Path $MmTargetDir | Out-Null
    Expand-Archive -Path $MmZip -DestinationPath $MmTargetDir -Force
    Remove-Item $MmZip -Force
}

$GltfPackExe = Join-Path $RepoRoot "Tools\bin\gltfpack.exe"
if (-not (Test-Path $GltfPackExe)) {
    Write-Host "[gltfpack] zeux/meshoptimizer gltfpack CLI indiriliyor..." -ForegroundColor Cyan
    $MeshOptRel = Invoke-RestMethod -Uri "https://api.github.com/repos/zeux/meshoptimizer/releases/latest" -UseBasicParsing
    $GltfAsset = $MeshOptRel.assets | Where-Object { $_.name -match "gltfpack-windows\.zip$" } | Select-Object -First 1
    if ($GltfAsset) {
        $Gz = Join-Path $RepoRoot "Tools\bin\gltfpack-windows.zip"
        & curl.exe -L -sS -o $Gz $GltfAsset.browser_download_url
        Expand-Archive -Path $Gz -DestinationPath (Join-Path $RepoRoot "Tools\bin") -Force
        Remove-Item $Gz -Force
    }
}

# 7. Unity 6000.6.7f1 Headless Kontrolu
$UnityEditorExe = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
$UnityHubExe = "C:\Program Files\Unity Hub\Unity Hub.exe"
if ((-not $SkipUnityEditorInstall) -and (-not (Test-Path $UnityEditorExe)) -and (Test-Path $UnityHubExe)) {
    Write-Host "[Unity] Unity 6000.6.7f1 (b22767f04f04) kurulumu kontrol ediliyor..." -ForegroundColor Cyan
    Write-Host "  NOT: Unity Hub uzerinden Unity ID girisi ve Personal lisans aktivasyonu gerekiyorsa kullanici onayi beklenir." -ForegroundColor Yellow
}

Write-Host "[Stratocraft] Ortam kurulum adimlari tamamlandi." -ForegroundColor Green
