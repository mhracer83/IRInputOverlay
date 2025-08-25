using System;
using System.Timers;
using IRSDKSharper;

namespace IRInputOverlay
{
    public sealed class TelemetryService : IDisposable
    {
        private IRacingSdk? _irsdk;
        private readonly System.Timers.Timer _poll = new System.Timers.Timer(16);     // ~60 Hz
        private readonly System.Timers.Timer _reconnect = new System.Timers.Timer(2000);
        private DateTime _start = DateTime.UtcNow;
        private DateTime _lastSampleUtc = DateTime.MinValue;
        private bool _connected;

        public double SteeringAngleRangeDeg { get; set; } = 540;

        public event Action<bool>? ConnectionChanged;
        public readonly Action<Sample> _onSample;

        public TelemetryService(Action<Sample> onSample)
        {
            _onSample = onSample;
            _poll.Elapsed += (_, __) => Poll();
            _reconnect.Elapsed += (_, __) => TryEnsureConnection();
            _poll.AutoReset = true;
            _reconnect.AutoReset = true;
        }

        public void Start()
        {
            CreateSdk();
            _poll.Start();
            _reconnect.Start();
        }

        public void Stop()
        {
            _poll.Stop();
            _reconnect.Stop();
            if (_irsdk != null)
            {
                try { _irsdk.Stop(); } catch { }
                _irsdk = null;
            }
        }

        private void CreateSdk()
        {
            try
            {
                _irsdk = new IRacingSdk();
                _irsdk.Start();
            }
            catch { _irsdk = null; }
        }

        private void TryEnsureConnection()
        {
            if (_irsdk == null)
            {
                CreateSdk();
                return;
            }
            if ((DateTime.UtcNow - _lastSampleUtc).TotalSeconds > 8.0)
            {
                try { _irsdk.Stop(); } catch { }
                _irsdk = null;
                if (_connected) { _connected = false; ConnectionChanged?.Invoke(false); }
            }
        }

        private void Poll()
        {
            try
            {
                if (_irsdk == null) return;
                if (!_irsdk.IsConnected)
                {
                    if (_connected) { _connected = false; ConnectionChanged?.Invoke(false); }
                    return;
                }

                float throttle = 0f; try { throttle = _irsdk.Data.GetFloat("Throttle"); } catch { }           // 0..1
                float brake    = 0f; try { brake    = _irsdk.Data.GetFloat("Brake"); }    catch { }           // 0..1
                float steerRad = 0f; try { steerRad = _irsdk.Data.GetFloat("SteeringWheelAngle"); } catch { } // radians

                float clutch = 0f;
                try { clutch = _irsdk.Data.GetFloat("Clutch"); }
                catch {
                    try { clutch = _irsdk.Data.GetFloat("ClutchRaw"); }
                    catch {
                        try { clutch = _irsdk.Data.GetFloat("ClutchPedal"); }
                        catch {
                            try { clutch = _irsdk.Data.GetFloat("ClutchAxis"); }
                            catch { clutch = 0f; }
                        }
                    }
                } // 0..1

                // NEW: speed (m/s) and gear
                float speedMS = 0f;  try { speedMS = _irsdk.Data.GetFloat("Speed"); } catch { } // meters/second
                int   gear    = 0;   try { gear    = _irsdk.Data.GetInt("Gear");   } catch { }

                _lastSampleUtc = DateTime.UtcNow;
                if (!_connected) { _connected = true; ConnectionChanged?.Invoke(true); }

                // Steering percent based on your configured angle range
                double steerDeg   = steerRad * (180.0 / Math.PI);
                double halfRange  = Math.Max(10.0, SteeringAngleRangeDeg * 0.5);
                double steeringPct = Math.Clamp(steerDeg / halfRange, -1.0, 1.0);

                var t = (DateTime.UtcNow - _start).TotalSeconds;
                _onSample?.Invoke(new Sample
                {
                    Timestamp   = t,
                    Throttle    = throttle,
                    Brake       = brake,
                    SteeringPct = steeringPct,
                    Clutch      = clutch,

                    // NEW:
                    SpeedMph    = speedMS * 2.23693629, // m/s -> mph
                    Gear        = gear
                });
            }
            catch
            {
                // swallow; reconnect loop will handle
            }
        }

        public void Dispose() => Stop();

        public struct Sample
        {
            public double Timestamp;
            public double Throttle;
            public double Brake;
            public double SteeringPct; // -1..+1
            public double Clutch;      // 0..1

            // NEW:
            public double SpeedMph;
            public int    Gear;
        }
    }
}

