using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NINA.Plugin.QualitySessionMeter.UI;

public partial class OptionsHelpResources : ResourceDictionary {
    public OptionsHelpResources() {
        InitializeComponent();
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
