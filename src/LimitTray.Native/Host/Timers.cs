using Windows.Win32;
using Windows.Win32.Foundation;

namespace LimitTray.Native.Host;

internal sealed unsafe class Timers(MessageWindow window)
{
    public const nuint Staleness = 1;
    public const nuint SaveHistory = 2;
    public const uint StalenessMilliseconds = 60_000;
    public const uint HistoryDebounceMilliseconds = 10_000;

    public void StartStaleness() => PInvoke.SetTimer(window.Handle, Staleness, StalenessMilliseconds, null);
    public void DebounceHistorySave() => PInvoke.SetTimer(window.Handle, SaveHistory, HistoryDebounceMilliseconds, null);
    public void StopHistorySave() => PInvoke.KillTimer(window.Handle, SaveHistory);
    public void StopAll()
    {
        PInvoke.KillTimer(window.Handle, Staleness);
        PInvoke.KillTimer(window.Handle, SaveHistory);
    }
}
