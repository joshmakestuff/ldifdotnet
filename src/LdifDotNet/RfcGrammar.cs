using System.Text;

namespace LdifDotNet;

/// <summary>
/// Grammar helpers with exactly one implementation, shared across the library —
/// and, via a linked compile item in LdifDotNet.Schema.csproj, with the schema
/// assembly — so the copies cannot drift.
/// </summary>
internal static class RfcGrammar
{
    /// <summary>
    /// Strict UTF-8: rejects invalid octets rather than decoding them to U+FFFD,
    /// which would silently collapse distinct invalid inputs into one value.
    /// </summary>
    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Numeric OID, deliberately the looser RFC 2849 ldap-oid grammar
    /// (1*DIGIT *("." 1*DIGIT)) rather than RFC 4512's numericoid: leading-zero
    /// arcs and single-arc OIDs are accepted. Correct per spec on every LDIF
    /// boundary, and matched to slapd on the schema boundary — slaptest 2.6
    /// accepts 01.2.3.4.5, 1.02.3, and bare 1 in attributetype directives, so
    /// tightening this would reject files slapd loads. Pinned by tests; do not
    /// "fix" toward RFC 4512 without re-probing slapd.
    /// </summary>
    internal static bool IsNumericOid(string text)
    {
        bool expectDigit = true;
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
                expectDigit = false;
            else if (c == '.' && !expectDigit)
                expectDigit = true;
            else
                return false;
        }
        return text.Length > 0 && !expectDigit;
    }
}
