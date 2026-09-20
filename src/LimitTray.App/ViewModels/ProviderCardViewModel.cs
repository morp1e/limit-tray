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

public sealed class ProviderCardViewModel : ObservableObject
{
    private readonly App _app;
    private readonly QuotaSnapshot _snapshot;
    private readonly UsageHistory _history;
    private readonly Strings _strings;
    private readonly AppSettings _settings;
    private readonly DateTimeOffset _now;
    private bool _isExpanded;
    private string? _terminalError;
    private int _terminalErrorGeneration;

    public ProviderCardViewModel(
        QuotaSnapshot snapshot,
        UsageHistory history,
        AppSettings settings,
        Strings strings,
        DateTimeOffset now,
        App app)
    {
        _app = app;
        _snapshot = snapshot;
        _history = history;
        _strings = strings;
        _settings = settings;
        _now = now;

        Provider = snapshot.Provider;
        Title = QuotaFormatter.ProviderTitle(snapshot.Provider, strings);
        HasData = snapshot.Session is not null || snapshot.Weekly is not null;
        IsStale = snapshot.Health == HealthState.Stale;
        HealthText = HasData ? null : QuotaFormatter.HealthText(snapshot, strings);
        StaleBadgeText = IsStale
            ? string.Format(CultureInfo.InvariantCulture, strings.StaleBadge,
                QuotaFormatter.Age(snapshot.FetchedAt, now, strings))
            : null;
        // "Updated just now" already carries its own verb; wrapping it in "Last updated ..."
        // read as a stutter on screen.
        var age = QuotaFormatter.Age(snapshot.FetchedAt, now, strings);
        LastUpdatedText = age == strings.UpdatedNow
            ? age
            : string.Format(CultureInfo.InvariantCulture, strings.LastUpdated, age);

        var windows = Windows(snapshot).ToList();
        var fullest = windows.Count == 0
            ? ((WindowKind Kind, QuotaWindow Window)?)null
            : windows.MaxBy(pair => pair.Window.Percent);
        RingPercent = fullest is null ? 0 : Math.Clamp(fullest.Value.Window.Percent, 0, 100);
        RingPercentText = fullest is null ? null : QuotaFormatter.Percent(fullest.Value.Window.Percent, strings);
        RingColour = fullest is null
            ? Brushes.Solid(Theme.ColourFor(snapshot.Provider, QuotaSeverity.Normal, snapshot.Health),
                Theme.OpacityFor(snapshot.Health))
            : Brushes.Solid(Theme.ColourFor(
                snapshot.Provider,
                QuotaFormatter.SeverityFor(fullest.Value.Window.Percent, settings.Thresholds),
                snapshot.Health),
                Theme.OpacityFor(snapshot.Health));

        ResetShortText = BuildResetShort(windows, now, strings);
        Rows = windows.Select(pair => new WindowRowViewModel(
            pair.Kind, pair.Window, snapshot, history, settings, strings, now)).ToList();
        _isExpanded = settings.ExpandedProviders.Contains(snapshot.Provider);
        OpenTerminalCommand = new RelayCommand(OpenTerminal);
    }

    public string Provider { get; }
    public string Title { get; }
    public double RingPercent { get; }
    public string? RingPercentText { get; }
    public System.Windows.Media.Brush RingColour { get; }
    public string ResetShortText { get; }
    public IReadOnlyList<WindowRowViewModel> Rows { get; }
    public bool IsStale { get; }
    public string? StaleBadgeText { get; }
    public string? HealthText { get; }
    public bool HasData { get; }
    public string LastUpdatedText { get; }
    public string OpenTerminalText => _strings.OpenTerminal;
    public string OpenTerminalGlyph => "⌁";
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

            var expanded = new HashSet<string>(_app.Settings.ExpandedProviders, StringComparer.Ordinal);
            if (value) expanded.Add(Provider);
            else expanded.Remove(Provider);
            _app.ApplySettings(_app.Settings with { ExpandedProviders = expanded });
        }
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
