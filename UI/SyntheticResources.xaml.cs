using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.QualitySessionMeter.UI;

[Export(typeof(ResourceDictionary))]
public partial class SyntheticResources : ResourceDictionary {
    public SyntheticResources() {
        InitializeComponent();
    }
}
