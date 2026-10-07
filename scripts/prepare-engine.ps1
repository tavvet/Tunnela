# Extract only the pinned upstream runtime and its licenses. Never runs the engine.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = '1.1.7'
$taskArchiveHash = 'AFF81FF4EBCC00D9EDE7F29D0FBB56D277D784168080513074FFB8F357AD3796'
$taskEngineHash = '86A5992D4F841DCF605DA374D2D74BAB035FB2C0D9B32719BDD9F80CFDD56C68'
$taskWintunHash = 'E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE'
$taskDownloads = Join-Path $taskRoot '.cache\engine-downloads'
$taskArchiveName = "trusttunnel_client-v$taskVersion-windows-x86_64.zip"
$taskArchive = Join-Path $taskDownloads $taskArchiveName
$taskEngineDirectory = Join-Path $taskRoot 'runtime\engine'
New-Item -ItemType Directory -Path $taskDownloads -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskArchive) -or (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskArchiveHash) {
    $taskPartial = "$taskArchive.$([guid]::NewGuid().ToString('N')).partial"
    try {
        Invoke-WebRequest -Uri "https://github.com/TrustTunnel/TrustTunnelClient/releases/download/v$taskVersion/$taskArchiveName" -OutFile $taskPartial -TimeoutSec 180 -UseBasicParsing
        if ((Get-FileHash -LiteralPath $taskPartial -Algorithm SHA256).Hash -ne $taskArchiveHash) { throw 'Downloaded TrustTunnel archive hash mismatch.' }
        Move-Item -LiteralPath $taskPartial -Destination $taskArchive -Force
    } finally {
        if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial }
    }
}
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskArchiveHash) {
    throw 'TrustTunnel archive hash mismatch; nothing was extracted.'
}
New-Item -ItemType Directory -Path $taskEngineDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskZip = [IO.Compression.ZipFile]::OpenRead($taskArchive)
try {
    $taskFiles = @{
        'trusttunnel_client.exe' = 'trusttunnel_client.exe'
        'wintun.dll' = 'wintun.dll'
        'LICENSE.txt' = 'TRUSTTUNNEL_LICENSE.txt'
        'WINTUN_LICENSE.txt' = 'WINTUN_LICENSE.txt'
    }
    foreach ($taskFile in $taskFiles.GetEnumerator()) {
        $taskEntry = $taskZip.GetEntry($taskFile.Key)
        if (-not $taskEntry) { throw "Expected engine archive member missing: $($taskFile.Key)" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry, (Join-Path $taskEngineDirectory $taskFile.Value), $true)
    }
} finally { $taskZip.Dispose() }
if ((Get-FileHash -LiteralPath (Join-Path $taskEngineDirectory 'trusttunnel_client.exe')).Hash -ne $taskEngineHash -or
    (Get-FileHash -LiteralPath (Join-Path $taskEngineDirectory 'wintun.dll')).Hash -ne $taskWintunHash) {
    throw 'Extracted runtime hash mismatch.'
}
@{
    TrustTunnelVersion = $taskVersion
    TrustTunnelSource = "https://github.com/TrustTunnel/TrustTunnelClient/releases/tag/v$taskVersion"
    TrustTunnelArchiveSha256 = $taskArchiveHash
    TrustTunnelExeSha256 = $taskEngineHash
    WintunVersion = '0.14.1'
    WintunSource = 'https://www.wintun.net/'
    WintunDllSha256 = $taskWintunHash
    Architecture = 'x64'
    Executed = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEngineDirectory 'provenance.json') -Encoding UTF8
Write-Host "TrustTunnel $taskVersion runtime is prepared. No engine or service has been run."
