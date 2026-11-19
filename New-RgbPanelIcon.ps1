param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'RgbPanel.ico'),
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $radius
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconFrame([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $scale = [float]($size / 128.0)
        $graphics.ScaleTransform($scale, $scale)

        $plate = New-RoundedPath 5 5 118 118 25
        $surface = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.RectangleF]::new(5, 5, 118, 118),
            [Drawing.Color]::FromArgb(40, 49, 74), [Drawing.Color]::FromArgb(13, 18, 31), 55.0
        )
        $rim = [Drawing.Pen]::new([Drawing.Color]::FromArgb(95, 117, 158), [float]2)
        try {
            $graphics.FillPath($surface, $plate)
            $graphics.DrawPath($rim, $plate)
        } finally {
            $rim.Dispose()
            $surface.Dispose()
            $plate.Dispose()
        }

        $bars = @(
            @{X=29; Top=56; Height=37; Light=[Drawing.Color]::FromArgb(199, 158, 255); Dark=[Drawing.Color]::FromArgb(150, 100, 255)},
            @{X=56; Top=27; Height=66; Light=[Drawing.Color]::FromArgb(159, 210, 255); Dark=[Drawing.Color]::FromArgb(89, 152, 251)},
            @{X=83; Top=43; Height=50; Light=[Drawing.Color]::FromArgb(176, 255, 239); Dark=[Drawing.Color]::FromArgb(60, 222, 188)}
        )
        foreach ($bar in $bars) {
            $lens = New-RoundedPath $bar.X $bar.Top 16 $bar.Height 8
            $light = [Drawing.Drawing2D.LinearGradientBrush]::new(
                [Drawing.RectangleF]::new($bar.X, $bar.Top, 16, $bar.Height),
                $bar.Light, $bar.Dark, [Drawing.Drawing2D.LinearGradientMode]::Vertical
            )
            try {
                if ($size -ge 48) {
                    $glowShape = New-RoundedPath ($bar.X - 2) ($bar.Top - 2) 20 ($bar.Height + 4) 10
                    $glow = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(48, $bar.Light))
                    try { $graphics.FillPath($glow, $glowShape) }
                    finally { $glow.Dispose(); $glowShape.Dispose() }
                }
                $graphics.FillPath($light, $lens)
            } finally { $light.Dispose(); $lens.Dispose() }
        }

        $png = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
            return ,$png.ToArray()
        } finally { $png.Dispose() }
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$frames = foreach ($size in @(16, 24, 32, 40, 48, 64, 96, 128, 256)) {
    [pscustomobject]@{ Size = $size; Bytes = New-IconFrame $size }
}

$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $edge = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($edge)
        $writer.Write($edge)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }

if ($PreviewPath) {
    [IO.File]::WriteAllBytes($PreviewPath, [byte[]]$frames[-1].Bytes)
}
