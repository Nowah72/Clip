# PowerShell script to convert PNG to ICO format
# This uses .NET libraries to create a proper multi-resolution ICO file

Add-Type -AssemblyName System.Drawing

$pngPath = Join-Path $PSScriptRoot "app_icon.png"
$icoPath = Join-Path $PSScriptRoot "app_icon.ico"

if (-not (Test-Path $pngPath)) {
    Write-Error "app_icon.png not found. Please run create_icon.ps1 first."
    exit 1
}

# Load the PNG
$originalImage = [System.Drawing.Image]::FromFile($pngPath)

# Create multiple sizes for the ICO
$sizes = @(256, 128, 64, 48, 32, 16)
$icons = @()

foreach ($size in $sizes) {
    $resizedBitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($resizedBitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($originalImage, 0, 0, $size, $size)
    $graphics.Dispose()

    $icons += $resizedBitmap
}

# Save as ICO
$memoryStream = New-Object System.IO.MemoryStream
$binaryWriter = New-Object System.IO.BinaryWriter($memoryStream)

# ICO header
$binaryWriter.Write([UInt16]0) # Reserved
$binaryWriter.Write([UInt16]1) # Image type (1 = icon)
$binaryWriter.Write([UInt16]$icons.Count) # Number of images

$offset = 6 + ($icons.Count * 16) # Header + directory entries

# Write directory entries
foreach ($icon in $icons) {
    $ms = New-Object System.IO.MemoryStream
    $icon.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData = $ms.ToArray()
    $ms.Dispose()

    $width = if ($icon.Width -eq 256) { 0 } else { $icon.Width }
    $height = if ($icon.Height -eq 256) { 0 } else { $icon.Height }
    $binaryWriter.Write([Byte]$width)
    $binaryWriter.Write([Byte]$height)
    $binaryWriter.Write([Byte]0) # Color palette
    $binaryWriter.Write([Byte]0) # Reserved
    $binaryWriter.Write([UInt16]1) # Color planes
    $binaryWriter.Write([UInt16]32) # Bits per pixel
    $binaryWriter.Write([UInt32]$imageData.Length)
    $binaryWriter.Write([UInt32]$offset)

    $offset += $imageData.Length
}

# Write image data
foreach ($icon in $icons) {
    $ms = New-Object System.IO.MemoryStream
    $icon.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData = $ms.ToArray()
    $binaryWriter.Write($imageData)
    $ms.Dispose()
    $icon.Dispose()
}

# Save to file
$icoData = $memoryStream.ToArray()
[System.IO.File]::WriteAllBytes($icoPath, $icoData)

$binaryWriter.Dispose()
$memoryStream.Dispose()
$originalImage.Dispose()

Write-Host "ICO file created successfully at: $icoPath"
Write-Host "You can now rebuild the project to use the new icon."
