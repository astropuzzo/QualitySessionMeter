using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class Resources : ResourceDictionary {
    public Resources() {
        InitializeComponent();

        // The original V1/V2 template remains in Resources.xaml for compatibility/history,
        // but V3.0.0.1 deliberately replaces it with the labeled/help-rich template.
        // This avoids N.I.N.A.'s toggle CheckBox template swallowing Content text and
        // keeps every setting self-explanatory without changing global N.I.N.A. styles.
        var polishedOptions = new OptionsHelpResources();
        this["QualitySessionMeter_Options"] = polishedOptions["QualitySessionMeter_Options_Polished"];
    }
}
