# Invoked only by the interactive installer/uninstaller after the user authorizes installation.
# Inspect is read-only. Development tests extract functions and replace system operations with mocks.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Inspect', 'Install', 'Uninstall')][string]$Action,
    [string]$PayloadDirectory,
    [string]$PayloadManifestHash,
    [string]$ControllerAccount,
    [ValidateSet('Json', 'Nsis')][string]$OutputFormat = 'Json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$script:EngineVersion = '1.1.7'
$script:EngineHash = '86A5992D4F841DCF605DA374D2D74BAB035FB2C0D9B32719BDD9F80CFDD56C68'
$script:WintunHash = 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'
$script:ServiceName = 'Tunnela'
$script:ProgramFilesRoot = [IO.Path]::GetFullPath([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)).TrimEnd('\')
$script:InstallRoot = Join-Path $script:ProgramFilesRoot 'Tunnela'
$script:ServiceExe = Join-Path $script:InstallRoot 'service\Tunnela.Service.exe'
$script:EngineExe = Join-Path $script:InstallRoot 'service\engine\trusttunnel_client.exe'
$script:BackendPath = $PSCommandPath
$script:MutationStarted = $false

function Fail-Installer([string]$Message) {
    $taskException = [InvalidOperationException]::new($Message)
    $taskException.Data['Tunnela.SafeMessage'] = $Message
    throw $taskException
}

function Get-SafeMessage($Exception) {
    for ($taskError = $Exception; $null -ne $taskError; $taskError = $taskError.InnerException) {
        if ($taskError.Data.Contains('Tunnela.SafeMessage')) { return [string]$taskError.Data['Tunnela.SafeMessage'] }
    }
    return 'The operation failed. User profiles were preserved. Close Tunnela and check the protected installation before retrying.'
}

function Test-SamePath([string]$First, [string]$Second) {
    return [string]::Equals([IO.Path]::GetFullPath($First).TrimEnd('\'), [IO.Path]::GetFullPath($Second).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparse([string]$Path) {
    $taskCurrent = [IO.Path]::GetFullPath($Path)
    while ($taskCurrent) {
        if (Test-Path -LiteralPath $taskCurrent) {
            if ((Get-Item -LiteralPath $taskCurrent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                Fail-Installer 'A protected installation path is redirected. Nothing will be installed or removed.'
            }
        }
        $taskCurrent = [IO.Path]::GetDirectoryName($taskCurrent.TrimEnd('\'))
    }
}

function Assert-ProtectedAcl([string]$Path) {
    Assert-NoReparse $Path
    $taskAcl = Get-Acl -LiteralPath $Path
    $taskTrusted = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    $taskOwner = $taskAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($taskOwner -notin $taskTrusted) { Fail-Installer 'A protected installation item has an untrusted owner.' }
    $taskWriteRights = [Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($taskRule in $taskAcl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($taskRule.AccessControlType -ne 'Allow' -or ($taskRule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly)) { continue }
        if (($taskRule.FileSystemRights -band $taskWriteRights) -and $taskRule.IdentityReference.Value -notin $taskTrusted) {
            Fail-Installer 'A protected installation item is writable by a non-administrator.'
        }
    }
}

function Get-TreeItems([string]$Path) {
    # Inspect each child before descending; never enumerate through a junction.
    $taskPending = [Collections.Generic.Queue[string]]::new()
    $taskPending.Enqueue($Path)
    while ($taskPending.Count -gt 0) {
        $taskItem = Get-Item -LiteralPath $taskPending.Dequeue() -Force
        if ($taskItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail-Installer 'A protected directory contains a reparse point.' }
        $taskItem
        if ($taskItem.PSIsContainer) {
            foreach ($taskChild in Get-ChildItem -LiteralPath $taskItem.FullName -Force) { $taskPending.Enqueue($taskChild.FullName) }
        }
    }
}

function Assert-ProtectedTree([string]$Path) {
    Assert-NoReparse $Path
    foreach ($taskItem in Get-TreeItems $Path) { Assert-ProtectedAcl $taskItem.FullName }
}

function Set-ProtectedTree([string]$Path) {
    $taskAdmin = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    foreach ($taskItem in Get-TreeItems $Path) {
        $taskAcl = if ($taskItem.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
        $taskAcl.SetOwner($taskAdmin)
        $taskAcl.SetAccessRuleProtection($true, $false)
        foreach ($taskAccess in @(@('S-1-5-18', 'FullControl'), @('S-1-5-32-544', 'FullControl'), @('S-1-5-32-545', 'ReadAndExecute'))) {
            $taskSid = [Security.Principal.SecurityIdentifier]::new($taskAccess[0])
            $taskRights = [Security.AccessControl.FileSystemRights]$taskAccess[1]
            if ($taskItem.PSIsContainer) {
                $taskRule = [Security.AccessControl.FileSystemAccessRule]::new($taskSid, $taskRights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            } else { $taskRule = [Security.AccessControl.FileSystemAccessRule]::new($taskSid, $taskRights, 'Allow') }
            $taskAcl.AddAccessRule($taskRule)
        }
        Set-Acl -LiteralPath $taskItem.FullName -AclObject $taskAcl
    }
}

function New-ProtectedDirectory([string]$Path) {
    Assert-NoReparse $Path
    if (Test-Path -LiteralPath $Path) { Fail-Installer 'A transaction directory already exists. Installation was not started.' }
    New-Item -ItemType Directory -Path $Path | Out-Null
    Set-ProtectedTree $Path
}

function Assert-ExecutionLocation {
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { Fail-Installer 'Use the 64-bit Windows installer.' }
    $taskPrincipal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $taskPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail-Installer 'Administrator permission is required for this installer.' }
    Assert-ProtectedAcl $script:ProgramFilesRoot
    $taskDirectory = Split-Path -Parent $script:BackendPath
    if (-not (Test-SamePath $taskDirectory $script:InstallRoot)) {
        $taskLeaf = Split-Path -Leaf $taskDirectory
        $taskGuid = [guid]::Empty
        if (-not (Test-SamePath (Split-Path -Parent $taskDirectory) $script:ProgramFilesRoot) -or
            -not $taskLeaf.StartsWith('Tunnela.Setup.', [StringComparison]::Ordinal) -or
            -not [guid]::TryParse($taskLeaf.Substring('Tunnela.Setup.'.Length), [ref]$taskGuid)) {
            Fail-Installer 'Run this backend only from the protected Tunnela installer.'
        }
    }
    Assert-ProtectedAcl $taskDirectory
    Assert-ProtectedAcl $script:BackendPath
}

function Get-OwnedService {
    $taskService = Get-CimInstance Win32_Service -Filter "Name='Tunnela'"
    if ($taskService) {
        $taskPath = ([string]$taskService.PathName).Trim()
        if ($taskPath.StartsWith('"') -and $taskPath.EndsWith('"')) { $taskPath = $taskPath.Substring(1, $taskPath.Length - 2) }
        if (-not [string]::Equals($taskPath, $script:ServiceExe, [StringComparison]::OrdinalIgnoreCase) -or
            $taskService.StartName -notin @('LocalSystem', 'NT AUTHORITY\SYSTEM')) {
            Fail-Installer 'Another service uses the Tunnela name or account. It has not been changed.'
        }
        if ($taskService.State -notin @('Running', 'Stopped')) { Fail-Installer 'The Tunnela service is changing state. Wait and retry.' }
    }
    return $taskService
}

function Get-Activity {
    $taskDesktop = @(Get-Process -Name 'Tunnela.Desktop' -ErrorAction SilentlyContinue).Count -gt 0
    $taskTunnel = $false
    foreach ($taskProcess in Get-CimInstance Win32_Process -Filter "Name='trusttunnel_client.exe'") {
        if (-not $taskProcess.ExecutablePath) { Fail-Installer 'A tunnel process cannot be identified safely. No service was stopped.' }
        if ([string]::Equals($taskProcess.ExecutablePath, $script:EngineExe, [StringComparison]::OrdinalIgnoreCase)) { $taskTunnel = $true }
    }
    return [pscustomobject]@{ DesktopRunning = $taskDesktop; TunnelRunning = $taskTunnel }
}

function Assert-Idle {
    $taskActivity = Get-Activity
    if ($taskActivity.TunnelRunning) { Fail-Installer 'Disconnect the VPN in Tunnela before continuing. The installer will not disconnect it for you.' }
    if ($taskActivity.DesktopRunning) { Fail-Installer 'Close the Tunnela window and tray application before continuing. No service was stopped.' }
}

function Get-ExistingController {
    $taskManifest = Join-Path $script:InstallRoot 'service\service-install.json'
    if (-not (Test-Path -LiteralPath $taskManifest -PathType Leaf)) {
        if ((Test-Path -LiteralPath (Join-Path $script:InstallRoot 'service')) -or (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'desktop'))) {
            Fail-Installer 'The previous installation has no valid controller record. It was preserved.'
        }
        return $null
    }
    Assert-ProtectedAcl $taskManifest
    if ((Get-Item -LiteralPath $taskManifest).Length -gt 8192) { Fail-Installer 'The previous installation record is invalid. It was preserved.' }
    try {
        $taskValue = Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json
        $taskSid = [Security.Principal.SecurityIdentifier]::new([string]$taskValue.ControllerSid)
        if (-not $taskSid.IsAccountSid()) { throw 'Invalid SID' }
        return $taskSid.Value
    } catch { Fail-Installer 'The previous controller record is invalid. It was preserved.' }
}

function Get-RecommendedAccount {
    $taskCurrent = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $taskInteractive = (Get-CimInstance Win32_ComputerSystem).UserName
        if ($taskInteractive -and $taskInteractive -notmatch '[\r\n|]') { return [string]$taskInteractive }
    } catch { }
    return $taskCurrent.Name
}

function Resolve-ControllerUser([string]$Account, [string]$ExistingSid) {
    if (-not ('Tunnela.InstallerAccounts' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
namespace Tunnela {
    public static class InstallerAccounts {
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern bool LookupAccountName(string system, string account, byte[] sid, ref uint sidSize, StringBuilder domain, ref uint domainSize, out int use);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern bool LookupAccountSid(string system, byte[] sid, StringBuilder name, ref uint nameSize, StringBuilder domain, ref uint domainSize, out int use);
        public static string ResolveUser(string account) {
            uint size=0, domainSize=0; int use;
            LookupAccountName(null, account, null, ref size, null, ref domainSize, out use);
            if(size==0 || Marshal.GetLastWin32Error()!=122) throw new Win32Exception();
            var bytes=new byte[size]; var domain=new StringBuilder((int)domainSize);
            if(!LookupAccountName(null, account, bytes, ref size, domain, ref domainSize, out use) || use!=1) throw new InvalidOperationException();
            return new SecurityIdentifier(bytes,0).Value;
        }
        public static string ValidateUserSid(string value) {
            var sid=new SecurityIdentifier(value); var bytes=new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes,0);
            uint size=0, domainSize=0; int use;
            LookupAccountSid(null, bytes, null, ref size, null, ref domainSize, out use);
            if(size==0 || Marshal.GetLastWin32Error()!=122) throw new Win32Exception();
            var name=new StringBuilder((int)size); var domain=new StringBuilder((int)domainSize);
            if(!LookupAccountSid(null,bytes,name,ref size,domain,ref domainSize,out use) || use!=1) throw new InvalidOperationException();
            return sid.Value;
        }
    }
}
'@ | Out-Null
    }
    try {
        if ($ExistingSid) { return [Tunnela.InstallerAccounts]::ValidateUserSid($ExistingSid) }
        if ([string]::IsNullOrWhiteSpace($Account) -or $Account -match '[\r\n|]') { Fail-Installer 'Enter a Windows user account, for example COMPUTER\User. Groups are not allowed.' }
        return [Tunnela.InstallerAccounts]::ResolveUser($Account)
    } catch { Fail-Installer 'The controller must resolve to an existing Windows user, not a group. The previous controller was not changed.' }
}

function Assert-X64Binary([string]$Path) {
    $taskStream = [IO.File]::OpenRead($Path)
    try {
        $taskReader = [IO.BinaryReader]::new($taskStream)
        if ($taskReader.ReadUInt16() -ne 0x5a4d) { Fail-Installer 'The installer contains an invalid executable.' }
        $taskStream.Position = 0x3c
        $taskOffset = $taskReader.ReadInt32()
        if ($taskOffset -lt 64 -or $taskOffset -gt ($taskStream.Length - 6)) { Fail-Installer 'The installer contains an invalid executable.' }
        $taskStream.Position = $taskOffset
        if ($taskReader.ReadUInt32() -ne 0x00004550 -or $taskReader.ReadUInt16() -ne 0x8664) { Fail-Installer 'The installer requires Windows x64 binaries.' }
    } finally { $taskStream.Dispose() }
}

function Assert-Payload([string]$Path) {
    $taskStage = Split-Path -Parent $script:BackendPath
    if ((Test-SamePath $taskStage $script:InstallRoot) -or -not (Test-SamePath $Path (Join-Path $taskStage 'payload'))) {
        Fail-Installer 'The payload must belong to the protected installer staging directory.'
    }
    Assert-ProtectedTree $Path
    Assert-PayloadManifest $Path $script:PayloadManifestHash
    foreach ($taskRelative in @('desktop\Tunnela.Desktop.exe', 'service\Tunnela.Service.exe', 'service\engine\trusttunnel_client.exe', 'service\engine\wintun.dll')) {
        $taskFile = Join-Path $Path $taskRelative
        if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { Fail-Installer 'The installer payload is incomplete.' }
        Assert-X64Binary $taskFile
    }
    Assert-ProtectedAcl (Join-Path $taskStage 'uninstall.exe')
    if ((Get-FileHash -LiteralPath (Join-Path $Path 'service\engine\trusttunnel_client.exe') -Algorithm SHA256).Hash -ne $script:EngineHash -or
        (Get-FileHash -LiteralPath (Join-Path $Path 'service\engine\wintun.dll') -Algorithm SHA256).Hash -ne $script:WintunHash) {
        Fail-Installer 'The bundled TrustTunnel 1.1.7 or Wintun 0.14.1 binary does not match the pinned release.'
    }
}

function Assert-PayloadManifest([string]$Path, [string]$ExpectedManifestHash) {
    if ($ExpectedManifestHash -notmatch '^[0-9A-Fa-f]{64}$') { Fail-Installer 'The installer payload manifest identity is missing or invalid.' }
    $taskManifestPath = Join-Path (Split-Path -Parent $Path) 'payload-manifest.json'
    Assert-ProtectedAcl $taskManifestPath
    if ((Get-Item -LiteralPath $taskManifestPath).Length -gt 16777216 -or
        (Get-FileHash -LiteralPath $taskManifestPath -Algorithm SHA256).Hash -ne $ExpectedManifestHash) {
        Fail-Installer 'The installer payload manifest does not match this package.'
    }
    try {
        # Windows PowerShell 5.1 emits a JSON array as one pipeline object; unwrap
        # the decoded value before iterating so every manifest entry is validated.
        $taskDecoded = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
        $taskEntries = @($taskDecoded)
    }
    catch { Fail-Installer 'The installer payload manifest is invalid.' }
    if ($taskEntries.Count -eq 0) { Fail-Installer 'The installer payload manifest is empty.' }
    $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $taskPrefix = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    foreach ($taskEntry in $taskEntries) {
        if ($null -eq $taskEntry.PSObject.Properties['RelativePath'] -or $null -eq $taskEntry.PSObject.Properties['Sha256']) {
            Fail-Installer 'The installer payload manifest has an invalid entry.'
        }
        $taskRelative = [string]$taskEntry.RelativePath
        $taskExpected = [string]$taskEntry.Sha256
        if ([string]::IsNullOrWhiteSpace($taskRelative) -or [IO.Path]::IsPathRooted($taskRelative) -or
            $taskRelative.Contains('/') -or $taskExpected -notmatch '^[0-9A-Fa-f]{64}$') {
            Fail-Installer 'The installer payload manifest has an unsafe path or hash.'
        }
        $taskParts = $taskRelative.Split('\')
        if ($taskParts.Count -lt 2 -or $taskParts[0] -notin @('desktop', 'service')) { Fail-Installer 'The payload contains a path outside its application directories.' }
        foreach ($taskPart in $taskParts) {
            if ([string]::IsNullOrWhiteSpace($taskPart) -or $taskPart -in @('.', '..') -or
                $taskPart.EndsWith('.') -or $taskPart.EndsWith(' ') -or
                $taskPart.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
                $taskPart -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$') {
                Fail-Installer 'The payload contains a non-canonical Windows path.'
            }
        }
        $taskFile = [IO.Path]::GetFullPath((Join-Path $Path $taskRelative))
        if (-not $taskFile.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not $taskSeen.Add($taskFile)) {
            Fail-Installer 'The payload manifest contains an outside or duplicate path.'
        }
        if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { Fail-Installer 'A file listed in the installer manifest is missing.' }
        if ((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskExpected) { Fail-Installer 'An installer payload file failed its integrity check.' }
    }
    $taskFiles = @(Get-TreeItems $Path | Where-Object { -not $_.PSIsContainer })
    if ($taskFiles.Count -ne $taskSeen.Count) { Fail-Installer 'The installer payload contains unexpected files.' }
    foreach ($taskFile in $taskFiles) {
        if (-not $taskSeen.Contains([IO.Path]::GetFullPath($taskFile.FullName))) { Fail-Installer 'The installer payload contains an unlisted file.' }
    }
}

function Copy-PayloadDirectory([string]$Source, [string]$Destination) {
    New-ProtectedDirectory $Destination
    foreach ($taskItem in Get-ChildItem -LiteralPath $Source -Force) { Copy-Item -LiteralPath $taskItem.FullName -Destination $Destination -Recurse -Force }
    Set-ProtectedTree $Destination
}

function Remove-InstallChild([string]$Path) {
    $taskFull = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $taskFull.StartsWith($script:InstallRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { Fail-Installer 'Unsafe cleanup path was rejected.' }
    if (Test-Path -LiteralPath $taskFull) {
        Assert-ProtectedTree $taskFull
        Remove-Item -LiteralPath $taskFull -Recurse -Force
    }
}

function Get-ServiceSnapshot($Service) {
    if (-not $Service) { return $null }
    $taskRegistry = Get-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $script:ServiceName)
    $taskDelayed = $taskRegistry.PSObject.Properties['DelayedAutostart']
    return [pscustomobject]@{
        State = $Service.State; StartMode = $Service.StartMode; DisplayName = $Service.DisplayName; Description = $Service.Description
        DelayedExists = $null -ne $taskDelayed; DelayedValue = if ($taskDelayed) { $taskDelayed.Value } else { 0 }
    }
}

function Stop-IdleOwnedService {
    Assert-Idle
    $taskService = Get-OwnedService
    if ($taskService -and $taskService.State -ne 'Stopped') {
        Stop-Service -Name $script:ServiceName -ErrorAction Stop
        $taskController = Get-Service -Name $script:ServiceName
        try { $taskController.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) } finally { $taskController.Dispose() }
    }
    Assert-Idle
}

function Start-OwnedService {
    $null = Get-OwnedService
    Start-Service -Name $script:ServiceName -ErrorAction Stop
    $taskController = Get-Service -Name $script:ServiceName
    try { $taskController.WaitForStatus('Running', [TimeSpan]::FromSeconds(30)) } finally { $taskController.Dispose() }
}

function Set-AutomaticService {
    $taskService = Get-OwnedService
    if (-not $taskService) {
        New-Service -Name $script:ServiceName -BinaryPathName ('"' + $script:ServiceExe + '"') -DisplayName 'Tunnela VPN Service' -Description 'Local VPN controller for Tunnela using the TrustTunnel CLI.' -StartupType Automatic | Out-Null
    } else { Set-Service -Name $script:ServiceName -StartupType Automatic -DisplayName 'Tunnela VPN Service' -Description 'Local VPN controller for Tunnela using the TrustTunnel CLI.' }
    New-ItemProperty -LiteralPath ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $script:ServiceName) -Name DelayedAutostart -PropertyType DWord -Value 0 -Force | Out-Null
}

function Restore-ServiceSnapshot($Snapshot) {
    $taskMode = switch ($Snapshot.StartMode) { 'Auto' { 'Automatic' }; 'Manual' { 'Manual' }; 'Disabled' { 'Disabled' }; default { Fail-Installer 'The previous service startup mode cannot be restored safely.' } }
    # A service can have been disabled while still running. Restore that state by
    # starting it temporarily in Manual mode, then applying Disabled again.
    $taskInitialMode = if ($taskMode -eq 'Disabled' -and $Snapshot.State -eq 'Running') { 'Manual' } else { $taskMode }
    Set-Service -Name $script:ServiceName -StartupType $taskInitialMode -DisplayName $Snapshot.DisplayName -Description $Snapshot.Description
    $taskKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\' + $script:ServiceName
    if ($Snapshot.DelayedExists) { New-ItemProperty -LiteralPath $taskKey -Name DelayedAutostart -PropertyType DWord -Value $Snapshot.DelayedValue -Force | Out-Null }
    else { Remove-ItemProperty -LiteralPath $taskKey -Name DelayedAutostart -ErrorAction SilentlyContinue }
    if ($Snapshot.State -eq 'Running') { Start-OwnedService }
    if ($taskInitialMode -ne $taskMode) { Set-Service -Name $script:ServiceName -StartupType $taskMode }
}

function Remove-OwnedService {
    $taskService = Get-OwnedService
    if (-not $taskService) { return }
    if ($taskService.State -ne 'Stopped') { Fail-Installer 'A running service will not be deleted.' }
    $taskResult = Invoke-CimMethod -InputObject $taskService -MethodName Delete
    if ($taskResult.ReturnValue -ne 0) { Fail-Installer 'Windows could not remove the Tunnela service.' }
    for ($taskAttempt = 0; $taskAttempt -lt 50; $taskAttempt++) {
        if (-not (Get-CimInstance Win32_Service -Filter "Name='Tunnela'")) { return }
        Start-Sleep -Milliseconds 100
    }
    Fail-Installer 'Windows has not finished removing the Tunnela service. Close service-management windows and retry.'
}

function New-Transaction {
    $script:MutationStarted = $true
    if (-not (Test-Path -LiteralPath $script:InstallRoot)) { New-ProtectedDirectory $script:InstallRoot }
    Assert-ProtectedAcl $script:InstallRoot
    $taskPath = Join-Path $script:InstallRoot ('.transaction.' + [guid]::NewGuid().ToString('N'))
    New-ProtectedDirectory $taskPath
    return $taskPath
}

function Invoke-Inspect {
    $taskService = Get-OwnedService
    if (Test-Path -LiteralPath $script:InstallRoot) { Assert-ProtectedTree $script:InstallRoot }
    $taskSid = Get-ExistingController
    if ($taskService -and -not $taskSid) { Fail-Installer 'The existing service has no controller record. It was preserved.' }
    $taskActivity = Get-Activity
    $taskAccount = Get-RecommendedAccount
    if ($taskSid) {
        $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $taskAccount = if ($taskIdentity.User.Value -eq $taskSid) { $taskIdentity.Name } else { $taskSid }
    }
    return [pscustomobject]@{
        Success = $true; Product = 'Tunnela'; InstallRoot = $script:InstallRoot
        ExistingInstallation = [bool]$taskSid; ExistingControllerSid = $taskSid; RecommendedControllerAccount = $taskAccount
        DesktopRunning = $taskActivity.DesktopRunning; TunnelRunning = $taskActivity.TunnelRunning
        ServiceState = if ($taskService) { $taskService.State } else { 'Absent' }
        CanProceed = -not ($taskActivity.DesktopRunning -or $taskActivity.TunnelRunning)
        Message = if ($taskActivity.TunnelRunning) { 'Disconnect the Tunnela VPN and close Tunnela before continuing.' } elseif ($taskActivity.DesktopRunning) { 'Close the Tunnela window and tray application before continuing.' } else { 'Ready.' }
    }
}

function Invoke-Install([string]$Payload, [string]$Account) {
    $taskInspection = Invoke-Inspect
    Assert-Idle
    Assert-Payload $Payload
    $taskSid = Resolve-ControllerUser $Account $taskInspection.ExistingControllerSid
    $taskOriginal = Get-ServiceSnapshot (Get-OwnedService)
    $taskTransaction = New-Transaction
    $taskNames = @('desktop', 'service', 'installer-actions.ps1', 'uninstall.exe')
    $taskOldMoved = [Collections.Generic.List[string]]::new()
    $taskNewMoved = [Collections.Generic.List[string]]::new()
    $taskServiceTouched = $false
    try {
        foreach ($taskName in @('desktop', 'service')) { Copy-PayloadDirectory (Join-Path $Payload $taskName) (Join-Path $taskTransaction ('new-' + $taskName)) }
        $taskManifest = @{ ControllerSid = $taskSid; EngineSha256 = $script:EngineHash; WintunSha256 = $script:WintunHash; EngineVersion = $script:EngineVersion }
        $taskManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskTransaction 'new-service\service-install.json') -Encoding UTF8
        Copy-Item -LiteralPath $script:BackendPath -Destination (Join-Path $taskTransaction 'new-installer-actions.ps1')
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $script:BackendPath) 'uninstall.exe') -Destination (Join-Path $taskTransaction 'new-uninstall.exe')
        Set-ProtectedTree $taskTransaction
        Assert-Idle
        $taskServiceTouched = $true
        Stop-IdleOwnedService
        foreach ($taskName in $taskNames) {
            $taskTarget = Join-Path $script:InstallRoot $taskName
            if (Test-Path -LiteralPath $taskTarget) {
                Move-Item -LiteralPath $taskTarget -Destination (Join-Path $taskTransaction ('old-' + $taskName))
                $taskOldMoved.Add($taskName)
            }
            Move-Item -LiteralPath (Join-Path $taskTransaction ('new-' + $taskName)) -Destination $taskTarget
            $taskNewMoved.Add($taskName)
        }
        Set-AutomaticService
        Start-OwnedService
    } catch {
        $taskFailure = Get-SafeMessage $_.Exception
        $taskRollbackComplete = $true
        try {
            if ($taskServiceTouched) { Stop-IdleOwnedService }
            if (-not $taskOriginal -and $taskServiceTouched) { Remove-OwnedService }
            foreach ($taskName in $taskNewMoved) { Remove-InstallChild (Join-Path $script:InstallRoot $taskName) }
            foreach ($taskName in $taskOldMoved) { Move-Item -LiteralPath (Join-Path $taskTransaction ('old-' + $taskName)) -Destination (Join-Path $script:InstallRoot $taskName) }
            if ($taskOriginal -and $taskServiceTouched) { Restore-ServiceSnapshot $taskOriginal }
            Remove-InstallChild $taskTransaction
        } catch { $taskRollbackComplete = $false }
        return [pscustomobject]@{ Success = $false; RollbackComplete = $taskRollbackComplete; Message = $taskFailure + $(if ($taskRollbackComplete) { ' The previous installation state was restored.' } else { ' Rollback could not finish safely. The protected transaction backup was retained; do not remove it.' }) }
    }
    $taskMessage = 'Tunnela was installed. Its automatic Windows service is idle; VPN auto-connect is disabled. User profiles were preserved.'
    try { Remove-InstallChild $taskTransaction } catch { $taskMessage += ' A protected old-files backup remains because cleanup could not finish.' }
    return [pscustomobject]@{ Success = $true; RollbackComplete = $true; Message = $taskMessage }
}

function Invoke-Uninstall {
    $null = Invoke-Inspect
    Assert-Idle
    $taskOriginal = Get-ServiceSnapshot (Get-OwnedService)
    if (-not (Test-Path -LiteralPath $script:InstallRoot)) { return [pscustomobject]@{ Success = $true; RollbackComplete = $true; Message = 'Tunnela is not installed. User profiles were preserved.' } }
    $taskTransaction = New-Transaction
    $taskMoved = [Collections.Generic.List[string]]::new()
    try {
        Stop-IdleOwnedService
        foreach ($taskName in @('desktop', 'service')) {
            $taskTarget = Join-Path $script:InstallRoot $taskName
            if (Test-Path -LiteralPath $taskTarget) { Move-Item -LiteralPath $taskTarget -Destination (Join-Path $taskTransaction $taskName); $taskMoved.Add($taskName) }
        }
        Remove-OwnedService
    } catch {
        $taskFailure = Get-SafeMessage $_.Exception
        $taskRollbackComplete = $true
        try {
            Assert-Idle
            foreach ($taskName in $taskMoved) { Move-Item -LiteralPath (Join-Path $taskTransaction $taskName) -Destination (Join-Path $script:InstallRoot $taskName) }
            if ($taskOriginal) { Restore-ServiceSnapshot $taskOriginal }
            Remove-InstallChild $taskTransaction
        } catch { $taskRollbackComplete = $false }
        return [pscustomobject]@{ Success = $false; RollbackComplete = $taskRollbackComplete; Message = $taskFailure + $(if ($taskRollbackComplete) { ' The previous installation state was restored.' } else { ' The protected backup was retained because rollback could not finish safely.' }) }
    }
    $taskMessage = 'The Tunnela service and application payload were removed. All user profiles were preserved.'
    try { Remove-InstallChild $taskTransaction } catch { $taskMessage += ' A protected payload backup remains because cleanup could not finish.' }
    return [pscustomobject]@{ Success = $true; RollbackComplete = $true; Message = $taskMessage }
}

try {
    Assert-ExecutionLocation
    $taskResult = switch ($Action) {
        'Inspect' { Invoke-Inspect }
        'Install' { Invoke-Install $PayloadDirectory $ControllerAccount }
        'Uninstall' { Invoke-Uninstall }
    }
    if ($OutputFormat -eq 'Nsis') {
        if ($Action -eq 'Inspect' -and $taskResult.Success -and $taskResult.CanProceed) {
            $taskPrefix = if ($taskResult.ExistingInstallation) { 'UPDATE' } else { 'NEW' }
            Write-Output ($taskPrefix + '|' + $taskResult.RecommendedControllerAccount)
        } else { Write-Output $taskResult.Message }
    } else { $taskResult | ConvertTo-Json -Compress }
    if (-not $taskResult.Success) { if ($taskResult.RollbackComplete) { exit 1 }; exit 2 }
    if ($Action -eq 'Inspect' -and -not $taskResult.CanProceed) { exit 1 }
    exit 0
} catch {
    $taskResult = [pscustomobject]@{ Success = $false; RollbackComplete = (-not $script:MutationStarted); Message = Get-SafeMessage $_.Exception }
    if ($OutputFormat -eq 'Nsis') { Write-Output $taskResult.Message } else { $taskResult | ConvertTo-Json -Compress }
    if ($taskResult.RollbackComplete) { exit 1 }; exit 2
}
