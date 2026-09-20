using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;

namespace LimitTray.App.ViewModels;

public sealed class PanelViewModel : ObservableObject
{
    private readonly App _app;
    private readonly UsageHistory _history;
    private readonly Strings _strings;
    private readonly Action _showSettings;
    private bool _isRefreshing;
    private string _footerText;

    public PanelViewModel(App app, UsageHistory history, Strings strings, Action showSettings)
    {
        _app = app;
        _history = history;
        _strings = strings;
        _showSettings = showSettings;
        _footerText = strings.NoData;
        VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;
        RefreshCommand = new RelayCommand(RefreshNow);
        SettingsCommand = new RelayCommand(_showSettings);
    }

    public ObservableCollection<ProviderCardViewModel> Cards { get; } = new();
    public string FooterText { get => _footerText; private set => Set(ref _footerText, value); }
    public string VersionText { get; }
    public string RefreshGlyph => "↻";
    public string SettingsGlyph => "⚙";
    public string RefreshText => _strings.Refresh;
    public string SettingsText => _strings.Settings;
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SettingsCommand { get; }

    public void Update(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        Cards.Clear();
        foreach (var snapshot in snapshots)
        {
            Cards.Add(new ProviderCardViewModel(
                snapshot, _history, _app.Settings, _strings, now, _app));
        }

        FooterText = snapshots.Count == 0
            ? _strings.NoData
            : QuotaFormatter.Age(snapshots.Max(snapshot => snapshot.FetchedAt), now, _strings);
    }

    private void RefreshNow()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        _app.RefreshNow();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            IsRefreshing = false;
        };
        timer.Start();
    }
}
