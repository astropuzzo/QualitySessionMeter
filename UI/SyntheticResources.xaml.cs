using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class SyntheticResources : ResourceDictionary {
    public SyntheticResources() {
        InitializeComponent();

        // Field-test-only fallback brushes still cross the plugin-resource/AvalonDock boundary.
        // Keep them frozen and resolve host theme resources only on materialized visual elements.
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

    private void SyntheticRootLoaded(object sender, RoutedEventArgs e) {
        if (sender is DependencyObject root) ThemeTree(root);
    }

    private static void ThemeTree(DependencyObject root) {
        switch (root) {
            case TextBlock text when text.Foreground is SolidColorBrush foreground:
                if (ColorEquals(foreground.Color, 0xF1, 0xF3, 0xF4)) {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
                } else if (ColorEquals(foreground.Color, 0x9A, 0xA0, 0xA6)) {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
                    text.Opacity = Math.Min(text.Opacity, 0.72);
                } else if (ColorEquals(foreground.Color, 0x8A, 0xB4, 0xF8)) {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
                }
                text.TextAlignment = TextAlignment.Left;
                break;

            case TextBox box:
                box.TextAlignment = TextAlignment.Left;
                box.HorizontalContentAlignment = HorizontalAlignment.Left;
                box.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                break;

            case ComboBox combo:
                combo.HorizontalContentAlignment = HorizontalAlignment.Left;
                combo.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                break;
        }

        int count;
        try {
            count = VisualTreeHelper.GetChildrenCount(root);
        } catch {
            return;
        }

        for (var i = 0; i < count; i++) {
            ThemeTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static bool ColorEquals(Color color, byte r, byte g, byte b) =>
        color.R == r && color.G == g && color.B == b;
}
