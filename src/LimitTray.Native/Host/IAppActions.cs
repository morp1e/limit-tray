using LimitTray.Core.History;
using LimitTray.Core.Model;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;

namespace LimitTray.Native.Host;

/// <summary>
/// Everything the popup may read from or ask of the host. The popup owns no state that
/// outlives it: it is created on open and destroyed on close, so it reads these each time.
/// All members are called on the UI thread.
/// </summary>
internal interface IAppActions
{
    AppSettings Settings { get; }
    Strings Strings { get; }
    UsageHistory History { get; }
    IReadOnlyList<QuotaSnapshot> Snapshots { get; }

    /// <summary>Resolved theme: the setting, or the Windows "apps use light theme" value when it is System.</summary>
    bool IsDarkTheme { get; }

    /// <summary>True when the last settings.json write failed (the settings page shows one line).</summary>
    bool SettingsSaveFailed { get; }

    /// <summary>Read back from the registry each time; the tray menu can change it too.</summary>
    bool StartupEnabled { get; }

    void ApplySettings(AppSettings next);

    /// <summary>Card expansion is a persisted view state; it changes no colour, icon or text.</summary>
    void RememberExpanded(IReadOnlySet<string> expanded);

    /// <summary>Manual refresh, limited to one per five seconds (the endpoint is shared).</summary>
    void RefreshNow();

    void SetStartup(bool enabled);

    /// <summary>False when neither Windows Terminal nor PowerShell could be started.</summary>
    bool OpenTerminal(string provider);

    void OpenRepository();
}
