using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Compact three-band V2 timeline: Quality/Confidence, Guide RMS, and image-signal deviations.
/// Rejected frames are presentation-only vertical markers with compact cause icons.
/// This control never participates in quality decisions.
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
        if (w <= 20 || h <= 55) return;

        var frames = ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();

        const double labelWidth = 74;
        const double markerLane = 30;
        const double gap = 5;
        double plotW = Math.Max(1, w - labelWidth);
        double bandH = Math.Max(18, (h - markerLane - gap * 2) / 3.0);

        var background = FrozenBrush(18, 22, 28);
        var borderPen = FrozenPen(48, 54, 61, 1);
        var textBrush = FrozenBrush(154, 160, 166);
        var qualityPen = FrozenPen(138, 180, 248, 1.8);
        var confidencePen = FrozenPen(197, 138, 249, 1.4);
        var guidePen = FrozenPen(129, 201, 149, 1.6);
        var starsPen = FrozenPen(253, 214, 99, 1.5);
        var backgroundPen = FrozenPen(242, 139, 130, 1.5);

        DrawRejectLaneLabel(dc, textBrush);

        for (int b = 0; b < 3; b++) {
            double y = markerLane + b * (bandH + gap);
            dc.DrawRectangle(background, borderPen, new Rect(labelWidth, y, plotW, bandH));
            dc.DrawLine(borderPen, new Point(labelWidth, y + bandH / 2), new Point(w, y + bandH / 2));
        }

        DrawLabel(dc, "Q / CONF", markerLane + 2, textBrush);
        DrawLabel(dc, "RMS", markerLane + bandH + gap + 2, textBrush);
        DrawLabel(dc, "IMG Δ", markerLane + 2 * (bandH + gap) + 2, textBrush);

        if (frames.Length == 0) return;

        DrawSeries(dc, frames, labelWidth, markerLane, plotW, bandH, f => f.OverallQuality, 0, 100, qualityPen);
        DrawSeries(dc, frames, labelWidth, markerLane, plotW, bandH, f => f.ConfidenceScore, 0, 100, confidencePen);

        double maxRms = Math.Max(2.0,
            frames.Where(f => Finite(f.GuideRmsArcsec)).Select(f => f.GuideRmsArcsec).DefaultIfEmpty(2).Max() * 1.10);
        DrawSeries(dc, frames, labelWidth, markerLane + bandH + gap, plotW, bandH, f => f.GuideRmsArcsec, 0, maxRms, guidePen);

        double imgTop = markerLane + 2 * (bandH + gap);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.StarDeviationPercent, -50, 50, starsPen);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.BackgroundDeviationPercent, -50, 50, backgroundPen);

        DrawStatusMarkers(dc, frames, labelWidth, plotW, markerLane, h);
    }

    private static void DrawStatusMarkers(
        DrawingContext dc,
        FrameQualityResult[] frames,
        double left,
        double plotW,
        double markerLane,
        double height) {

        var rejectedPen = FrozenPen(255, 110, 105, 1.5);
        var warningPen = FrozenPen(253, 214, 99, 0.9);
        var errorPen = FrozenPen(255, 120, 120, 1.1);
        var rejectedBand = FrozenBrush(44, 255, 110, 105);
        var badgeFill = FrozenBrush(230, 91, 36, 41);
        var badgeBorder = FrozenPen(255, 130, 125, 0.9);
        var badgeText = FrozenBrush(255, 255, 255);

        for (int i = 0; i < frames.Length; i++) {
            var frame = frames[i];
            double x = frames.Length == 1 ? left + plotW / 2 : left + i * plotW / (frames.Length - 1.0);

            if (frame.Status == FrameStatus.Rejected) {
                dc.DrawRectangle(rejectedBand, null, new Rect(x - 2.5, markerLane, 5, Math.Max(0, height - markerLane)));
                dc.DrawLine(rejectedPen, new Point(x, markerLane), new Point(x, height));
                DrawCauseBadge(dc, RejectionVisual.GetIcons(frame), x, frame.FrameIndex, markerLane, badgeFill, badgeBorder, badgeText);
                continue;
            }

            if (frame.Status == FrameStatus.Warning) {
                dc.DrawLine(warningPen, new Point(x, markerLane), new Point(x, height));
                continue;
            }

            if (frame.Status == FrameStatus.Error) {
                dc.DrawLine(errorPen, new Point(x, markerLane), new Point(x, height));
                DrawCauseBadge(dc, RejectionVisual.ErrorIcon, x, frame.FrameIndex, markerLane, badgeFill, badgeBorder, badgeText);
            }
        }
    }

    private static void DrawCauseBadge(
        DrawingContext dc,
        string icons,
        double x,
        int frameIndex,
        double markerLane,
        Brush fill,
        Pen border,
        Brush textBrush) {

        if (string.IsNullOrWhiteSpace(icons)) return;

        var text = new FormattedText(
            icons,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Emoji"),
            10.5,
            textBrush,
            1.0);

        double badgeHeight = 13;
        double badgeWidth = Math.Max(16, text.Width + 7);
        double y = frameIndex % 2 == 0 ? 1 : Math.Max(1, markerLane - badgeHeight - 1);
        var rect = new Rect(x - badgeWidth / 2, y, badgeWidth, badgeHeight);
        dc.DrawRoundedRectangle(fill, border, rect, 4, 4);
        dc.DrawText(text, new Point(rect.X + (badgeWidth - text.Width) / 2, rect.Y - 0.5));
    }

    private static void DrawRejectLaneLabel(DrawingContext dc, Brush brush) {
        var title = new FormattedText(
            "REJECT",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            8,
            brush,
            1.0);
        dc.DrawText(title, new Point(2, 3));

        var icons = new FormattedText(
            $"{RejectionVisual.GuideIcon} {RejectionVisual.SkyIcon} {RejectionVisual.BackgroundIcon}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Emoji"),
            9.5,
            brush,
            1.0);
        dc.DrawText(icons, new Point(2, 14));
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
            CultureInfo.InvariantCulture,
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

    private static SolidColorBrush FrozenBrush(byte a, byte r, byte g, byte b) {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(byte r, byte g, byte b, double thickness) {
        var pen = new Pen(FrozenBrush(r, g, b), thickness);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
