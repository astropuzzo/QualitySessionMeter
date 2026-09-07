using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter;

[Export(typeof(IPluginManifest))]
public sealed class QualitySessionMeterPlugin : PluginBase, INotifyPropertyChanged {
    private readonly IProfileService profileService;

    public IPluginOptionsAccessor PluginSettings { get; }
    public QualitySettings Settings { get; }

    [ImportingConstructor]
    public QualitySessionMeterPlugin(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator) {

        this.profileService = profileService;
        PluginSettings = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        Settings = new QualitySettings(PluginSettings);

        QualitySessionRuntimeRegistry.GetOrCreate(
            profileService,
            imageSaveMediator,
            guiderMediator,
            Settings);

        profileService.ProfileChanged += ProfileChanged;
    }

    private void ProfileChanged(object sender, EventArgs e) {
        Settings.NotifyProfileChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public override Task Teardown() {
        profileService.ProfileChanged -= ProfileChanged;
        QualitySessionRuntimeRegistry.DisposeCurrent();
        return base.Teardown();
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
