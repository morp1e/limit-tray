using LimitTray.Core.Settings;
using Xunit;

namespace LimitTray.Tests.Settings;

public class SettingsStoreTests
{
    [Fact]
    public void Load_MissingFileIsDefault()
    {
        var store = new SettingsStore(() => null, _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Load_ReadThrowingIsDefault()
    {
        var store = new SettingsStore(() => throw new IOException("locked"), _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Load_CorruptFileIsDefault()
    {
        var store = new SettingsStore(() => "{{{", _ => { });
        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Save_WritesOnlyWhenChanged()
    {
        var writes = new List<string>();
        var store = new SettingsStore(() => null, writes.Add);

        store.Save(AppSettings.Default);
        store.Save(AppSettings.Default);
        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });

        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public void Save_WriteFailureIsFlaggedNotThrown()
    {
        var store = new SettingsStore(() => null, _ => throw new UnauthorizedAccessException());

        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });

        Assert.True(store.LastSaveFailed);
    }

    [Fact]
    public void Save_SuccessClearsTheFlag()
    {
        var fail = true;
        var store = new SettingsStore(() => null, _ => { if (fail) throw new IOException(); });

        store.Save(AppSettings.Default with { Theme = ThemeMode.Dark });
        fail = false;
        store.Save(AppSettings.Default with { Theme = ThemeMode.Light });

        Assert.False(store.LastSaveFailed);
    }
}
