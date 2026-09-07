using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Compact three-band timeline: Quality/Confidence, Guide RMS, and image-signal deviations.
/// Rejected/warning/error markers are presentation-only and never participate in quality decisions.
/// </summary>
public sealed class QualityTimelineControl : FrameworkElement {
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    private const string TimelineHelp =
        "QSM multichannel timeline. Top band: blue = Quality score, purple = Confidence. " +
        "Middle band: green = exposure Guide RMS. Bottom band: yellow = star-count deviation, salmon = background deviation. " +
        "Vertical red lines mark REJECTED frames, amber lines mark WARNING frames and red ! marks analysis errors. " +
        "Cause codes: G = guiding/tracking, S = stars/transparency/cloud, B = background/haze/sky brightness. " +
        "Hover an event line for the exact frame, cause and raw rejection reason.";

    private sealed class MarkerHit {
        public Rect Area { get; init; }
        public string Text { get; init; } = "";
    }

    private readonly List<MarkerHit> markerHits = new();
    private INotifyCollectionChanged observed;
    private string activeToolTip = TimelineHelp;

    public QualityTimelineControl() {
        ToolTipService.SetShowDuration(this, 30000);
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetToolTip(this, TimelineHelp);
    }

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

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        string text = markerHits.FirstOrDefault(x => x.Area.Contains(p))?.Text ?? TimelineHelp;
        if (string.Equals(text, activeToolTip, StringComparison.Ordinal)) return;
        activeToolTip = text;
        ToolTipService.SetToolTip(this, text);
    }

    protected override void OnMouseLeave(MouseEventArgs e) {
        base.OnMouseLeave(e);
        if (string.Equals(activeToolTip, TimelineHelp, StringComparison.Ordinal)) return;
        activeToolTip = TimelineHelp;
        ToolTipService.SetToolTip(this, TimelineHelp);
    }

    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        markerHits.Clear();

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 40 || h <= 70) return;

        var frames = ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();

        const double labelWidth = 108;
        const double markerLane = 34;
        const double gap = 5;
        double plotW = Math.Max(1, w - labelWidth - 1);
        double bandH = Math.Max(18, (h - markerLane - gap * 2 - 1) / 3.0);

        var background = FrozenBrush(18, 22, 28);
        var borderPen = FrozenPen(48, 54, 61, 1);
        var textBrush = FrozenBrush(154, 160, 166);
        var qualityPen = FrozenPen(138, 180, 248, 1.8);
        var confidencePen = FrozenPen(197, 138, 249, 1.4);
        var guidePen = FrozenPen(129, 201, 149, 1.6);
        var starsPen = FrozenPen(253, 214, 99, 1.5);
        var backgroundPen = FrozenPen(242, 139, 130, 1.5);

        DrawEventLaneLabel(dc, textBrush);

        for (int b = 0; b < 3; b++) {
            double y = markerLane + b * (bandH + gap);
            dc.DrawRectangle(background, borderPen, new Rect(labelWidth, y, plotW, bandH));
            dc.DrawLine(borderPen, new Point(labelWidth, y + bandH / 2), new Point(labelWidth + plotW, y + bandH / 2));
        }

        DrawLabel(dc, "Q / CONF", markerLane + 2, textBrush);
        DrawLabel(dc, "RMS", markerLane + bandH + gap + 2, textBrush);
        DrawLabel(dc, "IMG DELTA", markerLane + 2 * (bandH + gap) + 2, textBrush);

        if (frames.Length == 0) return;

        DrawSeries(dc, frames, labelWidth, markerLane, plotW, bandH, f => f.OverallQuality, 0, 100, qualityPen);
        DrawSeries(dc, frames, labelWidth, markerLane, plotW, bandH, f => f.ConfidenceScore, 0, 100, confidencePen);

        double maxRms = Math.Max(2.0,
            frames.Where(f => Finite(f.GuideRmsArcsec)).Select(f => f.GuideRmsArcsec).DefaultIfEmpty(2).Max() * 1.10);
        DrawSeries(dc, frames, labelWidth, markerLane + bandH + gap, plotW, bandH, f => f.GuideRmsArcsec, 0, maxRms, guidePen);

        double imgTop = markerLane + 2 * (bandH + gap);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.StarDeviationPercent, -50, 50, starsPen);
        DrawSeries(dc, frames, labelWidth, imgTop, plotW, bandH, f => f.BackgroundDeviationPercent, -50, 50, backgroundPen);

        DrawStatusMarkers(dc, frames, labelWidth, plotW, markerLane, h - 1);
    }

    private void DrawStatusMarkers(
        DrawingContext dc,
        FrameQualityResult[] frames,
        double left,
        double plotW,
        double markerLane,
        double height) {

        var rejectedPen = FrozenPen(255, 110, 105, 1.5);
        var warningPen = FrozenPen(253, 214, 99, 1.0);
        var errorPen = FrozenPen(255, 120, 120, 1.4);
        var rejectedBand = FrozenBrush(34, 255, 110, 105);
        var badgeFill = FrozenBrush(235, 91, 36, 41);
        var badgeBorder = FrozenPen(255, 130, 125, 0.9);
        var badgeText = FrozenBrush(255, 255, 255);

        double lastBadgeRight = double.NegativeInfinity;

        for (int i = 0; i < frames.Length; i++) {
            var frame = frames[i];
            double x = frames.Length == 1 ? left + plotW / 2 : left + i * plotW / (frames.Length - 1.0);

            if (frame.Status == FrameStatus.Rejected) {
                dc.DrawRectangle(rejectedBand, null, new Rect(x - 2.5, markerLane, 5, Math.Max(0, height - markerLane)));
                dc.DrawLine(rejectedPen, new Point(x, markerLane), new Point(x, height));
                markerHits.Add(new MarkerHit {
                    Area = new Rect(x - 6, 0, 12, Math.Max(1, height)),
                    Text = RejectionVisual.GetTooltip(frame)
                });

                var rect = MeasureCauseBadge(RejectionVisual.GetIcons(frame), x, badgeText);
                if (rect.Left > lastBadgeRight + 3) {
                    DrawCauseBadge(dc, RejectionVisual.GetIcons(frame), rect, badgeFill, badgeBorder, badgeText);
                    lastBadgeRight = rect.Right;
                }
                continue;
            }

            if (frame.Status == FrameStatus.Warning) {
                dc.DrawLine(warningPen, new Point(x, markerLane), new Point(x, height));
                markerHits.Add(new MarkerHit {
                    Area = new Rect(x - 5, markerLane, 10, Math.Max(1, height - markerLane)),
                    Text = $"Frame #{frame.FrameIndex} · WARNING · {frame.ProbableCause} · {frame.ReasonText}"
                });
                continue;
            }

            if (frame.Status == FrameStatus.Error) {
                dc.DrawLine(errorPen, new Point(x, markerLane), new Point(x, height));
                markerHits.Add(new MarkerHit {
                    Area = new Rect(x - 6, 0, 12, Math.Max(1, height)),
                    Text = RejectionVisual.GetTooltip(frame)
                });

                var rect = MeasureCauseBadge(RejectionVisual.ErrorIcon, x, badgeText);
                if (rect.Left > lastBadgeRight + 3) {
                    DrawCauseBadge(dc, RejectionVisual.ErrorIcon, rect, badgeFill, badgeBorder, badgeText);
                    lastBadgeRight = rect.Right;
                }
            }
        }
    }

    private static Rect MeasureCauseBadge(string codes, double x, Brush textBrush) {
        if (string.IsNullOrWhiteSpace(codes)) return Rect.Empty;
        var text = Format(codes, 9.5, textBrush, FontWeights.SemiBold);
        const double badgeHeight = 15;
        double badgeWidth = Math.Max(17, text.Width + 8);
        return new Rect(x - badgeWidth / 2, 3, badgeWidth, badgeHeight);
    }

    private static void DrawCauseBadge(
        DrawingContext dc,
        string codes,
        Rect rect,
        Brush fill,
        Pen border,
        Brush textBrush) {

        if (string.IsNullOrWhiteSpace(codes) || rect.IsEmpty) return;
        var text = Format(codes, 9.5, textBrush, FontWeights.SemiBold);
        dc.DrawRoundedRectangle(fill, border, rect, 4, 4);
        dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2 - 0.5));
    }

    private static void DrawEventLaneLabel(DrawingContext dc, Brush brush) {
        dc.DrawText(Format("EVENTS", 8.5, brush, FontWeights.SemiBold), new Point(2, 2));
        dc.DrawText(Format("G guide  S sky  B bg  ! error", 8.2, brush, FontWeights.Normal), new Point(2, 15));
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

    private static void DrawLabel(DrawingContext dc, string text, double y, Brush brush) =>
        dc.DrawText(Format(text, 9, brush, FontWeights.Normal), new Point(2, y));

    private static FormattedText Format(string text, double size, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);

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
