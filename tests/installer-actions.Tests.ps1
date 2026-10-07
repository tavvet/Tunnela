# Isolated filesystem/command-mock checks. Never invokes the installer entry point,
# real SCM commands, registry writes, ACL changes, an application, or a VPN.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskSource = Join-Path $taskRepository 'scripts\installer-actions.ps1'
$taskTokens = $null
$taskErrors = $null
$taskAst = [Management.Automation.Language.Parser]::ParseFile($taskSource, [ref]$taskTokens, [ref]$taskErrors)
if ($taskErrors.Count -gt 0) { throw 'Installer source contains syntax errors.' }
$taskFunctions = $taskAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)
. ([scriptblock]::Create(($taskFunctions | ForEach-Object { $_.Extent.Text }) -join [Environment]::NewLine))

$taskTestRoot = Join-Path $taskRepository ('.artifacts\installer-tests\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTestRoot -Force | Out-Null
$script:InstallRoot = Join-Path $taskTestRoot 'Tunnela'
$script:ServiceExe = Join-Path $script:InstallRoot 'service\Tunnela.Service.exe'
$script:EngineExe = Join-Path $script:InstallRoot 'service\engine\trusttunnel_client.exe'
$script:ServiceName = 'Tunnela'
$script:EngineVersion = '1.1.7'
$script:EngineHash = '86A5992D4F841DCF605DA374D2D74BAB035FB2C0D9B32719BDD9F80CFDD56C68'
$script:WintunHash = 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'
$script:BackendPath = Join-Path $taskTestRoot 'stage\installer-actions.ps1'
$script:Payload = Join-Path $taskTestRoot 'stage\payload'
$script:TestSid = 'S-1-5-21-101-202-303-1001'
$script:Operations = [Collections.Generic.List[string]]::new()
$script:Checks = 0

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw ('FAILED: ' + $Message) }
    $script:Checks++
}
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $taskThrown = $false
    try { & $Action | Out-Null } catch { $taskThrown = $true }
    Assert-True $taskThrown $Message
}
function Write-TestFile([string]$Path, [string]$Content) {
    $taskParent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $taskParent)) { New-Item -ItemType Directory -Path $taskParent -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Content)
}

# OS and security operations are deliberately mocked. Production transaction/file
# code and fixed-path deletion guards are the original extracted functions above.
function Assert-ProtectedAcl([string]$Path) { if (-not (Test-Path -LiteralPath $Path)) { throw 'Missing protected test fixture.' } }
function Set-ProtectedTree([string]$Path) { }
function Assert-Payload([string]$Path) { Assert-True (Test-SamePath $Path $script:Payload) 'payload path preserved' }
function Resolve-ControllerUser([string]$Account, [string]$ExistingSid) { if ($ExistingSid) { return $ExistingSid }; return $script:TestSid }
function Get-RecommendedAccount { return 'TEST\User' }
function Get-CimInstance([string]$ClassName, [string]$Filter) {
    switch ($ClassName) {
        'Win32_Service' { return $script:FakeService }
        'Win32_Process' { if ($script:TunnelRunning) { return [pscustomobject]@{ ExecutablePath = $script:EngineExe } }; return }
        default { throw 'Unexpected CIM call in isolated test.' }
    }
}
function Get-Process([string]$Name, $ErrorAction) { if ($script:DesktopRunning) { return [pscustomobject]@{ Id = 999 } } }
function Get-ItemProperty([string]$LiteralPath) {
    if ($script:DelayedExists) { return [pscustomobject]@{ DelayedAutostart = $script:DelayedValue } }
    return [pscustomobject]@{}
}
function New-ItemProperty([string]$LiteralPath, [string]$Name, [string]$PropertyType, $Value, [switch]$Force) { $script:DelayedExists = $true; $script:DelayedValue = $Value }
function Remove-ItemProperty([string]$LiteralPath, [string]$Name, $ErrorAction) { $script:DelayedExists = $false }
function Set-Service([string]$Name, [string]$StartupType, [string]$DisplayName, [string]$Description) {
    $script:Operations.Add('Configure:' + $StartupType)
    if (-not $script:FakeService) { throw 'No fake service.' }
    $script:FakeService.StartMode = if ($StartupType -eq 'Automatic') { 'Auto' } else { $StartupType }
    if ($PSBoundParameters.ContainsKey('DisplayName')) { $script:FakeService.DisplayName = $DisplayName }
    if ($PSBoundParameters.ContainsKey('Description')) { $script:FakeService.Description = $Description }
}
function New-Service([string]$Name, [string]$BinaryPathName, [string]$DisplayName, [string]$Description, [string]$StartupType) {
    $script:Operations.Add('Create')
    $script:FakeService = [pscustomobject]@{ State = 'Stopped'; StartMode = 'Auto'; DisplayName = $DisplayName; Description = $Description; PathName = $BinaryPathName; StartName = 'LocalSystem' }
}
function Stop-Service([string]$Name, $ErrorAction) { $script:Operations.Add('Stop'); $script:FakeService.State = 'Stopped' }
function Start-Service([string]$Name, $ErrorAction) {
    $script:Operations.Add('Start')
    if ($script:FailStart -gt 0) { $script:FailStart--; throw 'Injected startup failure.' }
    if ($script:FakeService.StartMode -eq 'Disabled') { throw 'A disabled service cannot start.' }
    $script:FakeService.State = 'Running'
}
function Get-Service([string]$Name) {
    $taskController = [pscustomobject]@{}
    $taskController | Add-Member -MemberType ScriptMethod -Name WaitForStatus -Value { param($Status, $Timeout) if ($script:FakeService.State -ne $Status) { throw 'Unexpected fake service state.' } }
    $taskController | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
    return $taskController
}
function Invoke-CimMethod($InputObject, [string]$MethodName) {
    $script:Operations.Add('Delete')
    if ($script:FailDelete) { return [pscustomobject]@{ ReturnValue = 1 } }
    $script:FakeService = $null
    return [pscustomobject]@{ ReturnValue = 0 }
}
function Move-Item([string]$LiteralPath, [string]$Destination) {
    if ($script:FailMove -and (Split-Path -Leaf $LiteralPath) -eq $script:FailMove) { $script:FailMove = ''; throw 'Injected directory-swap failure.' }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
}

function Reset-Fixture([bool]$Existing) {
    if (Test-Path -LiteralPath $script:InstallRoot) {
        $taskAbsolute = [IO.Path]::GetFullPath($script:InstallRoot)
        if (-not $taskAbsolute.StartsWith($taskTestRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $taskAbsolute -Recurse -Force
    }
    $script:Operations.Clear()
    $script:DesktopRunning = $false; $script:TunnelRunning = $false; $script:FailStart = 0; $script:FailDelete = $false; $script:FailMove = ''
    $script:DelayedExists = $true; $script:DelayedValue = 1
    $script:FakeService = $null
    if ($Existing) {
        $script:FakeService = [pscustomobject]@{ State = 'Running'; StartMode = 'Manual'; DisplayName = 'Previous Tunnela'; Description = 'Previous description'; PathName = ('"' + $script:ServiceExe + '"'); StartName = 'LocalSystem' }
        Write-TestFile (Join-Path $script:InstallRoot 'desktop\marker.txt') 'old desktop'
        Write-TestFile (Join-Path $script:InstallRoot 'service\marker.txt') 'old service'
        Write-TestFile (Join-Path $script:InstallRoot 'service\service-install.json') ('{"ControllerSid":"' + $script:TestSid + '"}')
        Write-TestFile (Join-Path $script:InstallRoot 'installer-actions.ps1') 'old backend'
        Write-TestFile (Join-Path $script:InstallRoot 'uninstall.exe') 'old uninstaller'
    }
}
function Assert-OldRestored {
    Assert-True ((Get-Content -LiteralPath (Join-Path $script:InstallRoot 'desktop\marker.txt') -Raw) -eq 'old desktop') 'desktop backup restored'
    Assert-True ((Get-Content -LiteralPath (Join-Path $script:InstallRoot 'service\marker.txt') -Raw) -eq 'old service') 'service backup restored'
    Assert-True ((Get-Content -LiteralPath (Join-Path $script:InstallRoot 'installer-actions.ps1') -Raw) -eq 'old backend') 'backend backup restored'
    Assert-True ((Get-Content -LiteralPath (Join-Path $script:InstallRoot 'uninstall.exe') -Raw) -eq 'old uninstaller') 'uninstaller backup restored'
    Assert-True ($script:FakeService.State -eq 'Running') 'previous running state restored'
    Assert-True ($script:DelayedExists -and $script:DelayedValue -eq 1) 'delayed start setting restored'
}

function Write-TestManifest($Entries) {
    $taskManifestPath = Join-Path (Split-Path -Parent $script:Payload) 'payload-manifest.json'
    ConvertTo-Json -InputObject @($Entries) -Depth 4 | Set-Content -LiteralPath $taskManifestPath -Encoding UTF8
    return (Get-FileHash -LiteralPath $taskManifestPath -Algorithm SHA256).Hash
}

try {
    Write-TestFile $script:BackendPath 'new backend'
    Write-TestFile (Join-Path (Split-Path -Parent $script:BackendPath) 'uninstall.exe') 'new uninstaller'
    Write-TestFile (Join-Path $script:Payload 'desktop\marker.txt') 'new desktop'
    Write-TestFile (Join-Path $script:Payload 'service\marker.txt') 'new service'
    Write-TestFile (Join-Path $taskTestRoot 'profiles.dat') 'preserved user profiles'

    $taskManifestEntries = @(
        @{ RelativePath = 'desktop\marker.txt'; Sha256 = (Get-FileHash -LiteralPath (Join-Path $script:Payload 'desktop\marker.txt') -Algorithm SHA256).Hash },
        @{ RelativePath = 'service\marker.txt'; Sha256 = (Get-FileHash -LiteralPath (Join-Path $script:Payload 'service\marker.txt') -Algorithm SHA256).Hash }
    )
    $taskManifestHash = Write-TestManifest $taskManifestEntries
    Assert-PayloadManifest $script:Payload $taskManifestHash
    Assert-True $true 'exact payload manifest accepted'
    Assert-Throws { Assert-PayloadManifest $script:Payload ('0' * 64) } 'manifest identity mismatch rejected'
    Write-TestFile (Join-Path $script:Payload 'desktop\extra.dll') 'unlisted file'
    Assert-Throws { Assert-PayloadManifest $script:Payload $taskManifestHash } 'extra payload file rejected'
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath (Join-Path $script:Payload 'desktop\extra.dll')
    foreach ($taskBadPath in @('desktop\..\outside.txt', 'C:\outside.txt', '\\server\share\file', 'service\marker.txt:stream', 'service/marker.txt', 'service\NUL.txt', 'service\marker.txt.', 'service\marker.txt ')) {
        $taskBadHash = Write-TestManifest @(@{ RelativePath = $taskBadPath; Sha256 = ('0' * 64) })
        Assert-Throws { Assert-PayloadManifest $script:Payload $taskBadHash } 'unsafe manifest path rejected'
    }
    $taskDuplicateHash = Write-TestManifest @($taskManifestEntries[0], $taskManifestEntries[0])
    Assert-Throws { Assert-PayloadManifest $script:Payload $taskDuplicateHash } 'duplicate manifest path rejected'
    $taskManifestHash = Write-TestManifest $taskManifestEntries
    Write-TestFile (Join-Path $script:Payload 'desktop\marker.txt') 'tampered desktop'
    Assert-Throws { Assert-PayloadManifest $script:Payload $taskManifestHash } 'tampered payload hash rejected'
    Write-TestFile (Join-Path $script:Payload 'desktop\marker.txt') 'new desktop'

    Reset-Fixture $true
    $script:DesktopRunning = $true
    Assert-Throws { Invoke-Install $script:Payload 'TEST\OtherUser' } 'running GUI blocks install'
    Assert-True ($script:Operations.Count -eq 0) 'GUI refusal before any SCM mutation'
    $script:DesktopRunning = $false; $script:TunnelRunning = $true
    Assert-Throws { Invoke-Uninstall } 'running tunnel blocks uninstall'
    Assert-True ($script:Operations.Count -eq 0) 'VPN refusal before any SCM mutation'

    Reset-Fixture $true
    $script:FakeService.StartName = 'TEST\ServiceUser'
    Assert-Throws { Invoke-Install $script:Payload 'TEST\User' } 'foreign service account rejected'
    Assert-True ($script:Operations.Count -eq 0) 'foreign account untouched'
    $script:FakeService.StartName = 'LocalSystem'; $script:FakeService.PathName = 'C:\Other\service.exe'
    Assert-Throws { Invoke-Uninstall } 'foreign service path rejected'

    Reset-Fixture $true
    $script:FailStart = 1
    $taskResult = Invoke-Install $script:Payload 'TEST\OtherUser'
    Assert-True (-not $taskResult.Success -and $taskResult.RollbackComplete) 'failed startup rolls update back'
    Assert-OldRestored
    Assert-True ($script:FakeService.StartMode -eq 'Manual') 'previous manual start mode restored'

    Reset-Fixture $true
    $script:FakeService.StartMode = 'Disabled'; $script:FailStart = 1
    $taskResult = Invoke-Install $script:Payload 'TEST\User'
    Assert-True (-not $taskResult.Success -and $taskResult.RollbackComplete) 'running disabled service can be restored'
    Assert-OldRestored
    Assert-True ($script:FakeService.StartMode -eq 'Disabled') 'disabled mode restored after restarting previous service'

    Reset-Fixture $true
    $script:FailMove = 'new-service'
    $taskResult = Invoke-Install $script:Payload 'TEST\User'
    Assert-True (-not $taskResult.Success -and $taskResult.RollbackComplete) 'partial directory swap rolls back'
    Assert-OldRestored

    Reset-Fixture $false
    $script:FailStart = 1
    $taskResult = Invoke-Install $script:Payload 'TEST\User'
    Assert-True (-not $taskResult.Success -and $taskResult.RollbackComplete) 'first install startup failure rolls back'
    Assert-True ($null -eq $script:FakeService) 'new service removed during rollback'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'service'))) 'failed new service payload removed'

    Reset-Fixture $true
    $taskResult = Invoke-Install $script:Payload 'TEST\OtherUser'
    Assert-True $taskResult.Success 'successful mocked update commits'
    Assert-True ($script:FakeService.StartMode -eq 'Auto' -and $script:FakeService.State -eq 'Running') 'new service automatic and running'
    $taskManifest = Get-Content -LiteralPath (Join-Path $script:InstallRoot 'service\service-install.json') -Raw | ConvertFrom-Json
    Assert-True ($taskManifest.ControllerSid -eq $script:TestSid) 'existing controller SID preserved despite different parameter'
    Assert-True ($taskManifest.EngineSha256 -eq $script:EngineHash) 'manifest pins engine hash'

    Reset-Fixture $true
    $script:FailDelete = $true
    $taskResult = Invoke-Uninstall
    Assert-True (-not $taskResult.Success -and $taskResult.RollbackComplete) 'failed service deletion restores uninstall payload'
    Assert-OldRestored

    Reset-Fixture $true
    $taskResult = Invoke-Uninstall
    Assert-True $taskResult.Success 'successful mocked uninstall commits'
    Assert-True ($null -eq $script:FakeService) 'owned service removed'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'desktop')) -and -not (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'service'))) 'only payload directories removed'
    Assert-True (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'installer-actions.ps1')) 'backend remains for NSIS'
    Assert-True (Test-Path -LiteralPath (Join-Path $script:InstallRoot 'uninstall.exe')) 'uninstaller remains for NSIS'
    Assert-True ((Get-Content -LiteralPath (Join-Path $taskTestRoot 'profiles.dat') -Raw) -eq 'preserved user profiles') 'user profile sentinel preserved'
    Assert-Throws { Remove-InstallChild $script:InstallRoot } 'root recursive removal refused'
    Assert-Throws { Remove-InstallChild (Join-Path $taskTestRoot 'profiles.dat') } 'outside-root cleanup refused'
    Write-Output ('Passed ' + $script:Checks + ' isolated installer assertions. No real service, registry, ACL, application or VPN operation executed.')
} finally {
    $taskResolvedRoot = [IO.Path]::GetFullPath($taskTestRoot)
    $taskAllowedRoot = [IO.Path]::GetFullPath((Join-Path $taskRepository '.artifacts\installer-tests')).TrimEnd('\') + '\'
    if (-not $taskResolvedRoot.StartsWith($taskAllowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe final test cleanup path.' }
    if (Test-Path -LiteralPath $taskResolvedRoot) { Microsoft.PowerShell.Management\Remove-Item -LiteralPath $taskResolvedRoot -Recurse -Force }
}
