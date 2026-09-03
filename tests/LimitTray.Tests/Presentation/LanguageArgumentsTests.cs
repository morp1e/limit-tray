using System.Globalization;
using LimitTray.Core.Presentation;
using Xunit;

namespace LimitTray.Tests.Presentation;

public class LanguageArgumentsTests
{
    [Fact]
    public void SeparateEnglishArgument_SelectsEnglish() =>
        Assert.Same(Strings.English,
            LanguageArguments.Resolve(["--lang", "en"], CultureInfo.GetCultureInfo("tr-TR")));

    [Fact]
    public void EqualsTurkishArgument_SelectsTurkish() =>
        Assert.Same(Strings.Turkish,
            LanguageArguments.Resolve(["--lang=tr"], CultureInfo.GetCultureInfo("en-US")));

    [Fact]
    public void UnknownLanguage_FallsBackToCulture() =>
        Assert.Same(Strings.Turkish,
            LanguageArguments.Resolve(["--lang", "xx"], CultureInfo.GetCultureInfo("tr-TR")));

    [Fact]
    public void NoArguments_FallsBackToCulture() =>
        Assert.Same(Strings.English,
            LanguageArguments.Resolve([], CultureInfo.GetCultureInfo("en-US")));
}
