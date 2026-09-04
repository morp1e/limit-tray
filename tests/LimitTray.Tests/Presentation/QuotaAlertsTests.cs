using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class QuotaAlertsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static QuotaSnapshot Snapshot(
        double session, double? weekly = null, HealthState health = HealthState.Fresh) =>
        new("claude",
            new QuotaWindow(session, null, TimeSpan.FromHours(5)),
            weekly is null ? null : new QuotaWindow(weekly.Value, null, TimeSpan.FromDays(7)),
            health, Now, null);

    [Fact]
    public void BelowTheThreshold_IsSilent()
    {
        var alerts = new QuotaAlerts();

        Assert.Empty(alerts.Inspect(Snapshot(10)));
        Assert.Empty(alerts.Inspect(Snapshot(84)));
        Assert.Empty(alerts.Inspect(Snapshot(85)));
    }

    [Fact]
    public void CrossingTheThreshold_FiresOnce()
    {
        var alerts = new QuotaAlerts();

        Assert.Empty(alerts.Inspect(Snapshot(80)));

        var fired = alerts.Inspect(Snapshot(86));
        Assert.Single(fired);
        Assert.Equal(WindowKind.Session, fired[0].Kind);
        Assert.Equal(86, fired[0].Percent);
    }

    [Fact]
    public void StayingAboveTheThreshold_DoesNotRepeat()
    {
        var alerts = new QuotaAlerts();

        Assert.Single(alerts.Inspect(Snapshot(90)));
        Assert.Empty(alerts.Inspect(Snapshot(92)));
        Assert.Empty(alerts.Inspect(Snapshot(99)));
    }

    [Fact]
    public void FallingBackBelow_ArmsTheNextCrossing()
    {
        var alerts = new QuotaAlerts();

        Assert.Single(alerts.Inspect(Snapshot(90)));
        Assert.Empty(alerts.Inspect(Snapshot(4)));
        Assert.Single(alerts.Inspect(Snapshot(91)));
    }

    [Fact]
    public void TheTwoWindowsAreTrackedSeparately()
    {
        var alerts = new QuotaAlerts();

        var fired = alerts.Inspect(Snapshot(90, weekly: 95));

        Assert.Equal(2, fired.Count);
        Assert.Contains(fired, a => a.Kind == WindowKind.Session);
        Assert.Contains(fired, a => a.Kind == WindowKind.Weekly);
    }

    [Theory]
    [InlineData(HealthState.RateLimited)]
    [InlineData(HealthState.AuthMissing)]
    [InlineData(HealthState.ProtocolBroken)]
    [InlineData(HealthState.Stale)]
    public void UnhealthySnapshots_NeverFire(HealthState health) =>
        Assert.Empty(new QuotaAlerts().Inspect(Snapshot(99, health: health)));

    [Fact]
    public void Body_NamesTheProviderTheWindowAndThePercentage()
    {
        var alert = new QuotaAlert("claude", WindowKind.Session, 91);

        var english = QuotaAlerts.Body(alert, Strings.English);
        var turkish = QuotaAlerts.Body(alert, Strings.Turkish);

        Assert.Equal("Claude Usage, 5-hour window: 91%", english);
        Assert.Equal("Claude Kullanımı, 5 saatlik pencere: %91", turkish);
    }
}
