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

    /// <summary>RFC 4512 numericoid / RFC 2849 ldap-oid: 1*DIGIT *("." 1*DIGIT).</summary>
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
