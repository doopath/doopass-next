#requires -Version 5.1
<#
.SYNOPSIS
    Installs the Doopass web server to start automatically at Windows sign-in.

.DESCRIPTION
    Copies the given executable to %USERPROFILE%\.local\bin\doopass.exe, creates a
    hidden launcher (VBS) and registers it in the Startup folder. Also creates
    shortcuts that open the web interface in the browser.

    Designed to ship next to the executable in a GitHub release archive:
        install-autostart-win.ps1 .\doopass.exe lan

.PARAMETER ExePath
    Path to doopass.exe (required).

.PARAMETER Bind
    'local' (default, localhost only) or 'lan' (all interfaces).

.PARAMETER Port
    HTTP port (default 7240).

.EXAMPLE
    .\install-autostart-win.ps1 .\doopass.exe
    .\install-autostart-win.ps1 C:\Downloads\doopass.exe lan
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, HelpMessage = 'Path to doopass.exe')]
    [string]$ExePath,

    [Parameter(Position = 1)]
    [ValidateSet('local', 'localhost', 'lan')]
    [string]$Bind = 'local',

    [ValidateRange(1, 65535)]
    [int]$Port = 7240,

    [switch]$DryRun,

    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

$script:DiscoveryPort = 7241

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

if (-not (Test-Path $ExePath)) {
    throw "Executable not found: $ExePath"
}

$ExePath = (Resolve-Path $ExePath).Path
$isLan = $Bind -eq 'lan'

$binDir = Join-Path $HOME '.local\bin'
$targetExe = Join-Path $binDir 'doopass.exe'
$launchVbs = Join-Path $binDir 'doopass-launch.vbs'

$startupDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupLink = Join-Path $startupDir 'doopass.lnk'
$desktopDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$programsDir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Programs)
$desktopUrl = Join-Path $desktopDir 'doopass.url'
$programsUrl = Join-Path $programsDir 'doopass.url'

$serveArgs = "serve --port $Port --no-browser"
if ($isLan) { $serveArgs += ' --lan' }

$webHost = if ($isLan) { Get-LanIp } else { 'localhost' }
if (-not $webHost) { $webHost = $env:COMPUTERNAME }
$webUrl = "http://{0}:{1}" -f $webHost, $Port

$runLine = 'shell.Run """' + $targetExe + '"" ' + $serveArgs + '", 0, False'
$vbs = 'Set shell = CreateObject("WScript.Shell")' + "`r`n" + $runLine + "`r`n"
$urlContent = "[InternetShortcut]`r`nURL=$webUrl`r`n"

Write-Host "Doopass autostart (Windows)" -ForegroundColor Cyan
Write-Host "  Source      : $ExePath"
Write-Host "  Install to  : $targetExe"
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

Get-Process -Name doopass -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
Copy-Item -Force $ExePath $targetExe
Set-Content -Path $launchVbs -Value $vbs -Encoding ASCII

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startupLink)
$shortcut.TargetPath = Join-Path $env:WINDIR 'System32\wscript.exe'
$shortcut.Arguments = '"' + $launchVbs + '"'
$shortcut.WorkingDirectory = $binDir
$shortcut.Description = 'Doopass web server (background)'
$shortcut.Save()

Set-Content -Path $programsUrl -Value $urlContent -Encoding ASCII
if (-not $NoDesktopShortcut) {
    Set-Content -Path $desktopUrl -Value $urlContent -Encoding ASCII
}

Write-Host ""
Write-Host "Installed. The web server will start automatically at your next sign-in." -ForegroundColor Green
Write-Host "Open the web interface any time: $webUrl"

if ($isLan) {
    # Discovery + API reachability: Android / phone clients find the host via
    # an incoming UDP broadcast on 7241, the API itself rides the TCP port.
    $rulesCreated = $false
    try {
        $firewallExists = Get-NetFirewallRule -DisplayName 'Doopass discovery' -ErrorAction SilentlyContinue
        if (-not $firewallExists) {
            New-NetFirewallRule -DisplayName 'Doopass discovery' -Direction Inbound -Protocol UDP -LocalPort $script:DiscoveryPort -Action Allow -Profile Private | Out-Null
            $rulesCreated = $true
        }

        $firewallExists = Get-NetFirewallRule -DisplayName 'Doopass web' -ErrorAction SilentlyContinue
        if (-not $firewallExists) {
            New-NetFirewallRule -DisplayName 'Doopass web' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Private | Out-Null
            $rulesCreated = $true
        }

        if ($rulesCreated) {
            Write-Host "Firewall rules added: UDP $script:DiscoveryPort (discovery, private networks) + TCP $Port (web/API)." -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Could not add firewall rules automatically; allow inbound UDP 7241 (discovery) and TCP $Port (web/API) on private networks manually."
    }

    Write-Warning "LAN mode listens on all interfaces. Plain HTTP is unencrypted -- pair it with --tls for phone clients."
}
