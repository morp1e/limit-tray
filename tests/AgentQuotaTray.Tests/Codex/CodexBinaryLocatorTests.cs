using AgentQuotaTray.Core.Codex;
using Xunit;

namespace AgentQuotaTray.Tests.Codex;

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
}
