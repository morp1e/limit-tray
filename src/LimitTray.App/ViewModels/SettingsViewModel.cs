using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.App.ViewModels;

public sealed record Choice<T>(T Value, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly App _app;
    private Strings _strings;
    private readonly Action _showPanel;

    public SettingsViewModel(App app, Strings strings, Action showPanel)
    {
        _app = app;
        _strings = strings;
        _showPanel = showPanel;
        ThemeOptions = BuildThemeOptions();
        LanguageOptions = BuildLanguageOptions();
        RefreshOptions = BuildRefreshOptions();
        CautionOptions = BuildCautionOptions();
        WarningOptions = BuildWarningOptions(_app.Settings.Thresholds.Caution);
        TrayStyleOptions = BuildTrayStyleOptions();
        TraySourceOptions = BuildTraySourceOptions();
        BackCommand = new RelayCommand(_showPanel);
        OpenGitHubCommand = new RelayCommand(OpenGitHub);
        _app.SettingsChanged += OnSettingsChanged;
        // The tray menu can flip the same registry key; both surfaces read it back.
        _app.StartupChanged += () => RaisePropertyChanged(nameof(StartWithWindows));
    }

    public IReadOnlyList<Choice<ThemeMode>> ThemeOptions { get; private set; }
    public IReadOnlyList<Choice<LanguageMode>> LanguageOptions { get; private set; }
    public IReadOnlyList<Choice<int>> RefreshOptions { get; private set; }
    public IReadOnlyList<Choice<double>> CautionOptions { get; private set; }
    public IReadOnlyList<Choice<double>> WarningOptions { get; private set; }
    public IReadOnlyList<Choice<TrayIconStyle>> TrayStyleOptions { get; private set; }
    public IReadOnlyList<Choice<TrayIconSource>> TraySourceOptions { get; private set; }

    public ThemeMode Theme
    {
        get => _app.Settings.Theme;
        set => Apply(_app.Settings with { Theme = value });
    }

    public LanguageMode Language
    {
        get => _app.Settings.Language;
        set => Apply(_app.Settings with { Language = value });
    }

    public int RefreshSeconds
    {
        get => _app.Settings.RefreshSeconds;
        set => Apply(_app.Settings with { RefreshSeconds = value });
    }

    public double CautionThreshold
    {
        get => _app.Settings.Thresholds.Caution;
        set
        {
            var warning = _app.Settings.Thresholds.Warning;
            if (warning < value + 5) warning = value + 5;
            Apply(_app.Settings with { Thresholds = new QuotaThresholds(value, warning) });
        }
    }

    public double WarningThreshold
    {
        get => _app.Settings.Thresholds.Warning;
        set => Apply(_app.Settings with
        {
            Thresholds = new QuotaThresholds(_app.Settings.Thresholds.Caution, value),
        });
    }

    public bool Notifications
    {
        get => _app.Settings.Notifications;
        set => Apply(_app.Settings with { Notifications = value });
    }

    public TrayIconStyle TrayStyle
    {
        get => _app.Settings.TrayStyle;
        set => Apply(_app.Settings with { TrayStyle = value });
    }

    public TrayIconSource TraySource
    {
        get => _app.Settings.TraySource;
        set => Apply(_app.Settings with { TraySource = value });
    }

    public bool StartWithWindows
    {
        get => StartupRegistration.IsEnabled();
        set
        {
            StartupRegistration.SetEnabled(value, _app.StartupArguments);
            RaisePropertyChanged();
            _app.NotifyStartupChanged();
        }
    }


    public string SettingsText => _strings.Settings;
    public string BackText => "\u2039 " + _strings.BackToPanel;
    public string BackGlyph => Glyphs.Back;
    public string AppearanceGroupText => _strings.GroupAppearance;
    public string DataGroupText => _strings.GroupData;
    public string TrayGroupText => _strings.GroupTray;
    public string SystemGroupText => _strings.GroupSystem;
    public string ThemeText => _strings.Theme;
    public string LanguageText => _strings.Language;
    public string RefreshIntervalText => _strings.RefreshInterval;
    public string CautionThresholdText => _strings.CautionThreshold;
    public string WarningThresholdText => _strings.WarningThreshold;
    public string NotificationsText => _strings.Notifications;
    public string TrayStyleText => _strings.TrayStyle;
    public string TraySourceText => _strings.TraySource;
    public string StartWithWindowsText => _strings.StartWithWindows;
    public string VersionText => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty);
    public string GitHubUrl => "https://github.com/morp1e/limit-tray";
    public string? SaveFailedText => _app.SettingsStore.LastSaveFailed ? _strings.SettingsSaveFailed : null;
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }

    public void UpdateStrings(Strings strings)
    {
        _strings = strings;
        RebuildOptions();
        RaiseTextProperties();
    }

    private void Apply(AppSettings settings)
    {
        if (settings == _app.Settings) return;
        _app.ApplySettings(settings);
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        RebuildWarningOptions();
        RaisePropertyChanged(nameof(Theme));
        RaisePropertyChanged(nameof(Language));
        RaisePropertyChanged(nameof(RefreshSeconds));
        RaisePropertyChanged(nameof(CautionThreshold));
        RaisePropertyChanged(nameof(WarningThreshold));
        RaisePropertyChanged(nameof(Notifications));
        RaisePropertyChanged(nameof(TrayStyle));
        RaisePropertyChanged(nameof(TraySource));
        RaisePropertyChanged(nameof(StartWithWindows));
        RaisePropertyChanged(nameof(SaveFailedText));
    }

    private void RebuildOptions()
    {
        ThemeOptions = BuildThemeOptions();
        LanguageOptions = BuildLanguageOptions();
        RefreshOptions = BuildRefreshOptions();
        CautionOptions = BuildCautionOptions();
        RebuildWarningOptions();
        TrayStyleOptions = BuildTrayStyleOptions();
        TraySourceOptions = BuildTraySourceOptions();
        RaisePropertyChanged(nameof(ThemeOptions));
        RaisePropertyChanged(nameof(LanguageOptions));
        RaisePropertyChanged(nameof(RefreshOptions));
        RaisePropertyChanged(nameof(CautionOptions));
        RaisePropertyChanged(nameof(TrayStyleOptions));
        RaisePropertyChanged(nameof(TraySourceOptions));
    }

    private void RebuildWarningOptions()
    {
        WarningOptions = BuildWarningOptions(_app.Settings.Thresholds.Caution);
        RaisePropertyChanged(nameof(WarningOptions));
    }

    private void RaiseTextProperties()
    {
        RaisePropertyChanged(nameof(SettingsText));
        RaisePropertyChanged(nameof(BackText));
        RaisePropertyChanged(nameof(AppearanceGroupText));
        RaisePropertyChanged(nameof(DataGroupText));
        RaisePropertyChanged(nameof(TrayGroupText));
        RaisePropertyChanged(nameof(SystemGroupText));
        RaisePropertyChanged(nameof(ThemeText));
        RaisePropertyChanged(nameof(LanguageText));
        RaisePropertyChanged(nameof(RefreshIntervalText));
        RaisePropertyChanged(nameof(CautionThresholdText));
        RaisePropertyChanged(nameof(WarningThresholdText));
        RaisePropertyChanged(nameof(NotificationsText));
        RaisePropertyChanged(nameof(TrayStyleText));
        RaisePropertyChanged(nameof(TraySourceText));
        RaisePropertyChanged(nameof(StartWithWindowsText));
        RaisePropertyChanged(nameof(SaveFailedText));
    }

    private IReadOnlyList<Choice<ThemeMode>> BuildThemeOptions() => new[]
    {
        new Choice<ThemeMode>(ThemeMode.System, _strings.ThemeSystem),
        new Choice<ThemeMode>(ThemeMode.Dark, _strings.ThemeDark),
        new Choice<ThemeMode>(ThemeMode.Light, _strings.ThemeLight),
    };

    private IReadOnlyList<Choice<LanguageMode>> BuildLanguageOptions() => new[]
    {
        new Choice<LanguageMode>(LanguageMode.System, _strings.LanguageSystem),
        new Choice<LanguageMode>(LanguageMode.Turkish, "TR"),
        new Choice<LanguageMode>(LanguageMode.English, "EN"),
    };

    private IReadOnlyList<Choice<int>> BuildRefreshOptions() => AppSettings.AllowedRefreshSeconds
        .Select(seconds => new Choice<int>(seconds, RefreshLabel(seconds)))
        .ToArray();

    private IReadOnlyList<Choice<double>> BuildCautionOptions() => Enumerable.Range(0, 7)
        .Select(index => (double)(50 + index * 5))
        .Select(value => new Choice<double>(value, value.ToString("0", CultureInfo.InvariantCulture) + "%"))
        .ToArray();

    private IReadOnlyList<Choice<double>> BuildWarningOptions(double caution) =>
        Enumerable.Range(0, (int)((95 - caution) / 5))
            .Select(index => caution + 5 + index * 5)
            .Select(value => new Choice<double>(value, value.ToString("0", CultureInfo.InvariantCulture) + "%"))
            .ToArray();

    private IReadOnlyList<Choice<TrayIconStyle>> BuildTrayStyleOptions() => new[]
    {
        new Choice<TrayIconStyle>(TrayIconStyle.Ring, _strings.TrayStyleRing),
        new Choice<TrayIconStyle>(TrayIconStyle.Number, _strings.TrayStyleNumber),
        new Choice<TrayIconStyle>(TrayIconStyle.DualBar, _strings.TrayStyleDualBar),
    };

    private IReadOnlyList<Choice<TrayIconSource>> BuildTraySourceOptions() => new[]
    {
        new Choice<TrayIconSource>(TrayIconSource.Highest, _strings.TraySourceHighest),
        new Choice<TrayIconSource>(TrayIconSource.ClaudeSession, _strings.TraySourceClaudeSession),
        new Choice<TrayIconSource>(TrayIconSource.ClaudeWeekly, _strings.TraySourceClaudeWeekly),
        new Choice<TrayIconSource>(TrayIconSource.CodexSession, _strings.TraySourceCodexSession),
        new Choice<TrayIconSource>(TrayIconSource.CodexWeekly, _strings.TraySourceCodexWeekly),
    };

    private string RefreshLabel(int seconds) => seconds % 60 == 0
        ? string.Format(CultureInfo.InvariantCulture, _strings.Minutes, seconds / 60)
        : string.Format(CultureInfo.InvariantCulture, _strings.Seconds, seconds);

    private static void OpenGitHub()
    {
        // A missing URL handler throws Win32Exception; on the UI thread that would take
        // the tray process down over a link.
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/morp1e/limit-tray",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to show: the address is printed next to the link.
        }
    }
}
