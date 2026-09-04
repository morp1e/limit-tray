using System;
using System.Collections.Generic;
using System.Globalization;
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
        _strings = LanguageArguments.Resolve(e.Args, CultureInfo.CurrentUICulture);
        _history = _historyStore.Load();
        _popup = new QuotaPopup(_strings, _history);

        _currentIcon = TrayIconRenderer.Render(null, hasUnhealthy: false);
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

    private void Notify(QuotaAlert alert) =>
        _trayIcon?.ShowBalloonTip(
            10_000,
            _strings.WarningNotificationTitle,
            QuotaAlerts.Body(alert, _strings),
            ToolTipIcon.Warning);

    private static IQuotaCollector BuildClaudeCollector(IHttpTransport transport) =>
        new ClaudeCollector(
            transport,
            ClaudeCredentialReader.FromDefaultPath(),
            () => DateTimeOffset.Now,
            Task.Delay);

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

    private void StartCollector(IQuotaCollector collector) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var snapshot in collector.Watch(_cts.Token))
                    _store.Apply(snapshot);
            }
            catch (OperationCanceledException) { }
        });

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

        var replacement = TrayIconRenderer.Render(
            QuotaFormatter.HighestPercent(snapshots),
            QuotaFormatter.HasUnhealthy(snapshots));

        var previous = _currentIcon;
        _currentIcon = replacement;
        _trayIcon.Icon = replacement.Icon;
        previous?.Dispose();

        var tooltip = QuotaFormatter.Tooltip(snapshots, DateTimeOffset.Now, _strings);
        _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (_popup is { IsVisible: true })
            _popup.Show(snapshots, DateTimeOffset.Now);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _housekeepingTimer?.Stop();
        _historyStore.Save(_history);
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        _currentIcon?.Dispose();
        _transport?.Dispose();
        base.OnExit(e);
    }
}
