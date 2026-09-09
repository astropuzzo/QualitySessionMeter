param(
    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "artifacts/store/featured.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$fullPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($fullPath)) | Out-Null

$size = 512
$visual = [System.Windows.Media.DrawingVisual]::new()
$dc = $visual.RenderOpen()
try {
    $background = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#171A20"))
    $accent = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString("#8AB4F8"))
    $background.Freeze()
    $accent.Freeze()

    $dc.DrawRoundedRectangle($background, $null, [System.Windows.Rect]::new(8, 8, 496, 496), 72, 72)

    $geometry = [System.Windows.Media.GeometryGroup]::new()
    $geometry.Children.Add([System.Windows.Media.EllipseGeometry]::new([System.Windows.Point]::new(32, 32), 29, 29))
    $geometry.Children.Add([System.Windows.Media.Geometry]::Parse("M 10,38 C 16,19 28,13 32,13 C 42,13 51,22 55,35"))
    $geometry.Children.Add([System.Windows.Media.Geometry]::Parse("M 32,32 L 47,22"))

    $transform = [System.Windows.Media.TransformGroup]::new()
    $transform.Children.Add([System.Windows.Media.ScaleTransform]::new(6.35, 6.35))
    $transform.Children.Add([System.Windows.Media.TranslateTransform]::new(52.8, 52.8))
    $geometry.Transform = $transform
    $geometry.Freeze()

    $pen = [System.Windows.Media.Pen]::new($accent, 13)
    $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $pen.Freeze()
    $dc.DrawGeometry($null, $pen, $geometry)
} finally {
    $dc.Close()
}

$bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [System.IO.File]::Create($fullPath)
try { $encoder.Save($stream) } finally { $stream.Dispose() }

Write-Host "Generated $fullPath"
