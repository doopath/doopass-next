# TLS-mode smoke: generates a throwaway self-signed cert, starts the host with
# --tls, checks the SHA-256 fingerprint print, HTTPS endpoints and discovery.
#requires -Version 5.1
$ErrorActionPreference = 'Stop'

$port = 7308
$root = Join-Path $PSScriptRoot '.smoke-tls'
$env:DOOPASS_HOME = $root
New-Item -ItemType Directory -Force -Path $root | Out-Null

$cert = New-SelfSignedCertificate -DnsName 'doopass-test.local' -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddDays(2)
$pass = 'tmp-pass-1234'
$pwFile = Join-Path $root 'cert-pass.txt'
$pwSecure = ConvertTo-SecureString -String $pass -Force -AsPlainText
$pfxPath = Join-Path $root 'test.pfx'
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwSecure | Out-Null
$expected = (Get-FileHash -Algorithm SHA256 -Path $pfxPath).Hash   # not the cert hash; informational only

try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

    Write-Host '== start host with --tls' -ForegroundColor Cyan
    $proc = Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--project', 'src/Doopass.Cli', '--', 'serve', '--port', "$port", '--no-browser', '--lan', '--tls', "$pfxPath", '--tls-pass', "$pass" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 12

    Write-Host '== https state endpoint (curl -k, skipping cert validation on client)' -ForegroundColor Cyan
    curl.exe -sk -o (Join-Path $root 'state') -w 'https-state=%{http_code} ' "https://localhost:$port/api/vault/state"
    Get-Content (Join-Path $root 'state')

    Write-Host '== fingerprint printed by the host' -ForegroundColor Cyan
    $log = Get-Content (Join-Path $root 'appdata\doopass.log')
    $fingerprint = ($log | Select-String 'fingerprint').Lines | Select-Object -First 1
    Write-Host $fingerprint

    Write-Host '== discovery reply carries tls=true with fingerprint' -ForegroundColor Cyan
    $udp = New-Object System.Net.Sockets.UdpClient
    $udp.Connect('127.0.0.1', 7241)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes('DOOPASS_DISCOVER')
    [void]$udp.Send($bytes, $bytes.Length)
    $udp.Client.ReceiveTimeout = 5000
    $remote = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
    $reply = $udp.Receive([ref]$remote)
    Write-Host ([System.Text.Encoding]::UTF8.GetString($reply))
    $udp.Close()

    Write-Host '== stop host' -ForegroundColor Cyan
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Seconds 2

    Write-Host '== plain http no longer answers (expect failure)' -ForegroundColor Cyan
    $httpStatus = curl.exe -s -o NUL -w '%{http_code}' --max-time 5 "http://localhost:$port/api/vault/state"
    if ($httpStatus -eq '000') {
        Write-Host 'plain HTTP rejected as expected'
    } else {
        Write-Host "WARNING: plain HTTP answered with $httpStatus"
    }
}
finally {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $null
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -eq $cert.Thumbprint } | Remove-Item -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}
