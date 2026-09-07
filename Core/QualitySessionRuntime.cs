using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Plugin.QualitySessionMeter.Models;
using NINA.Plugin.QualitySessionMeter.Settings;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.QualitySessionMeter.Core;

public sealed class QualitySessionRuntime : IDisposable {
    private readonly IProfileService profileService;
    private readonly IImageSaveMediator imageSaveMediator;
    private readonly QualitySettings settings;
    private readonly BaselineEngine baseline = new();
    private readonly GuideCollector guideCollector;
    private readonly QualityEngine qualityEngine = new();
    private readonly RejectedFileService rejectedFileService = new();
    private readonly SessionStore sessionStore = new();
    private readonly SemaphoreSlim processingLock = new(1, 1);
    private int frameIndex;
    private bool disposed;

    public event EventHandler<FrameQualityResult> FrameProcessed;

    public QualitySettings Settings => settings;
    public SessionStore Store => sessionStore;

    public QualitySessionRuntime(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        QualitySettings settings) {

        this.profileService = profileService;
        this.imageSaveMediator = imageSaveMediator;
        this.settings = settings;
        guideCollector = new GuideCollector(guiderMediator);

        imageSaveMediator.BeforeFinalizeImageSaved += BeforeFinalizeImageSaved;
        imageSaveMediator.ImageSaved += ImageSaved;
    }

    private async Task BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
        if (!settings.Enabled || e?.Image?.RawImageData?.MetaData?.Image == null) return;
        if (e.Image.RawImageData.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        try {
            var analysis = e.Image.RawImageData.StarDetectionAnalysis;
            if (analysis == null || analysis.DetectedStars <= 0) {
                var render = e.Image.RawImageData.RenderImage();
                render = await render.Stretch(
                    profileService.ActiveProfile.ImageSettings.AutoStretchFactor,
                    profileService.ActiveProfile.ImageSettings.BlackClipping,
                    profileService.ActiveProfile.ImageSettings.UnlinkedStretch);
                _ = await render.DetectStars(
                    false,
                    profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    default,
                    default);
            }
        } catch (Exception ex) {
            Logger.Warning($"QualitySessionMeter star detection fallback failed: {ex.Message}");
        }
    }

    private void ImageSaved(object sender, ImageSavedEventArgs e) {
        if (!settings.Enabled || e?.MetaData?.Image == null) return;
        if (e.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;
        _ = ProcessImageAsync(e);
    }

    private async Task ProcessImageAsync(ImageSavedEventArgs e) {
        await processingLock.WaitAsync();
        try {
            var meta = e.MetaData;
            var start = NormalizeUtc(meta.Image.ExposureStart);
            var guide = guideCollector.GetExposureMetrics(start, e.Duration, settings.ExcursionThreshold);

            var key = new BaselineKey(
                meta.Target?.Name,
                e.Filter ?? meta.FilterWheel?.Filter,
                e.Duration,
                meta.Camera?.Gain ?? -1,
                meta.Camera?.BinX ?? 1,
                meta.Camera?.BinY ?? 1,
                meta.Camera?.Name);

            var snapshot = baseline.GetSnapshot(key, settings.MinimumLearningFrames);
            var input = new FrameQualityInput {
                FrameIndex = Interlocked.Increment(ref frameIndex),
                TimestampUtc = DateTime.UtcNow,
                OriginalPath = e.PathToImage?.IsFile == true ? e.PathToImage.LocalPath : e.PathToImage?.ToString(),
                Target = meta.Target?.Name ?? "",
                Filter = e.Filter ?? meta.FilterWheel?.Filter ?? "",
                ExposureSeconds = e.Duration,
                Gain = meta.Camera?.Gain ?? -1,
                BinX = meta.Camera?.BinX ?? 1,
                BinY = meta.Camera?.BinY ?? 1,
                Camera = meta.Camera?.Name ?? "",
                StarCount = e.StarDetectionAnalysis?.DetectedStars ?? -1,
                BackgroundMedian = e.Statistics?.Median ?? double.NaN,
                Baseline = snapshot,
                Guide = guide
            };

            var result = qualityEngine.Evaluate(input, settings);

            if (result.Status is FrameStatus.Learning or FrameStatus.Accepted) {
                baseline.AddAccepted(key, input.StarCount, input.BackgroundMedian, settings.BaselineWindow);
            }

            if (result.Status == FrameStatus.Rejected && !settings.MonitorOnly) {
                try {
                    result.FinalPath = await rejectedFileService.ApplyAsync(result.OriginalPath, settings);
                } catch (Exception ex) {
                    result.ErrorMessage = $"Rejected-file action failed: {ex.Message}";
                    Logger.Warning($"QualitySessionMeter rejected-file action failed: {ex.Message}");
                }
            }

            await sessionStore.AppendAsync(result);
            FrameProcessed?.Invoke(this, result);
        } catch (Exception ex) {
            Logger.Error(ex);
            var result = new FrameQualityResult {
                FrameIndex = Interlocked.Increment(ref frameIndex),
                TimestampUtc = DateTime.UtcNow,
                OriginalPath = e.PathToImage?.ToString(),
                FinalPath = e.PathToImage?.ToString(),
                Status = FrameStatus.Error,
                OverallQuality = 0,
                ErrorMessage = ex.Message,
                ProbableCause = "ANALYSIS ERROR",
                MonitorOnly = settings.MonitorOnly
            };
            try { await sessionStore.AppendAsync(result); } catch { }
            FrameProcessed?.Invoke(this, result);
        } finally {
            processingLock.Release();
        }
    }

    public void ResetSession() {
        baseline.Clear();
        guideCollector.Clear();
        sessionStore.Reset();
        Interlocked.Exchange(ref frameIndex, 0);
    }

    private static DateTime NormalizeUtc(DateTime value) {
        if (value == DateTime.MinValue) return value;
        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        imageSaveMediator.BeforeFinalizeImageSaved -= BeforeFinalizeImageSaved;
        imageSaveMediator.ImageSaved -= ImageSaved;
        guideCollector.Dispose();
        processingLock.Dispose();
    }
}

public static class QualitySessionRuntimeRegistry {
    private static readonly object Sync = new();
    private static QualitySessionRuntime instance;

    public static QualitySessionRuntime GetOrCreate(
        IProfileService profileService,
        IImageSaveMediator imageSaveMediator,
        IGuiderMediator guiderMediator,
        QualitySettings settings) {
        lock (Sync) {
            instance ??= new QualitySessionRuntime(profileService, imageSaveMediator, guiderMediator, settings);
            return instance;
        }
    }

    public static QualitySessionRuntime Current {
        get { lock (Sync) return instance; }
    }

    public static void DisposeCurrent() {
        lock (Sync) {
            instance?.Dispose();
            instance = null;
        }
    }
}
