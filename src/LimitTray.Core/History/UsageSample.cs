namespace LimitTray.Core.History;

/// <summary>
/// One observation of a quota window. Only a timestamp and a percentage are ever
/// recorded; nothing derived from the credential is kept.
/// </summary>
public readonly record struct UsageSample(DateTimeOffset At, double Percent);
