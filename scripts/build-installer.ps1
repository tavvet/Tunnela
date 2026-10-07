[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = '0.1.0-preview.2'
$taskFileVersion = '0.1.0.2'
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'Run scripts/bootstrap-sdk.ps1 first.' }
& (Join-Path $PSScriptRoot 'bootstrap-installer.ps1')
& (Join-Path $PSScriptRoot 'prepare-engine.ps1')
& (Join-Path $PSScriptRoot 'build.ps1')
if (-not $?) { throw 'Build or isolated tests failed.' }
$taskWindowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $taskWindowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $taskRoot 'tests\installer-actions.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Isolated installer checks failed.' }

# Fresh isolated publishing avoids replacing the user's currently running preview.
$taskStagingRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot '.artifacts\installer-stage'))
$taskStage = Join-Path $taskStagingRoot ([guid]::NewGuid().ToString('N'))
$taskPayload = Join-Path $taskStage 'payload'
New-Item -ItemType Directory -Path $taskPayload -Force | Out-Null
Push-Location $taskRoot
try {
    foreach ($taskProject in @('Desktop', 'Service')) {
        $taskOutput = Join-Path $taskPayload $taskProject.ToLowerInvariant()
        & $taskDotnet publish "src/Tunnela.$taskProject/Tunnela.$taskProject.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$taskVersion" "-p:FileVersion=$taskFileVersion" -o $taskOutput
        if ($LASTEXITCODE -ne 0) { throw "Publishing $taskProject failed." }
        $taskLicenses = Join-Path $taskOutput 'licenses'
        New-Item -ItemType Directory -Path $taskLicenses -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskRoot 'packaging\licenses\TOMLYN_LICENSE.txt') -Destination $taskLicenses
        $taskRuntime = Get-Content -LiteralPath (Join-Path $taskOutput "Tunnela.$taskProject.runtimeconfig.json") -Raw | ConvertFrom-Json
        foreach ($taskFramework in $taskRuntime.runtimeOptions.includedFrameworks) {
            $taskPackageName = "$($taskFramework.name.ToLowerInvariant()).runtime.win-x64"
            $taskPackagePath = Join-Path $env:NUGET_PACKAGES "$taskPackageName\$($taskFramework.version)"
            $taskPackageLicenses = @(Get-ChildItem -LiteralPath $taskPackagePath -File | Where-Object { $_.Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
            if ($taskPackageLicenses.Count -eq 0) { throw "Runtime license missing: $taskPackageName" }
            foreach ($taskLicense in $taskPackageLicenses) {
                Copy-Item -LiteralPath $taskLicense.FullName -Destination (Join-Path $taskLicenses "$taskPackageName-$($taskLicense.Name).txt")
            }
        }
    }
    $taskEngineOutput = Join-Path $taskPayload 'service\engine'
    New-Item -ItemType Directory -Path $taskEngineOutput -Force | Out-Null
    foreach ($taskName in @('trusttunnel_client.exe', 'wintun.dll', 'TRUSTTUNNEL_LICENSE.txt', 'WINTUN_LICENSE.txt', 'provenance.json')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot "runtime\engine\$taskName") -Destination $taskEngineOutput
    }
    if (@(Get-ChildItem -LiteralPath $taskPayload -Recurse -File | Where-Object { $_.Extension -in @('.toml', '.dat') -or $_.Name -eq 'service-install.json' }).Count -gt 0) {
        throw 'Payload must not contain profiles, credentials or a machine-specific service manifest.'
    }
    $taskPayloadManifest = Join-Path $taskStage 'payload-manifest.json'
    $taskPayloadFiles = @(Get-ChildItem -LiteralPath $taskPayload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            RelativePath = $_.FullName.Substring($taskPayload.Length + 1)
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    ConvertTo-Json -InputObject $taskPayloadFiles -Depth 3 | Set-Content -LiteralPath $taskPayloadManifest -Encoding UTF8
    $taskManifestHash = (Get-FileHash -LiteralPath $taskPayloadManifest -Algorithm SHA256).Hash
    $taskBackend = Join-Path $PSScriptRoot 'installer-actions.ps1'
    $taskBackendHash = (Get-FileHash -LiteralPath $taskBackend -Algorithm SHA256).Hash
    $taskOutputDirectory = Join-Path $taskRoot '.artifacts\installer'
    New-Item -ItemType Directory -Path $taskOutputDirectory -Force | Out-Null
    $taskInstaller = Join-Path $taskOutputDirectory "Tunnela-$taskVersion-Setup-x64.exe"
    $taskCompiler = Join-Path $taskRoot '.tools\nsis-3.13\makensis.exe'
    $taskInstallerScript = Join-Path $taskRoot 'packaging\Tunnela.nsi'
    $taskInstallerScriptHash = (Get-FileHash -LiteralPath $taskInstallerScript -Algorithm SHA256).Hash
    & $taskCompiler /NOCONFIG /WX /INPUTCHARSET UTF8 "/DPAYLOAD_DIR=$taskPayload" "/DBACKEND_SCRIPT=$taskBackend" "/DBACKEND_SHA256=$taskBackendHash" "/DPAYLOAD_MANIFEST=$taskPayloadManifest" "/DPAYLOAD_MANIFEST_SHA256=$taskManifestHash" "/DOUTPUT_FILE=$taskInstaller" "/DPRODUCT_VERSION=$taskFileVersion" "/DDISPLAY_VERSION=$taskVersion" "/DREPO_ROOT=$taskRoot" $taskInstallerScript
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    if ((Get-FileHash -LiteralPath $taskInstallerScript -Algorithm SHA256).Hash -ne $taskInstallerScriptHash -or
        (Get-FileHash -LiteralPath $taskBackend -Algorithm SHA256).Hash -ne $taskBackendHash) { throw 'Installer sources changed during compilation; rebuild before using the package.' }
    $taskHash = (Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash
    "$taskHash  $([IO.Path]::GetFileName($taskInstaller))" | Set-Content -LiteralPath "$taskInstaller.sha256" -Encoding ASCII
    [pscustomobject]@{
        Installer = $taskInstaller
        Version = $taskVersion
        Sha256 = $taskHash
        Payload = $taskPayload
        EngineVersion = '1.1.7'
        BackendSha256 = $taskBackendHash
        InstallerScriptSha256 = $taskInstallerScriptHash
        PayloadManifestSha256 = $taskManifestHash
        SelfContained = $true
        Signed = $false
        InstallerExecuted = $false
    } | ConvertTo-Json | Set-Content -LiteralPath "$taskInstaller.build.json" -Encoding UTF8
    Get-Item -LiteralPath $taskInstaller | Select-Object FullName, Length
    Write-Host 'Built only. Installation, desktop and VPN tests are left to the user.'
} finally { Pop-Location }
