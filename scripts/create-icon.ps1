param([string]$SourcePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/App/Resources'
if (-not $SourcePath) { $SourcePath = Join-Path $output 'NEO-generated.png' }
$source = [Windows.Media.Imaging.BitmapImage]::new()
$source.BeginInit()
$source.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$source.UriSource = [Uri]::new([IO.Path]::GetFullPath($SourcePath))
$source.EndInit()
$source.Freeze()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::HighQuality)
    $draw = $visual.RenderOpen()
    $draw.DrawImage($source, [Windows.Rect]::new(0, 0, $size, $size))
    $draw.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $images += ,($stream.ToArray())
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $output 'NEO.png'), $stream.ToArray()) }
    $stream.Dispose()
}
$file = [IO.File]::Create((Join-Path $output 'NEO.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image) }
}
finally { $writer.Dispose(); $file.Dispose() }
Write-Host 'Generated NEO raster image packaged as PNG and multi-resolution Windows ICO.'
