<#
.SYNOPSIS
    Draws the app icon (src\BatteryInfo.App\BatteryInfo.ico): a blue rounded tile with a white battery, at every
    size Windows asks for. Run it again only to change the icon; the .ico is checked in.
#>
param(
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\BatteryInfo.App\BatteryInfo.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.01, $radius * 2)
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconFrame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $s = $size / 16.0

    # Tile: Windows blue, lighter at the top.
    $tile = New-RoundedRectangle 0 0 $size $size (3.5 * $s)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0),
        (New-Object System.Drawing.PointF 0, $size), ([System.Drawing.Color]::FromArgb(255, 71, 158, 245)),
        ([System.Drawing.Color]::FromArgb(255, 15, 108, 189))
    $g.FillPath($gradient, $tile)

    # Battery: white body and terminal, with a green charge level.
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 108, 203, 95))
    $bodyX = 2.5 * $s; $bodyY = 4.5 * $s; $bodyW = 10 * $s; $bodyH = 7 * $s
    $stroke = [Math]::Max(1.0, 1.25 * $s)
    $g.FillPath($white, (New-RoundedRectangle $bodyX $bodyY $bodyW $bodyH (1.5 * $s)))
    $g.FillPath($white, (New-RoundedRectangle ($bodyX + $bodyW - 0.5 * $s) (6.5 * $s) (1.75 * $s) (3 * $s) (0.6 * $s)))
    $innerX = $bodyX + $stroke; $innerY = $bodyY + $stroke
    $innerW = $bodyW - 2 * $stroke; $innerH = $bodyH - 2 * $stroke
    $g.FillPath($gradient, (New-RoundedRectangle $innerX $innerY $innerW $innerH (0.6 * $s)))
    $gap = [Math]::Max(0.75, 0.75 * $s)
    $g.FillPath($green, (New-RoundedRectangle ($innerX + $gap) ($innerY + $gap) (($innerW - 2 * $gap) * 0.7) ($innerH - 2 * $gap) (0.4 * $s)))

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Small sizes as a 32-bit DIB (bottom-up BGRA, then an empty AND mask): every icon reader takes these.
        $w = New-Object System.IO.BinaryWriter $stream
        $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0); $w.Write([uint32]0)
        $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bitmap.GetPixel($x, $y)
                $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
            }
        }
        $maskRow = [Math]::Ceiling($size / 32) * 4
        $w.Write((New-Object byte[] ($maskRow * $size)))
        $w.Flush()
    }
    $bitmap.Dispose()
    return , $stream.ToArray()
}

# An .ico file: a header, one directory entry per size, then each size's image.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = $sizes | ForEach-Object { , (New-IconFrame $_) }
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($Output, $file.ToArray())
Write-Host "Wrote $Output"
