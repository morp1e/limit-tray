using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using LimitTray.Core.Claude;
using LimitTray.Core.Codex;
using LimitTray.Core.Collectors;
using LimitTray.Core.Http;
using LimitTray.Core.Presentation;
using LimitTray.Core.Process;
using LimitTray.Core.Store;

namespace LimitTray.App;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _cts = new();
    private readonly QuotaStore _store = new(() => DateTimeOffset.Now);
    private NotifyIcon? _trayIcon;
    private QuotaPopup? _popup;
    private SystemHttpTransport? _transport;
    private System.Windows.Threading.DispatcherTimer? _stalenessTimer;
    private readonly Strings _strings = Strings.ForCulture(CultureInfo.CurrentUICulture);

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _popup = new QuotaPopup(_strings);

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(null, hasUnhealthy: false),
            Visible = true,
            Text = "Lim'it",
        };
        _trayIcon.Click += (_, _) => TogglePopup();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_strings.Exit, null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;

        _store.Changed += _ => Dispatcher.Invoke(UpdateTray);

        _stalenessTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _stalenessTimer.Tick += (_, _) => _store.RefreshStaleness();
        _stalenessTimer.Start();

        _transport = new SystemHttpTransport();
        StartCollector(BuildClaudeCollector(_transport));
        StartCollector(BuildCodexCollector());
    }

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
        var old = _trayIcon.Icon;
        _trayIcon.Icon = TrayIconRenderer.Render(
            QuotaFormatter.HighestPercent(snapshots),
            QuotaFormatter.HasUnhealthy(snapshots));
        old?.Dispose();

        var tooltip = QuotaFormatter.Tooltip(snapshots, DateTimeOffset.Now, _strings);
        _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (_popup is { IsVisible: true })
            _popup.Show(snapshots, DateTimeOffset.Now);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _cts.Cancel();
        _stalenessTimer?.Stop();
        if (_trayIcon is not null) _trayIcon.Visible = false;
        _trayIcon?.Dispose();
        _transport?.Dispose();
        base.OnExit(e);
    }
}
