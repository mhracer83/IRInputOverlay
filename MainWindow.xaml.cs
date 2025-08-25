using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRInputOverlay
{
    public partial class MainWindow : Window
    {
        private readonly TelemetryService _telemetry;
        private bool _clickThroughEnabled = false; // start interactive
        private AppSettings _settings = new AppSettings();
        private double _aspectRatio = 0.0;
		private volatile bool _isConnected;

        // Shared values from telemetry
        private double _rawThr, _rawBrk, _rawStr, _rawClu; // 0..100 each (steer mapped 0..100 for bar)

        // Wheel / gear / speed shared values (fed from TelemetryService.Sample)
        private double _steerPct;   // -1..+1
        private int _gear;          // R(<0), N(0), 1..8
        private double _speedMph;   // mph

        // EMA for line traces only
        private readonly EmaFilter _emaThrottle = new(0.6);
        private readonly EmaFilter _emaBrake = new(0.6);
        private readonly EmaFilter _emaSteer = new(0.6);

        // EMA for optional bar smoothing
        private readonly EmaFilter _emaBarThr = new(1.0);
        private readonly EmaFilter _emaBarBrk = new(1.0);
        private readonly EmaFilter _emaBarStr = new(1.0);
        private readonly EmaFilter _emaBarClu = new(1.0);
        private double _barAlpha = 1.0; // 1 = instant

        private readonly Brush _throttleBrush = Brushes.Green;
        private readonly Brush _brakeBrush = Brushes.OrangeRed;
        private readonly Brush _steerBrush = Brushes.Cyan;
        private readonly Brush _clutchBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0033CC")!);

        #region Global Hotkey
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const int HOTKEY_ID = 0xB001;
        private HwndSource? _hwndSource;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
			CloseBtn.Click += (_, __) => Close();
			
            // Load settings
            _settings = AppSettings.Load();
            if (!double.IsNaN(_settings.Left)) Left = _settings.Left;
            if (!double.IsNaN(_settings.Top)) Top = _settings.Top;
            if (_settings.Width > 0) Width = _settings.Width;
            if (_settings.Height > 0) Height = _settings.Height;
            ApplyBackgroundOpacity(_settings.BackgroundOpacity);
            SetLockAspect(_settings.LockAspectRatio);
            SetBarAlpha(_settings.BarAlphaPercent);
			
			ApplyWheelImage(); // loads Assets.default_wheel.png if present

            // Telemetry: write once into shared fields (single pipeline)
            _telemetry = new TelemetryService(
                onSample: s =>
                {
                    Interlocked.Exchange(ref _rawThr, s.Throttle * 100.0);
                    Interlocked.Exchange(ref _rawBrk, s.Brake * 100.0);
                    Interlocked.Exchange(ref _rawStr, (s.SteeringPct * 100.0 + 100.0) / 2.0); // 0..100 bar scale
                    Interlocked.Exchange(ref _rawClu, s.Clutch * 100.0);

                    // wheel / gear / speed
                    Interlocked.Exchange(ref _steerPct, s.SteeringPct);
                    Interlocked.Exchange(ref _gear, s.Gear);
                    Interlocked.Exchange(ref _speedMph, s.SpeedMph);
                });

            // UI refresh on CompositionTarget.Rendering for smoothness
            System.Windows.Media.CompositionTarget.Rendering += (_, __) =>
            {
                double rawThr = Interlocked.CompareExchange(ref _rawThr, 0.0, 0.0);
                double rawBrk = Interlocked.CompareExchange(ref _rawBrk, 0.0, 0.0);
                double rawStr = Interlocked.CompareExchange(ref _rawStr, 0.0, 0.0);
                double rawClu = Interlocked.CompareExchange(ref _rawClu, 0.0, 0.0);

                // steering pct, gear, speed for UI
                double steerPct = Interlocked.CompareExchange(ref _steerPct, 0.0, 0.0);
                int gear = Interlocked.CompareExchange(ref _gear, 0, 0);
                double speedMph = Interlocked.CompareExchange(ref _speedMph, 0.0, 0.0);

                // Bars: raw or lightly smoothed
                double thrBar = _barAlpha >= 0.999 ? rawThr : _emaBarThr.Update(rawThr);
                double brkBar = _barAlpha >= 0.999 ? rawBrk : _emaBarBrk.Update(rawBrk);
                double strBar = _barAlpha >= 0.999 ? rawStr : _emaBarStr.Update(rawStr);
                double cluBar = _barAlpha >= 0.999 ? rawClu : _emaBarClu.Update(rawClu);

                BarThrottle.BarBrush = _throttleBrush; BarThrottle.Value = thrBar;
                BarBrake.BarBrush    = _brakeBrush;    BarBrake.Value    = brkBar;
                // Steering bar was replaced by a wheel image; no BarSteer assignment.
                // If not connected, force clutch display to 0%
				if (!_isConnected)
				{
					BarClutch.BarBrush = _clutchBrush;
					BarClutch.Value = 0;
				}
				else
				{
					BarClutch.BarBrush = _clutchBrush;
					BarClutch.Value = 100 - cluBar;
				}

                // Wheel rotation (negative matches real iRacing direction on most setups)
                var angle = steerPct * (_telemetry.SteeringAngleRangeDeg * 0.5);
                WheelRotation.Angle = -angle;

                // Gear & speed labels
                GearText.Text  = gear < 0 ? "R" : (gear == 0 ? "N" : gear.ToString());
                SpeedText.Text = Math.Round(speedMph).ToString();

                // Traces: smoothed lines (no clutch in line graph)
                var thrLine = _emaThrottle.Update(rawThr);
                var brkLine = _emaBrake.Update(rawBrk);
                var strLine = _emaSteer.Update((rawStr - 50.0) * 2.0); // back to -100..+100 for trace
                CombinedGraph.AddPoint((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds, thrLine, brkLine, strLine);
            };

            // Drag to move (only when not click-through)
            this.MouseLeftButtonDown += (sender, e) =>
            {
                if (!_clickThroughEnabled && e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            // Cog click -> open settings
            CogBtn.Click += (_, __) =>
            {
                var win = new SettingsWindow(this, _settings);
                win.Owner = this;
                win.ShowDialog();
            };

            Loaded += (_, __) =>
            {
                EnableClickThrough(_clickThroughEnabled);
                _telemetry.Start();

                var hwnd = new WindowInteropHelper(this).Handle;
                _hwndSource = HwndSource.FromHwnd(hwnd);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(WndProc);
                    RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, (uint)KeyInterop.VirtualKeyFromKey(Key.O));
                }
            };

            Closed += (_, __) =>
            {
                _settings.Left = Left;
                _settings.Top = Top;
                _settings.Width = Width;
                _settings.Height = Height;
                _settings.Save();

                _telemetry.Stop();
                var hwnd = new WindowInteropHelper(this).Handle;
                try { UnregisterHotKey(hwnd, HOTKEY_ID); } catch { }
                if (_hwndSource != null) _hwndSource.RemoveHook(WndProc);
            };

            _telemetry.ConnectionChanged += connected =>
            {
				_isConnected = connected;
                Dispatcher.Invoke(() => StatusText.Text = connected ? "Connected" : "Not connected");
            };
        }

        public void ApplyBackgroundOpacity(int percent)
        {
            byte a = (byte)Math.Clamp((int)(percent * 2.55), 0, 255);
            var c = ((SolidColorBrush)RootChrome.Background).Color;
            RootChrome.Background = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }

        public void SetLockAspect(bool locked)
        {
            if (locked)
            {
                if (ActualHeight > 0) _aspectRatio = ActualWidth / ActualHeight;
                else _aspectRatio = Width / Height;
            }
            else
            {
                _aspectRatio = 0.0;
            }
        }

        public void SetBarAlpha(int percent)
        {
            _barAlpha = Math.Clamp(percent / 100.0, 0.0, 1.0);
            _emaBarThr.Alpha = _barAlpha;
            _emaBarBrk.Alpha = _barAlpha;
            _emaBarStr.Alpha = _barAlpha;
            _emaBarClu.Alpha = _barAlpha;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _clickThroughEnabled = !_clickThroughEnabled;
                EnableClickThrough(_clickThroughEnabled);
                CogBtn.Visibility = _clickThroughEnabled ? Visibility.Collapsed : Visibility.Visible;
				CloseBtn.Visibility = _clickThroughEnabled ? Visibility.Collapsed : Visibility.Visible;
                handled = true;
            }
            return IntPtr.Zero;
        }

		public void ApplyWheelImage()
		{
			try
			{
				string? chosen = null;

				// 1) Try user-selected wheel image
				if (!string.IsNullOrWhiteSpace(_settings?.WheelImagePath) &&
					File.Exists(_settings.WheelImagePath))
				{
					chosen = _settings.WheelImagePath;
				}
				else
				{
					// 2) Fallback: bundled default
					string candidate = Path.Combine(AppContext.BaseDirectory, "Assets.default_wheel.png");
					if (File.Exists(candidate))
						chosen = candidate;
				}

				if (chosen != null)
				{
					var bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.UriSource = new Uri(chosen, UriKind.Absolute);
					bmp.EndInit();
					WheelImage.Source = bmp;
				}
				else
				{
					WheelImage.Source = null;
				}
			}
			catch
			{
				// swallow; keep previous image
			}
		}



        #region Click-through (transparent hit test)
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void EnableClickThrough(bool enable)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enable)
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            else
                SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT);
            Title = $"IR Input Overlay {(enable ? "[Click-through]" : "[Interactive]")}";
        }
        #endregion
    }
}
