using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Image.FileFormat;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.QualitySessionMeter.Core;

public static class FrameReviewImage {
    public static IImageData Prepare(IImageData data, IImageDataFactory factory) {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(factory);
        if (!TryGetPattern(data.MetaData, out var pattern)) return data;
        var p = data.Properties;
        var raw = factory.CreateBaseImageData(data.Data, p.Width, p.Height, p.BitDepth, true, data.MetaData);
        return new ColorReviewData(raw, pattern);
    }

    public static bool TryGetPattern(ImageMetaData metadata, out SensorType pattern) {
        pattern = SensorType.Monochrome;
        var camera = metadata?.Camera;
        if (camera == null || camera.SensorType is not (SensorType.RGGB or SensorType.BGGR or SensorType.GRBG or SensorType.GBRG)) return false;
        string tile = camera.SensorType.ToString();
        int x = camera.BayerOffsetX & 1, y = camera.BayerOffsetY & 1;
        string phase = new(new[] { tile[y * 2 + x], tile[y * 2 + (x ^ 1)], tile[(y ^ 1) * 2 + x], tile[(y ^ 1) * 2 + (x ^ 1)] });
        return Enum.TryParse(phase, out pattern);
    }

    private sealed class ColorReviewData : IImageData {
        private readonly IImageData raw;
        private readonly Lazy<IRenderedImage> rendered;
        public ColorReviewData(IImageData raw, SensorType pattern) {
            this.raw = raw;
            var p = raw.Properties;
            // The host receives an already debayered display. Raw data retains its CFA flag,
            // so Image-view tools and explicit saves still refer to the original linear data.
            Properties = new ImageProperties(p.Width, p.Height, p.BitDepth, false, p.Gain, p.Offset);
            rendered = new(() => raw.RenderImage().Debayer(saveColorChannels: true, saveLumChannel: false, bayerPattern: pattern));
        }
        public IImageArray Data => raw.Data;
        public ImageProperties Properties { get; }
        public Nito.AsyncEx.AsyncLazy<IImageStatistics> Statistics => raw.Statistics;
        public void SetImageStatistics(IImageStatistics statistics) => raw.SetImageStatistics(statistics);
        public IStarDetectionAnalysis StarDetectionAnalysis { get => raw.StarDetectionAnalysis; set => raw.StarDetectionAnalysis = value; }
        public ImageMetaData MetaData => raw.MetaData;
        public IRenderedImage RenderImage() => rendered.Value;
        public BitmapSource RenderBitmapSource() => rendered.Value.Image;
        public ImagePatterns GetImagePatterns() => raw.GetImagePatterns();
        public Task<string> SaveToDisk(FileSaveInfo info, CancellationToken token = default, bool forceFileType = false) => raw.SaveToDisk(info, token, forceFileType);
        public Task<string> SaveToDisk(FileSaveInfo info, CancellationToken token, bool forceFileType, IList<ImagePattern> patterns) => raw.SaveToDisk(info, token, forceFileType, patterns);
#pragma warning disable CS0618
        [Obsolete] public Task<string> PrepareSave(FileSaveInfo info, CancellationToken token = default) => raw.PrepareSave(info, token);
        [Obsolete] public string FinalizeSave(string file, string pattern, IList<ImagePattern> patterns) => raw.FinalizeSave(file, pattern, patterns);
#pragma warning restore CS0618
    }
}
