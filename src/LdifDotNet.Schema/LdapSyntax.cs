namespace LdifDotNet.Schema;

/// <summary>An LDAP syntax definition (RFC 4512 §4.1.5).</summary>
public sealed class LdapSyntax
{
    internal LdapSyntax()
    {
    }

    /// <summary>
    /// Parses one bare parenthesized syntax description, the form a subschema
    /// subentry publishes as ldapSyntaxes values, e.g.
    /// "( 1.3.6.1.4.1.1466.115.121.1.28 DESC 'JPEG' X-NOT-HUMAN-READABLE 'TRUE' )".
    /// Strict: an unknown keyword, a non-numeric OID, or trailing text throws
    /// <see cref="LdapSchemaParseException"/>; for input a server published, use
    /// the lenient <see cref="LdapSchema.ParseSubschema(System.Collections.Generic.IEnumerable{string}, System.Collections.Generic.IEnumerable{string}, System.Collections.Generic.IEnumerable{string})"/>.
    /// </summary>
    public static LdapSyntax Parse(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new SchemaParser().ParseSyntaxDefinition(definition, lenient: false);
    }

    /// <summary>The numeric OID that identifies this syntax.</summary>
    public string Oid { get; internal set; } = "";

    /// <summary>
    /// All short names, in declaration order. Usually empty: RFC 4512's grammar
    /// gives syntaxes no NAME, but slapd accepts one in ldapsyntax directives
    /// and its own shipped pmi.schema uses it.
    /// </summary>
    public IReadOnlyList<string> Names { get; internal set; } = [];

    /// <summary>The first short name, or the OID when the definition has no name.</summary>
    public string Name => Names.Count > 0 ? Names[0] : Oid;

    /// <summary>The DESC text, if any.</summary>
    public string? Description { get; internal set; }

    /// <summary>X-* extensions and their values (names are case-insensitive).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Extensions { get; internal set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the definition asserts OpenLDAP's X-NOT-HUMAN-READABLE extension —
    /// how a server declares octet-carrying syntaxes no RFC lists. Only an
    /// explicit 'TRUE' asserts the flag; a published 'FALSE' is not an assertion.
    /// </summary>
    public bool NotHumanReadable => HasTrueExtension("X-NOT-HUMAN-READABLE");

    /// <summary>
    /// Whether the definition asserts OpenLDAP's X-BINARY-TRANSFER-REQUIRED
    /// extension (values must transfer via the ;binary option, RFC 4522). Only
    /// an explicit 'TRUE' asserts the flag.
    /// </summary>
    public bool BinaryTransferRequired => HasTrueExtension("X-BINARY-TRANSFER-REQUIRED");

    private bool HasTrueExtension(string name) =>
        Extensions.TryGetValue(name, out var values)
        && values.Count > 0
        && string.Equals(values[0], "TRUE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}
