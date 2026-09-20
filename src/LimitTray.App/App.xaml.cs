using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using LimitTray.Core.Claude;
using LimitTray.Core.Codex;
using LimitTray.Core.Collectors;
using LimitTray.Core.History;
using LimitTray.Core.Http;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Process;
using LimitTray.Core.Settings;
using LimitTray.Core.Store;

namespace LimitTray.App;

public partial class App : System.Windows.Application
{
    /// <summary>How often staleness is re-evaluated and the history is written out.</summary>
    private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _cts = new();
    private readonly QuotaStore _store = new(() => DateTimeOffset.Now);
    private readonly QuotaAlerts _alerts = new();
    private readonly HistoryStore _historyStore = HistoryStore.ForDefaultPath();
    private readonly SettingsStore _settingsStore = SettingsStore.ForDefaultPath();
    private AppSettings _settings = AppSettings.Default;
    private readonly List<IQuotaCollector> _collectors = new();
    private DateTimeOffset _lastManualRefresh = DateTimeOffset.MinValue;

    private UsageHistory _history = new();
    private NotifyIcon? _trayIcon;
    private RenderedIcon? _currentIcon;
    private QuotaPopup? _popup;
    private SystemHttpTransport? _transport;
    private System.Windows.Threading.DispatcherTimer? _housekeepingTimer;
    private ToolStripMenuItem? _startupItem;
    private Strings _strings = Strings.ForCulture(CultureInfo.CurrentUICulture);
    private IReadOnlyList<string> _arguments = Array.Empty<string>();

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _arguments = e.Args;
        _settings = _settingsStore.Load();
        _strings = ResolveStrings(e.Args, _settings.Language);
        _alerts.UpdateThresholds(_settings.Thresholds);
        _history = _historyStore.Load();
        _popup = new QuotaPopup(this, _strings, _history);

        _currentIcon = TrayIconRenderer.Render(
            TrayIconModelBuilder.Build(Array.Empty<QuotaSnapshot>(), _settings));
        _trayIcon = new NotifyIcon
        {
            Icon = _currentIcon.Icon,
            Visible = true,
            Text = "Lim'it",
        };

        // Click fires for either button, so the right button used to open the panel and
        // the context menu at the same time. Only the left button toggles.
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) TogglePopup();
        };

        _trayIcon.ContextMenuStrip = BuildMenu();

        _store.Changed += OnSnapshot;

        // Seeding happens after the tray icon exists, because applying a snapshot
        // immediately raises Changed and redraws it.
        SeedFromHistory();

        _housekeepingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = HousekeepingInterval,
        };
        _housekeepingTimer.Tick += (_, _) =>
        {
            _store.RefreshStaleness();
            _historyStore.Save(_history);
        };
        _housekeepingTimer.Start();

        _transport = new SystemHttpTransport();
        StartCollector(BuildClaudeCollector(_transport));
        StartCollector(BuildCodexCollector());

        if (Array.Exists(e.Args, argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(TogglePopup));

    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        if (StartupRegistration.IsSupported)
        {
            _startupItem = new ToolStripMenuItem(_strings.StartWithWindows)
            {
                Checked = StartupRegistration.IsEnabled(),
            };
            _startupItem.Click += (_, _) => ToggleStartup();
            menu.Items.Add(_startupItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add(_strings.Exit, null, (_, _) => Shutdown());
        return menu;
    }

    private static Strings ResolveStrings(IReadOnlyList<string> args, LanguageMode mode)
    {
        // The command line still wins, as it did in v0.2; the setting is the default under it.
        var fromArgs = LanguageArguments.Resolve(args, CultureInfo.CurrentUICulture);
        var explicitArg = args.Any(a => a.StartsWith("--lang", StringComparison.OrdinalIgnoreCase));
        if (explicitArg) return fromArgs;
        return mode switch
        {
            LanguageMode.Turkish => Strings.Turkish,
            LanguageMode.English => Strings.English,
            _ => Strings.ForCulture(CultureInfo.CurrentUICulture),
        };
    }

    private void ToggleStartup()
    {
        if (_startupItem is null) return;

        StartupRegistration.SetEnabled(!_startupItem.Checked, _arguments);

        // The state is read back rather than assumed: the key can be denied by policy,
        // and a tick that lies is worse than one that refuses to move.
        _startupItem.Checked = StartupRegistration.IsEnabled();
    }

    /// <summary>
    /// Puts the last known values on screen before the first request returns. They are
    /// stale by construction and carry the age they actually have, so a cold start
    /// during an outage shows real numbers instead of an empty panel.
    /// </summary>
    private void SeedFromHistory()
    {
        foreach (var provider in _history.Providers())
        {
            var snapshot = _history.LastKnown(provider);
            if (snapshot is not null) _store.Apply(snapshot);
        }
    }

    private void OnSnapshot(QuotaSnapshot snapshot)
    {
        _history.Observe(snapshot);

        var alerts = _alerts.Inspect(snapshot);
        Dispatcher.Invoke(() =>
        {
            UpdateTray();
            foreach (var alert in alerts) Notify(alert);
        });
    }

    private void Notify(QuotaAlert alert)
    {
        if (!_settings.Notifications) return;

        _trayIcon?.ShowBalloonTip(
            10_000,
            _strings.WarningNotificationTitle,
            QuotaAlerts.Body(alert, _strings),
            ToolTipIcon.Warning);
    }

    private IQuotaCollector BuildClaudeCollector(IHttpTransport transport) =>
        new ClaudeCollector(
            transport,
            ClaudeCredentialReader.FromDefaultPath(),
            () => DateTimeOffset.Now,
            Task.Delay,
            () => TimeSpan.FromSeconds(_settings.RefreshSeconds));

    private static IQuotaCollector BuildCodexCollector()
    {
        var binary = CodexBinaryLocator.LocateDefault();

        return new CodexCollector(
            () => binary is null
                ? throw new InvalidOperationException("codex.exe bulunamadi")
                : new StdioJsonRpcProcess(binary, "app-server"),
            () => DateTimeOffset.Now,
            Task.Delay,
            () => CodexRolloutReader.ReadLatest(
                CodexRolloutReader.DefaultSessionsRoot, DateTimeOffset.Now));
    }

    private void StartCollector(IQuotaCollector collector)
    {
        _collectors.Add(collector);
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var snapshot in collector.Watch(_cts.Token))
                    _store.Apply(snapshot);
            }
            catch (OperationCanceledException) { }
        });
    }

    private void TogglePopup()
    {
        if (_popup is null) return;
        if (_popup.IsVisible) _popup.Hide();
        else _popup.Show(_store.All(), DateTimeOffset.Now);
    }

    private void UpdateTray()
    {
        if (_trayIcon is null) return;

        var snapshots = _store.All();

        var replacement = TrayIconRenderer.Render(TrayIconModelBuilder.Build(snapshots, _settings));

        var previous = _currentIcon;
        _currentIcon = replacement;
        _trayIcon.Icon = replacement.Icon;
        previous?.Dispose();

        var tooltip = QuotaFormatter.Tooltip(snapshots, DateTimeOffset.Now, _strings);
        _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (_popup is { IsVisible: true })
            _popup.Show(snapshots, DateTimeOffset.Now);
    }

    public AppSettings Settings => _settings;

    public SettingsStore SettingsStore => _settingsStore;

    public event Action<AppSettings>? SettingsChanged;

    public void ApplySettings(AppSettings next)
    {
        var previous = _settings;
        _settings = next.Normalised();
        _settingsStore.Save(_settings);

        if (previous.Thresholds != _settings.Thresholds) _alerts.UpdateThresholds(_settings.Thresholds);
        if (previous.Language != _settings.Language)
        {
            _strings = ResolveStrings(_arguments, _settings.Language);
            _trayIcon!.ContextMenuStrip = BuildMenu();
        }
        // The interval delegate reads _settings on the next tick; a shorter interval takes
        // effect immediately by cutting the current wait short.
        if (previous.RefreshSeconds > _settings.RefreshSeconds)
            foreach (var collector in _collectors) collector.RequestRefresh();

        UpdateTray();
        SettingsChanged?.Invoke(_settings);
    }

    /// <summary>Manual refresh, rate limited to one per five seconds: the endpoint is shared.</summary>
    public void RefreshNow()
    {
        var now = DateTimeOffset.Now;
        if (now - _lastManualRefresh < TimeSpan.FromSeconds(5)) return;
        _lastManualRefresh = now;
        foreach (var collector in _collectors) collector.RequestRefresh();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _housekeepingTimer?.Stop();
        _historyStore.Save(_history);
        _settingsStore.Save(_settings);
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        _currentIcon?.Dispose();
        _transport?.Dispose();
        base.OnExit(e);
    }
}
