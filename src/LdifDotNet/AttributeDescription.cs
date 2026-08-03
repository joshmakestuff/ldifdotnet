namespace LdifDotNet;

/// <summary>
/// Helpers for LDAP attribute descriptions (RFC 4512 §2.5): an attribute type
/// name or OID followed by zero or more ";option" parts, e.g. "cn;lang-en" or
/// "userCertificate;binary" (the transfer option, RFC 4522). One definition of
/// "valid attribute description", shared with <see cref="LdifWriter"/>.
/// </summary>
public static class AttributeDescription
{
    /// <summary>
    /// The attribute type part, without any options: "cn;lang-en" yields "cn".
    /// A description with no options is returned unchanged.
    /// </summary>
    public static string TypeOf(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        int semicolon = description.IndexOf(';', StringComparison.Ordinal);
        return semicolon < 0 ? description : description[..semicolon];
    }

    /// <summary>
    /// Whether the description carries the given option (bare name, no leading
    /// semicolon), compared case-insensitively as RFC 4512 §2.5 requires:
    /// HasOption("userCertificate;binary", "binary") is true.
    /// </summary>
    public static bool HasOption(string description, string option)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(option);

        int start = description.IndexOf(';', StringComparison.Ordinal);
        while (start >= 0)
        {
            int end = description.IndexOf(';', start + 1);
            string candidate = end < 0 ? description[(start + 1)..] : description[(start + 1)..end];
            if (string.Equals(candidate, option, StringComparison.OrdinalIgnoreCase))
                return true;
            start = end;
        }
        return false;
    }

    /// <summary>
    /// RFC 2849 AttributeDescription: a numeric OID or a descr (ALPHA then
    /// ALPHA / DIGIT / "-"), followed by zero or more non-empty ";option" parts.
    /// </summary>
    public static bool IsValid(string description)
    {
        ArgumentNullException.ThrowIfNull(description);

        string[] parts = description.Split(';');
        if (!RfcGrammar.IsNumericOid(parts[0]) && !IsDescr(parts[0]))
            return false;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                return false;
            foreach (char c in parts[i])
            {
                if (!IsAttrTypeChar(c))
                    return false;
            }
        }
        return true;
    }

    private static bool IsDescr(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
            return false;
        foreach (char c in text)
        {
            if (!IsAttrTypeChar(c))
                return false;
        }
        return true;
    }

    private static bool IsAttrTypeChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';
}
