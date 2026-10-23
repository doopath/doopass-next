#requires -Version 5.1
<#
.SYNOPSIS
    Removes the Doopass autostart entry and shortcuts.

.PARAMETER KeepFiles
    Keep %USERPROFILE%\.local\bin\doopass.exe and the launcher.

.PARAMETER DryRun
    Print what would be done without changing anything.
#>
[CmdletBinding()]
param(
    [switch]$KeepFiles,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$binDir = Join-Path $HOME '.local\bin'
$targetExe = Join-Path $binDir 'doopass.exe'
$launchVbs = Join-Path $binDir 'doopass-launch.vbs'

$startupDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupLink = Join-Path $startupDir 'doopass.lnk'
$desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$programsDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$desktopUrl = Join-Path $desktopDir 'doopass.url'
$programsUrl = Join-Path $programsDir 'doopass.url'

Write-Host "Doopass autostart removal (Windows)" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "[dry-run] Would stop the doopass process" -ForegroundColor Yellow
    Write-Host "[dry-run] Would remove $startupLink" -ForegroundColor Yellow
    Write-Host "[dry-run] Would remove $programsUrl and $desktopUrl" -ForegroundColor Yellow
    if (-not $KeepFiles) {
        Write-Host "[dry-run] Would remove $targetExe and $launchVbs" -ForegroundColor Yellow
    }
    return
}

Get-Process -Name doopass -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($path in @($startupLink, $programsUrl, $desktopUrl)) {
    if (Test-Path $path) {
        Remove-Item -Force $path
        Write-Host "Removed $path"
    }
}

if (-not $KeepFiles) {
    foreach ($path in @($targetExe, $launchVbs)) {
        if (Test-Path $path) {
            Remove-Item -Force $path
            Write-Host "Removed $path"
        }
    }
}

Write-Host "Done." -ForegroundColor Green
