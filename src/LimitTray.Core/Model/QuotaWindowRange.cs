namespace LimitTray.Core.Model;

/// <summary>
/// The 0-100 invariant QuotaWindow advertises, checked where untrusted numbers enter.
/// A value outside it is a schema change, not a measurement, and is reported as
/// ProtocolBroken by the parsers rather than clamped into a plausible-looking gauge.
/// </summary>
public static class QuotaWindowRange
{
    public static bool IsValid(QuotaWindow? window) =>
        window is null
        || (window.Percent >= 0 && window.Percent <= 100
            && !double.IsNaN(window.Percent)
            && window.WindowLength > TimeSpan.Zero);
}
