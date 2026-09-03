namespace LimitTray.Core.Model;

public enum HealthState
{
    Fresh,
    Stale,
    RateLimited,
    AuthMissing,
    ProtocolBroken
}
