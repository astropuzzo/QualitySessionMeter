using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QualitySessionMeter.VisualSlices;

internal static class Program {
    private static string outputDirectory = "visual-output";
    private static string evidenceAssemblyDirectory = "";
    private static Assembly harnessAssembly;
    private static Assembly pluginAssembly;
    private static MethodInfo installHostResources;
    private static MethodInfo settingsMethod;
    private static MethodInfo framesMethod;
    private static MethodInfo eventsMethod;
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    public static void Main(string[] args) {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? "visual-output");
        Directory.CreateDirectory(outputDirectory);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            InitializeReflection();
            RenderTheme(dark: true);
            RenderTheme(dark: false);
        } finally {
            app.Shutdown();
        }
    }

    private static void InitializeReflection() {
        evidenceAssemblyDirectory = Path.GetFullPath(
            Path.Combine("QualitySessionMeter.VisualHarness", "bin", "Release", "net10.0-windows7.0"));
        var harnessPath = Path.Combine(evidenceAssemblyDirectory, "QualitySessionMeter.VisualHarness.dll");
        var pluginPath = Path.Combine(evidenceAssemblyDirectory, "QualitySessionMeter.dll");
        if (!File.Exists(harnessPath) || !File.Exists(pluginPath))
            throw new FileNotFoundException("VisualHarness evidence assemblies were not built before the slice renderer.");

        AssemblyLoadContext.Default.Resolving += ResolveEvidenceAssembly;
        pluginAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
        harnessAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(harnessPath);

        var staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var programType = harnessAssembly.GetType("QualitySessionMeter.VisualHarness.Program", throwOnError: true)!;
        installHostResources = programType.GetMethod("InstallHostResources", staticFlags)
            ?? throw new InvalidOperationException("VisualHarness.InstallHostResources not found.");

        var previewDataType = harnessAssembly.GetType("QualitySessionMeter.VisualHarness.PreviewData", throwOnError: true)!;
        settingsMethod = previewDataType.GetMethod("Settings", staticFlags)
            ?? throw new InvalidOperationException("PreviewData.Settings not found.");
        framesMethod = previewDataType.GetMethod("Frames", staticFlags)
            ?? throw new InvalidOperationException("PreviewData.Frames not found.");
        eventsMethod = previewDataType.GetMethod("Events", staticFlags)
            ?? throw new InvalidOperationException("PreviewData.Events not found.");
    }

    private static Assembly? ResolveEvidenceAssembly(AssemblyLoadContext context, AssemblyName name) {
        var candidate = Path.Combine(evidenceAssemblyDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

    private static void RenderTheme(bool dark) {
        installHostResources.Invoke(null, new object[] { dark });
        var suffix = dark ? "dark" : "light";
        var vms = CreateViewModels();

        RenderTemplateSlices(
            "NINA.Plugin.QualitySessionMeter.UI.OptionsHelpResources",
            "QualitySessionMeter_Options_Polished",
            vms.Options,
            1180,
            760,
            $"wpf-options-{suffix}-slice");

        RenderTemplateSlices(
            "NINA.Plugin.QualitySessionMeter.UI.Resources",
            "NINA.Plugin.QualitySessionMeter.UI.QualitySessionMeterDockable_Dockable",
            vms.Main,
            1420,
            760,
            $"wpf-main-panel-{suffix}-slice");

        RenderTemplateSlices(
            "NINA.Plugin.QualitySessionMeter.UI.ControlCenterResources",
            "NINA.Plugin.QualitySessionMeter.UI.QsmControlCenterDockable_Dockable",
            vms.Control,
            1180,
            760,
            $"wpf-control-center-{suffix}-slice");
    }

    private static PreviewViewModels CreateViewModels() {
        var settings = settingsMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("Preview settings could not be created.");
        var frames = framesMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("Preview frames could not be created.");
        var events = eventsMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("Preview events could not be created.");

        var options = NewObject("QualitySessionMeter.VisualHarness.OptionsPreviewVm");
        SetProperty(options, "Settings", settings);
        SetProperty(options, "WebDashboardAddress", "http://192.168.1.54:18974/");
        SetProperty(options, "WebDashboardStatus", "Running · read-only · LAN/VPN");

        var main = NewObject("QualitySessionMeter.VisualHarness.MainPreviewVm", settings, frames, events);
        if (frames is not IList frameList || frameList.Count == 0)
            throw new InvalidOperationException("Preview frame list is empty.");
        SetProperty(main, "CurrentFrame", frameList[frameList.Count - 1]);
        SetProperty(main, "ModeText", "ACTIVE REJECT HANDLING · Advanced Sequencer LIGHTs");
        SetProperty(main, "SessionFolder", @"C:\Users\astro\AppData\Local\NINA\QualitySessionMeter\Sessions\2026-09-08_22-24-00");
        SetProperty(main, "ReviewMessage", "Select a rejected frame to review it in N.I.N.A.'s Image view.");

        var calibration = NewObject("QualitySessionMeter.VisualHarness.CalibrationPreview");
        SetProperty(calibration, "Available", true);
        SetProperty(calibration, "Reason", "Stable sample window ready.");

        var control = NewObject("QualitySessionMeter.VisualHarness.ControlCenterPreviewVm");
        SetProperty(control, "Settings", settings);
        SetProperty(control, "Calibration", calibration);
        SetProperty(control, "ActivationText", "ON · ACTIVE FILE HANDLING · Advanced Sequencer LIGHTs");
        SetProperty(control, "CalibrationStatus", "READY · 18 stable accepted frames · M31 / L / 180 s / gain 100");
        SetProperty(control, "CalibrationValues", "RMS ≤ 1.20\" · excursion 2.20\" · hard 4.50\" · stars -32.0% · background +26.0/-24.0%");
        SetProperty(control, "PredictionText", "TRANSPARENCY: gradual degradation likely within 3 frames (83% conf.)");
        SetProperty(control, "EnvironmentText", "Humidity 63% · wind 2.1 m/s · no independent hard rejection from environment data.");

        return new PreviewViewModels(options, main, control);
    }

    private static object NewObject(string typeName, params object[] args) {
        var type = harnessAssembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(
            type,
            InstanceFlags,
            binder: null,
            args: args,
            culture: CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"Could not instantiate {typeName}.");
    }

    private static void SetProperty(object instance, string propertyName, object? value) {
        var property = instance.GetType().GetProperty(propertyName, InstanceFlags)
            ?? throw new InvalidOperationException($"Property {propertyName} not found on {instance.GetType().FullName}.");
        property.SetValue(instance, value);
    }

    private static void RenderTemplateSlices(
        string resourceTypeName,
        string templateKey,
        object dataContext,
        int width,
        int height,
        string baseName) {

        var resourceType = pluginAssembly.GetType(resourceTypeName, throwOnError: true)!;
        if (Activator.CreateInstance(resourceType) is not ResourceDictionary dictionary)
            throw new InvalidOperationException($"Could not instantiate {resourceTypeName}.");
        if (dictionary[templateKey] is not DataTemplate template)
            throw new InvalidOperationException($"Template {templateKey} not found.");

        var content = new ContentControl {
            Content = dataContext,
            ContentTemplate = template,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Width = width,
            Height = height
        };

        var renderHost = new Border {
            Width = width,
            Height = height,
            Child = content
        };
        renderHost.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");

        var window = new Window {
            Width = width,
            Height = height,
            Content = renderHost,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -30000,
            Top = -30000,
            SizeToContent = SizeToContent.Manual
        };

        window.Show();
        try {
            PumpDispatcher();
            renderHost.Measure(new Size(width, height));
            renderHost.Arrange(new Rect(0, 0, width, height));
            renderHost.UpdateLayout();
            PumpDispatcher();

            var scrollViewer = FindVisualChild<ScrollViewer>(content)
                ?? throw new InvalidOperationException($"No ScrollViewer found in {templateKey}.");

            var maxOffset = Math.Max(0, scrollViewer.ScrollableHeight);
            var positions = new[] {
                (Name: "top", Offset: 0.0),
                (Name: "middle", Offset: maxOffset / 2.0),
                (Name: "bottom", Offset: maxOffset)
            };

            foreach (var position in positions) {
                scrollViewer.ScrollToVerticalOffset(position.Offset);
                scrollViewer.UpdateLayout();
                PumpDispatcher();
                SaveVisualPng(renderHost, width, height, $"{baseName}-{position.Name}.png");
            }
        } finally {
            window.Close();
            PumpDispatcher();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject {
        if (root is T match) return match;
        int count;
        try {
            count = VisualTreeHelper.GetChildrenCount(root);
        } catch {
            return null;
        }

        for (var i = 0; i < count; i++) {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static void SaveVisualPng(Visual visual, int width, int height, string fileName) {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(outputDirectory, fileName));
        encoder.Save(stream);
    }

    private static void PumpDispatcher() {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed record PreviewViewModels(object Options, object Main, object Control);
}
