using LimitTray.Native.Host;

namespace LimitTray.Native.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_FixtureDataDirAndShow()
    {
        var cl = CommandLine.Parse(new[] { "--fixture", "f.json", "--data-dir", @"C:\t", "--show", "--lang=en" }, @"C:\d");
        Assert.Equal("f.json", cl.FixturePath);
        Assert.Equal(@"C:\t", cl.DataDirectory);
        Assert.True(cl.Show);
        Assert.Contains("--lang=en", cl.Raw);
    }

    [Fact]
    public void Parse_Defaults() =>
        Assert.Equal(Path.GetFullPath(@"C:\d"), CommandLine.Parse(Array.Empty<string>(), @"C:\d").DataDirectory);
}
