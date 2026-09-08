using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace NINA.Plugin.QualitySessionMeter.UI;

/// <summary>
/// Presentation-only overlay that adds a readable frame-number axis to the existing QSM
/// multichannel timeline without changing any quality/session logic.
///
/// The timeline already reserves a 38px event lane above the three data bands. Cause badges
/// occupy the upper half of that lane; this adorner uses the otherwise-unused lower half for
/// adaptive Frame # tick labels. This keeps frame identity visible without stealing height from
/// the Quality/Confidence, Guide RMS, or Stars/Background plots.
/// </summary>
internal sealed class TimelineFrameAxisAdorner : Adorner, IDisposable {
    private readonly QualityTimelineControl timeline;
    private readonly DependencyPropertyDescriptor itemsSourceDescriptor;
    private INotifyCollectionChanged observedCollection;
    private bool disposed;

    public TimelineFrameAxisAdorner(QualityTimelineControl adornedElement) : base(adornedElement) {
        timeline = adornedElement ?? throw new ArgumentNullException(nameof(adornedElement));
        IsHitTestVisible = false;

        itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
            QualityTimelineControl.ItemsSourceProperty,
            typeof(QualityTimelineControl));
        itemsSourceDescriptor?.AddValueChanged(timeline, ItemsSourceChanged);

        timeline.SizeChanged += TimelineSizeChanged;
        AttachCollection(timeline.ItemsSource);
    }

    private void TimelineSizeChanged(object sender, SizeChangedEventArgs e) => InvalidateVisual();

    private void ItemsSourceChanged(object sender, EventArgs e) {
        AttachCollection(timeline.ItemsSource);
        InvalidateVisual();
    }

    private void AttachCollection(IEnumerable source) {
        if (observedCollection != null) observedCollection.CollectionChanged -= CollectionChanged;
        observedCollection = source as INotifyCollectionChanged;
        if (observedCollection != null) observedCollection.CollectionChanged += CollectionChanged;
    }

    private void CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);
        if (disposed) return;

        var width = timeline.ActualWidth;
        if (width <= 80) return;

        var frames = timeline.ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();

        // Must stay exactly aligned with QualityTimelineControl's render geometry.
        var labelWidth = Math.Clamp(width * 0.27, 150, 215);
        var plotWidth = Math.Max(1, width - labelWidth - 1);
        const double markerLane = 38;
        const double tickTop = 31;
        const double labelY = 20.5;

        var foreground = ThemeBrush("ButtonForegroundBrush", 220, 224, 230);
        var secondary = ThemeBrush("ButtonForegroundBrush", 190, 196, 204);
        var accent = ThemeBrush("PrimaryBrush", 138, 180, 248);
        var border = ThemeBrush("BorderBrush", 80, 88, 98);
        var tickPen = new Pen(border, 0.8);

        // Explicit axis name in the legend lane so the numbers are not ambiguous.
        dc.DrawText(Format("FRAME #", 7.8, secondary, FontWeights.SemiBold), new Point(3, labelY + 1));

        // A subtle baseline visually associates the labels with the timeline's x positions.
        dc.DrawLine(tickPen, new Point(labelWidth, markerLane - 0.5), new Point(labelWidth + plotWidth, markerLane - 0.5));

        if (frames.Length == 0) return;

        // Adaptive density: approximately one label every 70px, always preserving first+last.
        // On narrow/mobile-like dock widths this naturally collapses to 2–3 labels; on a wide
        // panel it can show up to 12 without becoming a wall of numbers.
        var desiredTicks = Math.Clamp((int)Math.Floor(plotWidth / 70.0) + 1, 2, 12);
        var tickCount = Math.Min(frames.Length, desiredTicks);
        var indexes = BuildTickIndexes(frames.Length, tickCount);

        foreach (var index in indexes) {
            var x = frames.Length == 1
                ? labelWidth + plotWidth / 2
                : labelWidth + index * plotWidth / (frames.Length - 1.0);

            dc.DrawLine(tickPen, new Point(x, tickTop), new Point(x, markerLane));

            var isLatest = index == frames.Length - 1;
            var label = Format("#" + frames[index].FrameIndex.ToString(CultureInfo.InvariantCulture),
                isLatest ? 8.4 : 8.0,
                isLatest ? accent : foreground,
                isLatest ? FontWeights.SemiBold : FontWeights.Normal);

            double textX;
            if (index == 0) textX = x + 2;
            else if (index == frames.Length - 1) textX = x - label.Width - 2;
            else textX = x - label.Width / 2;

            // Keep labels inside the plot even when the first/last frame number becomes wide.
            textX = Math.Clamp(textX, labelWidth + 1, Math.Max(labelWidth + 1, labelWidth + plotWidth - label.Width - 1));
            dc.DrawText(label, new Point(textX, labelY));
        }
    }

    private static IReadOnlyList<int> BuildTickIndexes(int frameCount, int tickCount) {
        if (frameCount <= 0 || tickCount <= 0) return Array.Empty<int>();
        if (frameCount == 1 || tickCount == 1) return new[] { 0 };

        var indexes = new SortedSet<int> { 0, frameCount - 1 };
        for (var i = 1; i < tickCount - 1; i++) {
            var index = (int)Math.Round(i * (frameCount - 1.0) / (tickCount - 1.0));
            indexes.Add(Math.Clamp(index, 0, frameCount - 1));
        }
        return indexes.ToArray();
    }

    private static FormattedText Format(string text, double size, Brush brush, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, 1.0);

    private static Brush ThemeBrush(string key, byte r, byte g, byte b) {
        try {
            if (Application.Current?.TryFindResource(key) is Brush brush) return brush;
        } catch { }
        var fallback = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (fallback.CanFreeze) fallback.Freeze();
        return fallback;
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        if (observedCollection != null) observedCollection.CollectionChanged -= CollectionChanged;
        observedCollection = null;
        timeline.SizeChanged -= TimelineSizeChanged;
        itemsSourceDescriptor?.RemoveValueChanged(timeline, ItemsSourceChanged);
    }
}

/// <summary>
/// Attaches exactly one frame-axis adorner to each timeline when AvalonDock has created an
/// AdornerLayer. Attachment/removal always runs on the timeline's dispatcher.
/// </summary>
internal static class TimelineFrameAxisBehavior {
    private sealed class Holder {
        public AdornerLayer Layer { get; init; }
        public TimelineFrameAxisAdorner Adorner { get; init; }
    }

    private static readonly ConditionalWeakTable<QualityTimelineControl, Holder> Attached = new();

    public static void Attach(QualityTimelineControl timeline) {
        if (timeline == null || Attached.TryGetValue(timeline, out _)) return;

        timeline.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
            if (!timeline.IsLoaded || Attached.TryGetValue(timeline, out _)) return;
            var layer = AdornerLayer.GetAdornerLayer(timeline);
            if (layer == null) return;

            var adorner = new TimelineFrameAxisAdorner(timeline);
            layer.Add(adorner);
            Attached.Add(timeline, new Holder { Layer = layer, Adorner = adorner });
            timeline.Unloaded += TimelineUnloaded;
        }));
    }

    private static void TimelineUnloaded(object sender, RoutedEventArgs e) {
        if (sender is not QualityTimelineControl timeline) return;
        timeline.Unloaded -= TimelineUnloaded;

        if (!Attached.TryGetValue(timeline, out var holder)) return;
        try { holder.Layer?.Remove(holder.Adorner); } catch { }
        holder.Adorner?.Dispose();
        Attached.Remove(timeline);
    }
}
