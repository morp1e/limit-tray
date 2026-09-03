using LimitTray.Core.Model;
using LimitTray.Core.Store;
using Xunit;

namespace LimitTray.Tests.Store;

public class QuotaStoreTests
{
    private DateTimeOffset _now = new(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

    private QuotaStore Build() => new(() => _now);

    private QuotaSnapshot Fresh(string provider, double percent) =>
        new(provider, new QuotaWindow(percent, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, _now, null);

    [Fact]
    public void Apply_StoresAndReturnsSnapshot()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(14.0, store.Get("claude")!.Session!.Percent);
        Assert.Null(store.Get("codex"));
    }

    [Fact]
    public void Apply_RaisesChangedEvent()
    {
        var store = Build();
        var seen = new List<string>();
        store.Changed += s => seen.Add(s.Provider);

        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(new[] { "claude" }, seen);
    }

    [Fact]
    public void RefreshStaleness_AfterFiveMinutes_MarksStale()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddMinutes(6);
        store.RefreshStaleness();

        Assert.Equal(HealthState.Stale, store.Get("claude")!.Health);
        Assert.Equal(14.0, store.Get("claude")!.Session!.Percent);
    }

    [Fact]
    public void RefreshStaleness_WithinFiveMinutes_StaysFresh()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddMinutes(4);
        store.RefreshStaleness();

        Assert.Equal(HealthState.Fresh, store.Get("claude")!.Health);
    }

    [Fact]
    public void RefreshStaleness_DoesNotDowngradeErrorStates()
    {
        var store = Build();
        store.Apply(QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, _now, "sinirli"));

        _now = _now.AddMinutes(10);
        store.RefreshStaleness();

        Assert.Equal(HealthState.RateLimited, store.Get("claude")!.Health);
    }

    [Fact]
    public void RefreshStaleness_RaisesChangedOnlyOnTransition()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));
        var count = 0;
        store.Changed += _ => count++;

        _now = _now.AddMinutes(6);
        store.RefreshStaleness();
        store.RefreshStaleness();

        Assert.Equal(1, count);
    }

    [Fact]
    public void All_ReturnsSnapshotsInStableProviderOrder()
    {
        var store = Build();
        store.Apply(Fresh("codex", 36.0));
        store.Apply(Fresh("claude", 14.0));

        Assert.Equal(new[] { "claude", "codex" }, store.All().Select(s => s.Provider));
    }

    [Fact]
    public void Apply_UnhealthyAfterHealthy_KeepsWindowsAndOldFetchedAt()
    {
        var store = Build();
        var fetchedAt = _now;
        var healthy = new QuotaSnapshot(
            "claude",
            new QuotaWindow(14.0, null, TimeSpan.FromHours(5)),
            new QuotaWindow(12.0, null, TimeSpan.FromDays(7)),
            HealthState.Fresh,
            fetchedAt,
            null);
        store.Apply(healthy);

        _now = _now.AddMinutes(2);
        store.Apply(QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, _now, "throttled"));

        var applied = store.Get("claude")!;
        Assert.Equal(HealthState.RateLimited, applied.Health);
        Assert.Equal("throttled", applied.Detail);
        Assert.Equal(healthy.Session, applied.Session);
        Assert.Equal(healthy.Weekly, applied.Weekly);
        Assert.Equal(fetchedAt, applied.FetchedAt);
    }

    [Fact]
    public void Apply_UnhealthyWithNoPreviousData_KeepsWindowsEmpty()
    {
        var store = Build();
        var unhealthy = QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, _now, "throttled");

        store.Apply(unhealthy);

        Assert.Equal(unhealthy, store.Get("claude"));
    }

    [Fact]
    public void Apply_LaterHealthySnapshotFullyReplacesUnhealthySnapshot()
    {
        var store = Build();
        store.Apply(QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, _now, "throttled"));

        var healthy = new QuotaSnapshot(
            "claude",
            new QuotaWindow(21.0, null, TimeSpan.FromHours(5)),
            new QuotaWindow(33.0, null, TimeSpan.FromDays(7)),
            HealthState.Fresh,
            _now.AddMinutes(2),
            null);
        store.Apply(healthy);

        Assert.Equal(healthy, store.Get("claude"));
    }
}
