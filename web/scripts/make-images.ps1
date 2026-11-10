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

function Save-Png($bitmap, [string]$path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "wrote $path"
}

$small = New-Canvas 240 120
$small[1].DrawImage($logo, 0, 0, 240, 120)
Save-Png $small[0] (Join-Path $public "assets\img\logo-small.png")
$small[1].Dispose(); $small[0].Dispose()

# リンクを貼ったときの画像 (public/og.png) は、丸ゴシックの文字で描くために scripts/make-images.mjs で作る

# apple-touch-icon.png とアプリのアイコンは、public/favicon.svg から scripts/make-icons.mjs で作る

$logo.Dispose()
