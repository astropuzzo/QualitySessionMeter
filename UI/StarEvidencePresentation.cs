using NINA.Plugin.QualitySessionMeter.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>Fresh controls for each dispatcher; image pixels are frozen before sharing.</summary>
public static class StarEvidencePresentation {
    public static object CreateTooltip(FrameQualityResult frame, string metrics) {
        var panel = new StackPanel { MaxWidth = 540 };
        void Text(string value) => panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
        Text(metrics);
        Text(frame.DecisionSummary);
        Text(frame.ImageEvidence.Detail);
        var preview = frame.ImageEvidence.Preview;
        if (preview != null) {
            var image = new System.Windows.Controls.Image { Source = preview, Width = 405, Height = 162, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(image);
            Text(frame.ImageEvidence.PreviewCaption);
        }
        return panel;
    }
}
