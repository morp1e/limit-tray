using LimitTray.Core.History;
using LimitTray.Core.Model;
using Xunit;

namespace LimitTray.Tests.History;

public class UsageHistoryTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);

    private static QuotaSnapshot Fresh(
        double sessionPercent, DateTimeOffset at,
        DateTimeOffset? resetsAt = null, double? weeklyPercent = null) =>
        new("claude",
            new QuotaWindow(sessionPercent, resetsAt, FiveHours),
            weeklyPercent is null ? null : new QuotaWindow(weeklyPercent.Value, null, TimeSpan.FromDays(7)),
            HealthState.Fresh, at, null);

    private static void AssertClose(TimeSpan expected, TimeSpan actual) =>
        Assert.True(Math.Abs((expected - actual).TotalSeconds) < 1.0,
            $"expected {expected}, got {actual}");

    /// <summary>Records a straight ramp of `count` points across `span`.</summary>
    private static UsageHistory Ramp(
        double from, double to, int count, TimeSpan span, DateTimeOffset? resetsAt = null)
    {
        var history = new UsageHistory();
        for (var i = 0; i < count; i++)
        {
            var fraction = count == 1 ? 0 : (double)i / (count - 1);
            history.Observe(Fresh(
                from + (to - from) * fraction,
                Start + span * fraction,
                resetsAt));
        }
        return history;
    }

    [Fact]
    public void Observe_IgnoresUnhealthySnapshots()
    {
        var history = new UsageHistory();

        history.Observe(QuotaSnapshot.Unhealthy(
            "claude", HealthState.RateLimited, Start, "throttled"));
        history.Observe(QuotaSnapshot.Unhealthy(
            "claude", HealthState.AuthMissing, Start, "no token"));

        Assert.Empty(history.Samples("claude", WindowKind.Session));
        Assert.Null(history.LastKnown("claude"));
    }

    [Fact]
    public void Observe_IgnoresStaleSnapshots()
    {
        var history = new UsageHistory();

        history.Observe(new QuotaSnapshot(
            "claude", new QuotaWindow(40, null, FiveHours), null,
            HealthState.Stale, Start, null));

        Assert.Empty(history.Samples("claude", WindowKind.Session));
    }

    [Fact]
    public void Observe_RecordsBothWindows()
    {
        var history = new UsageHistory();
        history.Observe(Fresh(20, Start, weeklyPercent: 55));

        Assert.Single(history.Samples("claude", WindowKind.Session));
        Assert.Single(history.Samples("claude", WindowKind.Weekly));
        Assert.Equal(55, history.Samples("claude", WindowKind.Weekly)[0].Percent);
    }

    [Fact]
    public void Observe_IgnoresRepeatedTimestamp()
    {
        var history = new UsageHistory();
        history.Observe(Fresh(20, Start));
        history.Observe(Fresh(21, Start));

        var samples = history.Samples("claude", WindowKind.Session);
        Assert.Single(samples);
        Assert.Equal(20, samples[0].Percent);
    }

    [Fact]
    public void Observe_PercentDrop_StartsANewSeries()
    {
        var history = new UsageHistory();
        history.Observe(Fresh(80, Start));
        history.Observe(Fresh(85, Start.AddMinutes(10)));
        history.Observe(Fresh(2, Start.AddMinutes(20)));

        var samples = history.Samples("claude", WindowKind.Session);
        Assert.Single(samples);
        Assert.Equal(2, samples[0].Percent);
    }

    [Fact]
    public void Observe_ResetMovingForward_StartsANewSeries()
    {
        var first = Start.AddHours(2);
        var second = Start.AddHours(7);

        var history = new UsageHistory();
        history.Observe(Fresh(40, Start, first));
        history.Observe(Fresh(41, Start.AddMinutes(10), first));
        history.Observe(Fresh(42, Start.AddMinutes(20), second));

        Assert.Single(history.Samples("claude", WindowKind.Session));
    }

    [Fact]
    public void Observe_DropsSamplesOlderThanTheWindow()
    {
        var history = new UsageHistory();
        history.Observe(Fresh(10, Start));
        history.Observe(Fresh(20, Start.AddHours(6)));

        var samples = history.Samples("claude", WindowKind.Session);
        Assert.Single(samples);
        Assert.Equal(20, samples[0].Percent);
    }

    [Fact]
    public void Observe_CapsTheNumberOfSamples()
    {
        var history = new UsageHistory();
        for (var i = 0; i < UsageHistory.MaxSamples + 50; i++)
            history.Observe(Fresh(1, Start.AddSeconds(i)));

        Assert.Equal(UsageHistory.MaxSamples, history.Samples("claude", WindowKind.Session).Count);
    }

    [Fact]
    public void LastKnown_IsStaleAndKeepsTheOriginalFetchTime()
    {
        var history = new UsageHistory();
        history.Observe(Fresh(33, Start, weeklyPercent: 44));

        var snapshot = history.LastKnown("claude");

        Assert.NotNull(snapshot);
        Assert.Equal(HealthState.Stale, snapshot!.Health);
        Assert.Equal(Start, snapshot.FetchedAt);
        Assert.Equal(33, snapshot.Session!.Percent);
        Assert.Equal(44, snapshot.Weekly!.Percent);
        Assert.Null(snapshot.Detail);
    }

    [Fact]
    public void LastKnown_UnknownProvider_IsNull() =>
        Assert.Null(new UsageHistory().LastKnown("codex"));

    [Fact]
    public void Estimate_TooFewSamples_IsNull()
    {
        var history = Ramp(10, 20, 2, TimeSpan.FromHours(1));
        Assert.Null(history.Estimate("claude", WindowKind.Session, Start.AddHours(1)));
    }

    [Fact]
    public void Estimate_TooShortASpan_IsNull()
    {
        var history = Ramp(10, 20, 5, TimeSpan.FromMinutes(5));
        Assert.Null(history.Estimate("claude", WindowKind.Session, Start.AddMinutes(5)));
    }

    [Fact]
    public void Estimate_FlatUsage_IsNull()
    {
        var history = Ramp(30, 30, 6, TimeSpan.FromHours(2));
        Assert.Null(history.Estimate("claude", WindowKind.Session, Start.AddHours(2)));
    }

    [Fact]
    public void Estimate_AlreadyFull_IsNull()
    {
        var history = Ramp(80, 100, 6, TimeSpan.FromHours(2));
        Assert.Null(history.Estimate("claude", WindowKind.Session, Start.AddHours(2)));
    }

    [Fact]
    public void Estimate_FitsTheRateAndProjectsExhaustion()
    {
        // 10% to 20% across one hour is 10 percent per hour; 80 percent remain,
        // so the window fills eight hours after the last observation.
        var history = Ramp(10, 20, 5, TimeSpan.FromHours(1));
        var now = Start.AddHours(1);

        var estimate = history.Estimate("claude", WindowKind.Session, now);

        Assert.NotNull(estimate);
        Assert.Equal(10.0, estimate!.PercentPerHour, 6);
        AssertClose(TimeSpan.FromHours(8), estimate.TimeToFull);
        AssertClose(TimeSpan.FromHours(8), estimate.ExhaustsAt - now);
        Assert.False(estimate.ResetsFirst);
        Assert.Equal(5, estimate.Samples);
    }

    [Fact]
    public void Estimate_CountsDownFromNow_NotFromTheLastSample()
    {
        var history = Ramp(10, 20, 5, TimeSpan.FromHours(1));

        var estimate = history.Estimate("claude", WindowKind.Session, Start.AddHours(3));

        AssertClose(TimeSpan.FromHours(6), estimate!.TimeToFull);
    }

    [Fact]
    public void Estimate_PastTheProjection_ClampsToZero()
    {
        var history = Ramp(10, 20, 5, TimeSpan.FromHours(1));

        var estimate = history.Estimate("claude", WindowKind.Session, Start.AddHours(40));

        Assert.Equal(TimeSpan.Zero, estimate!.TimeToFull);
    }

    [Fact]
    public void Estimate_ResetBeforeExhaustion_SaysSo()
    {
        // The window fills eight hours after the last sample, but resets in two.
        var reset = Start.AddHours(3);
        var history = Ramp(10, 20, 5, TimeSpan.FromHours(1), reset);

        var estimate = history.Estimate("claude", WindowKind.Session, Start.AddHours(1));

        Assert.True(estimate!.ResetsFirst);
    }

    [Fact]
    public void Estimate_SmallJitterKeepsTheSeriesAndTheFit()
    {
        // Readings wobble by a few tenths without the window having reset. That is under
        // the rollover tolerance, so the series survives and the fit still reads the
        // underlying ramp of roughly twelve percent per hour.
        var history = new UsageHistory();
        double[] percents = [10, 13, 12.5, 15, 17, 16.5, 22];
        for (var i = 0; i < percents.Length; i++)
            history.Observe(Fresh(percents[i], Start.AddMinutes(10 * i)));

        Assert.Equal(percents.Length, history.Samples("claude", WindowKind.Session).Count);

        var estimate = history.Estimate("claude", WindowKind.Session, Start.AddHours(1));

        Assert.NotNull(estimate);
        Assert.InRange(estimate!.PercentPerHour, 9.0, 15.0);
    }

    [Fact]
    public void Estimate_AfterALargeDrop_HasNoHistoryToFit()
    {
        // A drop past the tolerance is read as a rollover. Withdrawing the projection is
        // the correct answer here: a fit spanning a reset would be a confident wrong one.
        var history = Ramp(10, 40, 6, TimeSpan.FromHours(1));
        history.Observe(Fresh(2, Start.AddHours(1).AddMinutes(10)));

        Assert.Null(history.Estimate("claude", WindowKind.Session, Start.AddHours(2)));
    }

    [Fact]
    public void Estimate_UnknownSeries_IsNull() =>
        Assert.Null(new UsageHistory().Estimate("codex", WindowKind.Weekly, Start));
}
