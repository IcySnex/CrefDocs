using System.Text;

namespace CrefDocs.Rendering;

internal static class Slug
{
    public static string Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                AppendSeparator(builder);
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Trim('-');
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '-')
        {
            builder.Append('-');
        }
    }
}
