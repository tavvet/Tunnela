[CmdletBinding()]
param([switch]$Publish, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskLocalDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
$taskDotnet = if (Test-Path -LiteralPath $taskLocalDotnet) { $taskLocalDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.cache\dotnet'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
$env:DOTNET_ROOT = Split-Path -Parent $taskDotnet
Push-Location $taskRoot
try {
    & $taskDotnet restore Tunnela.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $taskDotnet build Tunnela.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & $taskDotnet test Tunnela.slnx -c Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Isolated tests failed.' }
    }
    if ($Publish) {
        foreach ($taskProject in @('Desktop', 'Service')) {
            $taskOutput = Join-Path $taskRoot ".artifacts\publish\tunnela\$($taskProject.ToLowerInvariant())"
            & $taskDotnet publish "src/Tunnela.$taskProject/Tunnela.$taskProject.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $taskOutput
            if ($LASTEXITCODE -ne 0) { throw "Publishing $taskProject failed." }
        }
        Write-Host 'Published to .artifacts\publish\tunnela. No application or service has been started.'
    }
} finally { Pop-Location }
