# Run manually from an elevated PowerShell after reviewing docs/TESTING.md.
# This developer installer changes the host only when explicitly invoked by the user.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EngineDirectory,
    [string]$ControllerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
)
$ErrorActionPreference = 'Stop'
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskPrincipal = [Security.Principal.WindowsPrincipal]::new($taskIdentity)
if (-not $taskPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated PowerShell.' }
$taskSid = [Security.Principal.SecurityIdentifier]::new($ControllerSid)
if ($taskSid.IsWellKnown([Security.Principal.WellKnownSidType]::LocalSystemSid)) { throw 'ControllerSid must identify the interactive user, not SYSTEM.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPublish = Join-Path $taskRoot '.artifacts\publish\tunnela'
$taskEngine = (Resolve-Path -LiteralPath $EngineDirectory).Path
$taskCli = Join-Path $taskEngine 'trusttunnel_client.exe'
$taskWintun = Join-Path $taskEngine 'wintun.dll'
foreach ($taskFile in @($taskCli, $taskWintun, (Join-Path $taskPublish 'service\Tunnela.Service.exe'), (Join-Path $taskPublish 'desktop\Tunnela.Desktop.exe'))) {
    if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw "Missing file: $taskFile. Build with scripts/build.ps1 -Publish first; supply the official v1.1.7 x64 CLI and x64 Wintun." }
}
function Assert-X64Binary([string]$Path) {
    $taskStream = [IO.File]::OpenRead($Path)
    try {
        $taskReader = [IO.BinaryReader]::new($taskStream)
        if ($taskReader.ReadUInt16() -ne 0x5a4d) { throw "Not a PE binary: $Path" }
        $taskStream.Position = 0x3c
        $taskPeOffset = $taskReader.ReadInt32()
        if ($taskPeOffset -lt 64 -or $taskPeOffset -gt ($taskStream.Length - 6)) { throw "Invalid PE binary: $Path" }
        $taskStream.Position = $taskPeOffset
        if ($taskReader.ReadUInt32() -ne 0x00004550 -or $taskReader.ReadUInt16() -ne 0x8664) { throw "An x64 binary is required: $Path" }
    } finally { $taskStream.Dispose() }
}
Assert-X64Binary $taskCli
Assert-X64Binary $taskWintun
$taskProgramFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$taskInstallRoot = [IO.Path]::GetFullPath((Join-Path $taskProgramFiles 'Tunnela'))
if (-not $taskInstallRoot.StartsWith(([IO.Path]::GetFullPath($taskProgramFiles).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe install path.' }
$taskServiceDir = Join-Path $taskInstallRoot 'service'
$taskDesktopDir = Join-Path $taskInstallRoot 'desktop'
$taskServiceExe = Join-Path $taskServiceDir 'Tunnela.Service.exe'
$taskServiceName = 'Tunnela'
$taskExisting = Get-CimInstance Win32_Service -Filter "Name='$taskServiceName'"
if ($taskExisting -and $taskExisting.PathName.Trim('"') -ne $taskServiceExe) { throw 'A different service already uses this name. It has not been changed.' }
# Refuse an active session before stopping even the idle controller service.
if (Get-Process -Name 'Tunnela.Desktop' -ErrorAction SilentlyContinue) { throw 'Disconnect the VPN in Tunnela and close Tunnela before installing or updating. No service was stopped.' }
$taskRunningCli = Get-CimInstance Win32_Process -Filter "Name='trusttunnel_client.exe'" | Where-Object { $_.ExecutablePath -eq (Join-Path $taskServiceDir 'engine\trusttunnel_client.exe') }
if ($taskRunningCli) { throw 'The Tunnela tunnel process is still running. Disconnect it safely before updating. No service was stopped.' }
if ($taskExisting -and $taskExisting.State -ne 'Stopped') {
    Stop-Service -Name $taskServiceName -ErrorAction Stop
    (Get-Service -Name $taskServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
if (Test-Path -LiteralPath $taskInstallRoot) {
    $taskReparseItems = @(Get-Item -LiteralPath $taskInstallRoot) + @(Get-ChildItem -LiteralPath $taskInstallRoot -Force -Recurse)
    if ($taskReparseItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'The installation directory contains a reparse point. Installation stopped.' }
}
New-Item -ItemType Directory -Path $taskInstallRoot -Force | Out-Null
$taskAcl = [Security.AccessControl.DirectorySecurity]::new()
$taskAdminSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$taskAcl.SetOwner($taskAdminSid)
$taskAcl.SetAccessRuleProtection($true, $false)
foreach ($taskAccess in @(@('S-1-5-18','FullControl'), @('S-1-5-32-544','FullControl'), @('S-1-5-32-545','ReadAndExecute'))) {
    $taskRule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskAccess[0]), [Security.AccessControl.FileSystemRights]$taskAccess[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $taskAcl.AddAccessRule($taskRule)
}
Set-Acl -LiteralPath $taskInstallRoot -AclObject $taskAcl
New-Item -ItemType Directory -Path $taskServiceDir,$taskDesktopDir,(Join-Path $taskServiceDir 'engine') -Force | Out-Null
Copy-Item -Path (Join-Path $taskPublish 'service\*') -Destination $taskServiceDir -Recurse -Force
Copy-Item -Path (Join-Path $taskPublish 'desktop\*') -Destination $taskDesktopDir -Recurse -Force
Copy-Item -LiteralPath $taskCli,$taskWintun -Destination (Join-Path $taskServiceDir 'engine') -Force
foreach ($taskLicenseName in @('TRUSTTUNNEL_LICENSE.txt', 'WINTUN_LICENSE.txt')) {
    $taskLicensePath = Join-Path $taskEngine $taskLicenseName
    if (Test-Path -LiteralPath $taskLicensePath -PathType Leaf) { Copy-Item -LiteralPath $taskLicensePath -Destination (Join-Path $taskServiceDir 'engine') -Force }
}
$taskManifest = @{
    ControllerSid = $ControllerSid
    EngineSha256 = (Get-FileHash -LiteralPath $taskCli -Algorithm SHA256).Hash
    WintunSha256 = (Get-FileHash -LiteralPath $taskWintun -Algorithm SHA256).Hash
    EngineVersion = '1.1.7'
}
$taskManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskServiceDir 'service-install.json') -Encoding utf8
foreach ($taskInstalledItem in Get-ChildItem -LiteralPath $taskInstallRoot -Force -Recurse) {
    $taskChildAcl = Get-Acl -LiteralPath $taskInstalledItem.FullName
    $taskChildAcl.SetOwner($taskAdminSid)
    $taskChildAcl.SetAccessRuleProtection($false, $false)
    foreach ($taskExplicitRule in @($taskChildAcl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier]))) {
        $taskChildAcl.RemoveAccessRuleSpecific($taskExplicitRule)
    }
    Set-Acl -LiteralPath $taskInstalledItem.FullName -AclObject $taskChildAcl
}
if (-not $taskExisting) {
    New-Service -Name $taskServiceName -BinaryPathName ('"' + $taskServiceExe + '"') -DisplayName 'Tunnela VPN Service' -Description 'Local VPN controller for Tunnela using the TrustTunnel CLI.' -StartupType Manual | Out-Null
}
Start-Service -Name $taskServiceName
Write-Host "Installed for controller SID $ControllerSid. Service is idle; no VPN connection was requested."
Write-Host "Open manually: $(Join-Path $taskDesktopDir 'Tunnela.Desktop.exe')"
