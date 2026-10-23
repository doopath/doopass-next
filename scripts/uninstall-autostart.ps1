#requires -Version 5.1
<#
.SYNOPSIS
    Removes Doopass autostart and (optionally) its installed files.

.PARAMETER KeepFiles
    Do not delete %LOCALAPPDATA%\Doopass.

.PARAMETER DryRun
    Print what would be done without changing anything.
#>
[CmdletBinding()]
param(
    [switch]$KeepFiles,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'Doopass'
$startupDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupLink = Join-Path $startupDir 'Doopass.lnk'
$desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$programsDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$desktopUrl = Join-Path $desktopDir 'Doopass.url'
$programsUrl = Join-Path $programsDir 'Doopass.url'

Write-Host "Doopass autostart removal" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "[dry-run] Would stop the Doopass process" -ForegroundColor Yellow
    Write-Host "[dry-run] Would remove $startupLink" -ForegroundColor Yellow
    Write-Host "[dry-run] Would remove $programsUrl and $desktopUrl" -ForegroundColor Yellow
    if (-not $KeepFiles) {
        Write-Host "[dry-run] Would remove $installDir" -ForegroundColor Yellow
    }
    return
}

Get-Process -Name Doopass -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($path in @($startupLink, $programsUrl, $desktopUrl)) {
    if (Test-Path $path) {
        Remove-Item -Force $path
        Write-Host "Removed $path"
    }
}

if (-not $KeepFiles -and (Test-Path $installDir)) {
    Remove-Item -Recurse -Force $installDir
    Write-Host "Removed $installDir"
}

Write-Host "Done." -ForegroundColor Green
