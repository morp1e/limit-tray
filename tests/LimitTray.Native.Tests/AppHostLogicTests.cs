using LimitTray.Native.Host;
using LimitTray.Native.Platform;

namespace LimitTray.Native.Tests;

public class AppHostLogicTests
{
    [Fact]
    public void CodexRefreshAge_IncludesMissingAndOlderThanTwoMinutes()
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.True(AppHost.ShouldRefreshCodex(null, now));
        Assert.True(AppHost.ShouldRefreshCodex(now - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1), now));
        Assert.False(AppHost.ShouldRefreshCodex(now - TimeSpan.FromMinutes(2), now));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\LimitTray.exe\" --lang=en", "c:\\apps\\limittray.exe", true)]
    [InlineData("\"C:\\Old\\LimitTray.exe\"", "C:\\Apps\\LimitTray.exe", false)]
    public void StartupCommand_ComparisonUsesExecutablePath(string command, string current, bool expected) =>
        Assert.Equal(expected, StartupRegistration.MatchesExecutable(command, current));
}
