# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Konayukiw

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$public = Join-Path $root "public"
$logoPath = Join-Path $public "assets\img\logo.png"
$logo = [System.Drawing.Image]::FromFile($logoPath)

function New-Canvas([int]$width, [int]$height) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return @($bitmap, $graphics)
}

function Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

$flakeLines = @(
    @(30, 42, 30, 22), @(30, 31, 35.4, 25.6), @(30, 31, 24.6, 25.6),
    @(30, 42, 12.7, 32), @(20.5, 36.5, 18.5, 29.2), @(20.5, 36.5, 13.1, 38.5),
    @(30, 42, 12.7, 52), @(20.5, 47.5, 13.1, 45.5), @(20.5, 47.5, 18.5, 54.8),
    @(30, 42, 30, 62), @(30, 53, 24.6, 58.4), @(30, 53, 35.4, 58.4),
    @(30, 42, 47.3, 52), @(39.5, 47.5, 41.5, 54.8), @(39.5, 47.5, 46.9, 45.5),
    @(30, 42, 47.3, 32), @(39.5, 36.5, 46.9, 38.5), @(39.5, 36.5, 41.5, 29.2)
)

function Draw-Flake($graphics, [float]$cx, [float]$cy, [float]$scale, [int]$alpha) {
    foreach ($pass in @(@("#7391ee", 9), @("#ffffff", 6), @("#3aaee8", 3))) {
        $pen = New-Object System.Drawing.Pen (Color $pass[0] $alpha), ([float]$pass[1] * $scale)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        foreach ($l in $flakeLines) {
            $graphics.DrawLine($pen, $cx + ($l[0] - 30) * $scale, $cy + ($l[1] - 42) * $scale, $cx + ($l[2] - 30) * $scale, $cy + ($l[3] - 42) * $scale)
        }
        $pen.Dispose()
    }
}

function Save-Png($bitmap, [string]$path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "wrote $path"
}

$small = New-Canvas 240 120
$small[1].DrawImage($logo, 0, 0, 240, 120)
Save-Png $small[0] (Join-Path $public "assets\img\logo-small.png")
$small[1].Dispose(); $small[0].Dispose()

$og = New-Canvas 1200 630
$g = $og[1]
$rect = New-Object System.Drawing.Rectangle 0, 0, 1200, 630
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Color "#e4f5fe"), (Color "#fff1f7"), ([float]60)
$blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
$blend.Colors = @((Color "#dff3fe"), (Color "#ffffff"), (Color "#ffeef5"))
$blend.Positions = @([float]0, [float]0.55, [float]1)
$brush.InterpolationColors = $blend
$g.FillRectangle($brush, $rect)
$random = New-Object System.Random 7
for ($i = 0; $i -lt 70; $i++) {
    $x = $random.Next(0, 1200); $y = $random.Next(0, 630); $r = $random.Next(3, 9)
    $dot = New-Object System.Drawing.SolidBrush (Color ($(if ($i % 3 -eq 0) { "#ff8ab4" } else { "#7fd0f5" })) 70)
    $g.FillEllipse($dot, $x, $y, $r, $r)
    $dot.Dispose()
}
Draw-Flake $g 92 96 1.5 150
Draw-Flake $g 1110 540 1.9 150
Draw-Flake $g 1120 110 1.0 110
Draw-Flake $g 80 548 1.1 110
$g.DrawImage($logo, 150, 40, 900, 448)
$font = New-Object System.Drawing.Font "Yu Gothic UI", 34, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$text = "半角/全角 キーは、もう押さなくていい！"
$pill = New-Object System.Drawing.Drawing2D.GraphicsPath
$pw = 700; $ph = 70; $px = (1200 - $pw) / 2; $py = 515
$pill.AddArc($px, $py, $ph, $ph, 90, 180)
$pill.AddArc($px + $pw - $ph, $py, $ph, $ph, 270, 180)
$pill.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush (Color "#7391ee" 140)), $pill)
$g.TranslateTransform(0, -6)
$g.FillPath((New-Object System.Drawing.SolidBrush (Color "#ffffff")), $pill)
$g.DrawPath((New-Object System.Drawing.Pen (Color "#7391ee"), 3), $pill)
$g.ResetTransform()
$g.DrawString($text, $font, (New-Object System.Drawing.SolidBrush (Color "#33415c")), (New-Object System.Drawing.RectangleF 0, ($py + 7), 1200, 60), $format)
Save-Png $og[0] (Join-Path $public "og.png")
$g.Dispose(); $og[0].Dispose()

$touch = New-Canvas 180 180
$tg = $touch[1]
$tg.Clear((Color "#ffffff"))
$tg.FillEllipse((New-Object System.Drawing.SolidBrush (Color "#e6f5fd")), 14, 14, 152, 152)
Draw-Flake $tg 90 90 3.1 255
Save-Png $touch[0] (Join-Path $public "apple-touch-icon.png")
$tg.Dispose(); $touch[0].Dispose()

$logo.Dispose()
