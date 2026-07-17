#pragma warning disable MA0048 // Deliberate: the DN model types are a family colocated with Dn

using System.Text;

namespace LdifDotNet;

/// <summary>
/// RFC 4514 distinguished-name composition: escape attribute values, build RDNs
/// and DNs from parts, and parse a DN string into its ordered components. This is
/// deliberately DN <em>string</em> handling only — it does not normalize, compare,
/// or schema-match names. LDIF line encoding (RFC 2849) is a separate concern
/// handled by <see cref="LdifWriter"/> and <see cref="LdifReader"/>.
/// </summary>
public static class Dn
{
    /// <summary>
    /// Escapes an attribute value for use inside a DN per RFC 4514 §2.4:
    /// backslash-escapes <c>" + , ; &lt; &gt; \</c> anywhere, a leading <c>#</c> or space,
    /// and a trailing space; hex-escapes NUL (which RFC 4514 forbids even when
    /// backslash-escaped). Other characters, including control characters, are left
    /// as-is — the LDIF writer base64-encodes the resulting DN line when needed.
    /// </summary>
    public static string EscapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return value;

        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\0')
            {
                result.Append("\\00");
                continue;
            }
            bool needsEscape = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';'
                || (i == 0 && c is ' ' or '#')
                || (i == value.Length - 1 && c == ' ');
            if (needsEscape)
                result.Append('\\');
            result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>
    /// Reverses <see cref="EscapeValue"/> and, more generally, decodes any RFC 4514
    /// §2.3 escaping: <c>\c</c> yields the literal character, and runs of <c>\XX</c>
    /// hex pairs are decoded as UTF-8 octets (so <c>\C4\8D</c> becomes "č").
    /// A hexstring value (a leading unescaped <c>#</c>, i.e. BER-encoded) is returned
    /// verbatim without decoding. Throws on a malformed trailing escape.
    /// </summary>
    public static string UnescapeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0)
            return value;

        var result = new StringBuilder(value.Length);
        var hexRun = new List<byte>();

        void FlushHex()
        {
            if (hexRun.Count == 0)
                return;
            result.Append(Encoding.UTF8.GetString(hexRun.ToArray()));
            hexRun.Clear();
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\')
            {
                FlushHex();
                result.Append(c);
                continue;
            }

            if (i + 1 >= value.Length)
                throw new ArgumentException("DN value ends with a dangling '\\' escape.", nameof(value));

            char next = value[i + 1];
            if (IsHex(next))
            {
                if (i + 2 >= value.Length || !IsHex(value[i + 2]))
                    throw new ArgumentException($"DN value has a malformed hex escape '\\{next}'.", nameof(value));
                hexRun.Add((byte)((HexDigit(next) << 4) | HexDigit(value[i + 2])));
                i += 2;
                continue;
            }

            // A single escaped literal: RFC 4514 §2.3 permits only a special char,
            // another backslash, space, '#' or '=' after ESC.
            if (!IsEscapableLiteral(next))
                throw new ArgumentException($"DN value has an invalid escape '\\{next}'.", nameof(value));
            FlushHex();
            result.Append(next);
            i += 1;
        }

        FlushHex();
        return result.ToString();
    }

    /// <summary>
    /// Builds one RDN component, escaping the value: <c>Rdn("cn", "Smith, Jr.")</c>
    /// yields <c>cn=Smith\, Jr.</c>. Use <see cref="Combine"/> to attach it to a parent DN.
    /// </summary>
    public static string Rdn(string attributeType, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(attributeType);
        ArgumentNullException.ThrowIfNull(value);
        return $"{attributeType}={EscapeValue(value)}";
    }

    /// <summary>
    /// Joins already-formed DN parts (RDNs or whole DNs) with commas, skipping null or
    /// empty parts. The parts are assumed already escaped — a parent DN is appended
    /// verbatim: <c>Combine(Rdn("uid", uid), "ou=people,dc=example,dc=com")</c>.
    /// </summary>
    public static string Combine(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(',', parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Parses a DN string into its RDNs, most-specific first, with values unescaped.
    /// Multi-valued RDNs (<c>a=1+b=2</c>) are represented as one
    /// <see cref="RelativeDistinguishedName"/> with several attributes rather than being
    /// flattened. Insignificant whitespace around separators is tolerated. An empty or
    /// whitespace-only string yields an empty list; a component missing <c>=</c> throws.
    /// </summary>
    public static IReadOnlyList<RelativeDistinguishedName> Parse(string distinguishedName)
    {
        ArgumentNullException.ThrowIfNull(distinguishedName);

        var rdns = new List<RelativeDistinguishedName>();
        foreach (string rdnText in SplitUnescaped(distinguishedName, ','))
        {
            var attributes = new List<AttributeTypeAndValue>();
            foreach (string atavText in SplitUnescaped(rdnText, '+'))
            {
                string trimmed = TrimUnescaped(atavText);
                if (trimmed.Length == 0)
                    continue;

                int eq = IndexOfUnescaped(trimmed, '=');
                if (eq <= 0)
                    throw new ArgumentException($"DN component '{trimmed}' is not 'type=value'.", nameof(distinguishedName));

                string type = TrimUnescaped(trimmed[..eq]);
                if (type.Length == 0)
                    throw new ArgumentException($"DN component '{trimmed}' has an empty attribute type.", nameof(distinguishedName));

                string rawValue = TrimUnescaped(trimmed[(eq + 1)..]);
                if (rawValue.StartsWith('#'))
                {
                    // RFC 4514 hexstring: a BER-encoded value. Decoding BER is out of
                    // scope; supporting it half-way would silently corrupt the value on
                    // reserialize (the '#' would be escaped), so reject it explicitly.
                    throw new ArgumentException(
                        $"DN component '{trimmed}' uses a BER hexstring value ('#...'), which is not supported. Escape the leading '#' if it is literal.",
                        nameof(distinguishedName));
                }
                attributes.Add(new AttributeTypeAndValue(type, UnescapeValue(rawValue)));
            }

            if (attributes.Count > 0)
                rdns.Add(new RelativeDistinguishedName(attributes));
        }

        return rdns;
    }

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private static bool IsEscapableLiteral(char c) =>
        c is '"' or '+' or ',' or ';' or '<' or '>' or '\\' or ' ' or '#' or '=';

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    /// <summary>Splits on <paramref name="separator"/> characters that are not backslash-escaped, keeping escapes intact.</summary>
    private static List<string> SplitUnescaped(string s, char separator)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        bool escaped = false;
        foreach (char c in s)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                current.Append(c);
                escaped = true;
            }
            else if (c == separator)
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        segments.Add(current.ToString());
        return segments;
    }

    private static int IndexOfUnescaped(string s, char target)
    {
        bool escaped = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (escaped)
                escaped = false;
            else if (s[i] == '\\')
                escaped = true;
            else if (s[i] == target)
                return i;
        }
        return -1;
    }

    /// <summary>Trims insignificant leading/trailing ASCII spaces, preserving a backslash-escaped trailing space.</summary>
    private static string TrimUnescaped(string s)
    {
        int start = 0;
        while (start < s.Length && s[start] == ' ')
            start++;

        int end = s.Length;
        while (end > start && s[end - 1] == ' ')
        {
            int backslashes = 0;
            int j = end - 2;
            while (j >= start && s[j] == '\\')
            {
                backslashes++;
                j--;
            }
            if ((backslashes & 1) == 1)
                break; // this space is escaped ("\ "), so it is significant
            end--;
        }

        return s[start..end];
    }
}

/// <summary>One attribute type and its (unescaped) value within an RDN (RFC 4514 attributeTypeAndValue).</summary>
public readonly record struct AttributeTypeAndValue(string Type, string Value);

/// <summary>
/// A relative distinguished name: one attribute, or several joined by '+' in a
/// multi-valued RDN. Values are unescaped.
/// </summary>
public sealed class RelativeDistinguishedName
{
    internal RelativeDistinguishedName(IReadOnlyList<AttributeTypeAndValue> attributes)
    {
        Attributes = attributes;
    }

    /// <summary>The attributes of this RDN, in declaration order.</summary>
    public IReadOnlyList<AttributeTypeAndValue> Attributes { get; }

    /// <summary>True when this RDN joins more than one attribute with '+'.</summary>
    public bool IsMultiValued => Attributes.Count > 1;

    /// <summary>The single attribute of a single-valued RDN. Throws when <see cref="IsMultiValued"/>.</summary>
    public AttributeTypeAndValue SoleAttribute => IsMultiValued
        ? throw new InvalidOperationException("This RDN is multi-valued; use Attributes.")
        : Attributes[0];

    /// <summary>The attribute type of a single-valued RDN. Throws when <see cref="IsMultiValued"/>.</summary>
    public string Type => SoleAttribute.Type;

    /// <summary>The unescaped value of a single-valued RDN. Throws when <see cref="IsMultiValued"/>.</summary>
    public string Value => SoleAttribute.Value;

    /// <summary>The RDN in escaped RFC 4514 string form.</summary>
    public override string ToString() =>
        string.Join('+', Attributes.Select(a => $"{a.Type}={Dn.EscapeValue(a.Value)}"));
}
