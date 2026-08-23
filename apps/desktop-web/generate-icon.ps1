[CmdletBinding()]
param(
    [string] $Source = (Join-Path $PSScriptRoot 'app-icon.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$target = Join-Path $PSScriptRoot 'app.ico'
$sizes = 256, 128, 64, 48, 32, 24, 16

$src = [System.Drawing.Image]::FromFile($Source)
try {
    # Cover-crop to a centered square, then resize each icon entry from it.
    $edge = [Math]::Min($src.Width, $src.Height)
    $crop = New-Object System.Drawing.Rectangle(
        [int](($src.Width - $edge) / 2), [int](($src.Height - $edge) / 2), $edge, $edge)

    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $bmp.SetResolution(96.0, 96.0)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $g.Dispose() }
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $frames += ,([PSCustomObject]@{ Size = $size; Bytes = $ms.ToArray() })
        $ms.Dispose()
    }
}
finally { $src.Dispose() }

# Assemble the ICO container: one PNG-embedded entry per size (Vista+ format).
$fs = [System.IO.File]::Create($target)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([UInt16]0)              # reserved
    $bw.Write([UInt16]1)              # type: icon
    $bw.Write([UInt16]$frames.Count)  # entry count
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $s = $f.Size
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))  # width
        $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))  # height
        $bw.Write([byte]0)            # palette
        $bw.Write([byte]0)            # reserved
        $bw.Write([UInt16]1)          # color planes
        $bw.Write([UInt16]32)         # bits per pixel
        $bw.Write([UInt32]$f.Bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $bw.Write($f.Bytes) }
}
finally { $bw.Dispose(); $fs.Dispose() }

Write-Output "Generated $target ($($sizes -join ', ') px, PNG-embedded entries)"
