using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Tests.History;

public class HistoryStoreDirtyTests
{
    [Fact]
    public void Save_Unchanged_DoesNotSerializeAgain()
    {
        var serialized = 0;
        var store = new HistoryStore(() => null, _ => { },
            h => { serialized++; return HistoryFile.Write(h); });
        var history = new UsageHistory();
        history.Observe(new QuotaSnapshot("claude", new QuotaWindow(10, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, DateTimeOffset.UtcNow, null));

        store.Save(history);
        store.Save(history);
        Assert.Equal(1, serialized);

        history.Observe(new QuotaSnapshot("claude", new QuotaWindow(11, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, DateTimeOffset.UtcNow.AddMinutes(2), null));
        store.Save(history);
        Assert.Equal(2, serialized);
    }
}
