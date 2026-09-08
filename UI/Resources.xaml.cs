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

        // IMPORTANT: do not data-bind shared SolidColorBrush resources to N.I.N.A. theme brushes.
        // SolidColorBrush is a Freezable. A Binding makes it non-freezable and AvalonDock can later
        // materialize the template on a different dispatcher, causing a cross-thread XamlParseException.
        // Keep the fallback brushes immutable/cross-thread safe and apply DynamicResource references
        // to the actual UI elements when they are loaded on the owning UI dispatcher.
        FreezeBrush("QsmCard");
        FreezeBrush("QsmCardBorder");
        FreezeBrush("QsmPrimaryText");
        FreezeBrush("QsmSecondaryText");
        FreezeBrush("QsmAccent");

        AddLoadedHandler("QsmCardStyle", CardLoaded);
        AddLoadedHandler("QsmSectionTitle", SectionTitleLoaded);

        // Keep the legacy template for compatibility/history, while the active options page
        // is the native-style, help-rich template used for release validation.
        var polishedOptions = new OptionsHelpResources();
        this["QualitySessionMeter_Options"] = polishedOptions["QualitySessionMeter_Options_Polished"];
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

    private static void SectionTitleLoaded(object sender, RoutedEventArgs e) {
        if (sender is not TextBlock text) return;
        text.TextAlignment = TextAlignment.Left;
        text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
        text.Opacity = Math.Min(text.Opacity, 0.78);
    }

    private static void CardLoaded(object sender, RoutedEventArgs e) {
        if (sender is not Border card) return;

        // Resolve N.I.N.A. theme resources on the UI element/dispatcher itself. DynamicResource
        // remains theme-aware but does not share a mutable Freezable across dispatchers.
        card.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        ThemeCardTree(card);
    }

    private static void ThemeCardTree(DependencyObject root) {
        if (root is TextBlock text && text.Foreground is SolidColorBrush brush) {
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
