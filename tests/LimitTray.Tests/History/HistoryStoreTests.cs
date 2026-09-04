using LimitTray.Core.History;
using LimitTray.Core.Model;
using Xunit;

namespace LimitTray.Tests.History;

public class HistoryStoreTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static UsageHistory Populated()
    {
        var history = new UsageHistory();
        history.Observe(new QuotaSnapshot(
            "claude", new QuotaWindow(57, null, TimeSpan.FromHours(5)),
            new QuotaWindow(16, null, TimeSpan.FromDays(7)),
            HealthState.Fresh, Start, null));
        return history;
    }

    [Fact]
    public void Load_NoFile_IsEmptyNotNull()
    {
        var store = new HistoryStore(() => null, _ => { });

        var history = store.Load();

        Assert.NotNull(history);
        Assert.Empty(history.Providers());
    }

    [Fact]
    public void Load_CorruptFile_BehavesLikeAFirstRun()
    {
        var store = new HistoryStore(() => "{ this is not the file you are looking for", _ => { });

        Assert.Empty(store.Load().Providers());
    }

    [Fact]
    public void Load_UnreadableFile_DoesNotThrow()
    {
        var store = new HistoryStore(
            () => throw new UnauthorizedAccessException("denied"), _ => { });

        Assert.Empty(store.Load().Providers());
    }

    [Fact]
    public void SaveThenLoad_RestoresTheLastKnownValuesAsStale()
    {
        string? disk = null;
        var writer = new HistoryStore(() => disk, content => disk = content);
        writer.Save(Populated());

        var reader = new HistoryStore(() => disk, _ => { });
        var snapshot = reader.Load().LastKnown("claude");

        Assert.NotNull(snapshot);
        Assert.Equal(HealthState.Stale, snapshot!.Health);
        Assert.Equal(Start, snapshot.FetchedAt);
        Assert.Equal(57, snapshot.Session!.Percent);
        Assert.Equal(16, snapshot.Weekly!.Percent);
    }

    [Fact]
    public void Save_SkipsWritingWhenNothingChanged()
    {
        var writes = 0;
        var store = new HistoryStore(() => null, _ => writes++);
        var history = Populated();

        store.Save(history);
        store.Save(history);
        store.Save(history);

        Assert.Equal(1, writes);
    }

    [Fact]
    public void Save_WritesAgainAfterAChange()
    {
        var writes = 0;
        var store = new HistoryStore(() => null, _ => writes++);
        var history = Populated();

        store.Save(history);
        history.Observe(new QuotaSnapshot(
            "claude", new QuotaWindow(58, null, TimeSpan.FromHours(5)), null,
            HealthState.Fresh, Start.AddMinutes(2), null));
        store.Save(history);

        Assert.Equal(2, writes);
    }

    [Fact]
    public void Save_SkipsWritingWhatWasJustLoaded()
    {
        var content = HistoryFile.Write(Populated());
        var writes = 0;
        var store = new HistoryStore(() => content, _ => writes++);

        var history = store.Load();
        store.Save(history);

        Assert.Equal(0, writes);
    }

    [Fact]
    public void Save_FailedWrite_DoesNotThrowAndRetriesNextTime()
    {
        var attempts = 0;
        var store = new HistoryStore(() => null, _ =>
        {
            attempts++;
            throw new IOException("disk full");
        });

        var history = Populated();
        store.Save(history);
        store.Save(history);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, HistoryStore.DefaultPath, StringComparison.Ordinal);
        Assert.EndsWith("history.json", HistoryStore.DefaultPath, StringComparison.Ordinal);
    }
}
