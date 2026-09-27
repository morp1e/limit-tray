using LimitTray.Core.Fixtures;
using LimitTray.Core.History;
using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using LimitTray.Native.Graphics;
using LimitTray.Native.Popup;
using LimitTray.Native.Ui;

namespace LimitTray.Native.Measure;

/// <summary>
/// <c>LimitTray.exe --render out.png --fixture f.json [--expand claude,codex] [--theme light]
/// [--scale 1.5] [--lang tr|en] [--hover claude] [--now-ms 0]</c>
/// draws the popup exactly as the running app would, into a PNG, then exits. It is how
/// the native popup is compared with the v0.3 screenshots and how visual changes are
/// checked without clicking the tray or calling the real providers.
/// </summary>
internal static class RenderCommand
{
    public static bool TryRun(IReadOnlyList<string> args, out int exitCode)
    {
        exitCode = 0;
        var output = Value(args, "--render");
        if (output is null) return false;

        var fixture = Value(args, "--fixture");
        if (fixture is null)
        {
            exitCode = 2;
            return true;
        }

        var now = DateTimeOffset.Now;
        var data = FixtureFile.Read(File.ReadAllText(fixture), now);
        var history = new UsageHistory();
        foreach (var series in data.History) history.Import(series);

        // The store keeps retained numbers under a failure; replay the fixture through it.
        var store = new Core.Store.QuotaStore(() => now);
        foreach (var snapshot in data.Snapshots) store.Apply(snapshot);

        var expanded = (Value(args, "--expand") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var settings = AppSettings.Default with { ExpandedProviders = expanded };
        var strings = Value(args, "--lang") == "en" ? Strings.English : Strings.Turkish;
        var scale = float.TryParse(Value(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1f;
        var light = Value(args, "--theme") == "light";

        var page = new PanelPage
        {
            Palette = light ? Ui.Palette.LightTheme : Ui.Palette.DarkTheme,
            Strings = strings,
            AnimationsEnabled = false,
            Hovered = Value(args, "--hover"),
            HoveredKind = Value(args, "--hover") is null ? HitKind.None : HitKind.Card,
        };
        page.Update(store.All(), history, settings, now, 0);

        using var renderer = new PopupRenderer(scale);
        var surface = renderer.Render(page, 0);
        // A dark desktop behind the dark theme, a light one behind the light theme.
        PngWriter.Write(output, surface, light ? new Colour(0xE8, 0xE8, 0xE8) : new Colour(0x20, 0x20, 0x20));
        return true;
    }

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
