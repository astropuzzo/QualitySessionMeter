using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class ControlCenterResources : ResourceDictionary {
    public ControlCenterResources() {
        InitializeComponent();

        // These two styles occur immediately in the template. Their Loaded handlers give us a
        // UI-dispatcher-safe point from which to normalize the whole QSM Control Center without
        // replacing N.I.N.A.'s native control styles or sharing mutable theme brushes.
        AddLoadedHandler("QsmCcSectionTitle", ThemeControlCenterLoaded);
        AddLoadedHandler("QsmCcHelp", ThemeControlCenterLoaded);
    }

    private void AddLoadedHandler(string styleKey, RoutedEventHandler handler) {
        if (this[styleKey] is Style style && !style.IsSealed) {
            style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, handler));
        }
    }

    private static void ThemeControlCenterLoaded(object sender, RoutedEventArgs e) {
        if (sender is not DependencyObject source) return;

        // Walk up to this dockable's ScrollViewer, then normalize only its subtree.
        DependencyObject root = source;
        while (root is not ScrollViewer) {
            DependencyObject parent;
            try {
                parent = VisualTreeHelper.GetParent(root);
            } catch {
                return;
            }
            if (parent == null) return;
            root = parent;
        }

        ThemeTree(root);
    }

    private static void ThemeTree(DependencyObject root) {
        switch (root) {
            case Border border:
                if (border.Background is SolidColorBrush background) {
                    if (ColorEquals(background.Color, 0x17, 0x1A, 0x20)) {
                        border.SetResourceReference(Border.BackgroundProperty, "SecondaryBackgroundBrush");
                    } else if (ColorEquals(background.Color, 0x11, 0x15, 0x1B)) {
                        border.SetResourceReference(Border.BackgroundProperty, "TertiaryBackgroundBrush");
                    }
                }
                if (border.BorderBrush is SolidColorBrush borderBrush &&
                    ColorEquals(borderBrush.Color, 0x2A, 0x30, 0x39)) {
                    border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                }
                break;

            case TextBlock text:
                text.TextAlignment = TextAlignment.Left;
                if (text.Foreground is SolidColorBrush foreground &&
                    ColorEquals(foreground.Color, 0x8A, 0xB4, 0xF8)) {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
                } else {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
                }
                break;

            case TextBox box:
                box.TextAlignment = TextAlignment.Left;
                box.HorizontalContentAlignment = HorizontalAlignment.Left;
                box.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                break;

            case ComboBox combo:
                combo.HorizontalContentAlignment = HorizontalAlignment.Left;
                combo.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                foreach (var item in combo.Items) {
                    if (item is ComboBoxItem comboItem) {
                        comboItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                        comboItem.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                    }
                }
                break;

            case ComboBoxItem comboItem:
                comboItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                comboItem.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                break;

            case CheckBox checkBox:
                checkBox.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
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
