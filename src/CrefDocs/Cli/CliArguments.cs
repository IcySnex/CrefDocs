namespace CrefDocs.Cli;

internal sealed class CliArguments
{
    private readonly Dictionary<string, string?> _values;

    private CliArguments(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public static CliArguments Parse(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2)
            {
                throw new CliException($"Unexpected argument '{argument}'. Options must start with --.");
            }

            var separator = argument.IndexOf('=');
            var name = separator < 0 ? argument[2..] : argument[2..separator];
            string? value = separator < 0 ? null : argument[(separator + 1)..];
            if (separator < 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }

            if (!values.TryAdd(name, value))
            {
                throw new CliException($"Option '--{name}' was supplied more than once.");
            }
        }

        return new CliArguments(values);
    }

    public string Required(string name)
    {
        if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CliException($"Missing required option '--{name}'.");
        }

        return value;
    }

    public string Optional(string name, string defaultValue)
    {
        return _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public string? Optional(string name)
    {
        return _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    public bool Flag(string name)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return false;
        }

        if (value is not null)
        {
            throw new CliException($"Option '--{name}' does not accept a value.");
        }

        return true;
    }

    public void EnsureOnly(params string[] names)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var unknown = _values.Keys.FirstOrDefault(name => !allowed.Contains(name));
        if (unknown is not null)
        {
            throw new CliException($"Unknown option '--{unknown}'.");
        }
    }
}

internal sealed class CliException(string message) : Exception(message);

