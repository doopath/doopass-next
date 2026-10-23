#requires -Version 5.1
<#
.SYNOPSIS
    Builds self-contained, single-file portable executables for Doopass.

.DESCRIPTION
    Produces one executable per runtime identifier (default: win-x64 and linux-x64).
    All managed assemblies, the .NET runtime and the web static assets are bundled
    into a single file (extracted to a temp folder at runtime).

.EXAMPLE
    .\publish-portable.ps1
    .\publish-portable.ps1 -RuntimeIdentifiers win-x64
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string[]]$RuntimeIdentifiers = @('win-x64', 'linux-x64'),
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\Doopass.Cli\Doopass.Cli.csproj'

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'dist\portable'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path

foreach ($rid in $RuntimeIdentifiers) {
    $outDir = Join-Path $OutputRoot $rid
    if (Test-Path $outDir) {
        Remove-Item -Recurse -Force $outDir
    }

    Write-Host ""
    Write-Host "Publishing $rid -> $outDir" -ForegroundColor Cyan

    & dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $outDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid (exit code $LASTEXITCODE)"
    }
}

Write-Host ""
Write-Host "Done. Portable executables:" -ForegroundColor Green
Get-ChildItem -Path $OutputRoot -Recurse -File |
    Where-Object { $_.Name -eq 'doopass' -or $_.Name -eq 'doopass.exe' } |
    ForEach-Object {
        Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB))
    }

Write-Host ""
Write-Host "On Linux run: chmod +x ./doopass" -ForegroundColor DarkGray
