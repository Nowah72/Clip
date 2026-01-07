# PowerShell script to create a simple ICO file for Clip application
# This creates a 256x256 PNG and converts it to ICO format

Add-Type -AssemblyName System.Drawing

# Create a 256x256 bitmap
$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)

# Enable anti-aliasing
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Create gradient background (purple to pink)
$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$colorStart = [System.Drawing.Color]::FromArgb(168, 85, 247)  # #A855F7
$colorEnd = [System.Drawing.Color]::FromArgb(236, 72, 153)    # #EC4899
$gradientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $colorStart, $colorEnd, 45)

# Draw rounded rectangle background
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$radius = 48
$path.AddArc(0, 0, $radius, $radius, 180, 90)
$path.AddArc($size - $radius, 0, $radius, $radius, 270, 90)
$path.AddArc($size - $radius, $size - $radius, $radius, $radius, 0, 90)
$path.AddArc(0, $size - $radius, $radius, $radius, 90, 90)
$path.CloseFigure()
$graphics.FillPath($gradientBrush, $path)

# Draw clipboard icon in white
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 18)
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Clipboard body (main rectangle)
$clipboardRect = New-Object System.Drawing.Rectangle(70, 80, 116, 140)
$graphics.DrawRectangle($pen, $clipboardRect)

# Clipboard top clip
$clipPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$clipRect = New-Object System.Drawing.RectangleF(100, 50, 56, 40)
$clipPath.AddArc($clipRect, 180, 180)
$fillBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$graphics.FillPath($fillBrush, $clipPath)

# Checkmark inside clipboard
$checkPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 16)
$checkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$checkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$checkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$checkPoints = @(
    (New-Object System.Drawing.Point(90, 145)),
    (New-Object System.Drawing.Point(115, 170)),
    (New-Object System.Drawing.Point(165, 120))
)
$graphics.DrawLines($checkPen, $checkPoints)

# Save as PNG first
$pngPath = Join-Path $PSScriptRoot "app_icon.png"
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Clean up
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "PNG icon created at: $pngPath"
Write-Host ""
Write-Host "To convert to ICO format, you have two options:"
Write-Host "1. Use an online converter like https://convertio.co/png-ico/"
Write-Host "2. Use ImageMagick: magick convert app_icon.png -define icon:auto-resize=256,128,64,48,32,16 app_icon.ico"
Write-Host ""
Write-Host "Once you have app_icon.ico, place it in the project root and rebuild."
