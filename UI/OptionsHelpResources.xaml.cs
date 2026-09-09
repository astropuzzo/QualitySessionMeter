using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

public partial class OptionsHelpResources : ResourceDictionary {
    public OptionsHelpResources() {
        InitializeComponent();

        // N.I.N.A. host styles may center TextBox/ComboBox content even when the control itself
        // is left-aligned. Apply only local alignment/theme references when each QSM row is loaded
        // on the UI dispatcher. This preserves the native host style while making QSM consistently
        // left-aligned and avoids sharing theme SolidColorBrush instances across dispatchers.
        AddLoadedHandler("QsmOptionRow", OptionRowLoaded);
        AddLoadedHandler("QsmSectionHeader", OptionTextLoaded);
        AddLoadedHandler("QsmSubHeader", OptionTextLoaded);
        AddLoadedHandler("QsmHint", OptionTextLoaded);
    }

    private void AddLoadedHandler(string styleKey, RoutedEventHandler handler) {
        if (this[styleKey] is Style style && !style.IsSealed) {
            style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, handler));
        }
    }

    private static void OptionTextLoaded(object sender, RoutedEventArgs e) {
        if (sender is not TextBlock text) return;
        text.TextAlignment = TextAlignment.Left;
        text.SetResourceReference(TextBlock.ForegroundProperty, "ButtonForegroundBrush");
    }

    private static void OptionRowLoaded(object sender, RoutedEventArgs e) {
        if (sender is not Grid row) return;
        ThemeAndAlign(row);
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
                // Do not override the ON/OFF toggle template alignment; only fix text color.
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
