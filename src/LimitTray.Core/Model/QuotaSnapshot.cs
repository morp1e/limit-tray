namespace LimitTray.Core.Model;

public sealed record QuotaSnapshot(
    string Provider,
    QuotaWindow? Session,
    QuotaWindow? Weekly,
    HealthState Health,
    DateTimeOffset FetchedAt,
    string? Detail)
{
    public static QuotaSnapshot Unhealthy(
        string provider, HealthState health, DateTimeOffset now, string detail) =>
        new(provider, null, null, health, now, detail);
}
