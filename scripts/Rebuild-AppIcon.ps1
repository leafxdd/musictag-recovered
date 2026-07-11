[CmdletBinding()]
param(
    [string] $IconPath = 'src\MusicTag\app.ico'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedIconPath = (Resolve-Path -LiteralPath $IconPath).Path
$iconBytes = [IO.File]::ReadAllBytes($resolvedIconPath)
$entryCount = [BitConverter]::ToUInt16($iconBytes, 4)
if ($entryCount -le 0) {
    throw 'The icon contains no image frames.'
}

$entries = for ($index = 0; $index -lt $entryCount; $index++) {
    $entryOffset = 6 + (16 * $index)
    $width = if ($iconBytes[$entryOffset] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset] }
    $height = if ($iconBytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$iconBytes[$entryOffset + 1] }
    [pscustomobject]@{
        Width = $width
        Height = $height
        BitsPerPixel = [BitConverter]::ToUInt16($iconBytes, $entryOffset + 6)
        ByteCount = [int][BitConverter]::ToUInt32($iconBytes, $entryOffset + 8)
        ImageOffset = [int][BitConverter]::ToUInt32($iconBytes, $entryOffset + 12)
    }
}

$masterEntry = $entries |
    Where-Object { $_.BitsPerPixel -eq 32 } |
    Sort-Object { $_.Width * $_.Height } -Descending |
    Select-Object -First 1
if ($null -eq $masterEntry) {
    throw 'The icon has no 32-bit source frame.'
}
$masterIsPng = $iconBytes[$masterEntry.ImageOffset] -eq 0x89 -and
    $iconBytes[$masterEntry.ImageOffset + 1] -eq 0x50 -and
    $iconBytes[$masterEntry.ImageOffset + 2] -eq 0x4e -and
    $iconBytes[$masterEntry.ImageOffset + 3] -eq 0x47
$masterPngBytes = $null
if ($masterIsPng) {
    $masterPngBytes = New-Object byte[] $masterEntry.ByteCount
    [Array]::Copy($iconBytes, $masterEntry.ImageOffset, $masterPngBytes, 0, $masterEntry.ByteCount)
}

function New-BitmapFromIconEntry {
    param($Entry, [byte[]] $Bytes)

    $isPng = $Bytes[$Entry.ImageOffset] -eq 0x89 -and
        $Bytes[$Entry.ImageOffset + 1] -eq 0x50 -and
        $Bytes[$Entry.ImageOffset + 2] -eq 0x4e -and
        $Bytes[$Entry.ImageOffset + 3] -eq 0x47
    if ($isPng) {
        $pngBytes = New-Object byte[] $Entry.ByteCount
        [Array]::Copy($Bytes, $Entry.ImageOffset, $pngBytes, 0, $Entry.ByteCount)
        $stream = [IO.MemoryStream]::new($pngBytes, $false)
        try {
            $image = [Drawing.Image]::FromStream($stream)
            try {
                return [Drawing.Bitmap]::new($image)
            }
            finally {
                $image.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }

    $headerSize = [int][BitConverter]::ToUInt32($Bytes, $Entry.ImageOffset)
    if ($headerSize -lt 40 -or $Entry.BitsPerPixel -ne 32) {
        throw 'The largest icon frame is not a supported 32-bit DIB.'
    }
    $bitmap = [Drawing.Bitmap]::new($Entry.Width, $Entry.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixelOffset = $Entry.ImageOffset + $headerSize
    for ($y = 0; $y -lt $Entry.Height; $y++) {
        $sourceY = $Entry.Height - 1 - $y
        for ($x = 0; $x -lt $Entry.Width; $x++) {
            $sourceOffset = $pixelOffset + (($sourceY * $Entry.Width + $x) * 4)
            $color = [Drawing.Color]::FromArgb(
                $Bytes[$sourceOffset + 3],
                $Bytes[$sourceOffset + 2],
                $Bytes[$sourceOffset + 1],
                $Bytes[$sourceOffset])
            $bitmap.SetPixel($x, $y, $color)
        }
    }
    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([Drawing.Bitmap] $Source, [int] $Size)

    $target = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($target)
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.DrawImage($Source, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
        }
        finally {
            $graphics.Dispose()
        }
        $stream = [IO.MemoryStream]::new()
        try {
            $target.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            Write-Output -NoEnumerate $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $target.Dispose()
    }
}

$masterBitmap = New-BitmapFromIconEntry $masterEntry $iconBytes
try {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 256)
    $frames = foreach ($size in $sizes) {
        [byte[]] $pngBytes = if ($size -eq $masterEntry.Width -and $size -eq $masterEntry.Height -and $masterPngBytes) {
            $masterPngBytes
        }
        else {
            Convert-BitmapToPngBytes $masterBitmap $size
        }
        [pscustomobject]@{
            Size = $size
            Bytes = $pngBytes
        }
    }

    $output = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $imageOffset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$imageOffset)
            $imageOffset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
        $writer.Flush()
        [IO.File]::WriteAllBytes($resolvedIconPath, $output.ToArray())
    }
    finally {
        $writer.Dispose()
        $output.Dispose()
    }
}
finally {
    $masterBitmap.Dispose()
}

Write-Host "Rebuilt $resolvedIconPath with alpha-preserving PNG frames: $($sizes -join ', ')"
