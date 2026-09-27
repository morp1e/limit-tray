namespace LimitTray.Native.Ui;

internal enum Easing { CubicOut, Linear }

/// <summary>
/// A value that moves from where it is to a new target over a fixed time. Time is a
/// millisecond counter supplied by the caller, so tests can drive it. v0.3 used
/// 300 ms cubic ease-out for bars and rings, 150 ms for the page slide and a 600 ms
/// linear turn for the refresh icon; the same numbers live in <see cref="Durations"/>.
/// </summary>
internal sealed class Tween
{
    private double _from;
    private double _to;
    private long _start;
    private int _duration;
    private readonly Easing _easing;

    public Tween(double value, Easing easing = Easing.CubicOut)
    {
        _from = _to = value;
        _easing = easing;
    }

    public double Target => _to;

    /// <summary>Starts moving towards <paramref name="target"/> from the current value.</summary>
    public void MoveTo(double target, long nowMs, int durationMs)
    {
        if (target.Equals(_to) && !IsAnimating(nowMs)) return;
        _from = Value(nowMs);
        _to = target;
        _start = nowMs;
        _duration = Math.Max(0, durationMs);
    }

    /// <summary>Starts a run from an explicit value, for animations that restart (the refresh turn).</summary>
    public void Run(double from, double to, long nowMs, int durationMs)
    {
        _from = from;
        _to = to;
        _start = nowMs;
        _duration = Math.Max(0, durationMs);
    }

    /// <summary>Sets the value without animating (first display, animations off).</summary>
    public void Jump(double value)
    {
        _from = _to = value;
        _duration = 0;
    }

    public double Value(long nowMs)
    {
        if (_duration <= 0) return _to;
        var progress = Math.Clamp((nowMs - _start) / (double)_duration, 0, 1);
        var eased = _easing == Easing.Linear ? progress : 1 - Math.Pow(1 - progress, 3);
        return _from + (_to - _from) * eased;
    }

    public bool IsAnimating(long nowMs) => _duration > 0 && nowMs - _start < _duration;
}

internal static class Durations
{
    public const int Value = 300;
    public const int Slide = 150;
    public const int RefreshTurn = 600;
}
