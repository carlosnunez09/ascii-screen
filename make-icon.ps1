# Generates app.ico (a top-down shark and a fish on a deep-sea tile) - run once, the .ico is checked in.
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size

    # rounded deep-sea tile
    $r = $s * 0.22
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc(0, 0, $r*2, $r*2, 180, 90); $bg.AddArc($s-$r*2, 0, $r*2, $r*2, 270, 90)
    $bg.AddArc($s-$r*2, $s-$r*2, $r*2, $r*2, 0, 90); $bg.AddArc(0, $s-$r*2, $r*2, $r*2, 90, 90); $bg.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF 0,0), (New-Object System.Drawing.PointF $s,$s),
            [System.Drawing.Color]::FromArgb(255,18,52,72), [System.Drawing.Color]::FromArgb(255,5,14,22))
    $g.FillPath($grad, $bg)

    function P($x, $y) { New-Object System.Drawing.PointF ([single]($x * $s)), ([single]($y * $s)) }

    # shark, facing right, drawn as body + fins + tail
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddClosedCurve(@((P 0.90 0.50), (P 0.74 0.405), (P 0.52 0.375), (P 0.34 0.41), (P 0.22 0.46), (P 0.22 0.54), (P 0.34 0.59), (P 0.52 0.625), (P 0.74 0.595)), 0.5)
    $fin1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fin1.AddPolygon(@((P 0.60 0.43), (P 0.47 0.27), (P 0.41 0.28), (P 0.50 0.44)))
    $fin2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fin2.AddPolygon(@((P 0.60 0.57), (P 0.47 0.73), (P 0.41 0.72), (P 0.50 0.56)))
    $tail = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail.AddPolygon(@((P 0.24 0.50), (P 0.07 0.30), (P 0.12 0.50), (P 0.07 0.66)))
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,170,200,226))
    $g.FillPath($ink, $fin1); $g.FillPath($ink, $fin2); $g.FillPath($ink, $tail); $g.FillPath($ink, $body)

    # a little fish
    $fish = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fish.AddClosedCurve(@((P 0.30 0.80), (P 0.21 0.76), (P 0.14 0.80), (P 0.21 0.84)), 0.5)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,255,176,84))), $fish)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = @()
foreach ($sz in $sizes) {
    $b = New-Frame $sz; $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray()
    if ($sz -eq 256) { $b.Save((Join-Path $here "icon-preview.png")) }
}
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $here "app.ico"), $out.ToArray())
Write-Host "Wrote app.ico"

