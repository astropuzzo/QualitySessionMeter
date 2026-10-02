using NINA.Plugin.QualitySessionMeter.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System;
using System.Linq;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>Fresh controls for each dispatcher; image pixels are frozen before sharing.</summary>
public static class StarEvidencePresentation {
    public static object CreateTooltip(FrameQualityResult frame, string metrics) {
        var panel = new StackPanel { MaxWidth = 540 };
        void Text(string value) => panel.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
        Text(metrics);
        Text(frame.DecisionSummary);
        Text(frame.ImageEvidence.Detail);
        var context = new[] {
            frame.CameraOffset >= 0 ? $"Offset {frame.CameraOffset}" : "",
            frame.ReadoutModeIndex >= 0 ? $"Readout {frame.ReadoutModeIndex}" : "",
            frame.ImageWidth > 0 && frame.ImageHeight > 0 ? $"{frame.ImageWidth} × {frame.ImageHeight}" : "",
            frame.PierSide
        };
        if (context.Any(x => !string.IsNullOrEmpty(x))) Text(string.Join(" · ", context.Where(x => !string.IsNullOrEmpty(x))));
        var evidence=frame.ImageEvidence;
        if(evidence.Attempted) {
            string state=evidence.PhotometryState switch {
                PhotometryState.Reliable=>"Measured",PhotometryState.SpatiallyVariable=>"Uneven signal",
                PhotometryState.ReferenceLearning=>"Reference learning",PhotometryState.InsufficientMatches=>"Comparison unavailable",_=>"Not measured"
            };
            Text($"Stellar signal: {state} · {evidence.PhotometryRegionsMeasured}/{evidence.PhotometryRegionsExpected} sampled regions");
            if(evidence.ExpectedReferenceStars>0)Text($"Reference stars: {evidence.ExpectedReferenceStars} · no longer detected: {evidence.MissingReferenceStars}");
            if(evidence.SpatialSignalInconsistent)Text("Stellar signal varies between sampled regions or stars.");
            foreach(var region in evidence.Regions.OrderBy(r=>r.RegionId)) {
                string signal=region.PhotometryAvailable?$"{region.RelativeFlux:P0}":"Unavailable";
                string noise=double.IsFinite(region.Noise)?$" · noise {region.Noise:0.0} ADU":"";
                Text($"{(region.RegionId==0?"Center":"Outer region "+region.RegionId)}: signal {signal} · {region.MatchedStars}/{region.ExpectedReferenceStars} reference stars{noise}");
            }
        }
        var preview = frame.ImageEvidence.Preview;
        if (preview != null) {
            var image = new System.Windows.Controls.Image { Source = preview, Width = 405, Height = 162, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(image);
            Text(frame.ImageEvidence.PreviewCaption);
        }
        if (frame.ImageEvidence.ExtendedPreview is { } extended) {
            Text("Extended stellar profile");
            var image = new System.Windows.Controls.Image { Source=extended,Width=240,Height=240,HorizontalAlignment=HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);panel.Children.Add(image);
        }
        return panel;
    }
}
