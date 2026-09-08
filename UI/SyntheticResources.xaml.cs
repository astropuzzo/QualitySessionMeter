using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class SyntheticResources : ResourceDictionary {
    public SyntheticResources() {
        InitializeComponent();

        // Synthetic Lab is field-test-only, but its shared fallback brushes should still be
        // immutable so AvalonDock can safely materialize templates on the UI dispatcher.
        FreezeBrush("LabCard");
        FreezeBrush("LabWarn");
        FreezeBrush("LabMuted");
        FreezeBrush("LabAccent");
        FreezeBrush("LabSafe");
        FreezeBrush("LabBorder");
        FreezeBrush("LabText");
    }

    private void FreezeBrush(string key) {
        if (this[key] is SolidColorBrush brush && brush.CanFreeze && !brush.IsFrozen) {
            brush.Freeze();
        }
    }
}
