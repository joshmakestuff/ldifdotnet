namespace LdifDotNet;

/// <summary>An attribute description and its values, in document order.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "'Attribute' is the LDAP domain term; this type does not derive from System.Attribute.")]
public sealed class LdifAttribute
{
    public LdifAttribute(string name, params LdifValue[] values)
        : this(name, (IEnumerable<LdifValue>)values)
    {
    }

    public LdifAttribute(string name, IEnumerable<LdifValue> values)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>The attribute description, including any options (e.g. "userCertificate;binary").</summary>
    public string Name { get; }

    public IReadOnlyList<LdifValue> Values { get; }
}
