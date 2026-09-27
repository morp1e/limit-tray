using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LimitTray.Native.Host;

namespace LimitTray.Native;

internal static unsafe class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Measure.RenderCommand.TryRun(args, out var renderExit)) return renderExit;

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "limit-tray");
        var commandLine = CommandLine.Parse(args, dataDirectory);
        using var instance = SingleInstance.TryAcquire(commandLine.DataDirectory);
        if (instance is null) return 0;

        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var loadedIcon = PInvoke.LoadImage(
            (HINSTANCE)(nint)module.Value,
            new PCWSTR((char*)32512),
            GDI_IMAGE_TYPE.IMAGE_ICON,
            0,
            0,
            IMAGE_FLAGS.LR_DEFAULTSIZE | IMAGE_FLAGS.LR_SHARED);
        var icon = (HICON)(nint)loadedIcon.Value;
        if (icon == HICON.Null)
            return 1;

        using var host = new AppHost(commandLine, icon);
        return host.Run();
    }
}
