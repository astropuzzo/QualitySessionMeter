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
    private readonly QualitySessionWebServer webServer;

    public IPluginOptionsAccessor PluginSettings { get; }
    public QualitySettings Settings { get; }

    public string WebDashboardAddress => webServer?.AccessUrl ?? $"http://<NINA-PC-IP>:{Settings.WebDashboardPort}/";
    public string WebDashboardStatus => !Settings.WebDashboardEnabled
        ? "Disabled"
        : webServer?.Enabled == true
            ? "Running"
            : string.IsNullOrWhiteSpace(webServer?.LastError) ? "Stopped" : $"Stopped: {webServer.LastError}";

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

        // Existing tokenized OpenAstro/companion bridge. Kept unchanged for backward compatibility.
        httpBridge = new QualitySessionHttpBridge(imageSaveMediator);

        // Optional self-contained browser dashboard for a user's own LAN/VPN.
        // This has independent settings and does not require OpenAstro or QSM_REMOTE_TOKEN.
        webServer = new QualitySessionWebServer(imageSaveMediator, Settings);

        profileService.ProfileChanged += ProfileChanged;
        Settings.PropertyChanged += SettingsChanged;
    }

    private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
        if (e == null || string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardEnabled) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardPort) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardRequirePassword) ||
            e.PropertyName == nameof(QualitySettings.WebDashboardPasswordConfigured)) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebDashboardAddress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebDashboardStatus)));
        }
    }

    private void ProfileChanged(object sender, EventArgs e) {
        Settings.NotifyProfileChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public override Task Teardown() {
        profileService.ProfileChanged -= ProfileChanged;
        Settings.PropertyChanged -= SettingsChanged;
        webServer?.Dispose();
        httpBridge?.Dispose();
        mobileBridge?.Dispose();
        QualitySessionRuntimeRegistry.DisposeCurrent();
        return base.Teardown();
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
