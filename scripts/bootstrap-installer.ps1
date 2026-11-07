# Project-local portable compiler. Does not register or install anything in Windows.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = '3.13'
$taskHash = 'BA63DFFC4410EE89193E1CB5A41989991BD77C61068DA17E3156D136B7B0B3D8'
$taskTools = Join-Path $taskRoot '.tools'
$taskDirectory = Join-Path $taskTools "nsis-$taskVersion"
$taskCompiler = Join-Path $taskDirectory 'makensis.exe'
$taskReceipt = Join-Path $taskDirectory '.tunnela-extracted-sha256'
if ((Test-Path -LiteralPath $taskCompiler) -and (Test-Path -LiteralPath $taskReceipt) -and (Get-Content -LiteralPath $taskReceipt -Raw).Trim() -eq $taskHash) {
    $taskReportedVersion = & $taskCompiler /VERSION
    if ($LASTEXITCODE -ne 0 -or "$taskReportedVersion".Trim() -ne "v$taskVersion") { throw 'Unexpected local NSIS version.' }
    Write-Host "Project-local NSIS $taskVersion is available."
    return
}
$taskDownloads = Join-Path $taskRoot '.cache\installer-downloads'
New-Item -ItemType Directory -Path $taskDownloads -Force | Out-Null
$taskArchive = Join-Path $taskDownloads "nsis-$taskVersion.zip"
if (-not (Test-Path -LiteralPath $taskArchive) -or (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskHash) {
    # Pinned archive from the official NSIS project's SourceForge release.
    # Hash recorded from that HTTPS release download; not a publisher signature.
    $taskUrls = @(
        "https://downloads.sourceforge.net/project/nsis/NSIS%203/$taskVersion/nsis-$taskVersion.zip",
        "https://netix.dl.sourceforge.net/project/nsis/NSIS%203/$taskVersion/nsis-$taskVersion.zip"
    )
    $taskDownloaded = $false
    foreach ($taskUrl in $taskUrls) {
        $taskPartial = "$taskArchive.$([guid]::NewGuid().ToString('N')).partial"
        try {
            Invoke-WebRequest -Uri $taskUrl -OutFile $taskPartial -TimeoutSec 180 -UseBasicParsing
            $taskActualHash = (Get-FileHash -LiteralPath $taskPartial -Algorithm SHA256).Hash
            if ($taskActualHash -ne $taskHash) {
                # Some SourceForge mirrors return an HTML page with HTTP 200.
                # Never extract it or accept a different archive hash.
                Write-Warning "NSIS download from $(([uri]$taskUrl).Host) failed SHA-256 verification."
                continue
            }
            Move-Item -LiteralPath $taskPartial -Destination $taskArchive -Force
            $taskDownloaded = $true
            break
        } catch {
            Write-Warning "NSIS download attempt failed: $($_.Exception.Message)"
        } finally {
            if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial }
        }
    }
    if (-not $taskDownloaded) { throw 'No NSIS mirror supplied the pinned archive; nothing was extracted.' }
}
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskHash) {
    throw 'NSIS archive hash mismatch; nothing was extracted.'
}
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskTools -Force
$taskReportedVersion = & $taskCompiler /VERSION
if ($LASTEXITCODE -ne 0 -or "$taskReportedVersion".Trim() -ne "v$taskVersion") { throw 'Unexpected extracted NSIS version.' }
$taskHash | Set-Content -LiteralPath $taskReceipt -Encoding ASCII
Write-Host "NSIS $taskVersion is ready inside .tools; no global installation."
