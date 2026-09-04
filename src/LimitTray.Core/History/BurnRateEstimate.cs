namespace LimitTray.Core.History;

/// <summary>
/// A projection of when a quota window will reach 100%, derived from observed samples.
/// This is an estimate from past behaviour, never a promise: it is only produced when
/// there is enough measured history to support it.
/// </summary>
/// <param name="PercentPerHour">Fitted consumption rate.</param>
/// <param name="TimeToFull">Time from now until 100% at that rate. Never negative.</param>
/// <param name="ExhaustsAt">The projected instant of exhaustion.</param>
/// <param name="ResetsFirst">True when the window resets before the projection lands.</param>
/// <param name="Samples">How many observations the fit used.</param>
/// <param name="Span">The time the observations cover.</param>
public sealed record BurnRateEstimate(
    double PercentPerHour,
    TimeSpan TimeToFull,
    DateTimeOffset ExhaustsAt,
    bool ResetsFirst,
    int Samples,
    TimeSpan Span);
