param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml, System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

# 三個變體：A 藍底、右柱石墨青、單向粗箭頭（推薦）；B 藍底、全白、雙向箭頭；C 石墨底、青箭頭
function New-IconXaml {
    param([string]$Variant, [bool]$Detail, [bool]$Tiny = $false)

    switch ($Variant) {
        'A' { $bgTop = '#2489E5'; $bgBottom = '#0A57AD'; $left = '#FFFFFF'; $right = '#2DD4BF'; $arrow = '#FFFFFF'; $slot = '#0A57AD'; $rim = $null }
        'B' { $bgTop = '#2489E5'; $bgBottom = '#0A57AD'; $left = '#FFFFFF'; $right = '#FFFFFF'; $arrow = '#FFFFFF'; $slot = '#0A57AD'; $rim = $null }
        'C' { $bgTop = '#2E353D'; $bgBottom = '#1A1D21'; $left = '#E6E8EB'; $right = '#E6E8EB'; $arrow = '#2DD4BF'; $slot = '#1A1D21'; $rim = '#4A535E' }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<DrawingImage xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><DrawingImage.Drawing><DrawingGroup>')

    # 底：圓角方形漸層
    [void]$sb.Append("<GeometryDrawing><GeometryDrawing.Brush><LinearGradientBrush StartPoint='0,0' EndPoint='0,1'><GradientStop Color='$bgTop' Offset='0'/><GradientStop Color='$bgBottom' Offset='1'/></LinearGradientBrush></GeometryDrawing.Brush><GeometryDrawing.Geometry><RectangleGeometry Rect='0,0,256,256' RadiusX='58' RadiusY='58'/></GeometryDrawing.Geometry></GeometryDrawing>")
    if ($rim) {
        [void]$sb.Append("<GeometryDrawing><GeometryDrawing.Pen><Pen Brush='$rim' Thickness='6'/></GeometryDrawing.Pen><GeometryDrawing.Geometry><RectangleGeometry Rect='3,3,250,250' RadiusX='55' RadiusY='55'/></GeometryDrawing.Geometry></GeometryDrawing>")
    }

    if ($Tiny) {
        # 16px 專用：座標全部落在 16 的倍數，縮到 16px 時剛好對齊像素格
        [void]$sb.Append("<GeometryDrawing Brush='$left'><GeometryDrawing.Geometry><RectangleGeometry Rect='32,48,48,160' RadiusX='12' RadiusY='12'/></GeometryDrawing.Geometry></GeometryDrawing>")
        [void]$sb.Append("<GeometryDrawing Brush='$right'><GeometryDrawing.Geometry><RectangleGeometry Rect='176,48,48,160' RadiusX='12' RadiusY='12'/></GeometryDrawing.Geometry></GeometryDrawing>")
        if ($Variant -eq 'B') {
            [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Geometry><PathGeometry Figures='M80,80 H128 V64 L160,96 L128,128 V112 H80 Z'/></GeometryDrawing.Geometry></GeometryDrawing>")
            [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Geometry><PathGeometry Figures='M176,144 H128 V128 L96,160 L128,192 V176 H176 Z'/></GeometryDrawing.Geometry></GeometryDrawing>")
        }
        else {
            [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Geometry><PathGeometry Figures='M80,112 H128 V80 L168,128 L128,176 V144 H80 Z'/></GeometryDrawing.Geometry></GeometryDrawing>")
        }
        [void]$sb.Append('</DrawingGroup></DrawingImage.Drawing></DrawingImage>')
        return $sb.ToString()
    }

    # 左右柱體（DA / UA 伺服器）
    [void]$sb.Append("<GeometryDrawing Brush='$left'><GeometryDrawing.Geometry><RectangleGeometry Rect='40,60,38,136' RadiusX='11' RadiusY='11'/></GeometryDrawing.Geometry></GeometryDrawing>")
    [void]$sb.Append("<GeometryDrawing Brush='$right'><GeometryDrawing.Geometry><RectangleGeometry Rect='178,60,38,136' RadiusX='11' RadiusY='11'/></GeometryDrawing.Geometry></GeometryDrawing>")

    if ($Detail) {
        # 柱體上的插槽（大尺寸才畫）
        foreach ($x in 50, 188) {
            foreach ($y in 78, 96) {
                [void]$sb.Append("<GeometryDrawing Brush='$slot' ><GeometryDrawing.Geometry><RectangleGeometry Rect='$x,$y,18,8' RadiusX='4' RadiusY='4'/></GeometryDrawing.Geometry></GeometryDrawing>")
            }
        }
    }

    # 箭頭
    if ($Variant -eq 'B') {
        $top = 'M90,94 H134 V80 L170,104 L134,128 V114 H90 Z'
        $bottom = 'M166,142 H122 V128 L86,152 L122,176 V162 H166 Z'
        [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Pen><Pen Brush='$arrow' Thickness='6' LineJoin='Round'/></GeometryDrawing.Pen><GeometryDrawing.Geometry><PathGeometry Figures='$top'/></GeometryDrawing.Geometry></GeometryDrawing>")
        [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Pen><Pen Brush='$arrow' Thickness='6' LineJoin='Round'/></GeometryDrawing.Pen><GeometryDrawing.Geometry><PathGeometry Figures='$bottom'/></GeometryDrawing.Geometry></GeometryDrawing>")
    }
    else {
        $path = 'M90,114 H136 V92 L172,128 L136,164 V142 H90 Z'
        [void]$sb.Append("<GeometryDrawing Brush='$arrow'><GeometryDrawing.Pen><Pen Brush='$arrow' Thickness='6' LineJoin='Round'/></GeometryDrawing.Pen><GeometryDrawing.Geometry><PathGeometry Figures='$path'/></GeometryDrawing.Geometry></GeometryDrawing>")
    }

    [void]$sb.Append('</DrawingGroup></DrawingImage.Drawing></DrawingImage>')
    return $sb.ToString()
}

function Render-Png {
    param([System.Windows.Media.ImageSource]$Image, [int]$Size, [string]$Path)
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.DrawImage($Image, (New-Object System.Windows.Rect 0, 0, $Size, $Size))
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.File]::Create($Path)
    try { $encoder.Save($stream) } finally { $stream.Close() }
}

function Write-Ico {
    param([string[]]$PngPaths, [string]$IcoPath)
    $entries = @()
    foreach ($png in $PngPaths) {
        $bmp = [System.Drawing.Bitmap]::FromFile($png)
        $size = $bmp.Width
        if ($size -ge 256) {
            $data = [System.IO.File]::ReadAllBytes($png)
        }
        else {
            # 傳統 BMP 格式：BITMAPINFOHEADER + 由下而上的 BGRA + AND 遮罩
            $ms = New-Object System.IO.MemoryStream
            $w = New-Object System.IO.BinaryWriter $ms
            $w.Write([int32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
            $w.Write([int32]0); $w.Write([int32]($size * $size * 4)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([int32]0)
            for ($y = $size - 1; $y -ge 0; $y--) {
                for ($x = 0; $x -lt $size; $x++) {
                    $c = $bmp.GetPixel($x, $y)
                    $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
                }
            }
            $maskRow = [int](([math]::Ceiling($size / 8) + 3) / 4) * 4
            $w.Write((New-Object byte[] ($maskRow * $size)))
            $w.Flush()
            $data = $ms.ToArray()
            $w.Close()
        }
        $bmp.Dispose()
        $entries += [pscustomobject]@{ Size = $size; Data = $data }
    }

    $fs = [System.IO.File]::Create($IcoPath)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int32]$e.Data.Length); $bw.Write([int32]$offset)
        $offset += $e.Data.Length
    }
    foreach ($e in $entries) { $bw.Write($e.Data) }
    $bw.Close()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
foreach ($variant in 'A', 'B', 'C') {
    $full = [System.Windows.Markup.XamlReader]::Parse((New-IconXaml -Variant $variant -Detail $true))
    $simple = [System.Windows.Markup.XamlReader]::Parse((New-IconXaml -Variant $variant -Detail $false))
    $tiny = [System.Windows.Markup.XamlReader]::Parse((New-IconXaml -Variant $variant -Detail $false -Tiny $true))
    $pngs = @()
    foreach ($size in $sizes) {
        $image = if ($size -ge 48) { $full } elseif ($size -ge 24) { $simple } else { $tiny }
        $path = Join-Path $OutDir ("icon{0}-{1}.png" -f $variant, $size)
        Render-Png -Image $image -Size $size -Path $path
        if ($size -in 16, 24, 32, 48, 64, 128, 256) { $pngs += $path }
    }
    Write-Ico -PngPaths $pngs -IcoPath (Join-Path $OutDir ("icon{0}.ico" -f $variant))
}

# 預覽圖：每列一個變體，左 128px，接著淺底與深底各 48/32/16，最後 16px 放大 4 倍
$rowH = 190; $width = 980
$preview = New-Object System.Drawing.Bitmap $width, ($rowH * 3 + 20)
$g = [System.Drawing.Graphics]::FromImage($preview)
$g.Clear([System.Drawing.Color]::FromArgb(255, 243, 243, 243))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$font = New-Object System.Drawing.Font 'Microsoft JhengHei UI', 13
$small = New-Object System.Drawing.Font 'Microsoft JhengHei UI', 9
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 30, 30))
$labels = @{ A = '方案 A：藍底、右柱石墨青、單向箭頭（推薦）'; B = '方案 B：藍底、全白、雙向箭頭'; C = '方案 C：石墨底、青箭頭' }
$row = 0
foreach ($variant in 'A', 'B', 'C') {
    $top = 10 + $row * $rowH
    $g.DrawString($labels[$variant], $font, $textBrush, 16, $top)
    $y = $top + 34
    $img128 = [System.Drawing.Image]::FromFile((Join-Path $OutDir "icon$variant-128.png"))
    $g.DrawImage($img128, 16, $y, 128, 128)
    # 淺底條（像檔案總管）與深底條（像工作列）
    $lightRect = New-Object System.Drawing.Rectangle 170, $y, 250, 128
    $darkRect = New-Object System.Drawing.Rectangle 440, $y, 250, 128
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $lightRect)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), $darkRect)
    foreach ($base in 170, 440) {
        $x = $base + 16
        foreach ($size in 48, 32, 16) {
            $img = [System.Drawing.Image]::FromFile((Join-Path $OutDir "icon$variant-$size.png"))
            $g.DrawImage($img, $x, ($y + 64 - [int]($size / 2)), $size, $size)
            $x += $size + 28
        }
    }
    $g.DrawString('檔案總管 48 / 32 / 16', $small, $textBrush, 170, ($y + 130))
    $g.DrawString('工作列 48 / 32 / 16', $small, $textBrush, 440, ($y + 130))
    $img16 = [System.Drawing.Image]::FromFile((Join-Path $OutDir "icon$variant-16.png"))
    $g.DrawImage($img16, 720, ($y + 32), 64, 64)
    $img32 = [System.Drawing.Image]::FromFile((Join-Path $OutDir "icon$variant-32.png"))
    $g.DrawImage($img32, 810, ($y + 32), 64, 64)
    $g.DrawString('16px ×4      32px ×2', $small, $textBrush, 720, ($y + 100))
    $row++
}
$g.Dispose()
$preview.Save((Join-Path $OutDir 'preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
Write-Output "done: $OutDir"
