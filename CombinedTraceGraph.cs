using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace IRInputOverlay
{
    public class CombinedTraceGraph : FrameworkElement
    {
        public string Title { get; set; } = "Inputs";
        public Brush ThrottleBrush { get; set; } = Brushes.Lime;
        public Brush BrakeBrush { get; set; } = Brushes.OrangeRed;
        public Brush SteerBrush { get; set; } = Brushes.Cyan;

        private readonly Queue<(double t, double thr, double brk, double steer)> _points = new();
        private readonly object _lock = new();
        private const double Seconds = 10.0;
        private readonly Typeface _tf = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private const double HeaderHeight = 22.0;
        private const double MarginLeft = 36.0;
        private const double MarginRight = 8.0;
        private const double MarginBottom = 6.0;
        private const double MarginTop = 4.0;

        public void AddPoint(double t, double thrPct, double brkPct, double steerPctNegToPos)
        {
            lock (_lock)
            {
                _points.Enqueue((t, thrPct, brkPct, steerPctNegToPos));
                while (_points.Count > 0 && t - _points.Peek().t > Seconds) _points.Dequeue();
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(new Point(0, 0), RenderSize);
            var headerRect = new Rect(rect.Left, rect.Top, rect.Width, HeaderHeight);
            DrawHeader(dc, headerRect);

            var plotRect = new Rect(
                rect.Left + MarginLeft,
                rect.Top + HeaderHeight + MarginTop,
                Math.Max(0, rect.Width - MarginLeft - MarginRight),
                Math.Max(0, rect.Height - HeaderHeight - MarginTop - MarginBottom)
            );

            // Grid lines
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
            for (int i = 0; i <= 4; i++)
            {
                double y = plotRect.Top + i * plotRect.Height / 4.0;
                dc.DrawLine(gridPen, new Point(plotRect.Left, y), new Point(plotRect.Right, y));
            }
            // Midline (steer 0)
            var midY = plotRect.Top + plotRect.Height * 0.5;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1),
                        new Point(plotRect.Left, midY), new Point(plotRect.Right, midY));

            DrawYAxisLabels(dc, plotRect);

            List<(double t, double thr, double brk, double steer)> data;
            lock (_lock) data = _points.ToList();
            if (data.Count == 0) return;

            double tMax = data.Last().t;
            double tMin = tMax - Seconds;

            void DrawSeries(Func<(double t, double thr, double brk, double steer), double> sel, Brush brush, string mode)
            {
                var geo = new StreamGeometry();
                using var g = geo.Open();
                bool started = false;
                foreach (var p in data)
                {
                    double raw = sel(p);
                    double norm = mode == "steer" ? (raw + 100.0) / 200.0 : (raw / 100.0);
                    double x = plotRect.Left + (p.t - tMin) / Seconds * plotRect.Width;
                    double y = plotRect.Bottom - norm * plotRect.Height;
                    if (!started) { g.BeginFigure(new Point(x, y), false, false); started = true; }
                    else g.LineTo(new Point(x, y), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, new Pen(brush, 3), geo);
            }

            DrawSeries(p => p.thr, ThrottleBrush, "thr");
            DrawSeries(p => p.brk, BrakeBrush, "brk");
            DrawSeries(p => p.steer, SteerBrush, "steer");
        }

        private void DrawHeader(DrawingContext dc, Rect headerRect)
        {
            var title = new FormattedText(Title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _tf, 12, Brushes.White, 1.25);
            dc.DrawText(title, new Point(headerRect.Left + 8, headerRect.Top + (headerRect.Height - title.Height) / 2));

            double x = headerRect.Left + 8 + title.Width + 16;
            double y = headerRect.Top + headerRect.Height / 2;

            void Legend(Brush brush, string text)
            {
                dc.DrawEllipse(brush, null, new Point(x, y), 4, 4);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _tf, 12, Brushes.White, 1.25);
                dc.DrawText(ft, new Point(x + 8, y - ft.Height / 2));
                x += 8 + ft.Width + 16;
            }
            Legend(ThrottleBrush, "Throttle");
            Legend(BrakeBrush, "Brake");
            Legend(SteerBrush, "Steering");
        }

        private void DrawYAxisLabels(DrawingContext dc, Rect plotRect)
        {
            void LabelAt(double val)
            {
                double y = plotRect.Bottom - (val / 100.0) * plotRect.Height;
                string s = ((int)val).ToString(CultureInfo.InvariantCulture);
                var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _tf, 11, Brushes.White, 1.25);
                dc.DrawText(ft, new Point(plotRect.Left - 8 - ft.Width, y - ft.Height / 2));
            }
            LabelAt(100);
            LabelAt(50);
            LabelAt(0);
        }
    }
}
