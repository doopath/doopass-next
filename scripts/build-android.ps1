# Build helper for the Android thin client. Requires the Android SDK and a JDK 17:
#   ANDROID_SDK_ROOT  (e.g. F:\AndroidSdk)   - platforms/android-36, build-tools 36
#   JAVA_HOME         (e.g. ...\jdk-17.0.13+11)
# Usage:
#   scripts\build-android.ps1                         # Debug APK
#   scripts\build-android.ps1 -Configuration Release  # Release APK
#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [ValidateSet('apk', 'aab')]
    [string]$PackageFormat = 'apk'
)

$ErrorActionPreference = 'Stop'

if (-not $env:ANDROID_SDK_ROOT) { throw "Set ANDROID_SDK_ROOT (e.g. F:\AndroidSdk)." }
if (-not $env:JAVA_HOME) { throw "Set JAVA_HOME to a JDK 17 install." }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src\Doopass.Mobile\Doopass.Mobile.csproj'

& dotnet build $project -c $Configuration -f net10.0-android "-p:AndroidPackageFormat=$PackageFormat" -p:AndroidEnableMultiDex=true

exit $LASTEXITCODE
