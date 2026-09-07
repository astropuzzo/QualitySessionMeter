using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

internal static class PluginIcon {
    public static GeometryGroup CreateMeterGeometry() {
        var geometry = new GeometryGroup();
        geometry.Children.Add(new EllipseGeometry(new Point(32, 32), 29, 29));
        geometry.Children.Add(Geometry.Parse("M 10,38 C 16,19 28,13 32,13 C 42,13 51,22 55,35"));
        geometry.Children.Add(Geometry.Parse("M 32,32 L 47,22"));
        geometry.Freeze();
        return geometry;
    }
}
