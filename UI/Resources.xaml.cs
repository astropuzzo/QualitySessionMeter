using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class Resources : ResourceDictionary {
    public Resources() {
        InitializeComponent();

        // Shared fallback brushes may cross the plugin-resource/AvalonDock boundary, so they
        // must remain immutable. Host theme resources are resolved only on materialized visual
        // elements, on the dispatcher that owns those elements.
        FreezeBrush("QsmCard");
        FreezeBrush("QsmCardBorder");
        FreezeBrush("QsmPrimaryText");
        FreezeBrush("QsmSecondaryText");
        FreezeBrush("QsmAccent");

        // N.I.N.A. expects the historical QualitySessionMeter_Options key. Provide that key from
        // the single canonical Plugin Options template; there is no second settings/control-center UI.
        var polishedOptions = new OptionsHelpResources();
        this["QualitySessionMeter_Options"] = polishedOptions["QualitySessionMeter_Options_Polished"];
    }

    private void FreezeBrush(string key) {
        if (this[key] is SolidColorBrush brush && brush.CanFreeze && !brush.IsFrozen) {
            brush.Freeze();
        }
    }

    private void DockableLoaded(object sender, RoutedEventArgs e) {
        if (sender is DependencyObject root) ThemeCardTree(root);
    }

    private static void ThemeCardTree(DependencyObject root) {
        if (root is Border border) {
            if (border.Background is SolidColorBrush background && ColorEquals(background.Color, 0x17, 0x1A, 0x20)) {
                border.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundBrush");
            }
            if (border.BorderBrush is SolidColorBrush borderBrush && ColorEquals(borderBrush.Color, 0x2A, 0x30, 0x39)) {
                border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            }
        }

        if (root is QualityTimelineControl timeline) {
            TimelineFrameAxisBehavior.Attach(timeline);
        }

        if (root is TextBlock text && text.Foreground is SolidColorBrush brush) {
            text.TextAlignment = TextAlignment.Left;
            if (ColorEquals(brush.Color, 0xF1, 0xF3, 0xF4)) {
                text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
            } else if (ColorEquals(brush.Color, 0x9A, 0xA0, 0xA6)) {
                text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
                text.Opacity = Math.Min(text.Opacity, 0.74);
            } else if (ColorEquals(brush.Color, 0x8A, 0xB4, 0xF8)) {
                text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
            }
        }

        int count;
        try {
            count = VisualTreeHelper.GetChildrenCount(root);
        } catch {
            return;
        }

        for (var i = 0; i < count; i++) {
            ThemeCardTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static bool ColorEquals(Color color, byte r, byte g, byte b) =>
        color.R == r && color.G == g && color.B == b;
}
