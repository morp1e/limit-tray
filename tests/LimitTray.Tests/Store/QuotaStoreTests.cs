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
    public void RefreshStaleness_AfterTwoMinutes_MarksStale()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddMinutes(3);
        store.RefreshStaleness();

        Assert.Equal(HealthState.Stale, store.Get("claude")!.Health);
        Assert.Equal(14.0, store.Get("claude")!.Session!.Percent);
    }

    [Fact]
    public void RefreshStaleness_WithinTwoMinutes_StaysFresh()
    {
        var store = Build();
        store.Apply(Fresh("claude", 14.0));

        _now = _now.AddSeconds(90);
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

        _now = _now.AddMinutes(3);
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
}
