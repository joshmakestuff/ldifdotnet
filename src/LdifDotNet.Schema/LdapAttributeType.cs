namespace LdifDotNet.Schema;

/// <summary>An attribute type definition (RFC 4512 §4.1.2).</summary>
public sealed class LdapAttributeType
{
    internal LdapAttributeType()
    {
    }

    /// <summary>The numeric OID that identifies this attribute type.</summary>
    public string Oid { get; internal set; } = "";

    /// <summary>All short names, in declaration order. May be empty.</summary>
    public IReadOnlyList<string> Names { get; internal set; } = [];

    /// <summary>The first short name, or the OID when the definition has no name.</summary>
    public string Name => Names.Count > 0 ? Names[0] : Oid;

    /// <summary>The DESC text, if any.</summary>
    public string? Description { get; internal set; }

    /// <summary>Whether the definition carries the OBSOLETE keyword.</summary>
    public bool Obsolete { get; internal set; }

    /// <summary>Name or OID of the superior attribute type (SUP), if any.</summary>
    public string? SuperiorName { get; internal set; }

    /// <summary>Name or OID of the EQUALITY matching rule, if any.</summary>
    public string? Equality { get; internal set; }

    /// <summary>Name or OID of the ORDERING matching rule, if any.</summary>
    public string? Ordering { get; internal set; }

    /// <summary>Name or OID of the SUBSTR matching rule, if any.</summary>
    public string? Substring { get; internal set; }

    /// <summary>Syntax OID without any length bound, e.g. "1.3.6.1.4.1.1466.115.121.1.15".</summary>
    public string? Syntax { get; internal set; }

    /// <summary>Suggested minimum upper bound from "{n}" after the syntax OID, if present.</summary>
    public int? SyntaxLength { get; internal set; }

    /// <summary>Whether the definition carries the SINGLE-VALUE keyword.</summary>
    public bool SingleValue { get; internal set; }

    /// <summary>Whether the definition carries the COLLECTIVE keyword.</summary>
    public bool Collective { get; internal set; }

    /// <summary>Whether the definition carries the NO-USER-MODIFICATION keyword.</summary>
    public bool NoUserModification { get; internal set; }

    /// <summary>USAGE value (e.g. "directoryOperation"); null means userApplications.</summary>
    public string? Usage { get; internal set; }

    /// <summary>X-* extensions and their values (names are case-insensitive).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Extensions { get; internal set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}
