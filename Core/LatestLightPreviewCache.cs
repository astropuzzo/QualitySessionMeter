using NINA.Equipment.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>
/// Captures the latest real saved LIGHT image once and exposes a bounded display-only JPEG
/// to every QSM read-only frontend (Web Dashboard and companion/OpenAstro bridge).
///
/// The cache never reads, writes, renames or deletes the acquisition file. The BitmapSource
/// supplied by N.I.N.A. is cloned/frozen on the ImageSaved callback and JPEG encoding is moved
/// off the save callback so preview work cannot block acquisition longer than the clone step.
/// </summary>
public sealed class LatestLightPreviewCache : IDisposable {
    private const int PreviewMaxWidth = 1280;
    private const int PreviewJpegQuality = 82;

    private readonly IImageSaveMediator imageSaveMediator;
    private readonly object sync = new();
    private byte[] jpeg = Array.Empty<byte>();
    private DateTimeOffset capturedUtc;
    private string imageId = "";
    private string lastError = "";
    private long requestedGeneration;
    private long publishedGeneration;
    private bool disposed;

    public LatestLightPreviewCache(IImageSaveMediator imageSaveMediator) {
        this.imageSaveMediator = imageSaveMediator ?? throw new ArgumentNullException(nameof(imageSaveMediator));
        imageSaveMediator.ImageSaved += ImageSaved;
    }

    public LatestLightPreviewSnapshot Snapshot() {
        lock (sync) {
            return new LatestLightPreviewSnapshot(
                jpeg,
                capturedUtc,
                imageId,
                publishedGeneration,
                lastError);
        }
    }

    private void ImageSaved(object sender, ImageSavedEventArgs e) {
        if (disposed || e?.Image == null || e.MetaData?.Image == null) return;
        if (e.MetaData.Image.ImageType != CaptureSequence.ImageTypes.LIGHT) return;

        try {
            // Clone while still on the callback that owns the source BitmapSource, then freeze
            // the clone so the encoder can safely run on a worker thread.
            BitmapSource source = e.Image.Clone();
            if (source.CanFreeze) source.Freeze();

            var generation = Interlocked.Increment(ref requestedGeneration);
            var id = e.MetaData.Image.Id.ToString();
            var time = DateTimeOffset.UtcNow;

            _ = Task.Run(() => EncodeAndPublish(source, id, time, generation));
        } catch (Exception ex) {
            RecordError(ex);
        }
    }

    private void EncodeAndPublish(BitmapSource source, string id, DateTimeOffset time, long generation) {
        if (disposed || source == null) return;

        try {
            BitmapSource rendered = source;
            if (rendered.PixelWidth <= 0 || rendered.PixelHeight <= 0) return;

            var scale = Math.Min(1.0, PreviewMaxWidth / (double)rendered.PixelWidth);
            if (scale < 1.0) {
                var transformed = new TransformedBitmap(rendered, new ScaleTransform(scale, scale));
                if (transformed.CanFreeze) transformed.Freeze();
                rendered = transformed;
            }

            if (rendered.Format != PixelFormats.Bgr24) {
                var converted = new FormatConvertedBitmap(rendered, PixelFormats.Bgr24, null, 0);
                if (converted.CanFreeze) converted.Freeze();
                rendered = converted;
            }

            var encoder = new JpegBitmapEncoder { QualityLevel = PreviewJpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var memory = new MemoryStream();
            encoder.Save(memory);
            var bytes = memory.ToArray();
            if (bytes.Length == 0 || disposed) return;

            lock (sync) {
                // Encoding is asynchronous. Never allow an older, slower frame to replace a
                // newer preview that completed first.
                if (generation < publishedGeneration) return;
                jpeg = bytes;
                capturedUtc = time;
                imageId = id ?? "";
                publishedGeneration = generation;
                lastError = "";
            }
        } catch (Exception ex) {
            RecordError(ex);
        }
    }

    private void RecordError(Exception ex) {
        lock (sync) {
            lastError = ex?.Message ?? "Preview generation failed.";
        }
        // Preview is presentation-only. Errors are intentionally contained and must never
        // propagate into N.I.N.A. image saving or QSM frame classification.
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        imageSaveMediator.ImageSaved -= ImageSaved;
    }
}

public sealed class LatestLightPreviewSnapshot {
    public LatestLightPreviewSnapshot(byte[] jpeg, DateTimeOffset capturedUtc, string imageId, long generation, string lastError) {
        Jpeg = jpeg ?? Array.Empty<byte>();
        CapturedUtc = capturedUtc;
        ImageId = imageId ?? "";
        Generation = generation;
        LastError = lastError ?? "";
    }

    public byte[] Jpeg { get; }
    public DateTimeOffset CapturedUtc { get; }
    public string ImageId { get; }
    public long Generation { get; }
    public string LastError { get; }
    public bool Available => Jpeg.Length > 0;
    public string EntityTag => Available ? $"\"qsm-light-{Generation}\"" : "";
}
