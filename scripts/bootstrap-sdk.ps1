# Downloads a verified, pinned SDK into this repository. Does not install globally.
[CmdletBinding()]
param([string]$Version = '10.0.401')
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 slows large downloads considerably while drawing progress.
$ProgressPreference = 'SilentlyContinue'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools'
$taskSdkDir = Join-Path $taskTools 'dotnet'
$taskDotnet = Join-Path $taskSdkDir 'dotnet.exe'
if ((Test-Path -LiteralPath $taskDotnet) -and (Test-Path -LiteralPath (Join-Path $taskSdkDir "sdk\$Version"))) {
    Write-Host "Project-local .NET SDK $Version is already available."
    exit 0
}
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskMetadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -UseBasicParsing -TimeoutSec 60
$taskSdks = @($taskMetadata.releases | ForEach-Object { $_.sdks; $_.sdk })
$taskSdk = $taskSdks | Where-Object { $_.version -eq $Version } | Select-Object -First 1
if (-not $taskSdk) { throw "SDK $Version was not found in Microsoft's release metadata." }
$taskFile = $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $taskFile) { throw 'Windows x64 SDK archive was not found.' }
$taskUrl = [uri]$taskFile.url
if ($taskUrl.Scheme -ne 'https' -or $taskUrl.Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'Unexpected SDK download origin.' }
# Download from the URL in Microsoft's metadata. The former dotnetcli.azureedge.net mirror was
# retired in 2025; the release metadata hash remains authoritative for the archive.
$taskArchive = Join-Path $taskTools "dotnet-sdk-$Version-win-x64.zip"
Write-Host "Downloading .NET SDK $Version to .tools (no global installation)."
Invoke-WebRequest -Uri $taskUrl -OutFile $taskArchive -TimeoutSec 300 -UseBasicParsing
$taskActualHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA512).Hash
if ($taskActualHash -ne $taskFile.hash) { throw 'SDK archive hash verification failed. Archive was not extracted.' }
New-Item -ItemType Directory -Path $taskSdkDir -Force | Out-Null
Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskSdkDir -Force
Remove-Item -LiteralPath $taskArchive
Write-Host "SDK is ready: $taskDotnet"
