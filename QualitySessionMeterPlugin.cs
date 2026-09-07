using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.QualitySessionMeter.Core;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter;

[Export(typeof(IPluginManifest))]
public sealed class QualitySessionMeterPlugin : PluginBase, INotifyPropertyChanged {
    private readonly IProfileService profileService;
    private readonly QualitySessionMobileBridge mobileBridge;
    private readonly QualitySessionHttpBridge httpBridge;

    public IPluginOptionsAccessor PluginSettings { get; }
    public QualitySettings Settings { get; }

    [ImportingConstructor]
    public QualitySessionMeterPlugin(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        ISequenceMediator sequenceMediator,
        IWeatherDataMediator weatherDataMediator,
        IMessageBroker messageBroker) {

        this.profileService = profileService;
        PluginSettings = new PluginOptionsAccessor(profileService, PluginConstants.Identifier);
        Settings = new QualitySettings(PluginSettings);

        var runtime = QualitySessionRuntimeRegistry.GetOrCreate(
            profileService,
            imageSaveMediator,
            guiderMediator,
            sequenceMediator,
            Settings);
        runtime.AttachWeatherMediator(weatherDataMediator);

        // Read-only in-process companion contract for N.I.N.A. plugins.
        mobileBridge = new QualitySessionMobileBridge(messageBroker);

        // Optional read-only LAN/Tailscale bridge for OpenAstro Control or another trusted companion.
        // It remains disabled unless QSM_REMOTE_TOKEN is present in the N.I.N.A. process environment.
        httpBridge = new QualitySessionHttpBridge();

        profileService.ProfileChanged += ProfileChanged;
    }

    private void ProfileChanged(object sender, EventArgs e) {
        Settings.NotifyProfileChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public override Task Teardown() {
        profileService.ProfileChanged -= ProfileChanged;
        httpBridge?.Dispose();
        mobileBridge?.Dispose();
        QualitySessionRuntimeRegistry.DisposeCurrent();
        return base.Teardown();
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
