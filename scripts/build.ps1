# RADHA SHELL — build helper (Windows PowerShell 7+)
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')] [string]$Configuration = 'Release',
    [ValidateSet('x64','ARM64')]     [string]$Platform = 'x64',
    [switch]$Native,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "→ restoring" -ForegroundColor Cyan
    dotnet restore src/Radha.App/Radha.App.csproj -p:Platform=$Platform

    Write-Host "→ building Radha.App ($Configuration/$Platform)" -ForegroundColor Cyan
    dotnet build src/Radha.App/Radha.App.csproj -c $Configuration -p:Platform=$Platform --no-restore

    if ($Native) {
        Write-Host "→ building native bridge" -ForegroundColor Cyan
        cmake -B build -S src/Radha.Native -A $Platform
        cmake --build build --config $Configuration
        $out = Get-ChildItem -Path src/Radha.App/bin/$Platform/$Configuration -Recurse -Filter Radha.App.exe |
               Select-Object -First 1
        if ($out) { Copy-Item build/$Configuration/RadhaNative.dll $out.DirectoryName -Force }
    }

    if ($Test) {
        Write-Host "→ running core tests" -ForegroundColor Cyan
        dotnet run --project src/Radha.Tests -c $Configuration
    }

    Write-Host "✔ done" -ForegroundColor Green
}
finally { Pop-Location }
