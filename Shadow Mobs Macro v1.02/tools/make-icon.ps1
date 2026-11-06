# Makes icon.ico (the exe's icon) from assets\logo.png, at the sizes Windows asks for.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$logo = [Drawing.Image]::FromFile((Join-Path $root 'assets\logo.png'))
$sizes = 256, 64, 48, 32, 16
$pngs = foreach ($s in $sizes) {
    $b = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'AntiAlias'
    $g.DrawImage($logo, 0, 0, $s, $s)
    $g.Dispose()
    $m = New-Object IO.MemoryStream
    $b.Save($m, [Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $m.ToArray()
}
$logo.Dispose()
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root 'icon.ico'), $out.ToArray())
Write-Host "Wrote icon.ico"
