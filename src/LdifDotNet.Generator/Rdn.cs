using System.Text;

namespace LdifDotNet.Generator;

internal static class Rdn
{
    /// <summary>Escapes an attribute value for use in an RDN per RFC 4514.</summary>
    public static string Escape(string value)
    {
        if (value.Length == 0)
            return value;

        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool needsEscape = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';'
                || (i == 0 && c is ' ' or '#')
                || (i == value.Length - 1 && c == ' ');
            if (needsEscape)
                result.Append('\\');
            result.Append(c);
        }
        return result.ToString();
    }
}
