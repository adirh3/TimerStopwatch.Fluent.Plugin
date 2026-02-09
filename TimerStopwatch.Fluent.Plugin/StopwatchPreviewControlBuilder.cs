using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Blast.Core.Interfaces;

namespace TimerStopwatch.Fluent.Plugin;

public sealed class StopwatchPreviewControlBuilder : IResultPreviewControlBuilder
{
    public static StopwatchPreviewControlBuilder Instance { get; } = new();

    public PreviewBuilderDescriptor PreviewBuilderDescriptor { get; } = new()
    {
        Name = "Stopwatch",
        Description = "Live stopwatch preview",
        ShowPreviewAutomatically = true
    };

    public bool CanBuildPreviewForResult(ISearchResult searchResult)
    {
        if (searchResult is not TimerStopwatchSearchResult r)
            return false;

        return r.Command.Action is TimerStopwatchAction.StartStopwatch
            or TimerStopwatchAction.StopStopwatch
            or TimerStopwatchAction.ResetStopwatch;
    }

    public ValueTask<Control> CreatePreviewControl(ISearchResult searchResult)
    {
        return ValueTask.FromResult<Control>(new StopwatchPreviewControl());
    }

    private sealed class StopwatchPreviewControl : UserControl
    {
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _time;
        private readonly TextBlock _status;
        private readonly DialControl _dial;

        public StopwatchPreviewControl()
        {
            _time = new TextBlock
            {
                FontSize = 44,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _status = new TextBlock
            {
                FontSize = 14,
                Opacity = 0.75,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _dial = new DialControl
            {
                Width = 220,
                Height = 220,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            Content = new Border
            {
                Padding = new Thickness(18, 14),
                CornerRadius = new CornerRadius(18),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        _time,
                        _status,
                        new Border
                        {
                            Padding = new Thickness(6),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            CornerRadius = new CornerRadius(16),
                            Child = _dial
                        }
                    }
                }
            };

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (_, _) => Update();

            AttachedToVisualTree += (_, _) =>
            {
                Update();
                _timer.Start();
            };
            DetachedFromVisualTree += (_, _) => _timer.Stop();
        }

        private void Update()
        {
            var elapsed = StopwatchState.GetElapsed();
            bool running = StopwatchState.IsRunning();

            _time.Text = Format(elapsed);
            _status.Text = running ? "Running" : "Stopped";

            // TotalSeconds already includes fractional seconds. Using % keeps it in [0,60).
            double secHand = elapsed.TotalSeconds % 60.0;
            double angle = (secHand / 60.0) * 360.0;

            _dial.Angle = angle;
        }

        private static string Format(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return elapsed.ToString(@"h\:mm\:ss\.f");
            return elapsed.ToString(@"m\:ss\.f");
        }
    }

    private sealed class DialControl : Control
    {
        // Fallback palette (used if Fluent Search resources aren't available).
        private static readonly Color FallbackAccent = Color.Parse("#D09A2D");
        private static readonly Color FallbackRing = Color.Parse("#3A3A3A");

        public static readonly StyledProperty<double> AngleProperty =
            AvaloniaProperty.Register<DialControl, double>(nameof(Angle));

        public double Angle
        {
            get => GetValue(AngleProperty);
            set => SetValue(AngleProperty, value);
        }

        public DialControl()
        {
            AngleProperty.Changed.AddClassHandler<DialControl>((c, _) => c.InvalidateVisual());
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var b = Bounds;
            double size = Math.Min(b.Width, b.Height);
            if (size <= 0)
                return;

            double cx = b.X + (b.Width / 2);
            double cy = b.Y + (b.Height / 2);

            // Scale stroke widths with the available size.
            double ringThickness = Math.Max(6.0, size * 0.045);
            double handThickness = Math.Max(2.0, size * 0.014);
            double centerSize = Math.Max(8.0, size * 0.045);

            double outerRadius = (size / 2) - (ringThickness / 2);
            double handRadius = Math.Max(0, outerRadius - ringThickness - (handThickness / 2) - 2);

            // Pull Fluent Search appearance resources when available.
            // Keys come from Blast.Core.BlastBindings.
            var systemAccent = TryGetColor("SystemAccentColor") ?? FallbackAccent;
            var accentLight1 = TryGetColor("SystemAccentColorLight1") ?? systemAccent;
            var accentLight2 = TryGetColor("SystemAccentColorLight2") ?? systemAccent;
            var accentDark2 = TryGetColor("SystemAccentColorDark2") ?? systemAccent;

            // Avalonia DrawingContext overloads can differ across Fluent Search releases (plugins use host Avalonia).
            // Use DrawGeometry + Pen properties, which have been far more stable across versions.

            // The dark ring is derived from the accent so it fits the user's theme.
            var ringBrush = new SolidColorBrush(accentDark2) { Opacity = 0.55 };
            var accentBrush = new SolidColorBrush(accentLight2) { Opacity = 0.24 };
            var ringPen = new Pen(ringBrush, ringThickness);
            var accentPen = new Pen(accentBrush, ringThickness);

            var ringRect = new Rect(cx - outerRadius, cy - outerRadius, outerRadius * 2, outerRadius * 2);
            var ringGeo = new EllipseGeometry(ringRect);
            context.DrawGeometry(null, ringPen, ringGeo);
            context.DrawGeometry(null, accentPen, ringGeo);

            // Hand (keep inside the ring by construction; no clip required).
            double radians = (Angle - 90.0) * (Math.PI / 180.0);
            var end = new Point(cx + (Math.Cos(radians) * handRadius), cy + (Math.Sin(radians) * handRadius));
            var handPen = new Pen(new SolidColorBrush(systemAccent), handThickness) { LineCap = PenLineCap.Round };
            context.DrawLine(handPen, new Point(cx, cy), end);

            // Center dot
            double cr = centerSize / 2;
            var centerRect = new Rect(cx - cr, cy - cr, cr * 2, cr * 2);
            context.DrawGeometry(new SolidColorBrush(accentLight1), null, new EllipseGeometry(centerRect));
        }

        private Color? TryGetColor(string key)
        {
            // TryFindResource returns either a Color or a brush depending on how the theme stores it.
            var app = Application.Current;
            if (app == null)
                return null;

            if (!app.TryFindResource(key, out object? value) || value == null)
                return null;

            return value switch
            {
                Color c => c,
                ISolidColorBrush scb => scb.Color,
                _ => null
            };
        }
    }
}
