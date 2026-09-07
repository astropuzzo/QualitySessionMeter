using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Compact three-band V2 timeline: Quality/Confidence, Guide RMS, and image-signal deviations.
/// This is visualization only and never participates in quality decisions.
/// </summary>
public sealed class QualityTimelineControl : FrameworkElement {
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    private INotifyCollectionChanged observed;

    public IEnumerable ItemsSource {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var control = (QualityTimelineControl)d;
        if (control.observed != null) control.observed.CollectionChanged -= control.CollectionChanged;
        control.observed = e.NewValue as INotifyCollectionChanged;
        if (control.observed != null) control.observed.CollectionChanged += control.CollectionChanged;
        control.InvalidateVisual();
    }

    private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 20 || h <= 30) return;

        var frames = ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();

        const double labelWidth = 58;
        const double gap = 5;
        double plotW = Math.Max(1, w - labelWidth);
        double bandH = Math.Max(18, (h - gap * 2) / 3.0);

        var background = FrozenBrush(18, 22, 28);
        var borderPen = FrozenPen(48, 54, 61, 1);
        var textBrush = FrozenBrush(154, 160, 166);
        var qualityPen = FrozenPen(138, 180, 248, 1.8);
        var confidencePen = FrozenPen(197, 138, 249, 1.4);
        var guidePen = FrozenPen(129, 201, 149, 1.6);
        var starsPen = FrozenPen(253, 214, 99, 1.5);
        var backgroundPen = FrozenPen(242, 139, 130, 1.5);

        for (int b = 0; b < 3; b++) {
            double y = b * (bandH + gap);
            dc.DrawRectangle(background, borderPen, new Rect(labelWidth, y, plotW, bandH));
            dc.DrawLine(borderPen, new Point(labelWidth, y + bandH / 2), new Point(w, y + bandH / 2));
        }

        DrawLabel(dc, "Q / CONF", 2, textBrush);
        DrawLabel(dc, "RMS", bandH + gap + 2, textBrush);
        DrawLabel(dc, "IMG Δ", 2 * (bandH + gap) + 2, textBrush);

        if (frames.Length == 0) return;

        DrawSeries(dc, frames, labelWidth, 0, plotW, bandH, f => f.OverallQuality, 0, 100, qualityPen);
        DrawSeries(dc, frames, labelWidth, 0, plotW, bandH, f => f.ConfidenceScore, 0, 100, confidencePen);

        double maxRms = Math.Max(2.0,
            frames.Where(f => Finite(f.GuideRmsArcsec)).Select(f => f.GuideRmsArcsec).DefaultIfEmpty(2).Max() * 1.10);
        DrawSeries(dc, frames, labelWidth, bandH + gap, plotW, bandH, f => f.GuideRmsArcsec, 0, maxRms, guidePen);

        double imgTop = 2 * (bandH + gap);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.StarDeviationPercent, -50, 50, starsPen);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.BackgroundDeviationPercent, -50, 50, backgroundPen);

        var rejectedPen = FrozenPen(242, 139, 130, 0.8);
        var warningPen = FrozenPen(253, 214, 99, 0.8);
        var errorPen = FrozenPen(255, 120, 120, 1.0);
        for (int i = 0; i < frames.Length; i++) {
            Pen marker = frames[i].Status switch {
                FrameStatus.Rejected => rejectedPen,
                FrameStatus.Warning => warningPen,
                FrameStatus.Error => errorPen,
                _ => null
            };
            if (marker == null) continue;
            double x = frames.Length == 1 ? labelWidth + plotW / 2 : labelWidth + i * plotW / (frames.Length - 1.0);
            dc.DrawLine(marker, new Point(x, 0), new Point(x, h));
        }
    }

    private static void DrawSeries(
        DrawingContext dc,
        FrameQualityResult[] frames,
        double left,
        double top,
        double width,
        double height,
        Func<FrameQualityResult, double> selector,
        double min,
        double max,
        Pen pen) {

        Point? previous = null;
        for (int i = 0; i < frames.Length; i++) {
            double value = selector(frames[i]);
            if (!Finite(value)) { previous = null; continue; }
            value = Math.Clamp(value, min, max);
            double x = frames.Length == 1 ? left + width / 2 : left + i * width / (frames.Length - 1.0);
            double y = top + height * (1.0 - (value - min) / Math.Max(0.000001, max - min));
            var point = new Point(x, y);
            if (previous.HasValue) dc.DrawLine(pen, previous.Value, point);
            previous = point;
        }
    }

    private static void DrawLabel(DrawingContext dc, string text, double y, Brush brush) {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            9,
            brush,
            1.0);
        dc.DrawText(formatted, new Point(2, y));
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b) {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(byte r, byte g, byte b, double thickness) {
        var pen = new Pen(FrozenBrush(r, g, b), thickness);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
