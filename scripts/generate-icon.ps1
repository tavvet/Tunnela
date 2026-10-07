[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskAssetDirectory = Join-Path $taskRoot 'src\Tunnela.Desktop\Assets'
$taskIconPath = Join-Path $taskAssetDirectory 'Tunnela.ico'
# The matching editable vector is Assets/Tunnela.svg. No downloaded artwork or font is required.
$taskSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$taskImages = [Collections.Generic.List[byte[]]]::new()
foreach ($taskSize in $taskSizes) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskPath = [Drawing.Drawing2D.GraphicsPath]::new()
    $taskBackground = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#102b35'))
    $taskForeground = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#59d7c2'))
    $taskPng = [IO.MemoryStream]::new()
    try {
        $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $taskGraphics.ScaleTransform(($taskSize / 32.0), ($taskSize / 32.0))
        $taskPath.AddArc(1, 1, 14, 14, 180, 90)
        $taskPath.AddArc(17, 1, 14, 14, 270, 90)
        $taskPath.AddArc(17, 17, 14, 14, 0, 90)
        $taskPath.AddArc(1, 17, 14, 14, 90, 90)
        $taskPath.CloseFigure()
        $taskGraphics.FillPath($taskBackground, $taskPath)
        $taskPoints = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(8, 8), [Drawing.PointF]::new(24, 8),
            [Drawing.PointF]::new(24, 13), [Drawing.PointF]::new(18.5, 13),
            [Drawing.PointF]::new(18.5, 24), [Drawing.PointF]::new(13.5, 24),
            [Drawing.PointF]::new(13.5, 13), [Drawing.PointF]::new(8, 13)
        )
        $taskGraphics.FillPolygon($taskForeground, $taskPoints)
        $taskBitmap.Save($taskPng, [Drawing.Imaging.ImageFormat]::Png)
        $taskImages.Add($taskPng.ToArray())
    } finally {
        $taskPng.Dispose()
        $taskForeground.Dispose()
        $taskBackground.Dispose()
        $taskPath.Dispose()
        $taskGraphics.Dispose()
        $taskBitmap.Dispose()
    }
}
$taskOutput = [IO.File]::Create($taskIconPath)
$taskWriter = [IO.BinaryWriter]::new($taskOutput)
try {
    $taskWriter.Write([uint16]0)
    $taskWriter.Write([uint16]1)
    $taskWriter.Write([uint16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
        $taskDimension = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
        $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([uint16]0)
        $taskWriter.Write([uint16]1)
        $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskImages[$taskIndex].Length)
        $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskImages[$taskIndex].Length
    }
    foreach ($taskBytes in $taskImages) { $taskWriter.Write($taskBytes) }
} finally { $taskWriter.Dispose() }
Write-Host "Generated $taskIconPath"
