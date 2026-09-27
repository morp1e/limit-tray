namespace LimitTray.Native.Popup;

/// <summary>What the host tells the popup. Called on the UI thread.</summary>
internal interface IPopupController
{
    bool IsOpen { get; }

    /// <summary>Opens on the panel page, or closes when already open (tray left click).</summary>
    void Toggle();

    /// <summary>Opens directly on the settings page (tray menu "Settings").</summary>
    void OpenSettings();

    void Close();

    /// <summary>
    /// Snapshots, settings, strings or theme changed. A closed popup ignores it: it reads
    /// everything fresh when it next opens.
    /// </summary>
    void OnDataChanged();
}
