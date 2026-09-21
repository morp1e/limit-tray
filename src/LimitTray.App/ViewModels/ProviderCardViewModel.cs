using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action _run;

    public RelayCommand(Action run) => _run = run;

    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _run();
}

/// <summary>
/// One provider's card. The instance lives as long as the provider is shown and is
/// refreshed in place by <see cref="Refresh"/>, so a new reading changes numbers and
/// colours without tearing the card down; a rebuilt card would reset its animations
/// and flash.
/// </summary>
public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly App _app;
    private readonly UsageHistory _history;
    private Strings _strings;

    private string _title = string.Empty;
    private double _ringPercent;
    private string? _ringPercentText;
    private System.Windows.Media.Brush _ringColour = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _ringTextBrush = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Effects.Effect? _ringGlow;
    private string _resetShortText = string.Empty;
    private IReadOnlyList<WindowRowViewModel> _rows = Array.Empty<WindowRowViewModel>();
    private bool _isDimmed;
    private string? _staleBadgeText;
    private string? _healthText;
    private bool _hasData;
    private string _lastUpdatedText = string.Empty;
    private bool _isExpanded;
    private string? _terminalError;
    private int _terminalErrorGeneration;
    private QuotaSnapshot? _lastSnapshot;
    private QuotaThresholds? _lastThresholds;
    private readonly List<WindowRowViewModel> _rowList = new();

    public ProviderCardViewModel(
        QuotaSnapshot snapshot,
        UsageHistory history,
        AppSettings settings,
        Strings strings,
        DateTimeOffset now,
        App app)
    {
        _app = app;
        _history = history;
        _strings = strings;
        Provider = snapshot.Provider;
        _isExpanded = settings.ExpandedProviders.Contains(snapshot.Provider);
        OpenTerminalCommand = new RelayCommand(OpenTerminal);
        Refresh(snapshot, settings, strings, now);
    }

    public string Provider { get; }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public double RingPercent { get => _ringPercent; private set => Set(ref _ringPercent, value); }
    /// <summary>The fullest window's percentage, or the question mark when nothing is known.</summary>
    public string? RingPercentText { get => _ringPercentText; private set => Set(ref _ringPercentText, value); }
    public System.Windows.Media.Brush RingColour { get => _ringColour; private set => Set(ref _ringColour, value); }
    public System.Windows.Media.Brush RingTextBrush { get => _ringTextBrush; private set => Set(ref _ringTextBrush, value); }
    public System.Windows.Media.Effects.Effect? RingGlow { get => _ringGlow; private set => Set(ref _ringGlow, value); }
    public string ResetShortText { get => _resetShortText; private set => Set(ref _resetShortText, value); }
    public IReadOnlyList<WindowRowViewModel> Rows { get => _rows; private set => Set(ref _rows, value); }
    /// <summary>Anything that is not a fresh reading is drawn dimmed.</summary>
    public bool IsDimmed { get => _isDimmed; private set => Set(ref _isDimmed, value); }
    public string? StaleBadgeText { get => _staleBadgeText; private set => Set(ref _staleBadgeText, value); }
    /// <summary>The failure in words. Shown for every non-fresh state, with or without retained numbers.</summary>
    public string? HealthText { get => _healthText; private set => Set(ref _healthText, value); }
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
    public string LastUpdatedText { get => _lastUpdatedText; private set => Set(ref _lastUpdatedText, value); }
    public string OpenTerminalText => _strings.OpenTerminal;
    public string OpenTerminalGlyph => Glyphs.Terminal;
    public RelayCommand OpenTerminalCommand { get; }

    public string? TerminalError
    {
        get => _terminalError;
        private set => Set(ref _terminalError, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            foreach (var row in _rowList) row.IsExpanded = value;

            var expanded = new HashSet<string>(_app.Settings.ExpandedProviders, StringComparer.Ordinal);
            if (value) expanded.Add(Provider);
            else expanded.Remove(Provider);
            _app.RememberExpanded(expanded);
        }
    }

    /// <summary>
    /// Applies a new reading to the existing card. An unchanged reading only advances the
    /// age texts: nothing else can have changed, and rebuilding brushes and rows for it
    /// would restart the bar animations on every tick.
    /// </summary>
    public void Refresh(QuotaSnapshot snapshot, AppSettings settings, Strings strings, DateTimeOffset now)
    {
        var sameReading = snapshot == _lastSnapshot && ReferenceEquals(strings, _strings)
            && settings.Thresholds == _lastThresholds;
        _strings = strings;
        _lastSnapshot = snapshot;
        _lastThresholds = settings.Thresholds;
        var fresh = snapshot.Health == HealthState.Fresh;

        if (sameReading)
        {
            RefreshAges(snapshot, strings, now);
            return;
        }

        var alpha = Theme.OpacityFor(snapshot.Health);

        Title = QuotaFormatter.ProviderTitle(snapshot.Provider, strings);
        HasData = snapshot.Session is not null || snapshot.Weekly is not null;
        IsDimmed = !fresh;
        HealthText = fresh ? null : QuotaFormatter.HealthText(snapshot, strings);
        RefreshAges(snapshot, strings, now);

        var windows = Windows(snapshot).ToList();
        var fullest = windows.Count == 0
            ? ((WindowKind Kind, QuotaWindow Window)?)null
            : windows.MaxBy(pair => pair.Window.Percent);

        // An unknown value is the question mark, never an empty gauge that reads as 0%.
        RingPercent = fullest is null ? 0 : Math.Clamp(fullest.Value.Window.Percent, 0, 100);
        RingPercentText = fullest is null
            ? Glyphs.Unknown
            : QuotaFormatter.Percent(fullest.Value.Window.Percent, strings);

        var ringRgb = fullest is null
            ? Theme.ColourFor(snapshot.Provider, QuotaSeverity.Normal, snapshot.Health)
            : Theme.ColourFor(
                snapshot.Provider,
                QuotaFormatter.SeverityFor(fullest.Value.Window.Percent, settings.Thresholds),
                snapshot.Health);
        RingColour = Brushes.Solid(ringRgb, alpha);
        RingTextBrush = Brushes.Solid(Brushes.Lighter(ringRgb), alpha);
        RingGlow = fresh ? Brushes.Glow(ringRgb, 0.45) : null;

        RefreshRows(windows, snapshot, settings, strings, now);

        RaisePropertyChanged(nameof(OpenTerminalText));
    }

    /// <summary>Everything that changes with the clock alone.</summary>
    private void RefreshAges(QuotaSnapshot snapshot, Strings strings, DateTimeOffset now)
    {
        ResetShortText = BuildResetShort(Windows(snapshot).ToList(), now, strings);
        StaleBadgeText = snapshot.Health == HealthState.Stale
            ? string.Format(CultureInfo.InvariantCulture, strings.StaleBadge,
                QuotaFormatter.Age(snapshot.FetchedAt, now, strings))
            : null;

        // "Updated just now" already carries its own verb; wrapping it in "Last updated ..."
        // read as a stutter on screen.
        var age = QuotaFormatter.Age(snapshot.FetchedAt, now, strings);
        LastUpdatedText = age == strings.UpdatedNow
            ? age
            : string.Format(CultureInfo.InvariantCulture, strings.LastUpdated, age);
    }

    /// <summary>
    /// Rows are matched by window kind and refreshed; the list object is replaced only
    /// when the set of windows changes, which keeps the item containers alive.
    /// </summary>
    private void RefreshRows(
        IReadOnlyList<(WindowKind Kind, QuotaWindow Window)> windows,
        QuotaSnapshot snapshot, AppSettings settings, Strings strings, DateTimeOffset now)
    {
        var sameShape = _rowList.Count == windows.Count
            && _rowList.Zip(windows).All(pair => pair.First.Kind == pair.Second.Kind);

        if (sameShape)
        {
            foreach (var (row, pair) in _rowList.Zip(windows))
                row.Refresh(pair.Window, snapshot, _history, settings, strings, now);
            return;
        }

        _rowList.Clear();
        foreach (var pair in windows)
            _rowList.Add(new WindowRowViewModel(
                pair.Kind, pair.Window, snapshot, _history, settings, strings, now, _isExpanded));
        Rows = _rowList.ToList();
    }

    private void OpenTerminal()
    {
        if (TerminalLauncher.Open(Provider))
        {
            TerminalError = null;
            return;
        }

        TerminalError = _strings.TerminalFailed;
        var generation = ++_terminalErrorGeneration;
        ClearTerminalError(generation);
    }

    private async void ClearTerminalError(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (generation == _terminalErrorGeneration) TerminalError = null;
    }

    private static IEnumerable<(WindowKind Kind, QuotaWindow Window)> Windows(QuotaSnapshot snapshot)
    {
        if (snapshot.Session is not null) yield return (WindowKind.Session, snapshot.Session);
        if (snapshot.Weekly is not null) yield return (WindowKind.Weekly, snapshot.Weekly);
    }

    private static string BuildResetShort(
        IReadOnlyList<(WindowKind Kind, QuotaWindow Window)> windows,
        DateTimeOffset now,
        Strings strings)
    {
        var reset = windows
            .Select(pair => pair.Window.ResetsAt)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .FirstOrDefault();
        if (reset == default) return strings.ResetUnknown;

        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return strings.ResetShort.Replace("{0}", strings.Resetting);
        return string.Format(CultureInfo.InvariantCulture, strings.ResetShort,
            QuotaFormatter.Duration(remaining, strings));
    }
}
