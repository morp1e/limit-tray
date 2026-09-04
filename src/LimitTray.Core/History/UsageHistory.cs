using LimitTray.Core.Model;

namespace LimitTray.Core.History;

/// <summary>
/// Remembers recent quota observations per provider and window, and answers two
/// questions from them: what the last known values were (so a cold start during an
/// outage is not blank) and how fast the quota is being consumed.
///
/// The only things stored are percentages, window lengths and timestamps. Nothing
/// read from the credential file ever reaches this class.
/// </summary>
public sealed class UsageHistory
{
    /// <summary>Upper bound per series. At a 120s poll this is roughly seven hours.</summary>
    public const int MaxSamples = 220;

    /// <summary>A fit needs at least this many observations.</summary>
    public const int MinimumSamples = 3;

    /// <summary>A fit needs observations covering at least this much time.</summary>
    public static readonly TimeSpan MinimumSpan = TimeSpan.FromMinutes(10);

    /// <summary>Below this rate the window is treated as not moving; no projection.</summary>
    public const double MinimumRatePerHour = 0.5;

    /// <summary>A drop larger than this means the window rolled over, not that usage fell.</summary>
    public const double ResetDropTolerance = 1.0;

    private readonly Dictionary<SeriesKey, Series> _series = new();
    private readonly object _gate = new();

    private readonly record struct SeriesKey(string Provider, WindowKind Kind);

    private sealed class Series
    {
        public List<UsageSample> Samples { get; } = new();
        public TimeSpan WindowLength { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }

    /// <summary>
    /// Records the windows of a snapshot. Only Fresh snapshots are recorded: an error
    /// carries no measurement, and stale data is a value that was already recorded once
    /// at the time it was actually measured.
    /// </summary>
    public void Observe(QuotaSnapshot snapshot)
    {
        if (snapshot.Health != HealthState.Fresh) return;

        lock (_gate)
        {
            Record(snapshot.Provider, WindowKind.Session, snapshot.Session, snapshot.FetchedAt);
            Record(snapshot.Provider, WindowKind.Weekly, snapshot.Weekly, snapshot.FetchedAt);
        }
    }

    private void Record(string provider, WindowKind kind, QuotaWindow? window, DateTimeOffset at)
    {
        if (window is null) return;

        var key = new SeriesKey(provider, kind);
        if (!_series.TryGetValue(key, out var series))
        {
            series = new Series();
            _series[key] = series;
        }

        if (series.Samples.Count > 0)
        {
            var last = series.Samples[^1];

            // A percentage that fell, or a reset that moved forward, means the window
            // rolled over. Fitting a rate across a reset would turn two perfectly good
            // measurements into a negative slope, so the old series is dropped.
            var rolledOver =
                window.Percent < last.Percent - ResetDropTolerance ||
                (series.ResetsAt is { } previousReset && window.ResetsAt is { } nextReset &&
                 nextReset > previousReset + TimeSpan.FromMinutes(1));

            if (rolledOver) series.Samples.Clear();
            else if (at <= last.At) return; // duplicate or out of order; no new information
        }

        series.Samples.Add(new UsageSample(at, window.Percent));
        series.WindowLength = window.WindowLength;
        series.ResetsAt = window.ResetsAt;
        Trim(series, at);
    }

    private static void Trim(Series series, DateTimeOffset now)
    {
        if (series.WindowLength > TimeSpan.Zero)
        {
            var oldest = now - series.WindowLength;
            series.Samples.RemoveAll(s => s.At < oldest);
        }

        if (series.Samples.Count > MaxSamples)
            series.Samples.RemoveRange(0, series.Samples.Count - MaxSamples);
    }

    /// <summary>Every provider that has at least one recorded window.</summary>
    public IReadOnlyList<string> Providers()
    {
        lock (_gate)
            return _series.Keys.Select(k => k.Provider).Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>The observations of one series, oldest first.</summary>
    public IReadOnlyList<UsageSample> Samples(string provider, WindowKind kind)
    {
        lock (_gate)
            return _series.TryGetValue(new SeriesKey(provider, kind), out var series)
                ? series.Samples.ToList()
                : Array.Empty<UsageSample>();
    }

    /// <summary>
    /// The last known values for a provider, always marked Stale and carrying the time
    /// they were actually measured. Null when nothing is known.
    /// </summary>
    public QuotaSnapshot? LastKnown(string provider)
    {
        lock (_gate)
        {
            var session = LastWindow(provider, WindowKind.Session);
            var weekly = LastWindow(provider, WindowKind.Weekly);
            if (session is null && weekly is null) return null;

            var fetchedAt = LastAt(provider, WindowKind.Session);
            var weeklyAt = LastAt(provider, WindowKind.Weekly);
            if (weeklyAt is not null && (fetchedAt is null || weeklyAt > fetchedAt))
                fetchedAt = weeklyAt;

            return new QuotaSnapshot(
                provider, session, weekly, HealthState.Stale, fetchedAt!.Value, null);
        }
    }

    private QuotaWindow? LastWindow(string provider, WindowKind kind)
    {
        if (!_series.TryGetValue(new SeriesKey(provider, kind), out var series)) return null;
        if (series.Samples.Count == 0) return null;
        return new QuotaWindow(series.Samples[^1].Percent, series.ResetsAt, series.WindowLength);
    }

    private DateTimeOffset? LastAt(string provider, WindowKind kind)
    {
        if (!_series.TryGetValue(new SeriesKey(provider, kind), out var series)) return null;
        return series.Samples.Count == 0 ? null : series.Samples[^1].At;
    }

    /// <summary>
    /// Fits a consumption rate over the retained observations and projects exhaustion.
    /// Returns null whenever the history cannot honestly support a projection: too few
    /// points, too short a span, a window that is not moving, or one already full.
    /// </summary>
    public BurnRateEstimate? Estimate(string provider, WindowKind kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_series.TryGetValue(new SeriesKey(provider, kind), out var series)) return null;

            var samples = series.Samples;
            if (samples.Count < MinimumSamples) return null;

            var first = samples[0];
            var last = samples[^1];
            var span = last.At - first.At;
            if (span < MinimumSpan) return null;
            if (last.Percent >= 100.0) return null;

            var slope = SlopePerHour(samples);
            if (double.IsNaN(slope) || slope < MinimumRatePerHour) return null;

            var exhaustsAt = last.At + TimeSpan.FromHours((100.0 - last.Percent) / slope);
            var remaining = exhaustsAt - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            var resetsFirst = series.ResetsAt is { } reset && reset <= exhaustsAt;

            return new BurnRateEstimate(
                slope, remaining, exhaustsAt, resetsFirst, samples.Count, span);
        }
    }

    /// <summary>
    /// Least-squares slope in percent per hour. A plain first-to-last difference would
    /// swing on a single noisy reading; the fit uses every retained point.
    /// </summary>
    private static double SlopePerHour(IReadOnlyList<UsageSample> samples)
    {
        var origin = samples[0].At;
        double meanHours = 0, meanPercent = 0;

        foreach (var sample in samples)
        {
            meanHours += (sample.At - origin).TotalHours;
            meanPercent += sample.Percent;
        }

        meanHours /= samples.Count;
        meanPercent /= samples.Count;

        double covariance = 0, variance = 0;
        foreach (var sample in samples)
        {
            var hours = (sample.At - origin).TotalHours - meanHours;
            covariance += hours * (sample.Percent - meanPercent);
            variance += hours * hours;
        }

        return variance <= 0 ? double.NaN : covariance / variance;
    }

    /// <summary>Every non-empty series, for serialisation.</summary>
    public IReadOnlyList<UsageSeries> Export()
    {
        lock (_gate)
            return _series
                .Where(pair => pair.Value.Samples.Count > 0)
                .OrderBy(pair => pair.Key.Provider, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Kind)
                .Select(pair => new UsageSeries(
                    pair.Key.Provider, pair.Key.Kind, pair.Value.WindowLength,
                    pair.Value.ResetsAt, pair.Value.Samples.ToList()))
                .ToList();
    }

    /// <summary>Replaces one series wholesale, for deserialisation.</summary>
    public void Import(UsageSeries series)
    {
        var ordered = series.Samples.OrderBy(s => s.At).ToList();
        if (ordered.Count == 0) return;

        lock (_gate)
        {
            var stored = new Series
            {
                WindowLength = series.WindowLength,
                ResetsAt = series.ResetsAt,
            };
            stored.Samples.AddRange(ordered);
            if (stored.Samples.Count > MaxSamples)
                stored.Samples.RemoveRange(0, stored.Samples.Count - MaxSamples);
            _series[new SeriesKey(series.Provider, series.Kind)] = stored;
        }
    }
}
