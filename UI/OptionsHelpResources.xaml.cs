using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

public partial class OptionsHelpResources : ResourceDictionary {
    public OptionsHelpResources() {
        InitializeComponent();
    }

    private void OptionsRootLoaded(object sender, RoutedEventArgs e) {
        // N.I.N.A. hosts plugin option templates inside containers whose default content
        // alignment can vary by theme/layout. Explicitly anchor the QSM page to the left
        // instead of relying on the host's inherited HorizontalContentAlignment.
        if (sender is ScrollViewer scroll) {
            scroll.HorizontalContentAlignment = HorizontalAlignment.Left;
            if (scroll.Content is FrameworkElement content) {
                content.HorizontalAlignment = HorizontalAlignment.Left;
                content.VerticalAlignment = VerticalAlignment.Top;
            }
            ThemeAndAlign(scroll);
            return;
        }

        if (sender is DependencyObject root) ThemeAndAlign(root);
    }

    private static void ThemeAndAlign(DependencyObject root) {
        switch (root) {
            case TextBlock text:
                text.TextAlignment = TextAlignment.Left;
                text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
                break;

            case TextBox box:
                box.TextAlignment = TextAlignment.Left;
                box.HorizontalContentAlignment = HorizontalAlignment.Left;
                box.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
                break;

            case PasswordBox password:
                password.HorizontalContentAlignment = HorizontalAlignment.Left;
                password.SetResourceReference(Control.ForegroundProperty, "ButtonForegroundBrush");
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
                // Preserve N.I.N.A.'s ON/OFF template geometry; normalize only text color.
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
            ThemeAndAlign(VisualTreeHelper.GetChild(root, i));
        }
    }

    private void DashboardPasswordLostFocus(object sender, KeyboardFocusChangedEventArgs e) {
        if (sender is not PasswordBox box || string.IsNullOrEmpty(box.Password)) return;
        if (box.DataContext is QualitySessionMeterPlugin plugin) {
            plugin.Settings.SetWebDashboardPassword(box.Password);
            box.Clear();
        }
    }

    private void ClearDashboardPassword(object sender, RoutedEventArgs e) {
        if (sender is Button button && button.DataContext is QualitySessionMeterPlugin plugin) {
            plugin.Settings.ClearWebDashboardPassword();
        }
    }

    private void CopyDashboardAddress(object sender, RoutedEventArgs e) {
        if (sender is not Button button || button.DataContext is not QualitySessionMeterPlugin plugin) return;
        var address = plugin.WebDashboardAddress;
        if (string.IsNullOrWhiteSpace(address) || address.Contains('<')) {
            button.Content = "No LAN IP";
            return;
        }

        try {
            Clipboard.SetText(address);
            button.Content = "Copied";
        } catch {
            button.Content = "Copy failed";
        }
    }
}
