namespace LimitTray.Core.Model;

/// <summary>A single quota window. Percent is in the range 0-100.</summary>
public sealed record QuotaWindow(
    double Percent,
    DateTimeOffset? ResetsAt,
    TimeSpan WindowLength);
