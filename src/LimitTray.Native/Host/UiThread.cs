using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LimitTray.Native.Host;

internal static class UiThread
{
    private const uint WorkMessage = PInvoke.WM_APP + 2;
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static int _threadId;
    private static HWND _window;

    public static void Initialize(MessageWindow window)
    {
        _window = window.Handle;
        _threadId = Environment.CurrentManagedThreadId;
        window.Register(WorkMessage, (_, _) =>
        {
            while (Queue.TryDequeue(out var action)) action();
            return new LRESULT(0);
        });
    }

    /// <summary>
    /// Queues work for the UI thread. Called from collector threads, so it never throws:
    /// a failed post (the window is gone during shutdown) would otherwise end the process
    /// from a background thread. Work already queued runs with the next successful post.
    /// </summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Queue.Enqueue(action);
        PInvoke.PostMessage(_window, WorkMessage, default, default);
    }

    public static bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;
}
