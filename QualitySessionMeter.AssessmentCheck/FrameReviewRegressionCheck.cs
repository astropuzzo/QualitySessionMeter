using NINA.Core.Enum;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Plugin.QualitySessionMeter.Core;
using System.Reflection;
using System.Windows.Media;

static class FrameReviewRegressionCheck {
    public static void Run(Action<bool, string> check) {
        var factory = new ReviewFactory();
        var cases = new[] {
            (SensorType.RGGB, new[] { SensorType.RGGB, SensorType.GRBG, SensorType.GBRG, SensorType.BGGR }),
            (SensorType.BGGR, new[] { SensorType.BGGR, SensorType.GBRG, SensorType.GRBG, SensorType.RGGB }),
            (SensorType.GRBG, new[] { SensorType.GRBG, SensorType.RGGB, SensorType.BGGR, SensorType.GBRG }),
            (SensorType.GBRG, new[] { SensorType.GBRG, SensorType.BGGR, SensorType.RGGB, SensorType.GRBG })
        };
        foreach (var (sensor, phases) in cases) for (int offset = 0; offset < 4; offset++) {
            var metadata = new ImageMetaData();
            metadata.Camera.SensorType = sensor;
            metadata.Camera.BayerOffsetX = offset % 2;
            metadata.Camera.BayerOffsetY = offset / 2;
            var expected = phases[offset];
            check(FrameReviewImage.TryGetPattern(metadata, out var pattern) && pattern == expected,
                $"review resolves {sensor} offset {offset % 2},{offset / 2}");
            const int size = 16;
            var tile = expected.ToString();
            var pixels = new ushort[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                pixels[y * size + x] = tile[(y % 2) * 2 + x % 2] switch { 'R' => 6000, 'G' => 2000, _ => 500 };
            var original = pixels.ToArray();
            var source = factory.CreateBaseImageData(pixels, size, size, 16, false, metadata);
            var review = FrameReviewImage.Prepare(source, factory);
            var image = review.RenderImage();
            var colors = new ushort[size * size * 3];
            image.Image.CopyPixels(colors, size * 6, 0);
            int center = (8 * size + 8) * 3;
            check(image.Image.Format == PixelFormats.Rgb48 && colors[center] == 6000 && colors[center + 1] == 2000 && colors[center + 2] == 500,
                $"native review debayer preserves RGB channels for {sensor} offset {offset % 2},{offset / 2}");
            check(!review.Properties.IsBayered && image.RawImageData.Properties.IsBayered
                && ReferenceEquals(image.RawImageData.Data, source.Data) && ReferenceEquals(image.RawImageData.MetaData, metadata)
                && pixels.SequenceEqual(original) && !source.Properties.IsBayered,
                "review prevents a second host debayer without changing source pixels or metadata");
            check(ReferenceEquals(image, review.RenderImage()), "review reuses the debayered image");
        }
        foreach (var sensor in new[] { SensorType.Monochrome, SensorType.Color, SensorType.CMYG }) {
            var metadata = new ImageMetaData(); metadata.Camera.SensorType = sensor;
            var source = factory.CreateBaseImageData(new ushort[256], 16, 16, 16, false, metadata);
            check(ReferenceEquals(source, FrameReviewImage.Prepare(source, factory)), $"review does not infer a Bayer pattern for {sensor}");
        }
        var parity = new ImageMetaData(); parity.Camera.SensorType = SensorType.RGGB;
        parity.Camera.BayerOffsetX = -1; parity.Camera.BayerOffsetY = 2;
        check(FrameReviewImage.TryGetPattern(parity, out var shifted) && shifted == SensorType.GRBG,
            "review uses offset parity for negative and even offsets");
        check(!FrameReviewImage.TryGetPattern(null, out _), "missing metadata cannot infer color");
    }

    private sealed class ReviewFactory : IImageDataFactory {
        private readonly IStarDetection detection = DispatchProxy.Create<IStarDetection, EmptyDetection>();
        public BaseImageData CreateBaseImageData(ushort[] pixels, int width, int height, int bitDepth, bool isBayered, ImageMetaData metadata)
            => new(pixels, width, height, bitDepth, isBayered, metadata, null, detection, null);
        public BaseImageData CreateBaseImageData(IImageArray pixels, int width, int height, int bitDepth, bool isBayered, ImageMetaData metadata)
            => new(pixels, width, height, bitDepth, isBayered, metadata, null, detection, null);
        public Task<IImageData> CreateFromFile(string path, int bitDepth, bool isBayered, CancellationToken token) => throw new NotSupportedException();
#pragma warning disable CS0618
        public Task<IImageData> CreateFromFile(string path, int bitDepth, bool isBayered, RawConverterEnum converter, CancellationToken token) => throw new NotSupportedException();
#pragma warning restore CS0618
    }

    public class EmptyDetection : DispatchProxy {
        protected override object Invoke(MethodInfo method, object[] args) {
            if (method.Name == "CreateAnalysis") return null;
            throw new NotSupportedException(method.Name);
        }
    }
}
