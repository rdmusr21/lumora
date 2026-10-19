# Draws the Lumora icon (gradient squircle with "L") and packs it as a multi-size PNG .ico
param([string]$OutFile)
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $r = [single]($s * 0.22); $m = [single]([Math]::Max(1, $s * 0.03)); $w = [single]($s - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2*$r, 2*$r, 180, 90); $path.AddArc($m+$w-2*$r, $m, 2*$r, 2*$r, 270, 90)
    $path.AddArc($m+$w-2*$r, $m+$w-2*$r, 2*$r, 2*$r, 0, 90); $path.AddArc($m, $m+$w-2*$r, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(10,132,255)), ([System.Drawing.Color]::FromArgb(255,55,95)), ([single]45)
    $g.FillPath($br, $path)
    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($s * 0.56)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('L', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([single]($s*0.02)), $s, $s), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($OutFile); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
