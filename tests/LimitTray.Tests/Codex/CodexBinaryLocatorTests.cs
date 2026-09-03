using LimitTray.Core.Codex;
using LimitTray.Core.Process;
using Xunit;

namespace LimitTray.Tests.Codex;

public class CodexBinaryLocatorTests
{
    [Fact]
    public void Locate_ReturnsFirstExistingCandidate()
    {
        var candidates = new[] { @"C:\yok\codex.exe", @"C:\var\codex.exe" };

        var found = CodexBinaryLocator.Locate(
            candidates, p => p == @"C:\var\codex.exe");

        Assert.Equal(@"C:\var\codex.exe", found);
    }

    [Fact]
    public void Locate_NoneExist_ReturnsNull() =>
        Assert.Null(CodexBinaryLocator.Locate(new[] { @"C:\yok\codex.exe" }, _ => false));

    [Fact]
    public void DefaultCandidates_IncludeNpmVendorPath()
    {
        var candidates = CodexBinaryLocator.DefaultCandidates().ToList();

        Assert.Contains(candidates, c =>
            c.Contains("codex-win32-x64", StringComparison.OrdinalIgnoreCase) &&
            c.EndsWith("codex.exe", StringComparison.OrdinalIgnoreCase));
    }

    // Regresyon: BOM'lu bir encoding app-server'i tamamen susturur. Bu testi
    // kiran bir degisiklik, birim testleri yesil birakip uygulamayi sessizce
    // olduren turdendir — olculdu 2026-09-03.
    [Fact]
    public void StdioProcessEncoding_EmitsNoByteOrderMark() =>
        Assert.Empty(StdioJsonRpcProcess.Utf8NoBom.GetPreamble());
}
