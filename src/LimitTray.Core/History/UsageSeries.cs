using LimitTray.Core.Model;

namespace LimitTray.Core.History;

/// <summary>One provider window and the observations recorded for it.</summary>
public sealed record UsageSeries(
    string Provider,
    WindowKind Kind,
    TimeSpan WindowLength,
    DateTimeOffset? ResetsAt,
    IReadOnlyList<UsageSample> Samples);
