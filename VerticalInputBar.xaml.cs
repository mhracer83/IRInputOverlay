using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IRInputOverlay
{
    public partial class VerticalInputBar : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(VerticalInputBar),
                new PropertyMetadata(0.0, OnChanged));

        public static readonly DependencyProperty BarBrushProperty =
            DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(VerticalInputBar),
                new PropertyMetadata(Brushes.White, OnChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Brush BarBrush
        {
            get => (Brush)GetValue(BarBrushProperty);
            set => SetValue(BarBrushProperty, value);
        }

        public VerticalInputBar()
        {
            InitializeComponent();
            Loaded += (_, __) => UpdateVisual();
            SizeChanged += (_, __) => UpdateVisual();
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VerticalInputBar b) b.UpdateVisual();
        }

        private void UpdateVisual()
        {
            var v = Value;
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            v = Math.Clamp(v, 0, 100);
            ValueBlock.Text = Math.Round(v).ToString();

            var brush = BarBrush ?? Brushes.White;
            FillRect.Fill = brush;

            if (brush is SolidColorBrush scb)
                Track.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, scb.Color.R, scb.Color.G, scb.Color.B));
            else
                Track.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 255, 255, 255));

            Scale.ScaleY = v / 100.0;
        }
    }
}
