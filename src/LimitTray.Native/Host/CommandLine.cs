namespace LimitTray.Native.Host;

internal sealed record CommandLine(
    string? FixturePath,
    string DataDirectory,
    IReadOnlyList<string> Raw,
    bool Show)
{
    public static CommandLine Parse(IReadOnlyList<string> args, string defaultDataDirectory)
    {
        string? fixture = null;
        var dataDirectory = defaultDataDirectory;
        var show = false;

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (argument.Equals("--fixture", StringComparison.OrdinalIgnoreCase))
                fixture = Value(args, ref i, argument);
            else if (argument.Equals("--data-dir", StringComparison.OrdinalIgnoreCase))
                dataDirectory = Value(args, ref i, argument)!;
            else if (argument.Equals("--show", StringComparison.OrdinalIgnoreCase))
                show = true;
        }

        return new CommandLine(fixture, Path.GetFullPath(dataDirectory), args.ToArray(), show);
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Missing value for " + option + ".");
        var value = args[++index];
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing value for " + option + ".");
        return value;
    }
}
