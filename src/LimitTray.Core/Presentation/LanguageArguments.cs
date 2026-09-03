using System.Globalization;

namespace LimitTray.Core.Presentation;

public static class LanguageArguments
{
    public static Strings Resolve(IReadOnlyList<string> args, CultureInfo culture)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];

            if (argument.Equals("--lang=en", StringComparison.OrdinalIgnoreCase))
                return Strings.English;

            if (argument.Equals("--lang=tr", StringComparison.OrdinalIgnoreCase))
                return Strings.Turkish;

            if (!argument.Equals("--lang", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Count)
                break;

            var value = args[index + 1];
            if (value.Equals("en", StringComparison.OrdinalIgnoreCase))
                return Strings.English;
            if (value.Equals("tr", StringComparison.OrdinalIgnoreCase))
                return Strings.Turkish;

            break;
        }

        return Strings.ForCulture(culture);
    }
}
