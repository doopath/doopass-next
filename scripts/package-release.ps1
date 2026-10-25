#requires -Version 5.1
<#
.SYNOPSIS
    Packages the portable executables + install/uninstall scripts into release archives.

.DESCRIPTION
    Assumes scripts\publish-portable.ps1 has already been run. Produces:
      dist\release\doopass-<Version>-win-x64.zip
      dist\release\doopass-<Version>-linux-x64.tar.gz

.EXAMPLE
    .\package-release.ps1
    .\package-release.ps1 -Version 5.0.0
#>
[CmdletBinding()]
param(
    [string]$Version = '5.0.0',
    [string]$PortableRoot,
    [string]$ReleaseRoot,
    [switch]$SkipWindows,
    [switch]$SkipLinux
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $PortableRoot) { $PortableRoot = Join-Path $repoRoot 'dist\portable' }
if (-not $ReleaseRoot) { $ReleaseRoot = Join-Path $repoRoot 'dist\release' }

$scriptsDir = (Resolve-Path $PSScriptRoot).Path

New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

if (-not $SkipWindows) {
    $exe = Join-Path $PortableRoot 'win-x64\doopass.exe'
    if (-not (Test-Path $exe)) { throw "Missing $exe - run publish-portable.ps1 first." }

    $staging = Join-Path $ReleaseRoot '.staging-win'
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null

    Copy-Item $exe (Join-Path $staging 'doopass.exe')
    Copy-Item (Join-Path $scriptsDir 'install-autostart-win.ps1') $staging
    Copy-Item (Join-Path $scriptsDir 'uninstall-autostart-win.ps1') $staging

    $readme = @"
Doopass $Version (win-x64) - portable, self-contained
=====================================================

Run the web interface manually:
    .\doopass.exe serve --port 7240
    .\doopass.exe serve --port 7240 --lan          # reachable from other devices
    .\doopass.exe serve --port 7240 --lan --tls mycert.pfx --tls-pass mypass

Run the terminal UI:
    .\doopass.exe

Thin clients (phone / other PCs):
    The web interface also exposes a REST API for the Doopass Android app:
    host discovery uses UDP 7241 (responses only when the server runs).
    With --lan, allow inbound TCP 7240 and UDP 7241 on private networks.
    --tls enables HTTPS; the startup log prints the SHA-256 fingerprint the
    phone pins on first connect.

Install autostart (web server at sign-in):
    powershell -ExecutionPolicy Bypass -File .\install-autostart-win.ps1 .\doopass.exe
    powershell -ExecutionPolicy Bypass -File .\install-autostart-win.ps1 .\doopass.exe lan

Remove autostart:
    powershell -ExecutionPolicy Bypass -File .\uninstall-autostart-win.ps1
"@
    Set-Content -Path (Join-Path $staging 'README.txt') -Value $readme -Encoding ASCII

    $zip = Join-Path $ReleaseRoot "doopass-$Version-win-x64.zip"
    Write-Host "Creating $zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force

    Remove-Item -Recurse -Force $staging
}

if (-not $SkipLinux) {
    $exe = Join-Path $PortableRoot 'linux-x64\doopass'
    if (-not (Test-Path $exe)) { throw "Missing $exe - run publish-portable.ps1 first." }

    if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
        throw "tar not found - install it (or use WSL) to build the Linux archive."
    }

    $staging = Join-Path $ReleaseRoot '.staging-linux'
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null

    Copy-Item $exe (Join-Path $staging 'doopass')
    Copy-Item (Join-Path $scriptsDir 'install-autostart-linux.sh') $staging
    Copy-Item (Join-Path $scriptsDir 'uninstall-autostart-linux.sh') $staging
    Copy-Item (Join-Path $scriptsDir 'doopass.service') $staging

    $readme = @"
Doopass $Version (linux-x64) - portable, self-contained
=======================================================

Run the web interface manually:
    chmod +x ./doopass
    ./doopass serve --port 7240
    ./doopass serve --port 7240 --lan          # reachable from other devices
    ./doopass serve --port 7240 --lan --tls mycert.pfx --tls-pass mypass

Thin clients (phone / other PCs):
    The web interface also exposes a REST API for the Doopass Android app:
    host discovery uses UDP 7241 (responses only when the server runs).
    With --lan, allow inbound TCP 7240 and UDP 7241 on private networks.

Install autostart (systemd user service):
    chmod +x install-autostart-linux.sh
    ./install-autostart-linux.sh ./doopass
    ./install-autostart-linux.sh ./doopass lan

Remove autostart:
    ./uninstall-autostart-linux.sh
"@
    Set-Content -Path (Join-Path $staging 'README.txt') -Value $readme -Encoding ASCII

    $tgz = Join-Path $ReleaseRoot "doopass-$Version-linux-x64.tar.gz"
    Write-Host "Creating $tgz"
    & tar -czf $tgz -C $staging .

    Remove-Item -Recurse -Force $staging
}

Write-Host ""
Write-Host "Release artifacts:" -ForegroundColor Green
Get-ChildItem -Path $ReleaseRoot -File |
    Where-Object { $_.Name -like "doopass-*" } |
    ForEach-Object {
        Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB))
    }
