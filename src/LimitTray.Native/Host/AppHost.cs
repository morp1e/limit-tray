using System.Globalization;
using System.Runtime.InteropServices;
using LimitTray.Core.Claude;
using LimitTray.Core.Codex;
using LimitTray.Core.Collectors;
using LimitTray.Core.Fixtures;
using LimitTray.Core.History;
using LimitTray.Core.Http;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Process;
using LimitTray.Core.Settings;
using LimitTray.Core.Store;
using LimitTray.Native.Graphics;
using LimitTray.Native.Platform;
using LimitTray.Native.Popup;
using LimitTray.Native.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LimitTray.Native.Host;

internal sealed class AppHost : IAppActions, IDisposable
{
    private static readonly TimeSpan CollectorRestartDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CodexPopupAge = TimeSpan.FromMinutes(2);

    private readonly CommandLine _commandLine;
    private readonly CancellationTokenSource _cts = new();
    private readonly MessageWindow _window;
    private readonly Timers _timers;
    private readonly QuotaStore _store;
    private readonly HistoryStore _historyStore;
    private readonly SettingsStore _settingsStore;
    private readonly QuotaAlerts _alerts = new();
    private readonly UsageHistory _history;
    private readonly List<IQuotaCollector> _collectors = new();
    private readonly SystemHttpTransport? _transport;
    private readonly TrayUpdater _trayUpdater = new();
    private readonly TrayIcon _tray;
    private readonly TrayMenu _menu;
    private readonly IPopupController _popup;
    private readonly ITrayIconPainter _painter;
    private readonly HICON _applicationIcon;
    private HICON _currentIcon;
    private AppSettings _settings;
    private Strings _strings;
    private bool _isDarkTheme;
    private bool _graphicsFailureShown;
    private bool _shuttingDown;
    private DateTimeOffset _lastManualRefresh = DateTimeOffset.MinValue;

    public AppHost(CommandLine commandLine, HICON applicationIcon)
    {
        _commandLine = commandLine;
        _applicationIcon = applicationIcon;
        Directory.CreateDirectory(commandLine.DataDirectory);
        _settingsStore = new SettingsStore(
            () => Read(Path.Combine(commandLine.DataDirectory, "settings.json")),
            content => Write(Path.Combine(commandLine.DataDirectory, "settings.json"), content));
        _historyStore = new HistoryStore(
            () => Read(Path.Combine(commandLine.DataDirectory, "history.json")),
            content => Write(Path.Combine(commandLine.DataDirectory, "history.json"), content));

        _settings = _settingsStore.Load().Normalised();
        _strings = ResolveStrings(commandLine.Raw, _settings.Language);
        _isDarkTheme = ResolveDarkTheme(_settings.Theme);
        _alerts.UpdateThresholds(_settings.Thresholds);
        // A cold start shows the last known values with their real age instead of a blank
        // panel (v0.2 issue #2); the lane's first cut created an empty history here.
        _history = _historyStore.Load();
        _store = new QuotaStore(() => DateTimeOffset.Now,
            provider => QuotaStore.StaleAfterFor(provider, _settings.RefreshSeconds));

        _window = new MessageWindow();
        UiThread.Initialize(_window);
        _timers = new Timers(_window);
        _painter = new TrayIconPainter();
        _currentIcon = _painter.Paint(TrayIconModelBuilder.Build(Array.Empty<QuotaSnapshot>(), _settings), IconSize());
        if (_currentIcon == HICON.Null) _currentIcon = applicationIcon;
        _tray = new TrayIcon(_window, _currentIcon,
            QuotaFormatter.Tooltip(Array.Empty<QuotaSnapshot>(), DateTimeOffset.Now, _strings));
        _popup = new PopupController(this, _tray.GetIconRect);
        if (_popup is PopupController controller)
            controller.GraphicsFailed += OnGraphicsFailed;
        _menu = new TrayMenu(_window, () => _strings, () => StartupEnabled, SetStartup,
            _popup.OpenSettings, Shutdown);

        // Click fires for either button, so the right button used to open the panel and
        // the context menu at the same time. Only the left button toggles.
        _tray.LeftClick += OnTrayLeftClick;
        _tray.RightClick += _menu.Show;
        _store.Changed += OnSnapshotApplied;
        RegisterWindowMessages();
        _timers.StartStaleness();

        if (StartupRegistration.IsEnabled() && !StartupRegistration.PointsToCurrentExecutable())
            StartupRegistration.SetEnabled(true, commandLine.Raw);

        if (_commandLine.FixturePath is { } fixturePath)
            StartFixture(fixturePath);
        else
        {
            _transport = new SystemHttpTransport();
            StartCollector(BuildClaudeCollector(_transport));
            StartCollector(BuildCodexCollector());
        }

        // Seed only after the icon and change handler exist; applying a stale cache entry
        // immediately invokes the same UI update path used for a live collector result.
        SeedFromHistory();

        if (_commandLine.Show) UiThread.Post(_popup.Toggle);
    }

    public AppSettings Settings => _settings;
    public Strings Strings => _strings;
    public UsageHistory History => _history;
    public IReadOnlyList<QuotaSnapshot> Snapshots => _store.All();
    public bool IsDarkTheme => _isDarkTheme;
    public bool SettingsSaveFailed => _settingsStore.LastSaveFailed;
    public bool StartupEnabled => StartupRegistration.IsEnabled();

    public int Run() => MessageWindow.RunLoop();

    private static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static Strings ResolveStrings(IReadOnlyList<string> args, LanguageMode mode)
    {
        // The command line still wins, as it did in v0.2; the setting is the default under it.
        if (args.Any(a => a.StartsWith("--lang", StringComparison.OrdinalIgnoreCase)))
            return LanguageArguments.Resolve(args, CultureInfo.CurrentUICulture);
        return mode switch
        {
            LanguageMode.Turkish => Strings.Turkish,
            LanguageMode.English => Strings.English,
            _ => Strings.ForCulture(CultureInfo.CurrentUICulture),
        };
    }

    private static bool ResolveDarkTheme(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemTheme.IsDark(),
    };

    private void RegisterWindowMessages()
    {
        var timerMessage = (uint)PInvoke.WM_TIMER;
        _window.Register(timerMessage, OnTimer);
        var settingChange = (uint)PInvoke.WM_SETTINGCHANGE;
        _window.Register(settingChange, OnSettingChange);
        var probe = PInvoke.RegisterWindowMessage("LimitTray.Probe");
        _window.Register(probe, (wParam, _) =>
        {
            if (wParam.Value == 1) _popup.Toggle();
            else if (wParam.Value == 2) _popup.OpenSettings();
            return new LRESULT(0);
        });
    }

    private LRESULT? OnTimer(WPARAM wParam, LPARAM lParam)
    {
        if (wParam.Value == Timers.Staleness)
            _store.RefreshStaleness();
        else if (wParam.Value == Timers.SaveHistory)
        {
            _timers.StopHistorySave();
            _historyStore.Save(_history);
        }
        return new LRESULT(0);
    }

    private LRESULT? OnSettingChange(WPARAM wParam, LPARAM lParam)
    {
        var name = lParam.Value == 0 ? null : Marshal.PtrToStringUni((nint)lParam.Value);
        if (string.Equals(name, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
            UpdateResolvedTheme();
        return null;
    }

    private void UpdateResolvedTheme()
    {
        var dark = ResolveDarkTheme(_settings.Theme);
        if (_isDarkTheme == dark) return;
        _isDarkTheme = dark;
        UpdateTray();
        _popup.OnDataChanged();
    }

    private void StartFixture(string path)
    {
        var data = FixtureFile.Read(File.ReadAllText(path), DateTimeOffset.Now);
        foreach (var series in data.History) _history.Import(series);
        foreach (var providerGroup in data.Snapshots.GroupBy(snapshot => snapshot.Provider, StringComparer.Ordinal))
            StartCollector(new FixtureCollector(providerGroup.Key, providerGroup.ToArray()));
    }

    private IQuotaCollector BuildClaudeCollector(IHttpTransport transport) => new ClaudeCollector(
        transport,
        ClaudeCredentialReader.FromDefaultPath(),
        () => DateTimeOffset.Now,
        Task.Delay,
        () => TimeSpan.FromSeconds(_settings.RefreshSeconds));

    private static IQuotaCollector BuildCodexCollector()
    {
        var binary = CodexBinaryLocator.LocateDefault();
        var reader = new CodexServerReader(
            () => binary is null
                ? throw new InvalidOperationException("codex.exe not found")
                : new StdioJsonRpcProcess(binary, "app-server"),
            () => DateTimeOffset.Now);
        var tail = new CodexRolloutTail(CodexRolloutTail.DefaultSessionsRoot, () => DateTimeOffset.Now);
        return new CodexCollector(reader.ReadOnceAsync, tail.Poll, () => DateTimeOffset.Now, Task.Delay);
    }

    private void StartCollector(IQuotaCollector collector)
    {
        _collectors.Add(collector);
        // A fault that escapes a collector is reported as ProtocolBroken with the
        // exception type, and the watch restarts after a pause; an unobserved faulted
        // task would freeze that provider silently until the next launch.
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await foreach (var snapshot in collector.Watch(_cts.Token).ConfigureAwait(false))
                        UiThread.Post(() => _store.Apply(snapshot));
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    var failed = QuotaSnapshot.Unhealthy(collector.Provider, HealthState.ProtocolBroken,
                        DateTimeOffset.Now, ex.GetType().Name);
                    UiThread.Post(() => _store.Apply(failed));
                }

                try { await Task.Delay(CollectorRestartDelay, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    private void SeedFromHistory()
    {
        foreach (var provider in _history.Providers())
        {
            var snapshot = _history.LastKnown(provider);
            if (snapshot is not null) _store.Apply(snapshot);
        }
    }

    private void OnSnapshotApplied(QuotaSnapshot snapshot)
    {
        var before = _history.Version;
        _history.Observe(snapshot);
        if (_history.Version != before) _timers.DebounceHistorySave();

        foreach (var alert in _alerts.Inspect(snapshot))
        {
            if (_settings.Notifications)
                _tray.ShowBalloon(_strings.WarningNotificationTitle, QuotaAlerts.Body(alert, _strings));
        }

        UpdateTray();
        _popup.OnDataChanged();
    }

    private int IconSize()
    {
        const Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX smallIconMetric =
            Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXSMICON;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            var dpi = PInvoke.GetDpiForSystem();
            if (dpi == 0) dpi = 96;
            return PInvoke.GetSystemMetricsForDpi(smallIconMetric, dpi);
        }

        return PInvoke.GetSystemMetrics(smallIconMetric);
    }

    private void UpdateTray()
    {
        var snapshots = _store.All();
        var model = TrayIconModelBuilder.Build(snapshots, _settings);
        var size = IconSize();
        if (_trayUpdater.ShouldRedraw(model, size, _isDarkTheme))
        {
            var replacement = _painter.Paint(model, size);
            if (replacement != HICON.Null)
            {
                var previous = _currentIcon;
                _currentIcon = replacement;
                _tray.SetIcon(replacement);
                if (previous != HICON.Null && previous != _applicationIcon) PInvoke.DestroyIcon(previous);
            }
        }

        _tray.SetTooltip(QuotaFormatter.Tooltip(snapshots, DateTimeOffset.Now, _strings));
    }

    private void OnTrayLeftClick()
    {
        _popup.Toggle();
        if (_popup.IsOpen && ShouldRefreshCodex(_store.Get("codex")?.FetchedAt, DateTimeOffset.Now))
            _collectors.FirstOrDefault(c => string.Equals(c.Provider, "codex", StringComparison.OrdinalIgnoreCase))?.RequestRefresh();
    }

    internal static bool ShouldRefreshCodex(DateTimeOffset? fetchedAt, DateTimeOffset now) =>
        fetchedAt is null || now - fetchedAt.Value > CodexPopupAge;

    public void ApplySettings(AppSettings next)
    {
        var previous = _settings;
        _settings = next.Normalised();
        _settingsStore.Save(_settings);
        if (previous.Thresholds != _settings.Thresholds) _alerts.UpdateThresholds(_settings.Thresholds);
        if (previous.Language != _settings.Language) _strings = ResolveStrings(_commandLine.Raw, _settings.Language);

        if (previous.RefreshSeconds > _settings.RefreshSeconds)
            // The interval delegate reads settings on its next wait; a shorter interval
            // takes effect now by cutting the current wait short.
            foreach (var collector in _collectors) collector.RequestRefresh();

        // Thresholds, tray style/source and language change what the tray draws.
        var oldDark = _isDarkTheme;
        _isDarkTheme = ResolveDarkTheme(_settings.Theme);
        var redraw = oldDark != _isDarkTheme ||
            previous.Thresholds != _settings.Thresholds ||
            previous.TrayStyle != _settings.TrayStyle ||
            previous.TraySource != _settings.TraySource ||
            previous.Language != _settings.Language;
        if (redraw) UpdateTray();
        _popup.OnDataChanged();
    }

    public void RememberExpanded(IReadOnlySet<string> expanded)
    {
        // Expansion is persisted view state. It changes no colour, icon or text, so it
        // is saved without a redraw; that redraw made the panel look like it closed.
        _settings = _settings with { ExpandedProviders = expanded };
        _settingsStore.Save(_settings);
    }

    public void RefreshNow()
    {
        var now = DateTimeOffset.Now;
        if (now - _lastManualRefresh < TimeSpan.FromSeconds(5)) return;
        _lastManualRefresh = now;
        foreach (var collector in _collectors) collector.RequestRefresh();
    }

    public void SetStartup(bool enabled)
    {
        StartupRegistration.SetEnabled(enabled, _commandLine.Raw);
        // The state is read back rather than assumed: policy can deny the registry write.
        _ = StartupEnabled;
    }

    public bool OpenTerminal(string provider) => TerminalLauncher.Open(provider);
    public void OpenRepository() => Browser.OpenRepository();

    private void OnGraphicsFailed()
    {
        if (_graphicsFailureShown) return;
        _graphicsFailureShown = true;
        _tray.ShowBalloon(_strings.WarningNotificationTitle, _strings.GraphicsFailure);
    }

    private void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _cts.Cancel();
        _timers.StopAll();
        _historyStore.Save(_history);
        _settingsStore.Save(_settings);
        _popup.Close();
        _tray.Dispose();
        if (_currentIcon != HICON.Null && _currentIcon != _applicationIcon)
        {
            PInvoke.DestroyIcon(_currentIcon);
            _currentIcon = HICON.Null;
        }
        PInvoke.PostQuitMessage(0);
    }

    public void Dispose()
    {
        if (!_shuttingDown) Shutdown();
        _transport?.Dispose();
        _cts.Dispose();
        _window.Dispose();
    }
}
