using LimitTray.Core.Model;
using LimitTray.Core.Store;

namespace LimitTray.Tests.Store;

public class QuotaStoreStalenessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static QuotaSnapshot Fresh(string p) =>
        new(p, new QuotaWindow(10, null, TimeSpan.FromHours(5)), null, HealthState.Fresh, T0, null);

    [Theory]
    [InlineData("codex", 120, 25 * 60)]
    [InlineData("claude", 60, 5 * 60)]
    [InlineData("claude", 120, 5 * 60)]
    [InlineData("claude", 300, 750)]
    public void StaleAfterFor_MatchesTheSpec(string provider, int refresh, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), QuotaStore.StaleAfterFor(provider, refresh));

    [Fact]
    public void Codex_StaysFreshBetweenSparseReads()
    {
        var now = T0.AddMinutes(20);
        var store = new QuotaStore(() => now, p => QuotaStore.StaleAfterFor(p, 120));
        store.Apply(Fresh("codex"));
        store.RefreshStaleness();
        Assert.Equal(HealthState.Fresh, store.Get("codex")!.Health);

        now = T0.AddMinutes(26);
        store.RefreshStaleness();
        Assert.Equal(HealthState.Stale, store.Get("codex")!.Health);
    }

    [Fact]
    public void DefaultConstructor_KeepsFiveMinutes()
    {
        var now = T0.AddMinutes(6);
        var store = new QuotaStore(() => now);
        store.Apply(Fresh("codex"));
        store.RefreshStaleness();
        Assert.Equal(HealthState.Stale, store.Get("codex")!.Health);
    }
}
