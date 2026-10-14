using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BatteryInfo.Core.History;

namespace BatteryInfo.App.Controls;

/// <summary>Battery level over time: a line per component (single battery, or left/right/case) on a 0-100 % axis
/// with 25 % gridlines and day boundaries, charging spans tinted, and gaps where the device was not read.</summary>
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = Register(nameof(Series), typeof(IReadOnlyList<HistorySeries>), null);
    public static readonly DependencyProperty FromProperty = Register(nameof(From), typeof(DateTimeOffset), default(DateTimeOffset));
    public static readonly DependencyProperty ToProperty = Register(nameof(To), typeof(DateTimeOffset), default(DateTimeOffset));
    public static readonly DependencyProperty LineBrushProperty = Register(nameof(LineBrush), typeof(Brush), Brushes.DodgerBlue);
    public static readonly DependencyProperty SecondLineBrushProperty = Register(nameof(SecondLineBrush), typeof(Brush), Brushes.Orange);
    public static readonly DependencyProperty ThirdLineBrushProperty = Register(nameof(ThirdLineBrush), typeof(Brush), Brushes.MediumPurple);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty ChargingBrushProperty = Register(nameof(ChargingBrush), typeof(Brush), Brushes.Green);

    private const double LabelSize = 10;
    private const double AxisWidth = 30;
    private const double LegendHeight = 18;
    private const double DayLabelHeight = 16;
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public IReadOnlyList<HistorySeries>? Series
    {
        get => (IReadOnlyList<HistorySeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public DateTimeOffset From
    {
        get => (DateTimeOffset)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public DateTimeOffset To
    {
        get => (DateTimeOffset)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>The single battery, or the left earbud.</summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush SecondLineBrush
    {
        get => (Brush)GetValue(SecondLineBrushProperty);
        set => SetValue(SecondLineBrushProperty, value);
    }

    public Brush ThirdLineBrush
    {
        get => (Brush)GetValue(ThirdLineBrushProperty);
        set => SetValue(ThirdLineBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush ChargingBrush
    {
        get => (Brush)GetValue(ChargingBrushProperty);
        set => SetValue(ChargingBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 130 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= AxisWidth + 10 || height <= LegendHeight + DayLabelHeight + 10 || To <= From) return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(AxisWidth, LegendHeight, width - AxisWidth - 2, height - LegendHeight - DayLabelHeight);
        var series = Series ?? [];

        DrawPercentGrid(dc, plot, pixelsPerDip);
        DrawDays(dc, plot, pixelsPerDip);

        if (series.Count == 0 || series.All(s => s.Segments.Count == 0))
        {
            var empty = Label("No history yet", pixelsPerDip, LabelBrush, LabelSize + 2);
            dc.DrawText(empty, new Point(plot.Left + (plot.Width - empty.Width) / 2, plot.Top + (plot.Height - empty.Height) / 2));
            return;
        }

        dc.PushClip(new RectangleGeometry(plot));
        DrawChargingSpans(dc, plot, series);
        for (var i = 0; i < series.Count; i++) DrawSeries(dc, plot, series[i], BrushFor(i));
        dc.Pop();

        if (series.Count > 1) DrawLegend(dc, series, pixelsPerDip);
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(HistoryChart),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static string LegendName(string component) => component.Length == 0 ? "Battery" : component;

    private Brush BrushFor(int index) => index switch
    {
        0 => LineBrush,
        1 => SecondLineBrush,
        _ => ThirdLineBrush,
    };

    private double X(Rect plot, DateTimeOffset time) =>
        plot.Left + (time - From).TotalSeconds / (To - From).TotalSeconds * plot.Width;

    private static double Y(Rect plot, int percent) => plot.Bottom - Math.Clamp(percent, 0, 100) / 100.0 * plot.Height;

    private void DrawPercentGrid(DrawingContext dc, Rect plot, double pixelsPerDip)
    {
        var pen = Frozen(new Pen(GridBrush, 1));
        for (var percent = 0; percent <= 100; percent += 25)
        {
            var y = Math.Round(Y(plot, percent)) + 0.5;
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Label($"{percent}%", pixelsPerDip, LabelBrush, LabelSize);
            dc.DrawText(label, new Point(plot.Left - 4 - label.Width, y - label.Height / 2));
        }
    }

    // Local midnights as faint verticals, with the weekday under each day.
    private void DrawDays(DrawingContext dc, Rect plot, double pixelsPerDip)
    {
        var pen = Frozen(new Pen(GridBrush, 1) { DashStyle = DashStyles.Dot });
        var dayStart = new DateTimeOffset(From.Date, From.Offset);
        for (var day = dayStart; day < To; day = day.AddDays(1))
        {
            var start = day < From ? From : day;
            var end = day.AddDays(1) > To ? To : day.AddDays(1);
            if (day >= From)
            {
                var x = Math.Round(X(plot, day)) + 0.5;
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
            var left = X(plot, start);
            var right = X(plot, end);
            var label = Label(day.ToString("ddd", CultureInfo.InvariantCulture), pixelsPerDip, LabelBrush, LabelSize);
            if (right - left < label.Width + 2) continue;
            dc.DrawText(label, new Point((left + right - label.Width) / 2, plot.Bottom + 2));
        }
    }

    private void DrawChargingSpans(DrawingContext dc, Rect plot, IReadOnlyList<HistorySeries> series)
    {
        var tint = ChargingBrush.Clone();
        tint.Opacity = 0.18;
        tint.Freeze();
        foreach (var segment in series.SelectMany(s => s.Segments))
        {
            for (var i = 0; i + 1 < segment.Count; i++)
            {
                if (!segment[i].IsCharging) continue;
                var left = X(plot, segment[i].Time);
                var right = X(plot, segment[i + 1].Time);
                dc.DrawRectangle(tint, null, new Rect(left, plot.Top, Math.Max(1, right - left), plot.Height));
            }
        }
    }

    private void DrawSeries(DrawingContext dc, Rect plot, HistorySeries series, Brush brush)
    {
        var pen = Frozen(new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        foreach (var segment in series.Segments)
        {
            if (segment.Count == 1)
            {
                dc.DrawEllipse(brush, null, new Point(X(plot, segment[0].Time), Y(plot, segment[0].Percent)), 1.8, 1.8);
                continue;
            }
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(X(plot, segment[0].Time), Y(plot, segment[0].Percent)), false, false);
                context.PolyLineTo(segment.Skip(1).Select(p => new Point(X(plot, p.Time), Y(plot, p.Percent))).ToList(), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawLegend(DrawingContext dc, IReadOnlyList<HistorySeries> series, double pixelsPerDip)
    {
        var x = AxisWidth;
        for (var i = 0; i < series.Count; i++)
        {
            var pen = Frozen(new Pen(BrushFor(i), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            var label = Label(LegendName(series[i].Component), pixelsPerDip, LabelBrush, LabelSize + 1);
            var middle = LegendHeight / 2 - 1;
            dc.DrawLine(pen, new Point(x, middle), new Point(x + 12, middle));
            dc.DrawText(label, new Point(x + 16, middle - label.Height / 2));
            x += 16 + label.Width + 14;
        }
    }

    private static FormattedText Label(string text, double pixelsPerDip, Brush brush, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelFace, size, brush, pixelsPerDip);

    private static Pen Frozen(Pen pen)
    {
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
