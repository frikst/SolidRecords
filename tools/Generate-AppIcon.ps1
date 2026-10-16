$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assetDirectory = Join-Path $PSScriptRoot '../src/SolidRecords.App/Assets'
$previewDirectory = Join-Path $PSScriptRoot '../artifacts/icon-preview'
New-Item -ItemType Directory -Force $previewDirectory | Out-Null
$source = [System.Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $assetDirectory 'SolidRecords.xaml')))
# Explicit frames cover the tray, 150% DPI, taskbar and Explorer without upscaling.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    # Keep the transparent margin from the original 32-unit design grid.
    $drawing.DrawImage($source, [System.Windows.Rect]::new($size / 32.0, $size / 32.0, $size * 30 / 32.0, $size * 30 / 32.0))
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    [IO.File]::WriteAllBytes((Join-Path $previewDirectory "icon-$size.png"), $bytes)
    $frames += ,$bytes
}

$output = [IO.File]::Create((Join-Path $assetDirectory 'SolidRecords.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($bytes in $frames) { $writer.Write([byte[]]$bytes) }
}
finally { $writer.Dispose() }
Write-Output "Created SolidRecords.ico: $($sizes -join ', ') px. Previews: $previewDirectory"
