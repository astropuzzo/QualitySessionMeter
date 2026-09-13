using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.UI;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QualitySessionMeter.VisualHarness;

internal static class Program {
    private static ObservableCollection<FrameQualityResult> replayFrames;
    private static string outputDirectory = "visual-output";

    [STAThread]
    public static void Main(string[] args) {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? "visual-output");
        Directory.CreateDirectory(outputDirectory);
        if (args.Length > 1) {
            var options = new JsonSerializerOptions(); options.Converters.Add(new FiniteDoubleJsonConverter());
            replayFrames = new ObservableCollection<FrameQualityResult>(JsonSerializer.Deserialize<List<FrameQualityResult>>(File.ReadAllText(args[1]), options));
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            RenderThemeSet(dark: true);
            RenderThemeSet(dark: false);
            RenderStarfieldPreview();
            File.WriteAllText(Path.Combine(outputDirectory, "wpf-visual-manifest.json"), JsonSerializer.Serialize(new {
                generatedUtc = DateTimeOffset.UtcNow,
                source = "QualitySessionMeter.VisualHarness",
                note = "These are deterministic WPF visual-regression renders, not evidence of AvalonDock/N.I.N.A. runtime integration.",
                files = Directory.GetFiles(outputDirectory, "*.png").Select(Path.GetFileName).OrderBy(x => x).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
        } finally {
            app.Shutdown();
        }
    }

    private static void RenderThemeSet(bool dark) {
        InstallHostResources(dark);
        var suffix = dark ? "dark" : "light";

        var settings = PreviewData.Settings();
        var frames = replayFrames ?? PreviewData.Frames();
        var events = replayFrames == null ? PreviewData.Events() : new ObservableCollection<SessionEvent>();
        if (replayFrames != null) { settings.MaxGuideRms = 1.6; settings.HardExcursionThreshold = 3; settings.ExcursionMinimumDuration = 5; }
        var current = frames.Last();
        var evidenceFrame = frames.LastOrDefault(f => f.ImageEvidence.Available);
        if (evidenceFrame != null) {
            RenderElement((FrameworkElement)StarEvidencePresentation.CreateTooltip(evidenceFrame, evidenceFrame.FrameIdentity), 560, 370, $"wpf-star-evidence-{suffix}.png");
            File.WriteAllText(Path.Combine(outputDirectory,"star-evidence.json"), JsonSerializer.Serialize(evidenceFrame.ImageEvidence));
        }

        var optionsVm = new OptionsPreviewVm {
            Settings = settings,
            WebDashboardAddress = "http://192.168.1.54:18974/",
            WebDashboardStatus = "Running · read-only · LAN/VPN"
        };
        RenderTemplate(
            new OptionsHelpResources(),
            "QualitySessionMeter_Options_Polished",
            optionsVm,
            1180,
            1000,
            $"wpf-options-{suffix}.png");

        var mainVm = new MainPreviewVm(settings, frames, events) {
            CurrentFrame = current,
            ModeText = replayFrames == null ? "ACTIVE REJECT HANDLING · Advanced Sequencer LIGHTs" : "LOCAL REPLAY · 13 sample images · no file changes",
            SessionFolder = @"C:\Users\astro\AppData\Local\NINA\QualitySessionMeter\Sessions\2026-09-08_22-24-00",
            ReviewMessage = "Select a rejected frame to review it in N.I.N.A.'s Image view."
        };
        RenderTemplate(
            new Resources(),
            "NINA.Plugin.QualitySessionMeter.UI.QualitySessionMeterDockable_Dockable",
            mainVm,
            1420,
            1000,
            $"wpf-main-panel-{suffix}.png");

        var controlVm = new ControlCenterPreviewVm {
            Settings = settings,
            ActivationText = "ON · ACTIVE FILE HANDLING · Advanced Sequencer LIGHTs",
            Calibration = new CalibrationPreview { Available = true, Reason = "Stable sample window ready." },
            CalibrationStatus = "READY · 18 stable accepted frames · M31 / L / 180 s / gain 100",
            CalibrationValues = "RMS ≤ 1.20\" · excursion 2.20\" · hard 4.50\" · stars -32.0% · background +26.0/-24.0%",
            PredictionText = "TRANSPARENCY: gradual degradation likely within 3 frames (83% conf.)",
            EnvironmentText = "Humidity 63% · wind 2.1 m/s · no independent hard rejection from environment data."
        };
        RenderTemplate(
            new ControlCenterResources(),
            "NINA.Plugin.QualitySessionMeter.UI.QsmControlCenterDockable_Dockable",
            controlVm,
            1180,
            1060,
            $"wpf-control-center-{suffix}.png");

#if QSM_DEVELOPMENT
        var syntheticVm = new SyntheticPreviewVm {
            SafetyText = "Synthetic Lab is isolated: no camera, no real files, no rejected-file mutation.",
            Scenarios = new[] {
                new ScenarioPreview("Canonical regression"),
                new ScenarioPreview("Average night"),
                new ScenarioPreview("Severe / disaster night")
            },
            SelectedScenario = new ScenarioPreview("Average night"),
            ScenarioDescription = "Normal variation with warnings, a wind excursion and a temporary transparency/background event.",
            ScenarioExpectedSummary = "Expected: accepted majority, deterministic WARNING/REJECTED markers, recovery to healthy frames.",
            Status = "PASS",
            Progress = "36 / 36",
            FrameDelayMs = 150,
            RunningScenario = "Average night — complete",
            LastScenario = "frame_036_recovery_after_cloud.fits",
            FailureSummary = "All expected verdicts and event groups matched the synthetic oracle.",
            CanRun = true,
            CanStop = false,
            CanReturnLive = true,
            SessionFolder = @"C:\Users\astro\AppData\Local\NINA\QualitySessionMeter\Synthetic\2026-09-08"
        };
        RenderTemplate(
            new SyntheticResources(),
            "NINA.Plugin.QualitySessionMeter.UI.SyntheticLabDockable_Dockable",
            syntheticVm,
            960,
            900,
            $"wpf-synthetic-lab-{suffix}.png");
#endif

        var timelineWide = new QualityTimelineControl {
            ItemsSource = frames,
            MaxGuideRms = settings.MaxGuideRms,
            MaxStarLossPercent = settings.MaxStarLossPercent,
            MaxBackgroundIncreasePercent = settings.MaxBackgroundIncreasePercent,
            MaxBackgroundDecreasePercent = settings.MaxBackgroundDecreasePercent,
            EnableGuideRms = settings.EnableGuideRms,
            EnableStarCount = settings.EnableStarCount,
            EnableBackground = settings.EnableBackground
        };
        RenderElement(timelineWide, 1420, 390, $"wpf-timeline-wide-{suffix}.png");

        var timelineNarrow = new QualityTimelineControl {
            ItemsSource = frames,
            MaxGuideRms = settings.MaxGuideRms,
            MaxStarLossPercent = settings.MaxStarLossPercent,
            MaxBackgroundIncreasePercent = settings.MaxBackgroundIncreasePercent,
            MaxBackgroundDecreasePercent = settings.MaxBackgroundDecreasePercent,
            EnableGuideRms = settings.EnableGuideRms,
            EnableStarCount = settings.EnableStarCount,
            EnableBackground = settings.EnableBackground
        };
        RenderElement(timelineNarrow, 720, 390, $"wpf-timeline-narrow-{suffix}.png");
    }

    private static void InstallHostResources(bool dark) {
        var app = Application.Current;
        app.Resources.Clear();

        var brushes = new ResourceDictionary {
            ["BackgroundBrush"] = FrozenBrush(dark ? "#2B353B" : "#F1F3F4"),
            ["SecondaryBackgroundBrush"] = FrozenBrush(dark ? "#1B2025" : "#FFFFFF"),
            ["TertiaryBackgroundBrush"] = FrozenBrush(dark ? "#151A1F" : "#E8ECEF"),
            ["BorderBrush"] = FrozenBrush(dark ? "#465159" : "#B9C2C8"),
            ["ButtonForegroundBrush"] = FrozenBrush(dark ? "#EEF2F5" : "#20262B"),
            ["PrimaryBrush"] = FrozenBrush(dark ? "#62A89A" : "#317F70")
        };
        app.Resources.MergedDictionaries.Add(brushes);
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(HostStylesXaml));
    }

    private static SolidColorBrush FrozenBrush(string color) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static void RenderTemplate(ResourceDictionary dictionary, string key, object dataContext, int width, int height, string fileName) {
        if (dictionary[key] is not DataTemplate template) throw new InvalidOperationException($"Template '{key}' was not found.");
        var content = new ContentControl {
            Content = dataContext,
            ContentTemplate = template,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        RenderElement(content, width, height, fileName);
    }

    private static void RenderElement(FrameworkElement element, int width, int height, string fileName) {
        element.Width = width;
        element.Height = height;
        element.UseLayoutRounding = true;
        element.SnapsToDevicePixels = true;
        element.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
        if (element is Control control) control.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");

        var host = new Border {
            Width = width,
            Height = height,
            Child = element,
            Padding = new Thickness(0)
        };
        host.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");

        var window = new Window {
            Width = width,
            Height = height,
            Content = host,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -30000,
            Top = -30000,
            Background = Brushes.Transparent,
            AllowsTransparency = false,
            SizeToContent = SizeToContent.Manual
        };

        window.Show();
        PumpDispatcher();
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        PumpDispatcher();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        SavePng(bitmap, Path.Combine(outputDirectory, fileName));
        window.Close();
        PumpDispatcher();
    }

    private static void PumpDispatcher() {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void SavePng(BitmapSource bitmap, string path) {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void RenderStarfieldPreview() {
        const int width = 1280;
        const int height = 720;
        var visual = new DrawingVisual();
        var random = new Random(31057);
        using (var dc = visual.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 12, 18)), null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(45, 80, 110, 165)), null, new Point(790, 330), 330, 185);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(35, 190, 105, 165)), null, new Point(735, 355), 230, 115);
            for (var i = 0; i < 520; i++) {
                var x = random.NextDouble() * width;
                var y = random.NextDouble() * height;
                var radius = random.NextDouble() < 0.06 ? 1.8 + random.NextDouble() * 1.8 : 0.45 + random.NextDouble() * 0.75;
                var alpha = (byte)random.Next(135, 255);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, 230, 238, 255)), null, new Point(x, y), radius, radius);
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, Path.Combine(outputDirectory, "sample-light-preview.png"));
    }

    private const string HostStylesXaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="{x:Type TextBlock}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
  </Style>
  <Style TargetType="{x:Type TextBox}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="5,3"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="TextAlignment" Value="Left"/>
  </Style>
  <Style TargetType="{x:Type PasswordBox}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="5,3"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
  </Style>
  <Style TargetType="{x:Type Button}">
    <Setter Property="Foreground" Value="#FFFFFF"/>
    <Setter Property="Background" Value="{DynamicResource PrimaryBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="HorizontalContentAlignment" Value="Center"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
  </Style>
  <Style TargetType="{x:Type ComboBox}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ComboBox}">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="7,4">
            <Grid>
              <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,0,22,0"/>
              <TextBlock Text="▾" HorizontalAlignment="Right" VerticalAlignment="Center" Opacity="0.72"/>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="{x:Type ComboBoxItem}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Padding" Value="6,3"/>
  </Style>
  <Style TargetType="{x:Type CheckBox}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type CheckBox}">
          <Grid Width="78" Height="25" HorizontalAlignment="Left">
            <Border x:Name="Track" Background="{DynamicResource TertiaryBackgroundBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1" CornerRadius="12.5">
              <Grid>
                <Ellipse x:Name="Knob" Width="19" Height="19" Margin="3" HorizontalAlignment="Left" Fill="#7D858C"/>
                <TextBlock x:Name="State" Text="OFF" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0" FontSize="11"/>
              </Grid>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Track" Property="Background" Value="{DynamicResource PrimaryBrush}"/>
              <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
              <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/>
              <Setter TargetName="Knob" Property="Fill" Value="#F5F7F8"/>
              <Setter TargetName="State" Property="Text" Value="ON"/>
              <Setter TargetName="State" Property="HorizontalAlignment" Value="Left"/>
              <Setter TargetName="State" Property="Margin" Value="9,0,0,0"/>
              <Setter TargetName="State" Property="Foreground" Value="#FFFFFF"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Track" Property="Opacity" Value="0.45"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType="{x:Type DataGrid}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource SecondaryBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="HorizontalGridLinesBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="VerticalGridLinesBrush" Value="Transparent"/>
    <Setter Property="GridLinesVisibility" Value="Horizontal"/>
    <Setter Property="RowBackground" Value="{DynamicResource SecondaryBackgroundBrush}"/>
    <Setter Property="AlternatingRowBackground" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="AlternationCount" Value="2"/>
    <Setter Property="HeadersVisibility" Value="Column"/>
    <Setter Property="RowHeight" Value="27"/>
  </Style>
  <Style TargetType="{x:Type DataGridColumnHeader}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource TertiaryBackgroundBrush}"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}"/>
    <Setter Property="BorderThickness" Value="0,0,0,1"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
  </Style>
  <Style TargetType="{x:Type DataGridCell}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="5,2"/>
  </Style>
  <Style TargetType="{x:Type DataGridRow}">
    <Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/>
    <Setter Property="Background" Value="{DynamicResource SecondaryBackgroundBrush}"/>
  </Style>
</ResourceDictionary>
""";
}

internal sealed class PreviewSettings {
    public bool Enabled { get; set; } = true;
    public int MonitoringScopeIndex { get; set; } = 1;
    public bool MonitorOnly { get; set; }
    public int BaselineWindow { get; set; } = 12;
    public int MinimumLearningFrames { get; set; } = 4;
    public bool EnableGuideRms { get; set; } = true;
    public double MaxGuideRms { get; set; } = 1.50;
    public bool EnableSustainedExcursion { get; set; } = true;
    public double ExcursionThreshold { get; set; } = 2.00;
    public double ExcursionMinimumDuration { get; set; } = 2.0;
    public bool EnableHardExcursion { get; set; } = true;
    public double HardExcursionThreshold { get; set; } = 5.0;
    public bool EnableStarCount { get; set; } = true;
    public double MaxStarLossPercent { get; set; } = 35;
    public bool EnableBackground { get; set; } = true;
    public double MaxBackgroundIncreasePercent { get; set; } = 30;
    public double MaxBackgroundDecreasePercent { get; set; } = 30;
    public int AdaptiveThresholdModeIndex { get; set; } = 1;
    public int CalibrationWindow { get; set; } = 12;
    public double AutoSafetyMaxGuideRms { get; set; } = 2.50;
    public double AutoSafetyMaxExcursion { get; set; } = 4.0;
    public double AutoSafetyMaxHardExcursion { get; set; } = 8.0;
    public double AutoSafetyMaxStarLossPercent { get; set; } = 45;
    public double AutoSafetyMaxBackgroundPercent { get; set; } = 45;
    public double WorstMetricWeight { get; set; } = 0.70;
    public int RejectedFileActionIndex { get; set; } = 1;
    public bool PredictiveWarningsEnabled { get; set; } = true;
    public bool EnvironmentalCorrelationEnabled { get; set; } = true;
    public bool ImageEvidenceEnabled { get; set; } = true;
    public int ShapeTargetStars { get; set; } = 100;
    public double ShapeMaxEccentricity { get; set; } = .6;
    public double ShapeMaxTailPercent { get; set; } = 2;
    public double ShapeMaxDoublePeakPercent { get; set; } = 8;
    public bool WebDashboardEnabled { get; set; } = true;
    public int WebDashboardPort { get; set; } = 18974;
    public bool WebDashboardRequirePassword { get; set; }
    public string WebDashboardPasswordStatus { get; set; } = "No password configured";
}

internal sealed class OptionsPreviewVm {
    public PreviewSettings Settings { get; set; }
    public string WebDashboardAddress { get; set; }
    public string WebDashboardStatus { get; set; }
}

internal sealed class CalibrationPreview {
    public bool Available { get; set; }
    public string Reason { get; set; }
}

internal sealed class ControlCenterPreviewVm {
    public PreviewSettings Settings { get; set; }
    public CalibrationPreview Calibration { get; set; }
    public string ActivationText { get; set; }
    public string CalibrationStatus { get; set; }
    public string CalibrationValues { get; set; }
    public string PredictionText { get; set; }
    public string EnvironmentText { get; set; }
    public ICommand ApplyCalibrationCommand => null;
    public ICommand IgnoreCalibrationCommand => null;
}

internal sealed class MainPreviewVm {
    public Visibility CalibrationSuggestionVisibility => Visibility.Collapsed;
    public MainPreviewVm(PreviewSettings settings, ObservableCollection<FrameQualityResult> frames, ObservableCollection<SessionEvent> events) {
        Settings = settings;
        Frames = frames;
        Events = events;
        BestAccepted = new ObservableCollection<FrameQualityResult>(frames.Where(x => x.IsUsable).OrderByDescending(x => x.OverallQuality).Take(4));
        WorstAccepted = new ObservableCollection<FrameQualityResult>(frames.Where(x => x.IsUsable).OrderBy(x => x.OverallQuality).Take(4));
        RejectedFrames = new ObservableCollection<FrameQualityResult>(frames.Where(x => x.Status == FrameStatus.Rejected));
        SelectedRejectedFrame = RejectedFrames.FirstOrDefault();
    }

    public PreviewSettings Settings { get; }
    public ObservableCollection<FrameQualityResult> Frames { get; }
    public ObservableCollection<SessionEvent> Events { get; }
    public ObservableCollection<FrameQualityResult> BestAccepted { get; }
    public ObservableCollection<FrameQualityResult> WorstAccepted { get; }
    public ObservableCollection<FrameQualityResult> RejectedFrames { get; }
    public FrameQualityResult CurrentFrame { get; set; }
    public FrameQualityResult SelectedRejectedFrame { get; set; }
    public string ModeText { get; set; }
    public string SessionFolder { get; set; }
    public string ReviewMessage { get; set; }
    public IEnumerable<FrameQualityResult> SecondPassFrames => Frames.Where(x => x.ImageEvidence.Attempted).Reverse().Take(50);
    public int Captured => Frames.Count;
    public int Accepted => Frames.Count(x => x.Status == FrameStatus.Accepted);
    public int Warning => Frames.Count(x => x.Status == FrameStatus.Warning);
    public int Rejected => Frames.Count(x => x.Status == FrameStatus.Rejected);
    public int Usable => Accepted + Warning;
    public int EventCount => Events.Count;
    public double AcceptanceRate => Usable + Rejected == 0 ? 0 : Usable * 100.0 / (Usable + Rejected);
    public double SessionQuality => Frames.Where(x => x.IsUsable).Select(x => x.OverallQuality).DefaultIfEmpty(0).Average();
    public double SessionConfidence => Frames.Where(x => x.IsUsable).Select(x => x.ConfidenceScore).Where(double.IsFinite).DefaultIfEmpty(0).Average();
    public bool CanReviewSelectedRejected => false;
    public bool CanRestoreSelectedRejected => false;
    public ICommand OpenSelectedRejectedCommand => null;
    public ICommand RestoreSelectedRejectedCommand => null;
    public ICommand OpenReportCommand => null;
    public ICommand OpenSessionFolderCommand => null;
    public ICommand ResetSessionCommand => null;
}

internal sealed class ScenarioPreview {
    public ScenarioPreview(string displayName) { DisplayName = displayName; }
    public string DisplayName { get; }
    public override string ToString() => DisplayName;
}

internal sealed class SyntheticPreviewVm {
    public string SafetyText { get; set; }
    public IEnumerable<ScenarioPreview> Scenarios { get; set; }
    public ScenarioPreview SelectedScenario { get; set; }
    public string ScenarioDescription { get; set; }
    public string ScenarioExpectedSummary { get; set; }
    public string Status { get; set; }
    public string Progress { get; set; }
    public int FrameDelayMs { get; set; }
    public string RunningScenario { get; set; }
    public string LastScenario { get; set; }
    public string FailureSummary { get; set; }
    public bool CanRun { get; set; }
    public bool CanStop { get; set; }
    public bool CanReturnLive { get; set; }
    public string SessionFolder { get; set; }
    public ICommand RunCommand => null;
    public ICommand StopCommand => null;
    public ICommand OpenFolderCommand => null;
    public ICommand ReturnLiveCommand => null;
}

internal static class PreviewData {
    public static PreviewSettings Settings() => new();

    public static ObservableCollection<FrameQualityResult> Frames() {
        var frames = new ObservableCollection<FrameQualityResult>();
        const int size = 512;
        var field = Enumerable.Repeat(1000f, size*size).ToArray();
        for (int y=40;y<480;y+=48) for(int x=40;x<480;x+=48)
            for(int dy=-12;dy<=12;dy++) for(int dx=-12;dx<=12;dx++)
                field[(y+dy)*size+x+dx] += (float)(6000*Math.Exp(-.5*(dx*dx/5.0+dy*dy/1.4)));
        var evidence = ImageEvidenceAnalyzer.Analyze(new ImageSample(field,size,size));
        var random = new Random(1305);
        for (var i = 1; i <= 36; i++) {
            var status = i <= 4 ? FrameStatus.Learning : FrameStatus.Accepted;
            if (i is 10 or 17 or 25) status = FrameStatus.Warning;
            if (i is 12 or 13 or 21 or 22 or 29) status = FrameStatus.Rejected;
            if (i == 30) status = FrameStatus.Error;

            var guide = 0.43 + random.NextDouble() * 0.20;
            var starsDelta = (random.NextDouble() - 0.5) * 8.0;
            var bgDelta = (random.NextDouble() - 0.5) * 6.0;
            var cause = "NORMAL";
            var pattern = GuidePatternKind.Stable;

            if (i is 10 or 12 or 13) {
                guide = i == 13 ? 2.55 : 1.72 + random.NextDouble() * 0.35;
                cause = "WIND / GUIDING DISTURBANCE";
                pattern = GuidePatternKind.WindLike;
            }
            if (i is 17 or 21 or 22) {
                starsDelta = i == 17 ? -27 : -42 - random.NextDouble() * 8;
                cause = "CLOUD / TRANSPARENCY LOSS";
            }
            if (i is 25 or 29) {
                bgDelta = i == 25 ? 24 : 42;
                cause = "BACKGROUND / HAZE EVENT";
            }
            if (i == 30) {
                cause = "ANALYSIS DATA UNAVAILABLE";
                guide = double.NaN;
                starsDelta = double.NaN;
                bgDelta = double.NaN;
            }

            var quality = status switch {
                FrameStatus.Learning => 0,
                FrameStatus.Error => 0,
                FrameStatus.Rejected => Math.Max(18, 56 - random.NextDouble() * 18),
                FrameStatus.Warning => 70 + random.NextDouble() * 7,
                _ => 91 + random.NextDouble() * 9
            };
            var confidence = status == FrameStatus.Learning ? 25 + i * 13 : status == FrameStatus.Error ? 0 : 84 + random.NextDouble() * 14;
            var starBaseline = 720.0;
            var starCount = double.IsFinite(starsDelta) ? (int)Math.Round(starBaseline * (1 + starsDelta / 100.0)) : -1;
            var bgBaseline = 2280.0;
            var bg = double.IsFinite(bgDelta) ? bgBaseline * (1 + bgDelta / 100.0) : double.NaN;
            var original = $@"C:\Astro\M31\2026-09-08\frame_{i:000}_M31_L_180s.fits";
            var final = status == FrameStatus.Rejected ? $@"C:\Astro\M31\2026-09-08\BAD_frame_{i:000}_M31_L_180s.fits" : original;

            var frame = new FrameQualityResult {
                AssessmentVersion = "1.4",
                ImageEvidence = i is 12 or 13 ? evidence : new ImageEvidence(),
                DecisionSummary = status == FrameStatus.Rejected ? "Rejected: configured limit exceeded." : "Kept: no enabled rejection rule failed.",
                FrameIndex = i,
                TimestampUtc = new DateTime(2026, 9, 8, 20, 30, 0, DateTimeKind.Utc).AddMinutes(i * 3),
                OriginalPath = original,
                FinalPath = final,
                Target = "M31",
                Filter = "L",
                ExposureSeconds = 180,
                Gain = 100,
                BinX = 1,
                BinY = 1,
                Camera = "ASI2600MM Pro",
                StarCount = starCount,
                StarBaseline = i <= 4 ? double.NaN : starBaseline,
                StarDeviationPercent = i <= 4 ? double.NaN : starsDelta,
                BackgroundMedian = bg,
                BackgroundBaseline = i <= 4 ? double.NaN : bgBaseline,
                BackgroundDeviationPercent = i <= 4 ? double.NaN : bgDelta,
                GuideSamples = status == FrameStatus.Error ? 0 : 122,
                GuideRmsArcsec = guide,
                MaxGuideExcursionArcsec = double.IsFinite(guide) ? guide * 2.2 : double.NaN,
                SustainedGuideExcursionSeconds = status == FrameStatus.Rejected && cause.StartsWith("WIND") ? 2.8 : 0,
                GuidePattern = pattern,
                GuidePatternConfidence = status == FrameStatus.Error ? double.NaN : 90,
                GuidePatternDetail = pattern == GuidePatternKind.WindLike ? "Bursty signed guide error with multiple excursions." : "Stable guide-error distribution.",
                StarTrendKind = i > 23 && i < 30 ? TrendInterpretationKind.GradualChange : TrendInterpretationKind.Stable,
                BackgroundTrendKind = i > 23 && i < 30 ? TrendInterpretationKind.GradualChange : TrendInterpretationKind.Stable,
                OverallQuality = quality,
                ConfidenceScore = Math.Min(confidence,65),
                ConfidenceReason = status == FrameStatus.Learning ? $"Adaptive baseline is still learning ({i}/4 minimum samples)." : "Measured channels complete and consistent with the active context.",
                Status = status,
                ProbableCause = cause,
                MonitorOnly = false,
                PredictiveWarning = i == 28,
                PredictiveConfidence = i == 28 ? 83 : double.NaN,
                PredictiveChannel = i == 28 ? "TRANSPARENCY" : "",
                PredictiveMessage = i == 28 ? "gradual degradation approaching threshold" : "",
                EnvironmentAvailable = true,
                EnvironmentalHint = "Humidity 63% · wind 2.1 m/s"
            };
            if (status == FrameStatus.Rejected) frame.RejectReasons.Add(cause.StartsWith("WIND") ? "Exposure guiding exceeded configured limit" : cause.StartsWith("CLOUD") ? "Star loss exceeded configured limit" : "Background deviation exceeded configured limit");
            if (status == FrameStatus.Error) frame.ErrorMessage = "Required analysis data was unavailable.";
            frames.Add(frame);
        }
        return frames;
    }

    public static ObservableCollection<SessionEvent> Events() => new(new[] {
        new SessionEvent {
            EventIndex = 1,
            Type = SessionEventType.GuidingDisturbance,
            StartUtc = new DateTime(2026, 9, 8, 21, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 9, 8, 21, 9, 0, DateTimeKind.Utc),
            FirstFrameIndex = 10,
            LastFrameIndex = 13,
            AffectedFrames = 4,
            RejectedFrames = 2,
            WarningFrames = 1,
            MeanConfidence = 91,
            PeakConfidence = 96,
            PrimaryCause = "WIND / GUIDING DISTURBANCE"
        },
        new SessionEvent {
            EventIndex = 2,
            Type = SessionEventType.CloudTransparency,
            StartUtc = new DateTime(2026, 9, 8, 21, 24, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 9, 8, 21, 39, 0, DateTimeKind.Utc),
            FirstFrameIndex = 17,
            LastFrameIndex = 22,
            AffectedFrames = 6,
            RejectedFrames = 2,
            WarningFrames = 1,
            MeanConfidence = 89,
            PeakConfidence = 97,
            PrimaryCause = "CLOUD / TRANSPARENCY LOSS"
        },
        new SessionEvent {
            EventIndex = 3,
            Type = SessionEventType.BackgroundHaze,
            StartUtc = new DateTime(2026, 9, 8, 21, 48, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 9, 8, 22, 0, 0, DateTimeKind.Utc),
            FirstFrameIndex = 25,
            LastFrameIndex = 29,
            AffectedFrames = 5,
            RejectedFrames = 1,
            WarningFrames = 1,
            MeanConfidence = 88,
            PeakConfidence = 94,
            PrimaryCause = "BACKGROUND / HAZE EVENT"
        }
    });
}
