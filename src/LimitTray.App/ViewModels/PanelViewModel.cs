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
    private Strings _strings;
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
    public string RefreshGlyph => Glyphs.Refresh;
    public string SettingsGlyph => Glyphs.Settings;
    public string RefreshText => _strings.Refresh;
    public string SettingsText => _strings.Settings;
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SettingsCommand { get; }

    public void UpdateStrings(Strings strings)
    {
        _strings = strings;
        RaisePropertyChanged(nameof(FooterText));
        RaisePropertyChanged(nameof(RefreshText));
        RaisePropertyChanged(nameof(SettingsText));
    }

    /// <summary>
    /// Refreshes existing cards in place and only adds or removes when the set of
    /// providers changes, so a reading never rebuilds the visual tree.
    /// </summary>
    public void Update(IReadOnlyList<QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var existing = Cards.FirstOrDefault(card => card.Provider == snapshot.Provider);
            if (existing is null)
            {
                Cards.Insert(Math.Min(index, Cards.Count), new ProviderCardViewModel(
                    snapshot, _history, _app.Settings, _strings, now, _app));
                continue;
            }

            existing.Refresh(snapshot, _app.Settings, _strings, now);
            var currentIndex = Cards.IndexOf(existing);
            if (currentIndex != index && index < Cards.Count) Cards.Move(currentIndex, index);
        }

        for (var index = Cards.Count - 1; index >= 0; index--)
        {
            if (snapshots.All(snapshot => snapshot.Provider != Cards[index].Provider))
                Cards.RemoveAt(index);
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
