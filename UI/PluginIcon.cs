using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Quality gauge (three bands) around a star. N.I.N.A. fills icon geometry, so every figure is a
/// closed shape; open strokes would render as filled blobs.
/// </summary>
internal static class PluginIcon {
    public const string GaugeLow = "M 8.24,49.63 A 29,29 0 0 1 10.79,13.22 L 15.54,17.66 A 22.5,22.5 0 0 0 13.57,45.91 Z";
    public const string GaugeMid = "M 12.97,11.11 A 29,29 0 0 1 51.03,11.11 L 46.76,16.02 A 22.5,22.5 0 0 0 17.24,16.02 Z";
    public const string GaugeHigh = "M 53.21,13.22 A 29,29 0 0 1 55.76,49.63 L 50.43,45.91 A 22.5,22.5 0 0 0 48.46,17.66 Z";
    public const string Star = "M 32.0,18.0 Q 34.2,30.8 47.0,33.0 Q 34.2,35.2 32.0,48.0 Q 29.8,35.2 17.0,33.0 Q 29.8,30.8 32.0,18.0 Z";

    public static GeometryGroup CreateMeterGeometry() {
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var figure in new[] { GaugeLow, GaugeMid, GaugeHigh, Star }) geometry.Children.Add(Geometry.Parse(figure));
        geometry.Freeze();
        return geometry;
    }
}
