using NINA.Plugin.QualitySessionMeter.UI;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace QualitySessionMeter.DispatcherCheck;

internal static class Program {
    private sealed record Templates(
        DataTemplate Main,
        DataTemplate Options,
        DataTemplate ControlCenter,
        DataTemplate Synthetic);

    [STAThread]
    private static int Main() {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        Templates templates = null;
        Exception producerFailure = null;
        Exception consumerFailure = null;

        var producer = new Thread(() => {
            try {
                // This intentionally creates plugin ResourceDictionaries on dispatcher A.
                // N.I.N.A. can discover/export resource dictionaries before AvalonDock later
                // materializes their templates on the UI dispatcher.
                var resources = new Resources();
                var controlCenter = new ControlCenterResources();
                var synthetic = new SyntheticResources();

                templates = new Templates(
                    RequireTemplate(resources, "NINA.Plugin.QualitySessionMeter.UI.QualitySessionMeterDockable_Dockable"),
                    RequireTemplate(resources, "QualitySessionMeter_Options"),
                    RequireTemplate(controlCenter, "NINA.Plugin.QualitySessionMeter.UI.QsmControlCenterDockable_Dockable"),
                    RequireTemplate(synthetic, "NINA.Plugin.QualitySessionMeter.UI.SyntheticLabDockable_Dockable"));
            } catch (Exception ex) {
                producerFailure = ex;
            }
        });
        producer.SetApartmentState(ApartmentState.STA);
        producer.Name = "QSM resource producer dispatcher";
        producer.Start();
        producer.Join();

        if (producerFailure != null) {
            Console.Error.WriteLine("RESOURCE PRODUCER FAILURE");
            Console.Error.WriteLine(producerFailure);
            return 2;
        }
        if (templates == null) {
            Console.Error.WriteLine("Resource producer returned no templates.");
            return 3;
        }

        var consumer = new Thread(() => {
            try {
                // Dispatcher B is deliberately distinct from the resource producer.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                InstallHostResources(app);
                try {
                    Materialize("main dockable", templates.Main);
                    Materialize("options", templates.Options);
                    Materialize("control center", templates.ControlCenter);
                    Materialize("synthetic lab", templates.Synthetic);
                } finally {
                    app.Shutdown();
                }
            } catch (Exception ex) {
                consumerFailure = ex;
            }
        });
        consumer.SetApartmentState(ApartmentState.STA);
        consumer.Name = "QSM AvalonDock-like consumer dispatcher";
        consumer.Start();
        consumer.Join();

        if (consumerFailure != null) {
            Console.Error.WriteLine("CROSS-DISPATCHER TEMPLATE MATERIALIZATION FAILURE");
            Console.Error.WriteLine(consumerFailure);
            return 4;
        }

        Console.WriteLine("PASS: QSM ResourceDictionaries created on STA dispatcher A and all release/field-test templates materialized and laid out on independent STA dispatcher B.");
        return 0;
    }

    private static DataTemplate RequireTemplate(ResourceDictionary dictionary, string key) {
        if (dictionary[key] is not DataTemplate template) {
            throw new InvalidOperationException($"Missing DataTemplate '{key}'.");
        }
        return template;
    }

    private static void InstallHostResources(Application app) {
        app.Resources["BackgroundBrush"] = FrozenBrush("#2B353B");
        app.Resources["SecondaryBackgroundBrush"] = FrozenBrush("#1B2025");
        app.Resources["TertiaryBackgroundBrush"] = FrozenBrush("#151A1F");
        app.Resources["BorderBrush"] = FrozenBrush("#465159");
        app.Resources["ButtonForegroundBrush"] = FrozenBrush("#EEF2F5");
        app.Resources["PrimaryBrush"] = FrozenBrush("#62A89A");

        // Minimal implicit host styles are intentionally created on dispatcher B. If a QSM
        // template accidentally retains a Style object owned by dispatcher A, applying/layout
        // on this dispatcher reproduces Style.CheckTargetType/VerifyAccess failures.
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/></Style>
  <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/></Style>
  <Style TargetType="Button"><Setter Property="Foreground" Value="{DynamicResource ButtonForegroundBrush}"/></Style>
</ResourceDictionary>
"""));
    }

    private static SolidColorBrush FrozenBrush(string value) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static void Materialize(string name, DataTemplate template) {
        var host = new ContentControl {
            Content = new object(),
            ContentTemplate = template,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Width = 1100,
            Height = 900
        };

        var window = new Window {
            Width = 1100,
            Height = 900,
            Content = host,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -30000,
            Top = -30000
        };

        try {
            window.Show();
            Pump();
            host.Measure(new Size(1100, 900));
            host.Arrange(new Rect(0, 0, 1100, 900));
            host.UpdateLayout();
            Pump();
            Console.WriteLine($"PASS: {name}");
        } catch (Exception ex) {
            throw new InvalidOperationException($"Template '{name}' failed across dispatchers.", ex);
        } finally {
            try { window.Close(); } catch { }
            Pump();
        }
    }

    private static void Pump() {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
