using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Three-band live timeline: Quality/Confidence, Guide RMS, and image-signal deviations.
/// Presentation-only: it never participates in quality decisions.
/// </summary>
public sealed class QualityTimelineControl : FrameworkElement {
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty MaxGuideRmsProperty =
        DependencyProperty.Register(nameof(MaxGuideRms), typeof(double), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxStarLossPercentProperty =
        DependencyProperty.Register(nameof(MaxStarLossPercent), typeof(double), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(35.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxBackgroundIncreasePercentProperty =
        DependencyProperty.Register(nameof(MaxBackgroundIncreasePercent), typeof(double), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxBackgroundDecreasePercentProperty =
        DependencyProperty.Register(nameof(MaxBackgroundDecreasePercent), typeof(double), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EnableGuideRmsProperty =
        DependencyProperty.Register(nameof(EnableGuideRms), typeof(bool), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EnableStarCountProperty =
        DependencyProperty.Register(nameof(EnableStarCount), typeof(bool), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EnableBackgroundProperty =
        DependencyProperty.Register(nameof(EnableBackground), typeof(bool), typeof(QualityTimelineControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private const string TimelineHelp =
        "QSM timeline. Blue = Quality (0–100), purple = Confidence (0–100), green = Guide RMS in arcseconds. " +
        "Yellow Stars Δ and salmon Background Δ are percentages RELATIVE TO the rolling clean-frame baseline; the dashed 0% line is that baseline. " +
        "Dashed colored lines show active reject limits. Red vertical lines mark REJECTED frames, amber lines mark WARNING frames, and ! marks analysis errors. " +
        "G = guiding/tracking, S = stars/transparency/cloud, B = background/haze/sky brightness. Hover the graph for exact frame values, baseline values and limits.";

    private sealed class MarkerHit {
        public Rect Area { get; init; }
        public string Text { get; init; } = "";
    }

    private readonly List<MarkerHit> markerHits = new();
    private INotifyCollectionChanged observed;
    private string activeToolTip = TimelineHelp;
    private FrameQualityResult[] renderedFrames = Array.Empty<FrameQualityResult>();
    private double renderedLeft;
    private double renderedPlotWidth;
    private double renderedMarkerLane;
    private double renderedBandHeight;
    private double renderedGap;

    public QualityTimelineControl() {
        ToolTipService.SetShowDuration(this, 30000);
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetToolTip(this, TimelineHelp);
    }

    public IEnumerable ItemsSource {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double MaxGuideRms { get => (double)GetValue(MaxGuideRmsProperty); set => SetValue(MaxGuideRmsProperty, value); }
    public double MaxStarLossPercent { get => (double)GetValue(MaxStarLossPercentProperty); set => SetValue(MaxStarLossPercentProperty, value); }
    public double MaxBackgroundIncreasePercent { get => (double)GetValue(MaxBackgroundIncreasePercentProperty); set => SetValue(MaxBackgroundIncreasePercentProperty, value); }
    public double MaxBackgroundDecreasePercent { get => (double)GetValue(MaxBackgroundDecreasePercentProperty); set => SetValue(MaxBackgroundDecreasePercentProperty, value); }
    public bool EnableGuideRms { get => (bool)GetValue(EnableGuideRmsProperty); set => SetValue(EnableGuideRmsProperty, value); }
    public bool EnableStarCount { get => (bool)GetValue(EnableStarCountProperty); set => SetValue(EnableStarCountProperty, value); }
    public bool EnableBackground { get => (bool)GetValue(EnableBackgroundProperty); set => SetValue(EnableBackgroundProperty, value); }

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

        string text = markerHits.FirstOrDefault(x => x.Area.Contains(p))?.Text;
        if (text == null && renderedFrames.Length > 0 && p.X >= renderedLeft && p.X <= renderedLeft + renderedPlotWidth && p.Y >= renderedMarkerLane) {
            int i = renderedFrames.Length == 1
                ? 0
                : (int)Math.Round((p.X - renderedLeft) / Math.Max(1, renderedPlotWidth) * (renderedFrames.Length - 1));
            i = Math.Clamp(i, 0, renderedFrames.Length - 1);
            text = BuildFrameTooltip(renderedFrames[i], p.Y);
        }
        text ??= TimelineHelp;

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
        if (w <= 80 || h <= 100) return;

        var frames = ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();

        // Reserve a real legend column. The old fixed 154px lane let long legend text draw
        // directly over the plot. Every label is now clipped to this column.
        double labelWidth = Math.Clamp(w * 0.27, 150, 215);
        const double markerLane = 38;
        const double gap = 6;
        double plotW = Math.Max(1, w - labelWidth - 1);
        double bandH = Math.Max(22, (h - markerLane - gap * 2 - 1) / 3.0);

        renderedFrames = frames;
        renderedLeft = labelWidth;
        renderedPlotWidth = plotW;
        renderedMarkerLane = markerLane;
        renderedBandHeight = bandH;
        renderedGap = gap;

        // Structural colors follow the active N.I.N.A. profile theme. Diagnostic series keep
        // stable semantic colors so Quality/Confidence/Guide/Stars/Background remain recognizable.
        var background = ThemeBrush("SecondaryBackgroundBrush", 18, 22, 28);
        var borderPen = ThemePen("BorderBrush", 48, 54, 61, 1.0);
        var gridPen = ThemePen("BorderBrush", 56, 62, 70, 0.7);
        var textBrush = ThemeBrush("ButtonForegroundBrush", 220, 224, 230);
        var secondaryText = ThemeBrush("ButtonForegroundDisabledBrush", 150, 158, 168);
        var qualityBrush = FrozenBrush(138, 180, 248);
        var confidenceBrush = FrozenBrush(197, 138, 249);
        var guideBrush = FrozenBrush(129, 201, 149);
        var starsBrush = FrozenBrush(253, 214, 99);
        var backgroundBrush = FrozenBrush(242, 139, 130);
        var qualityPen = FrozenPen(138, 180, 248, 1.8);
        var confidencePen = FrozenPen(197, 138, 249, 1.5);
        var guidePen = FrozenPen(129, 201, 149, 1.7);
        var starsPen = FrozenPen(253, 214, 99, 1.6);
        var backgroundPen = FrozenPen(242, 139, 130, 1.6);

        DrawEventLaneLabel(dc, textBrush, secondaryText, labelWidth - 8);

        double topBand = markerLane;
        double rmsBand = markerLane + bandH + gap;
        double imgBand = markerLane + 2 * (bandH + gap);

        DrawLegendItem(dc, 3, topBand + 3, labelWidth - 8, qualityBrush, secondaryText, "Quality", "0–100");
        DrawLegendItem(dc, 3, topBand + 25, labelWidth - 8, confidenceBrush, secondaryText, "Confidence", "0–100");
        DrawLegendItem(dc, 3, rmsBand + 5, labelWidth - 8, guideBrush, secondaryText, "Guide RMS", "arcsec · lower is better");
        DrawLegendItem(dc, 3, imgBand + 3, labelWidth - 8, starsBrush, secondaryText, "Stars Δ", EnableStarCount ? $"% vs baseline · reject < -{MaxStarLossPercent:0.#}%" : "% vs baseline · disabled");
        DrawLegendItem(dc, 3, imgBand + 27, labelWidth - 8, backgroundBrush, secondaryText, "Background Δ", EnableBackground ? $"limits -{MaxBackgroundDecreasePercent:0.#}/+{MaxBackgroundIncreasePercent:0.#}%" : "% vs baseline · disabled");

        // Everything belonging to the graph is clipped to the plot rectangle. This prevents
        // marker badges/reference labels from leaking into the legend on narrow dock layouts.
        dc.PushClip(new RectangleGeometry(new Rect(labelWidth, 0, plotW, h)));

        for (int b = 0; b < 3; b++) {
            double y = markerLane + b * (bandH + gap);
            dc.DrawRectangle(background, borderPen, new Rect(labelWidth, y, plotW, bandH));
            dc.DrawLine(gridPen, new Point(labelWidth, y + bandH / 2), new Point(labelWidth + plotW, y + bandH / 2));
        }

        DrawScaleHint(dc, labelWidth + plotW - 29, topBand + 2, "100", secondaryText);
        DrawScaleHint(dc, labelWidth + plotW - 18, topBand + bandH - 13, "0", secondaryText);

        if (frames.Length == 0) {
            DrawCenteredMessage(dc, labelWidth, markerLane, plotW, h - markerLane, "Waiting for assessed LIGHT frames", secondaryText);
            dc.Pop();
            return;
        }

        DrawSeries(dc, frames, labelWidth, topBand, plotW, bandH, f => f.OverallQuality, 0, 100, qualityPen);
        DrawSeries(dc, frames, labelWidth, topBand, plotW, bandH, f => f.ConfidenceScore, 0, 100, confidencePen);

        double maxObservedRms = frames.Where(f => Finite(f.GuideRmsArcsec)).Select(f => f.GuideRmsArcsec).DefaultIfEmpty(0).Max();
        double maxRms = Math.Max(2.0, maxObservedRms * 1.15);
        if (EnableGuideRms && Finite(MaxGuideRms) && MaxGuideRms > 0) maxRms = Math.Max(maxRms, MaxGuideRms * 1.30);
        DrawSeries(dc, frames, labelWidth, rmsBand, plotW, bandH, f => f.GuideRmsArcsec, 0, maxRms, guidePen);

        if (EnableGuideRms && Finite(MaxGuideRms) && MaxGuideRms > 0) {
            double y = ValueToY(MaxGuideRms, 0, maxRms, rmsBand, bandH);
            DrawReferenceLine(dc, labelWidth, plotW, y, FrozenDashedPen(129, 201, 149, 1.0), $"RMS limit {MaxGuideRms:0.00}\"", guideBrush, background);
        }
        DrawScaleHint(dc, labelWidth + 4, rmsBand + 2, $"0–{maxRms:0.0}\"", secondaryText);

        double observedAbs = frames.SelectMany(f => new[] { AbsFinite(f.StarDeviationPercent), AbsFinite(f.BackgroundDeviationPercent) }).DefaultIfEmpty(0).Max();
        double thresholdAbs = Math.Max(MaxStarLossPercent, Math.Max(MaxBackgroundIncreasePercent, MaxBackgroundDecreasePercent));
        double imgAbsMax = Math.Max(50.0, Math.Max(observedAbs * 1.15, thresholdAbs * 1.25));
        imgAbsMax = Math.Min(500.0, imgAbsMax);

        DrawSeries(dc, frames, labelWidth, imgBand, plotW, bandH, f => f.StarDeviationPercent, -imgAbsMax, imgAbsMax, starsPen);
        DrawSeries(dc, frames, labelWidth, imgBand, plotW, bandH, f => f.BackgroundDeviationPercent, -imgAbsMax, imgAbsMax, backgroundPen);

        double zeroY = ValueToY(0, -imgAbsMax, imgAbsMax, imgBand, bandH);
        DrawReferenceLine(dc, labelWidth, plotW, zeroY, ThemeDashedPen("BorderBrush", 120, 128, 138, 1.1), "0% rolling baseline", textBrush, background);

        if (EnableStarCount && MaxStarLossPercent > 0) {
            double y = ValueToY(-MaxStarLossPercent, -imgAbsMax, imgAbsMax, imgBand, bandH);
            DrawReferenceLine(dc, labelWidth, plotW, y, FrozenDashedPen(253, 214, 99, 0.9), null, starsBrush, background);
        }
        if (EnableBackground) {
            if (MaxBackgroundIncreasePercent > 0) {
                double y = ValueToY(MaxBackgroundIncreasePercent, -imgAbsMax, imgAbsMax, imgBand, bandH);
                DrawReferenceLine(dc, labelWidth, plotW, y, FrozenDashedPen(242, 139, 130, 0.9), null, backgroundBrush, background);
            }
            if (MaxBackgroundDecreasePercent > 0) {
                double y = ValueToY(-MaxBackgroundDecreasePercent, -imgAbsMax, imgAbsMax, imgBand, bandH);
                DrawReferenceLine(dc, labelWidth, plotW, y, FrozenDashedPen(242, 139, 130, 0.9), null, backgroundBrush, background);
            }
        }
        DrawScaleHint(dc, labelWidth + 4, imgBand + 2, $"±{imgAbsMax:0}%", secondaryText);

        DrawStatusMarkers(dc, frames, labelWidth, plotW, markerLane, h - 1);
        dc.Pop();
    }

    private string BuildFrameTooltip(FrameQualityResult frame, double y) {
        string band = "FRAME";
        if (y >= renderedMarkerLane && y < renderedMarkerLane + renderedBandHeight) band = "QUALITY / CONFIDENCE";
        else if (y < renderedMarkerLane + 2 * renderedBandHeight + renderedGap) band = "GUIDE RMS";
        else band = "IMAGE DELTA vs ROLLING BASELINE";

        var sb = new StringBuilder();
        sb.Append("Frame #").Append(frame.FrameIndex).Append(" · ").Append(frame.StatusText).Append(" · ").AppendLine(band);
        sb.Append("Quality ").Append(FormatValue(frame.OverallQuality, "0")).Append(" / 100 · Confidence ").Append(double.IsNaN(frame.ConfidenceScore) ? "N/A" : frame.ConfidenceScore.ToString("0", CultureInfo.InvariantCulture) + "%").AppendLine();
        sb.Append("Guide RMS ").Append(FormatArcsec(frame.GuideRmsArcsec));
        if (EnableGuideRms) sb.Append(" · limit ").Append(MaxGuideRms.ToString("0.00", CultureInfo.InvariantCulture)).Append('"');
        sb.AppendLine();

        if (frame.StarCount >= 0) {
            sb.Append("Stars ").Append(frame.StarCount.ToString(CultureInfo.InvariantCulture));
            if (Finite(frame.StarBaseline) && frame.StarBaseline > 0) {
                sb.Append(" · baseline ").Append(frame.StarBaseline.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" · Δ ").Append(FormatPercent(frame.StarDeviationPercent));
                if (EnableStarCount) sb.Append(" · reject below -").Append(MaxStarLossPercent.ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
            } else {
                sb.Append(" · baseline learning/not ready");
            }
            sb.AppendLine();
        }

        if (Finite(frame.BackgroundMedian)) {
            sb.Append("Background ").Append(frame.BackgroundMedian.ToString("0.##", CultureInfo.InvariantCulture));
            if (Finite(frame.BackgroundBaseline) && frame.BackgroundBaseline > 0) {
                sb.Append(" · baseline ").Append(frame.BackgroundBaseline.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append(" · Δ ").Append(FormatPercent(frame.BackgroundDeviationPercent));
                if (EnableBackground) sb.Append(" · limits -").Append(MaxBackgroundDecreasePercent.ToString("0.#", CultureInfo.InvariantCulture)).Append("/+").Append(MaxBackgroundIncreasePercent.ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
            } else {
                sb.Append(" · baseline learning/not ready");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(frame.ProbableCause)) sb.Append("Cause: ").Append(frame.ProbableCause);
        if (!string.IsNullOrWhiteSpace(frame.ReasonText) && frame.ReasonText != "—") sb.Append(" · ").Append(frame.ReasonText);
        return sb.ToString();
    }

    private void DrawStatusMarkers(DrawingContext dc, FrameQualityResult[] frames, double left, double plotW, double markerLane, double height) {
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
                markerHits.Add(new MarkerHit { Area = new Rect(x - 6, 0, 12, Math.Max(1, height)), Text = RejectionVisual.GetTooltip(frame) });
                var rect = MeasureCauseBadge(RejectionVisual.GetIcons(frame), x, badgeText);
                if (rect.Left > lastBadgeRight + 3) {
                    DrawCauseBadge(dc, RejectionVisual.GetIcons(frame), rect, badgeFill, badgeBorder, badgeText);
                    lastBadgeRight = rect.Right;
                }
                continue;
            }

            if (frame.Status == FrameStatus.Warning) {
                dc.DrawLine(warningPen, new Point(x, markerLane), new Point(x, height));
                markerHits.Add(new MarkerHit { Area = new Rect(x - 5, markerLane, 10, Math.Max(1, height - markerLane)), Text = $"Frame #{frame.FrameIndex} · WARNING · {frame.ProbableCause} · {frame.ReasonText}" });
                continue;
            }

            if (frame.Status == FrameStatus.Error) {
                dc.DrawLine(errorPen, new Point(x, markerLane), new Point(x, height));
                markerHits.Add(new MarkerHit { Area = new Rect(x - 6, 0, 12, Math.Max(1, height)), Text = RejectionVisual.GetTooltip(frame) });
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

    private static void DrawCauseBadge(DrawingContext dc, string codes, Rect rect, Brush fill, Pen border, Brush textBrush) {
        if (string.IsNullOrWhiteSpace(codes) || rect.IsEmpty) return;
        var text = Format(codes, 9.5, textBrush, FontWeights.SemiBold);
        dc.DrawRoundedRectangle(fill, border, rect, 4, 4);
        dc.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2 - 0.5));
    }

    private static void DrawEventLaneLabel(DrawingContext dc, Brush brush, Brush secondary, double maxWidth) {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(1, maxWidth), 36)));
        dc.DrawText(Format("EVENTS", 8.5, brush, FontWeights.SemiBold), new Point(3, 2));
        dc.DrawText(Format("G guide · S sky · B background · ! error", 8.1, secondary, FontWeights.Normal), new Point(3, 16));
        dc.Pop();
    }

    private static void DrawLegendItem(DrawingContext dc, double x, double y, double maxWidth, Brush color, Brush secondary, string name, string detail) {
        dc.PushClip(new RectangleGeometry(new Rect(x, y - 1, Math.Max(1, maxWidth), 23)));
        dc.DrawEllipse(color, null, new Point(x + 5, y + 6), 3.2, 3.2);
        dc.DrawText(Format(name, 9.1, color, FontWeights.SemiBold), new Point(x + 13, y));
        dc.DrawText(Format(detail, 7.7, secondary, FontWeights.Normal), new Point(x + 13, y + 11));
        dc.Pop();
    }

    private static void DrawReferenceLine(DrawingContext dc, double left, double width, double y, Pen pen, string label, Brush labelBrush, Brush backgroundBrush) {
        dc.DrawLine(pen, new Point(left, y), new Point(left + width, y));
        if (string.IsNullOrWhiteSpace(label)) return;
        var text = Format(label, 8.0, labelBrush, FontWeights.SemiBold);
        double tx = left + width - text.Width - 7;
        double ty = Math.Max(1, y - text.Height - 2);
        dc.DrawRectangle(backgroundBrush, null, new Rect(tx - 3, ty - 1, text.Width + 6, text.Height + 2));
        dc.DrawText(text, new Point(tx, ty));
    }

    private static void DrawScaleHint(DrawingContext dc, double x, double y, string text, Brush brush) =>
        dc.DrawText(Format(text, 7.6, brush, FontWeights.Normal), new Point(x, y));

    private static void DrawCenteredMessage(DrawingContext dc, double left, double top, double width, double height, string text, Brush brush) {
        var ft = Format(text, 10, brush, FontWeights.Normal);
        dc.DrawText(ft, new Point(left + (width - ft.Width) / 2, top + (height - ft.Height) / 2));
    }

    private static void DrawSeries(DrawingContext dc, FrameQualityResult[] frames, double left, double top, double width, double height, Func<FrameQualityResult, double> selector, double min, double max, Pen pen) {
        Point? previous = null;
        for (int i = 0; i < frames.Length; i++) {
            double raw = selector(frames[i]);
            if (!Finite(raw)) { previous = null; continue; }
            double value = Math.Clamp(raw, min, max);
            double x = frames.Length == 1 ? left + width / 2 : left + i * width / (frames.Length - 1.0);
            double y = ValueToY(value, min, max, top, height);
            var point = new Point(x, y);
            if (previous.HasValue) dc.DrawLine(pen, previous.Value, point);
            previous = point;
        }
    }

    private static double ValueToY(double value, double min, double max, double top, double height) =>
        top + height * (1.0 - (Math.Clamp(value, min, max) - min) / Math.Max(0.000001, max - min));

    private static FormattedText Format(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush, 1.0);

    private static string FormatValue(double value, string format) => Finite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : "N/A";
    private static string FormatArcsec(double value) => Finite(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) + "\"" : "N/A";
    private static string FormatPercent(double value) => Finite(value) ? value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "N/A";
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double AbsFinite(double value) => Finite(value) ? Math.Abs(value) : 0;

    private static Brush ThemeBrush(string key, byte fallbackR, byte fallbackG, byte fallbackB) {
        try {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        } catch { }
        return FrozenBrush(fallbackR, fallbackG, fallbackB);
    }

    private static Pen ThemePen(string key, byte fallbackR, byte fallbackG, byte fallbackB, double thickness) =>
        new(ThemeBrush(key, fallbackR, fallbackG, fallbackB), thickness);

    private static Pen ThemeDashedPen(string key, byte fallbackR, byte fallbackG, byte fallbackB, double thickness) =>
        new(ThemeBrush(key, fallbackR, fallbackG, fallbackB), thickness) { DashStyle = DashStyles.Dash };

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

    private static Pen FrozenDashedPen(byte r, byte g, byte b, double thickness) {
        var pen = new Pen(FrozenBrush(r, g, b), thickness) { DashStyle = DashStyles.Dash };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}