using LimitTray.Core.Fixtures;
using LimitTray.Core.History;
using LimitTray.Core.Model;

namespace LimitTray.Tests.Fixtures;

public class FixtureFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static string Repo(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "LimitTray.sln"))) dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir, rel);
    }

    [Fact]
    public void Normal_ReadsBothProvidersWithRelativeTimes()
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/normal.json")), Now);
        var claude = data.Snapshots.Single(s => s.Provider == "claude");
        Assert.Equal(18, claude.Session!.Percent);
        Assert.Equal(Now.AddMinutes(287), claude.Session.ResetsAt);
        Assert.Equal(Now, claude.FetchedAt);
    }

    [Fact]
    public void RateLimited_IsASequenceEndingUnhealthy()
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/ratelimited.json")), Now);
        var claude = data.Snapshots.Where(s => s.Provider == "claude").ToList();
        Assert.Equal(HealthState.Fresh, claude[0].Health);
        Assert.Equal(HealthState.RateLimited, claude[^1].Health);
        Assert.Null(claude[^1].Session);
    }

    [Theory]
    [InlineData("normal.json")] [InlineData("caution.json")] [InlineData("warning.json")]
    [InlineData("stale.json")] [InlineData("ratelimited.json")]
    public void EveryFixture_SupportsABurnRateEstimate(string file)
    {
        var data = FixtureFile.Read(File.ReadAllText(Repo("tools/Footprint/fixtures/" + file)), Now);
        var history = new UsageHistory();
        foreach (var s in data.History) history.Import(s);
        Assert.NotNull(history.Estimate("claude", WindowKind.Session, Now));
    }

    [Theory]
    [InlineData("""{"snapshots":[]}""", "version")]
    [InlineData("""{"version":1,"snapshots":[{"provider":"claude","health":"Great"}]}""", "health")]
    [InlineData("""{"version":1,"snapshots":[{"provider":"claude","health":"Fresh","session":{"percent":150}}]}""", "percent")]
    public void BadInput_ThrowsFormatExceptionNamingTheField(string json, string field)
    {
        var ex = Assert.Throws<FormatException>(() => FixtureFile.Read(json, Now));
        Assert.Contains(field, ex.Message);
    }
}
