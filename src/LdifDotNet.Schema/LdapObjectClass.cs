#pragma warning disable MA0048 // Deliberate: the Kind enum is colocated with its class

namespace LdifDotNet.Schema;

/// <summary>An object class definition (RFC 4512 §4.1.1).</summary>
public sealed class LdapObjectClass
{
    internal LdapObjectClass()
    {
    }

    /// <summary>The numeric OID that identifies this object class.</summary>
    public string Oid { get; internal set; } = "";

    /// <summary>All short names, in declaration order. May be empty.</summary>
    public IReadOnlyList<string> Names { get; internal set; } = [];

    /// <summary>The first short name, or the OID when the definition has no name.</summary>
    public string Name => Names.Count > 0 ? Names[0] : Oid;

    /// <summary>The DESC text, if any.</summary>
    public string? Description { get; internal set; }

    /// <summary>Whether the definition carries the OBSOLETE keyword.</summary>
    public bool Obsolete { get; internal set; }

    /// <summary>Names or OIDs of superior object classes (SUP), in declaration order.</summary>
    public IReadOnlyList<string> SuperiorNames { get; internal set; } = [];

    /// <summary>RFC 4512 defaults to STRUCTURAL when no kind keyword is present.</summary>
    public LdapObjectClassKind Kind { get; internal set; } = LdapObjectClassKind.Structural;

    /// <summary>Names of attributes this class requires (MUST), not including inherited ones.</summary>
    public IReadOnlyList<string> Must { get; internal set; } = [];

    /// <summary>Names of attributes this class allows (MAY), not including inherited ones.</summary>
    public IReadOnlyList<string> May { get; internal set; } = [];

    /// <summary>X-* extensions and their values (names are case-insensitive).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Extensions { get; internal set; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}

/// <summary>The kind of an object class (RFC 4512 §2.4).</summary>
public enum LdapObjectClassKind
{
    /// <summary>A template other classes derive from; entries cannot belong to it directly.</summary>
    Abstract,

    /// <summary>Defines what an entry fundamentally is; every entry has exactly one structural chain.</summary>
    Structural,

    /// <summary>Mixes additional attributes into entries of any structural class.</summary>
    Auxiliary,
}
