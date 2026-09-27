using LimitTray.Native.Host;

namespace LimitTray.Native.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void MutexName_IsStableAndCaseInsensitive()
    {
        var a = SingleInstance.MutexName(@"C:\Users\x\AppData\Local\limit-tray");
        var b = SingleInstance.MutexName(@"c:\users\X\appdata\local\LIMIT-TRAY");
        Assert.Equal(a, b);
        Assert.StartsWith("LimitTray-", a);
        Assert.Equal("LimitTray-".Length + 16, a.Length);
    }

    [Fact]
    public void MutexName_DiffersPerDataDirectory() =>
        Assert.NotEqual(
            SingleInstance.MutexName(@"C:\a"),
            SingleInstance.MutexName(@"C:\b"));
}
