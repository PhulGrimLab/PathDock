<#
.SYNOPSIS
    PathDock 앱 아이콘(Assets\PathDock.ico, PathDock.png)을 생성한다. 재생성이 필요할 때만 실행한다(산출물은 저장소에 포함됨).
.DESCRIPTION
    둥근 사각형 파란 그라데이션 위에 폴더 윤곽 + 터미널 프롬프트(>_)를 그린 로고를 여러 크기의 PNG로 렌더링해
    PNG 압축 방식 ICO(256까지)로 묶는다. System.Drawing만 사용한다.
#>
[CmdletBinding()]
param([string]$OutDir)
$ErrorActionPreference = "Stop"
if (-not $OutDir) { $OutDir = Join-Path (Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) "src") "PathDock.App") "Assets" }

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 배경: 둥근 사각형 + 대각선 그라데이션
    $m = [math]::Max(1, $size * 0.03)
    $r = $size * 0.22
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 0x2F, 0x80, 0xED)
    $c2 = [System.Drawing.Color]::FromArgb(255, 0x5B, 0x3D, 0xD8)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $c1, $c2, 45.0
    $g.FillPath($brush, $path)

    # 폴더 윤곽(흰색): 탭이 달린 폴더
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([math]::Max(1.2, $size * 0.055))
    $pen.LineJoin = 'Round'
    $x = $size * 0.2; $y = $size * 0.28; $w = $size * 0.6; $h = $size * 0.46
    $tab = $size * 0.22
    $pts = @(
        (New-Object System.Drawing.PointF $x, ($y + $h)),
        (New-Object System.Drawing.PointF $x, $y),
        (New-Object System.Drawing.PointF ($x + $tab), $y),
        (New-Object System.Drawing.PointF ($x + $tab + $size * 0.06), ($y + $size * 0.08)),
        (New-Object System.Drawing.PointF ($x + $w), ($y + $size * 0.08)),
        (New-Object System.Drawing.PointF ($x + $w), ($y + $h))
    )
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)

    # 터미널 프롬프트 ">_"
    $fs = [math]::Max(6, $size * 0.26)
    $font = New-Object System.Drawing.Font "Consolas", $fs, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF $x, ($y + $size * 0.1), $w, ($h - $size * 0.08)
    $g.DrawString(">_", $font, [System.Drawing.Brushes]::White, $textRect, $sf)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Logo $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir "PathDock.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO 컨테이너: 헤더(6) + 항목(16 x N) + PNG 데이터. BinaryWriter 대신 바이트 리스트로 조립한다.
$bytes = [System.Collections.Generic.List[byte]]::new()
function Add-U16([int]$v) { $script:bytes.AddRange([System.BitConverter]::GetBytes([uint16]$v)) }
function Add-U32([long]$v) { $script:bytes.AddRange([System.BitConverter]::GetBytes([uint32]$v)) }
Add-U16 0; Add-U16 1; Add-U16 $pngs.Count
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bytes.Add([byte]$dim); $bytes.Add([byte]$dim); $bytes.Add([byte]0); $bytes.Add([byte]0)
    Add-U16 1; Add-U16 32
    Add-U32 $p.Bytes.Length; Add-U32 $offset
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bytes.AddRange([byte[]]$p.Bytes) }
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "PathDock.ico"), $bytes.ToArray())
Write-Host "생성 완료: $OutDir"
