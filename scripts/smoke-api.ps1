# Acceptance helper: runs the documented curl scenario against a throwaway host.
# JSON bodies ride in files to avoid PowerShell-to-curl quoting pitfalls.
#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$port = 7307
$root = Join-Path $PSScriptRoot '.smoke-api'
$env:DOOPASS_HOME = $root
New-Item -ItemType Directory -Force -Path $root | Out-Null

$proc = Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--project', 'src/Doopass.Cli', '--', 'serve', '--port', "$port", '--no-browser', '--lan' -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 12

$base = "http://localhost:$port"
function Write-Body($name, $text) {
    $path = Join-Path $root $name
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
    return $path
}

try {
    Write-Host '== anonymous state' -ForegroundColor Cyan
    curl.exe -s $base/api/vault/state

    Write-Host '== init' -ForegroundColor Cyan
    $pwFile = Write-Body 'pw.json' '{"password":"Test 1234"}'
    $init = curl.exe -s -X POST -H 'Content-Type: application/json' --data-binary "@$pwFile" $base/api/auth/init
    $token = ($init | ConvertFrom-Json).token
    Write-Host "token: $($token.Substring(0, 12))..."
    $auth = "Authorization: Bearer $token"

    Write-Host '== wrong password -> 401' -ForegroundColor Cyan
    $wrongFile = Write-Body 'wrong.json' '{"password":"wrong"}'
    curl.exe -s -o (Join-Path $root 'body') -w '%{http_code} ' -X POST -H 'Content-Type: application/json' --data-binary "@$wrongFile" $base/api/auth/login
    Get-Content (Join-Path $root 'body')

    Write-Host '== keys empty' -ForegroundColor Cyan
    curl.exe -s -H $auth $base/api/vault/keys

    Write-Host '== add pair' -ForegroundColor Cyan
    $pairFile = Write-Body 'pair.json' '{"key":"github","value":"s3cret=/"}'
    curl.exe -s -o NUL -w 'status=%{http_code}' -X POST -H $auth -H 'Content-Type: application/json' --data-binary "@$pairFile" $base/api/vault/pairs

    Write-Host ''
    Write-Host '== value' -ForegroundColor Cyan
    curl.exe -s -H $auth "$base/api/vault/value?key=github"

    Write-Host ''
    Write-Host '== backups' -ForegroundColor Cyan
    curl.exe -s -o NUL -w 'create=%{http_code} ' -X POST -H $auth $base/api/backups
    $names = ((curl.exe -s -H $auth $base/api/backups) | ConvertFrom-Json).names
    Write-Host ($names -join '; ')

    Write-Host '== restore' -ForegroundColor Cyan
    $escapeName = [uri]::EscapeDataString($names[0])
    curl.exe -s -o NUL -w "restore=%{http_code}" -X POST -H $auth "$base/api/backups/restore?name=$escapeName"

    Write-Host ''
    Write-Host '== settings' -ForegroundColor Cyan
    (curl.exe -s -H $auth $base/api/settings) | ForEach-Object { $_.Substring(0, [Math]::Min(120, $_.Length)) }

    Write-Host '== hub negotiate with token' -ForegroundColor Cyan
    curl.exe -s -o (Join-Path $root 'neg') -w 'negotiate=%{http_code} ' -X POST -H $auth "$base/hubs/vault/negotiate?negotiateVersion=1"
    $neg = Get-Content (Join-Path $root 'neg') -Raw
    Write-Host ($neg.Substring(0, [Math]::Min(80, $neg.Length)))

    Write-Host '== hub negotiate without token (connection declines)' -ForegroundColor Cyan
    curl.exe -s -o NUL -w 'no-auth-negotiate=%{http_code}' "$base/hubs/vault/negotiate?negotiateVersion=1"

    Write-Host ''
    Write-Host '== logout then 401' -ForegroundColor Cyan
    curl.exe -s -o NUL -w 'logout=%{http_code} ' -X POST -H $auth $base/api/auth/logout
    curl.exe -s -o NUL -w 'keys-after=%{http_code} ' -H $auth $base/api/vault/keys
    curl.exe -s -H $auth $base/api/vault/keys

    Write-Host ''
    Write-Host '== discovery announce (LAN probe on UDP 7241)' -ForegroundColor Cyan
    Start-Sleep -Seconds 1
    $udp = New-Object System.Net.Sockets.UdpClient
    $udp.Connect('127.0.0.1', 7241)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes('DOOPASS_DISCOVER')
    [void]$udp.Send($bytes, $bytes.Length)
    $udp.Client.ReceiveTimeout = 5000
    $remote = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
    $reply = $udp.Receive([ref]$remote)
    [System.Text.Encoding]::UTF8.GetString($reply)
    $udp.Close()

    Write-Host '== host log tail' -ForegroundColor Cyan
    Get-Content (Join-Path $root 'appdata\doopass.log') -Tail 6
}
finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}
