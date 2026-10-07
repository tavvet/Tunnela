[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskPrincipal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $taskPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated PowerShell.' }
$taskProgramFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$taskRoot = [IO.Path]::GetFullPath((Join-Path $taskProgramFiles 'Tunnela'))
if (-not $taskRoot.StartsWith(([IO.Path]::GetFullPath($taskProgramFiles).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe uninstall path.' }
$taskExpectedExe = Join-Path $taskRoot 'service\Tunnela.Service.exe'
$taskService = Get-CimInstance Win32_Service -Filter "Name='Tunnela'"
if ($taskService -and $taskService.PathName.Trim('"') -ne $taskExpectedExe) { throw 'Service path does not match this product. Nothing was removed.' }
# Refuse an active session before stopping any service, including orphaned CLI sessions.
if (Get-Process -Name 'Tunnela.Desktop' -ErrorAction SilentlyContinue) { throw 'Disconnect the VPN in Tunnela and close Tunnela before uninstalling. No service was stopped.' }
$taskRemainingEngine = Get-CimInstance Win32_Process -Filter "Name='trusttunnel_client.exe'" | Where-Object { $_.ExecutablePath -eq (Join-Path $taskRoot 'service\engine\trusttunnel_client.exe') }
if ($taskRemainingEngine) { throw 'The Tunnela tunnel process is still running. Disconnect it safely before uninstalling. No service was stopped.' }
if ($taskService) {
    if ($taskService.State -ne 'Stopped') {
        Stop-Service -Name 'Tunnela' -ErrorAction Stop
        (Get-Service -Name 'Tunnela').WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
}
if ($taskService) {
    & sc.exe delete Tunnela
    if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
if (Test-Path -LiteralPath $taskRoot) {
    $taskItems = @(Get-Item -LiteralPath $taskRoot) + @(Get-ChildItem -LiteralPath $taskRoot -Force -Recurse)
    if ($taskItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Installation directory contains a reparse point; files were not removed.' }
    Remove-Item -LiteralPath $taskRoot -Recurse -Force
}
Write-Host 'Application and service removed. Your encrypted user profiles were preserved.'
