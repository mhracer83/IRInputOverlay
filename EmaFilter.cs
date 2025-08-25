namespace IRInputOverlay
{
    public sealed class EmaFilter
    {
        private bool _hasPrev;
        private double _prev;
        public double Alpha { get; set; } = 0.6;
        public EmaFilter() { }
        public EmaFilter(double alpha) { Alpha = alpha; }
        public double Update(double x)
        {
            if (!_hasPrev) { _prev = x; _hasPrev = true; return x; }
            _prev = Alpha * x + (1.0 - Alpha) * _prev;
            return _prev;
        }
        public void Reset() { _hasPrev = false; _prev = 0.0; }
    }
}
