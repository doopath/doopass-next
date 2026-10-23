#requires -Version 5.1
<#
.SYNOPSIS
    Installs Doopass to run the web server automatically at Windows sign-in.

.DESCRIPTION
    Copies the portable executable to %LOCALAPPDATA%\Doopass, creates a hidden
    launcher (VBS) that starts `Doopass serve`, and registers it in the user's
    Startup folder. Also creates shortcuts that open the web interface.

.PARAMETER ExePath
    Path to Doopass.exe. Defaults to dist\portable\win-x64\Doopass.exe in this repo.

.PARAMETER Port
    HTTP port (default 7240).

.PARAMETER Bind
    'localhost' (default, this PC only) or 'lan' (listen on all interfaces).

.PARAMETER DryRun
    Print what would be done without changing anything.

.EXAMPLE
    .\install-autostart.ps1
    .\install-autostart.ps1 -Bind lan -Port 7240
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [ValidateRange(1, 65535)]
    [int]$Port = 7240,
    [ValidateSet('localhost', 'lan')]
    [string]$Bind = 'localhost',
    [switch]$DryRun,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

function Resolve-DefaultExe {
    $candidates = @(
        (Join-Path $PSScriptRoot '..\dist\portable\win-x64\Doopass.exe'),
        (Join-Path $PSScriptRoot '..\dist\portable\Doopass.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return $null
}

function Get-LanIp {
    try {
        $ips = [System.Net.Dns]::GetHostAddresses([System.Net.Dns]::GetHostName()) |
            Where-Object {
                $_.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork -and
                -not [System.Net.IPAddress]::IsLoopback($_)
            }

        $first = $ips | Select-Object -First 1
        if ($first) { return $first.ToString() }
    }
    catch {
        # fall through
    }

    return $null
}

if (-not $ExePath) {
    $ExePath = Resolve-DefaultExe
    if (-not $ExePath) {
        throw "Doopass.exe not found. Run scripts\publish-portable.ps1 first, or pass -ExePath <path>."
    }
}

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

$ExePath = (Resolve-Path $ExePath).Path

$installDir = Join-Path $env:LOCALAPPDATA 'Doopass'
$targetExe = Join-Path $installDir 'Doopass.exe'
$launchVbs = Join-Path $installDir 'launch.vbs'
$startupDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupLink = Join-Path $startupDir 'Doopass.lnk'
$desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$programsDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$desktopUrl = Join-Path $desktopDir 'Doopass.url'
$programsUrl = Join-Path $programsDir 'Doopass.url'

$serveArgs = "serve --port $Port --no-browser"
if ($Bind -eq 'lan') {
    $serveArgs += ' --lan'
}

$webHost = if ($Bind -eq 'lan') { Get-LanIp } else { 'localhost' }
if (-not $webHost) { $webHost = $env:COMPUTERNAME }
$webUrl = "http://{0}:{1}" -f $webHost, $Port

$runLine = 'shell.Run """' + $targetExe + '"" ' + $serveArgs + '", 0, False'
$vbs = 'Set shell = CreateObject("WScript.Shell")' + "`r`n" + $runLine + "`r`n"
$urlContent = "[InternetShortcut]`r`nURL=$webUrl`r`n"

Write-Host "Doopass autostart installation" -ForegroundColor Cyan
Write-Host "  Source      : $ExePath"
Write-Host "  Install dir : $installDir"
Write-Host "  Bind        : $Bind ($webHost)"
Write-Host "  Web URL     : $webUrl"
Write-Host "  Autostart   : $startupLink"

if ($DryRun) {
    Write-Host ""
    Write-Host "[dry-run] Would create $launchVbs with:" -ForegroundColor Yellow
    Write-Host $vbs
    Write-Host "[dry-run] Would create startup shortcut: $startupLink" -ForegroundColor Yellow
    Write-Host "[dry-run] Would create web shortcut(s): $programsUrl, $desktopUrl" -ForegroundColor Yellow
    return
}

Get-Process -Name Doopass -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Force $ExePath $targetExe
Set-Content -Path $launchVbs -Value $vbs -Encoding ASCII

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startupLink)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\wscript.exe'
$shortcut.Arguments = '"' + $launchVbs + '"'
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = 'Doopass web server (background)'
$shortcut.Save()

Set-Content -Path $programsUrl -Value $urlContent -Encoding ASCII
if (-not $NoDesktopShortcut) {
    Set-Content -Path $desktopUrl -Value $urlContent -Encoding ASCII
}

Write-Host ""
Write-Host "Installed. The web server will start automatically at your next sign-in." -ForegroundColor Green
Write-Host "Open the web interface any time: $webUrl"

if ($Bind -eq 'lan') {
    Write-Warning "LAN mode listens on all interfaces over plain HTTP. Allow TCP port $Port in Windows Firewall if needed and only use it on trusted networks."
}
