#pragma warning disable MA0048 // Deliberate: base record and content record live together

namespace LdifDotNet;

/// <summary>Base type for all LDIF records (content records and change records).</summary>
public abstract class LdifRecord
{
    private protected LdifRecord(string dn)
    {
        Dn = dn ?? throw new ArgumentNullException(nameof(dn));
    }

    public string Dn { get; }
}

/// <summary>A content record: a DN and its attributes (RFC 2849 ldif-content).</summary>
public sealed class LdifContentRecord : LdifRecord
{
    public LdifContentRecord(string dn, params LdifAttribute[] attributes)
        : this(dn, (IEnumerable<LdifAttribute>)attributes)
    {
    }

    public LdifContentRecord(string dn, IEnumerable<LdifAttribute> attributes)
        : base(dn)
    {
        Attributes = attributes?.ToArray() ?? throw new ArgumentNullException(nameof(attributes));
    }

    public IReadOnlyList<LdifAttribute> Attributes { get; }

    /// <summary>The first attribute with the given name (case-insensitive), or null.</summary>
    public LdifAttribute? this[string name] =>
        Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}
