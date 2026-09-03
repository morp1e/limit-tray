namespace AgentQuotaTray.Core.Model;

/// <summary>Tek bir kota penceresi. Percent 0-100 araligindadir.</summary>
public sealed record QuotaWindow(
    double Percent,
    DateTimeOffset? ResetsAt,
    TimeSpan WindowLength);
