param(
    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory = "artifacts/visual"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$harnessDir = [System.IO.Path]::GetFullPath("QualitySessionMeter.VisualHarness/bin/Release/net10.0-windows7.0")
$harnessPath = Join-Path $harnessDir "QualitySessionMeter.VisualHarness.dll"
$pluginPath = Join-Path $harnessDir "QualitySessionMeter.dll"

if (-not (Test-Path $harnessPath)) { throw "VisualHarness assembly not found: $harnessPath" }
if (-not (Test-Path $pluginPath)) { throw "Plugin assembly not found beside VisualHarness: $pluginPath" }

$resolveHandler = [System.ResolveEventHandler]{
    param($sender, $eventArgs)
    try {
        $simpleName = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name + ".dll"
        $candidate = Join-Path $harnessDir $simpleName
        if (Test-Path $candidate) { return [System.Reflection.Assembly]::LoadFrom($candidate) }
    } catch { }
    return $null
}
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)

try {
    $harnessAssembly = [System.Reflection.Assembly]::LoadFrom($harnessPath)
    $pluginAssembly = [System.Reflection.Assembly]::LoadFrom($pluginPath)

    if ([System.Windows.Application]::Current -eq $null) {
        $app = [System.Windows.Application]::new()
        $app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
    } else {
        $app = [System.Windows.Application]::Current
    }

    $bindingFlags = [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Public
    $instanceFlags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic

    $programType = $harnessAssembly.GetType("QualitySessionMeter.VisualHarness.Program", $true)
    $installHostResources = $programType.GetMethod("InstallHostResources", $bindingFlags)
    if ($installHostResources -eq $null) { throw "InstallHostResources reflection hook not found." }

    $previewDataType = $harnessAssembly.GetType("QualitySessionMeter.VisualHarness.PreviewData", $true)
    $settingsMethod = $previewDataType.GetMethod("Settings", $bindingFlags)
    $framesMethod = $previewDataType.GetMethod("Frames", $bindingFlags)
    $eventsMethod = $previewDataType.GetMethod("Events", $bindingFlags)

    function Pump-Dispatcher {
        $frame = [System.Windows.Threading.DispatcherFrame]::new()
        $callback = [System.Action]{ $frame.Continue = $false }
        [System.Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke(
            [System.Windows.Threading.DispatcherPriority]::ApplicationIdle,
            $callback
        ) | Out-Null
        [System.Windows.Threading.Dispatcher]::PushFrame($frame)
    }

    function Find-VisualChild {
        param(
            [Parameter(Mandatory = $true)] [System.Windows.DependencyObject]$Root,
            [Parameter(Mandatory = $true)] [Type]$Type
        )
        if ($Type.IsInstanceOfType($Root)) { return $Root }
        $count = 0
        try { $count = [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($Root) } catch { return $null }
        for ($i = 0; $i -lt $count; $i++) {
            $child = [System.Windows.Media.VisualTreeHelper]::GetChild($Root, $i)
            $found = Find-VisualChild -Root $child -Type $Type
            if ($found -ne $null) { return $found }
        }
        return $null
    }

    function Save-VisualPng {
        param(
            [Parameter(Mandatory = $true)] [System.Windows.Media.Visual]$Visual,
            [Parameter(Mandatory = $true)] [int]$Width,
            [Parameter(Mandatory = $true)] [int]$Height,
            [Parameter(Mandatory = $true)] [string]$FileName
        )
        $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
            $Width, $Height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($Visual)
        $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path = Join-Path $OutputDirectory $FileName
        $stream = [System.IO.File]::Create($path)
        try { $encoder.Save($stream) } finally { $stream.Dispose() }
    }

    function New-ReflectedObject {
        param(
            [Parameter(Mandatory = $true)] [string]$TypeName,
            [object[]]$Arguments = @()
        )
        $type = $harnessAssembly.GetType($TypeName, $true)
        return [System.Activator]::CreateInstance($type, $instanceFlags, $null, $Arguments, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    function Set-ReflectedProperty {
        param(
            [Parameter(Mandatory = $true)] [object]$Object,
            [Parameter(Mandatory = $true)] [string]$Name,
            [object]$Value
        )
        $property = $Object.GetType().GetProperty($Name, $instanceFlags)
        if ($property -eq $null) { throw "Property '$Name' not found on $($Object.GetType().FullName)." }
        $property.SetValue($Object, $Value)
    }

    function New-ViewModels {
        $settings = $settingsMethod.Invoke($null, @())
        $frames = $framesMethod.Invoke($null, @())
        $events = $eventsMethod.Invoke($null, @())

        $options = New-ReflectedObject "QualitySessionMeter.VisualHarness.OptionsPreviewVm"
        Set-ReflectedProperty $options "Settings" $settings
        Set-ReflectedProperty $options "WebDashboardAddress" "http://192.168.1.54:18974/"
        Set-ReflectedProperty $options "WebDashboardStatus" "Running · read-only · LAN/VPN"

        $main = New-ReflectedObject "QualitySessionMeter.VisualHarness.MainPreviewVm" @($settings, $frames, $events)
        $lastFrame = $frames[$frames.Count - 1]
        Set-ReflectedProperty $main "CurrentFrame" $lastFrame
        Set-ReflectedProperty $main "ModeText" "ACTIVE REJECT HANDLING · Advanced Sequencer LIGHTs"
        Set-ReflectedProperty $main "SessionFolder" "C:\Users\astro\AppData\Local\NINA\QualitySessionMeter\Sessions\2026-09-08_22-24-00"
        Set-ReflectedProperty $main "ReviewMessage" "Select a rejected frame to review it in N.I.N.A.'s Image view."

        $calibration = New-ReflectedObject "QualitySessionMeter.VisualHarness.CalibrationPreview"
        Set-ReflectedProperty $calibration "Available" $true
        Set-ReflectedProperty $calibration "Reason" "Stable sample window ready."

        $control = New-ReflectedObject "QualitySessionMeter.VisualHarness.ControlCenterPreviewVm"
        Set-ReflectedProperty $control "Settings" $settings
        Set-ReflectedProperty $control "Calibration" $calibration
        Set-ReflectedProperty $control "ActivationText" "ON · ACTIVE FILE HANDLING · Advanced Sequencer LIGHTs"
        Set-ReflectedProperty $control "CalibrationStatus" "READY · 18 stable accepted frames · M31 / L / 180 s / gain 100"
        Set-ReflectedProperty $control "CalibrationValues" "RMS ≤ 1.20`" · excursion 2.20`" · hard 4.50`" · stars -32.0% · background +26.0/-24.0%"
        Set-ReflectedProperty $control "PredictionText" "TRANSPARENCY: gradual degradation likely within 3 frames (83% conf.)"
        Set-ReflectedProperty $control "EnvironmentText" "Humidity 63% · wind 2.1 m/s · no independent hard rejection from environment data."

        return @{ Settings = $settings; Options = $options; Main = $main; Control = $control }
    }

    function Render-TemplateSlices {
        param(
            [Parameter(Mandatory = $true)] [string]$ResourceTypeName,
            [Parameter(Mandatory = $true)] [string]$TemplateKey,
            [Parameter(Mandatory = $true)] [object]$DataContext,
            [Parameter(Mandatory = $true)] [int]$Width,
            [Parameter(Mandatory = $true)] [int]$Height,
            [Parameter(Mandatory = $true)] [string]$BaseName
        )

        $resourceType = $pluginAssembly.GetType($ResourceTypeName, $true)
        $dictionary = [System.Activator]::CreateInstance($resourceType)
        $template = $dictionary[$TemplateKey]
        if ($template -eq $null) { throw "Template '$TemplateKey' not found in $ResourceTypeName." }

        $content = [System.Windows.Controls.ContentControl]::new()
        $content.Content = $DataContext
        $content.ContentTemplate = $template
        $content.HorizontalContentAlignment = [System.Windows.HorizontalAlignment]::Stretch
        $content.VerticalContentAlignment = [System.Windows.VerticalAlignment]::Stretch

        $renderHost = [System.Windows.Controls.Border]::new()
        $renderHost.Width = $Width
        $renderHost.Height = $Height
        $renderHost.Child = $content
        $renderHost.SetResourceReference([System.Windows.Controls.Border]::BackgroundProperty, "BackgroundBrush")

        $window = [System.Windows.Window]::new()
        $window.Width = $Width
        $window.Height = $Height
        $window.Content = $renderHost
        $window.WindowStyle = [System.Windows.WindowStyle]::None
        $window.ResizeMode = [System.Windows.ResizeMode]::NoResize
        $window.ShowInTaskbar = $false
        $window.ShowActivated = $false
        $window.Left = -30000
        $window.Top = -30000
        $window.SizeToContent = [System.Windows.SizeToContent]::Manual
        $window.Show()

        try {
            Pump-Dispatcher
            $renderHost.Measure([System.Windows.Size]::new($Width, $Height))
            $renderHost.Arrange([System.Windows.Rect]::new(0, 0, $Width, $Height))
            $renderHost.UpdateLayout()
            Pump-Dispatcher

            $scroll = Find-VisualChild -Root $content -Type ([System.Windows.Controls.ScrollViewer])
            if ($scroll -eq $null) { throw "No ScrollViewer found in $TemplateKey." }

            $maxOffset = [Math]::Max(0.0, $scroll.ScrollableHeight)
            $offsets = [ordered]@{
                top = 0.0
                middle = $maxOffset / 2.0
                bottom = $maxOffset
            }

            foreach ($entry in $offsets.GetEnumerator()) {
                $scroll.ScrollToVerticalOffset([double]$entry.Value)
                $scroll.UpdateLayout()
                Pump-Dispatcher
                Save-VisualPng -Visual $renderHost -Width $Width -Height $Height -FileName "$BaseName-$($entry.Key).png"
            }
        } finally {
            $window.Close()
            Pump-Dispatcher
        }
    }

    foreach ($dark in @($true, $false)) {
        $installHostResources.Invoke($null, @($dark)) | Out-Null
        $suffix = if ($dark) { "dark" } else { "light" }
        $vms = New-ViewModels

        Render-TemplateSlices `
            -ResourceTypeName "NINA.Plugin.QualitySessionMeter.UI.OptionsHelpResources" `
            -TemplateKey "QualitySessionMeter_Options_Polished" `
            -DataContext $vms.Options -Width 1180 -Height 760 `
            -BaseName "wpf-options-$suffix-slice"

        Render-TemplateSlices `
            -ResourceTypeName "NINA.Plugin.QualitySessionMeter.UI.Resources" `
            -TemplateKey "NINA.Plugin.QualitySessionMeter.UI.QualitySessionMeterDockable_Dockable" `
            -DataContext $vms.Main -Width 1420 -Height 760 `
            -BaseName "wpf-main-panel-$suffix-slice"

        Render-TemplateSlices `
            -ResourceTypeName "NINA.Plugin.QualitySessionMeter.UI.ControlCenterResources" `
            -TemplateKey "NINA.Plugin.QualitySessionMeter.UI.QsmControlCenterDockable_Dockable" `
            -DataContext $vms.Control -Width 1180 -Height 760 `
            -BaseName "wpf-control-center-$suffix-slice"
    }

    Write-Host "Generated WPF scroll-slice evidence in $OutputDirectory"
}
finally {
    [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolveHandler)
}
