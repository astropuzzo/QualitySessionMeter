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

        // Synthetic Lab is field-test-only, but its shared fallback brushes still cross the same
        // AvalonDock resource/template boundary as production UI. Keep them frozen and resolve
        // host theme resources only on loaded visual elements, never by data-binding a Freezable.
        FreezeBrush("LabCard");
        FreezeBrush("LabWarn");
        FreezeBrush("LabMuted");
        FreezeBrush("LabAccent");
        FreezeBrush("LabSafe");
        FreezeBrush("LabBorder");
        FreezeBrush("LabText");

        AddLoadedHandler("LabCardStyle", LabCardLoaded);
    }

    private void FreezeBrush(string key) {
        if (this[key] is SolidColorBrush brush && brush.CanFreeze && !brush.IsFrozen) {
            brush.Freeze();
        }
    }

    private void AddLoadedHandler(string styleKey, RoutedEventHandler handler) {
        if (this[styleKey] is Style style && !style.IsSealed) {
            style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, handler));
        }
    }

    private static void LabCardLoaded(object sender, RoutedEventArgs e) {
        if (sender is not Border card) return;
        card.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ThemeTree(card);
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
