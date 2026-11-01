#requires -Version 5.1
<#
.SYNOPSIS
    Creates the local TLS certificate through the bundled executable.

.EXAMPLE
    .\setup-tls.ps1
    .\setup-tls.ps1 --force
#>
$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'doopass.exe'

if (Test-Path $exe) {
    & $exe setup-tls @args
}
else {
    $project = Join-Path $PSScriptRoot '..\src\Doopass.Cli\Doopass.Cli.csproj'
    & dotnet run --project $project -- setup-tls @args
}

exit $LASTEXITCODE
