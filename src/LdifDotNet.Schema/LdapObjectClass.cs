namespace LdifDotNet.Schema;

/// <summary>An object class definition (RFC 4512 §4.1.1).</summary>
public sealed class LdapObjectClass
{
    internal LdapObjectClass()
    {
    }

    public string Oid { get; internal set; } = "";

    /// <summary>All short names, in declaration order. May be empty.</summary>
    public IReadOnlyList<string> Names { get; internal set; } = [];

    /// <summary>The first short name, or the OID when the definition has no name.</summary>
    public string Name => Names.Count > 0 ? Names[0] : Oid;

    public string? Description { get; internal set; }

    public bool Obsolete { get; internal set; }

    /// <summary>Names or OIDs of superior object classes (SUP), in declaration order.</summary>
    public IReadOnlyList<string> SuperiorNames { get; internal set; } = [];

    /// <summary>RFC 4512 defaults to STRUCTURAL when no kind keyword is present.</summary>
    public LdapObjectClassKind Kind { get; internal set; } = LdapObjectClassKind.Structural;

    /// <summary>Names of attributes this class requires (MUST), not including inherited ones.</summary>
    public IReadOnlyList<string> Must { get; internal set; } = [];

    /// <summary>Names of attributes this class allows (MAY), not including inherited ones.</summary>
    public IReadOnlyList<string> May { get; internal set; } = [];

    /// <summary>X-* extensions and their values.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Extensions { get; internal set; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public override string ToString() => Name;
}

public enum LdapObjectClassKind
{
    Abstract,
    Structural,
    Auxiliary,
}
