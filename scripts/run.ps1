# RADHA SHELL — build & launch
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& "$PSScriptRoot/build.ps1" -Configuration Debug -Platform x64

$exe = Get-ChildItem -Path "$root/src/Radha.App/bin/x64/Debug" -Recurse -Filter Radha.App.exe |
       Select-Object -First 1
if (-not $exe) { throw "Radha.App.exe not found — did the build succeed?" }

Write-Host "→ launching $($exe.FullName)" -ForegroundColor Cyan
Start-Process -FilePath $exe.FullName
Write-Host "Try:  radha dashboard toggle   |   radha controlcenter toggle" -ForegroundColor DarkGray
