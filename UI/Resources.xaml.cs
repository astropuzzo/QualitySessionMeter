using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.ComponentModel.Composition;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class Resources : ResourceDictionary {
    public Resources() {
        InitializeComponent();

        // Keep the existing template structure, but make its shared chrome brushes follow
        // N.I.N.A.'s active color schema instead of hard-coded dark colors. StaticResource
        // references inside the templates keep pointing at these brush objects, while the
        // Color binding below updates them as the host theme changes.
        BindBrush("QsmCard", "SecondaryBackgroundBrush");
        BindBrush("QsmCardBorder", "BorderBrush");
        BindBrush("QsmPrimaryText", "ButtonForegroundBrush");
        BindBrush("QsmSecondaryText", "ButtonForegroundDisabledBrush");
        BindBrush("QsmAccent", "PrimaryBrush");

        // The legacy V1/V2 options template remains in Resources.xaml for compatibility/history,
        // but the active plugin options use the native-style, help-rich template.
        var polishedOptions = new OptionsHelpResources();
        this["QualitySessionMeter_Options"] = polishedOptions["QualitySessionMeter_Options_Polished"];
    }

    private void BindBrush(string targetKey, string hostKey) {
        try {
            if (this[targetKey] is not SolidColorBrush target) return;
            if (Application.Current?.TryFindResource(hostKey) is not SolidColorBrush source) return;
            BindingOperations.SetBinding(target, SolidColorBrush.ColorProperty, new Binding(nameof(SolidColorBrush.Color)) {
                Source = source,
                Mode = BindingMode.OneWay
            });
        } catch {
            // Theme binding is presentation-only. Keep the XAML fallback colors if the host
            // resource is unavailable in a future N.I.N.A. build.
        }
    }
}
