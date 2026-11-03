# Bouwt de losse PodPilot.exe (map publish\) en daarna de installer (installer\output\).
# Gebruik: powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-SkipInstaller]
param([switch]$SkipInstaller)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:Path = "C:\Program Files\dotnet;" + $env:Path

# 1. Self-contained single-file exe: geen .NET-installatie nodig op de doel-pc.
$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\PodPilot.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish mislukt ($LASTEXITCODE)" }

if ($SkipInstaller) { return }

# 2. Installer met Inno Setup 6 (winget install JRSoftware.InnoSetup).
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 niet gevonden. Installeer met: winget install JRSoftware.InnoSetup" }

& $iscc (Join-Path $PSScriptRoot 'PodPilot.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC mislukt ($LASTEXITCODE)" }
