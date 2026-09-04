Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# Draws the Optimus mark — a white lightning bolt on a rounded orange tile — and emits
# PNG frames (16/24/32/48/256) + a multi-resolution .ico. No source image needed.
$outDir = $args[0]
if (-not $outDir) { $outDir = $PSScriptRoot }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$brand  = [System.Drawing.Color]::FromArgb(255, 234, 88, 12)   # #EA580C orange-600
$brand2 = [System.Drawing.Color]::FromArgb(255, 194, 65, 12)   # #C2410C orange-700

function New-MarkBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::FromArgb(0,0,0,0))

  # Rounded tile (small outer margin) with a vertical orange gradient.
  $m = [Math]::Max(1, [int]($size * 0.06))
  $r = [Math]::Max(2, [int]($size * 0.22))
  $x = $m; $y = $m; $w = $size - 2*$m; $h = $size - 2*$m
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $path.AddArc($x, $y, $d, $d, 180, 90)
  $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $path.CloseFigure()
  $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $brand, $brand2, 90)
  $g.FillPath($brush, $path)
  $brush.Dispose(); $path.Dispose()

  # White lightning bolt, centred. Normalised points in a 0..1 box, y downward.
  $bolt = @(
    @(0.575,0.085), @(0.300,0.545), @(0.470,0.545), @(0.415,0.915),
    @(0.735,0.430), @(0.545,0.430), @(0.610,0.085)
  )
  $pts = @()
  foreach ($p in $bolt) {
    $pts += New-Object System.Drawing.PointF(($x + $p[0]*$w), ($y + $p[1]*$h))
  }
  $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
  $g.FillPolygon($white, [System.Drawing.PointF[]]$pts)
  $white.Dispose()

  $g.Dispose()
  return $bmp
}

$sizes = @(256, 48, 32, 24, 16)
$pngs = @{}
foreach ($s in $sizes) {
  $bmp = New-MarkBitmap $s
  $path = Join-Path $outDir ("optimus-$s.png")
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs[$s] = $path
  $bmp.Dispose()
}

# Multi-resolution .ico (PNG frames; Vista+/Win10/11).
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$imgData = @{}
foreach ($s in $sizes) { $imgData[$s] = [System.IO.File]::ReadAllBytes($pngs[$s]) }
$offset = 6 + 16*$sizes.Count
foreach ($s in $sizes) {
  $bytes = $imgData[$s]
  $wByte = if ($s -ge 256) {0} else {$s}
  $bw.Write([Byte]$wByte); $bw.Write([Byte]$wByte); $bw.Write([Byte]0); $bw.Write([Byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
  $offset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write($imgData[$s]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'optimus.ico'), $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output "Generated Optimus icons in $outDir"
