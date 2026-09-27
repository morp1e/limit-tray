using LimitTray.Core.Presentation;
using LimitTray.Core.Settings;
using LimitTray.Native.Host;

namespace LimitTray.Native.Tests;

public class TrayUpdaterTests
{
    private static TrayIconModel Model(double p) =>
        new(TrayIconStyle.Ring, new TrayBar(p, new Rgb(1, 2, 3), 255), null, null, false);

    [Fact]
    public void SameModel_DoesNotRedraw_ChangedModelOrSizeDoes()
    {
        var updater = new TrayUpdater();
        Assert.True(updater.ShouldRedraw(Model(10), 16, true));
        Assert.False(updater.ShouldRedraw(Model(10), 16, true));
        Assert.True(updater.ShouldRedraw(Model(11), 16, true));
        Assert.True(updater.ShouldRedraw(Model(11), 24, true));
        Assert.True(updater.ShouldRedraw(Model(11), 24, false));
    }
}
