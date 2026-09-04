Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# Builds a Win32 .res carrying RT_GROUP_ICON 101 + RT_ICON 1..4 + RT_STRING 101 (caption
# "Optimus"), consumed by Optimus.Resources.dll so the CorelDRAW toolbar button shows the mark.
# args: <iconsDir> <outResPath>
$iconsDir = $args[0]
$outRes   = $args[1]

$GROUP_ICON_ID = 101
$STRING_ID     = 101
$CAPTION       = "Optimus"

$RT_ICON = 3
$RT_GROUP_ICON = 14
$RT_STRING = 6

function Get-DibIcon([string]$png) {
  $bmp = [System.Drawing.Bitmap]::FromFile($png)
  $w = $bmp.Width; $h = $bmp.Height
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([UInt32]40); $bw.Write([Int32]$w); $bw.Write([Int32]($h*2))
  $bw.Write([UInt16]1);  $bw.Write([UInt16]32); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
  $bw.Write([Int32]0);   $bw.Write([Int32]0);   $bw.Write([UInt32]0); $bw.Write([UInt32]0)
  for ($y = $h-1; $y -ge 0; $y--) {
    for ($x = 0; $x -lt $w; $x++) {
      $p = $bmp.GetPixel($x,$y)
      $bw.Write([Byte]$p.B); $bw.Write([Byte]$p.G); $bw.Write([Byte]$p.R); $bw.Write([Byte]$p.A)
    }
  }
  $rowBytes = [Math]::Floor((($w + 31) / 32)) * 4
  for ($y = $h-1; $y -ge 0; $y--) {
    $row = New-Object 'System.Byte[]' $rowBytes
    for ($x = 0; $x -lt $w; $x++) {
      $p = $bmp.GetPixel($x,$y)
      if ($p.A -eq 0) {
        $byteIndex = [Math]::Floor($x / 8); $bitIndex = 7 - ($x % 8)
        $row[$byteIndex] = $row[$byteIndex] -bor (1 -shl $bitIndex)
      }
    }
    $bw.Write($row)
  }
  $bw.Flush(); $data = $ms.ToArray(); $bw.Dispose(); $ms.Dispose(); $bmp.Dispose()
  return [PSCustomObject]@{ Width=$w; Height=$h; Data=$data }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

function Write-ResEntry([System.IO.BinaryWriter]$w, [UInt16]$type, [UInt16]$name, [Byte[]]$data, [UInt16]$memFlags) {
  $w.Write([UInt32]$data.Length); $w.Write([UInt32]32)
  $w.Write([UInt16]0xFFFF); $w.Write([UInt16]$type)
  $w.Write([UInt16]0xFFFF); $w.Write([UInt16]$name)
  $w.Write([UInt32]0); $w.Write([UInt16]$memFlags); $w.Write([UInt16]0x0409)
  $w.Write([UInt32]0); $w.Write([UInt32]0)
  if ($data.Length -gt 0) { $w.Write($data) }
  $pad = (4 - ($data.Length % 4)) % 4
  for ($i=0; $i -lt $pad; $i++) { $w.Write([Byte]0) }
}

Write-ResEntry $w ([UInt16]0) ([UInt16]0) (New-Object 'System.Byte[]' 0) ([UInt16]0)

$frames = @(
  @{ id=1; png=(Join-Path $iconsDir 'optimus-16.png') },
  @{ id=2; png=(Join-Path $iconsDir 'optimus-24.png') },
  @{ id=3; png=(Join-Path $iconsDir 'optimus-32.png') },
  @{ id=4; png=(Join-Path $iconsDir 'optimus-48.png') }
)
$dibs = @()
foreach ($f in $frames) {
  $dib = Get-DibIcon $f.png
  $dibs += [PSCustomObject]@{ id=$f.id; w=$dib.Width; h=$dib.Height; data=$dib.Data }
  Write-ResEntry $w ([UInt16]$RT_ICON) ([UInt16]$f.id) $dib.Data ([UInt16]0x1010)
}

$grp = New-Object System.IO.MemoryStream
$gw  = New-Object System.IO.BinaryWriter($grp)
$gw.Write([UInt16]0); $gw.Write([UInt16]1); $gw.Write([UInt16]$dibs.Count)
foreach ($d in $dibs) {
  $bw2 = if ($d.w -ge 256) {0} else {$d.w}
  $bh2 = if ($d.h -ge 256) {0} else {$d.h}
  $gw.Write([Byte]$bw2); $gw.Write([Byte]$bh2); $gw.Write([Byte]0); $gw.Write([Byte]0)
  $gw.Write([UInt16]1); $gw.Write([UInt16]32); $gw.Write([UInt32]$d.data.Length); $gw.Write([UInt16]$d.id)
}
$gw.Flush()
Write-ResEntry $w ([UInt16]$RT_GROUP_ICON) ([UInt16]$GROUP_ICON_ID) $grp.ToArray() ([UInt16]0x1030)
$gw.Dispose(); $grp.Dispose()

$bundle = [Math]::Floor($STRING_ID / 16) + 1
$slot   = $STRING_ID % 16
$sms = New-Object System.IO.MemoryStream
$sw  = New-Object System.IO.BinaryWriter($sms)
for ($i=0; $i -lt 16; $i++) {
  if ($i -eq $slot) {
    $chars = $CAPTION.ToCharArray()
    $sw.Write([UInt16]$chars.Length)
    foreach ($c in $chars) { $sw.Write([UInt16]([int][char]$c)) }
  } else { $sw.Write([UInt16]0) }
}
$sw.Flush()
Write-ResEntry $w ([UInt16]$RT_STRING) ([UInt16]$bundle) $sms.ToArray() ([UInt16]0x1030)
$sw.Dispose(); $sms.Dispose()

$w.Flush()
[System.IO.File]::WriteAllBytes($outRes, $out.ToArray())
$w.Dispose(); $out.Dispose()
Write-Output "Wrote $outRes ($((Get-Item $outRes).Length) bytes). GROUP_ICON id=$GROUP_ICON_ID, STRING id=$STRING_ID."
