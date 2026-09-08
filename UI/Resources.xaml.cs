using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class Resources : ResourceDictionary {
    public Resources() {
        InitializeComponent();

        // Shared QSM chrome follows N.I.N.A.'s active profile color schema. The diagnostic
        // channel colors remain semantic/stable, but container, border and text colors must
        // never assume a specific light/dark theme.
        BindBrush("QsmCard", "SecondaryBackgroundBrush");
        BindBrush("QsmCardBorder", "BorderBrush");
        BindBrush("QsmPrimaryText", "ButtonForegroundBrush");
        BindBrush("QsmSecondaryText", "ButtonForegroundBrush", 0.74);
        BindBrush("QsmAccent", "PrimaryBrush");

        // Keep the legacy template for compatibility/history, while the active options page
        // is the native-style, help-rich template used for release validation.
        var polishedOptions = new OptionsHelpResources();
        this["QualitySessionMeter_Options"] = polishedOptions["QualitySessionMeter_Options_Polished"];
    }

    private void BindBrush(string targetKey, string hostKey, double opacity = 1.0) {
        try {
            if (this[targetKey] is not SolidColorBrush target) return;
            if (Application.Current?.TryFindResource(hostKey) is not SolidColorBrush source) return;
            target.Opacity = opacity;
            BindingOperations.SetBinding(target, SolidColorBrush.ColorProperty, new Binding(nameof(SolidColorBrush.Color)) {
                Source = source,
                Mode = BindingMode.OneWay
            });
        } catch {
            // Theme binding is presentation-only. Keep the XAML fallback colors if a future
            // N.I.N.A. build changes or removes a host resource key.
        }
    }
}
