using NINA.Plugin.QualitySessionMeter.Models;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.QualitySessionMeter.UI;

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
        if (w <= 2 || h <= 2) return;

        var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(48, 54, 61)), 1);
        guidePen.Freeze();
        foreach (var q in new[] { 50.0, 65.0, 80.0, 90.0 }) {
            double y = h - (q / 100.0) * h;
            dc.DrawLine(guidePen, new Point(0, y), new Point(w, y));
        }

        var frames = ItemsSource?.Cast<object>().OfType<FrameQualityResult>().TakeLast(160).ToArray()
            ?? Array.Empty<FrameQualityResult>();
        if (frames.Length == 0) return;

        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(138, 180, 248)), 2);
        linePen.Freeze();

        Point? previous = null;
        for (int i = 0; i < frames.Length; i++) {
            double x = frames.Length == 1 ? w / 2 : i * (w - 1) / (frames.Length - 1.0);
            double quality = Math.Clamp(frames[i].OverallQuality, 0, 100);
            double y = h - quality / 100.0 * (h - 1);
            var p = new Point(x, y);

            if (previous.HasValue) dc.DrawLine(linePen, previous.Value, p);

            Brush brush = frames[i].Status switch {
                FrameStatus.Rejected => new SolidColorBrush(Color.FromRgb(242, 139, 130)),
                FrameStatus.Warning => new SolidColorBrush(Color.FromRgb(253, 214, 99)),
                FrameStatus.Learning => new SolidColorBrush(Color.FromRgb(189, 193, 198)),
                FrameStatus.Error => new SolidColorBrush(Color.FromRgb(255, 120, 120)),
                _ => new SolidColorBrush(Color.FromRgb(129, 201, 149))
            };
            brush.Freeze();
            dc.DrawEllipse(brush, null, p, 3.5, 3.5);
            previous = p;
        }
    }
}
