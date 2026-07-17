#pragma warning disable MA0048 // Deliberate: this file groups the change-record family

namespace LdifDotNet;

/// <summary>Base type for change records (changetype: add/delete/modify/moddn).</summary>
public abstract class LdifChangeRecord : LdifRecord
{
    private protected LdifChangeRecord(string dn)
        : base(dn)
    {
    }

    /// <summary>Controls attached to this change record via "control:" lines.</summary>
    public IReadOnlyList<LdifControl> Controls { get; init; } = [];
}

/// <summary>An LDAP control attached to a change record.</summary>
public sealed class LdifControl
{
    /// <summary>Creates a control with the given OID, optional criticality, and optional value.</summary>
    public LdifControl(string oid, bool? criticality = null, LdifValue? value = null)
    {
        Oid = oid ?? throw new ArgumentNullException(nameof(oid));
        Criticality = criticality;
        Value = value;
    }

    /// <summary>The control's numeric OID.</summary>
    public string Oid { get; }

    /// <summary>The control's criticality; null when the LDIF line omits it.</summary>
    public bool? Criticality { get; }

    /// <summary>The control's value; null when the LDIF line has none.</summary>
    public LdifValue? Value { get; }
}

/// <summary>A change record with changetype: add.</summary>
public sealed class LdifAddRecord : LdifChangeRecord
{
    /// <summary>Creates an add change record with the given DN and attributes.</summary>
    public LdifAddRecord(string dn, params LdifAttribute[] attributes)
        : this(dn, (IEnumerable<LdifAttribute>)attributes)
    {
    }

    /// <summary>Creates an add change record with the given DN and attributes.</summary>
    public LdifAddRecord(string dn, IEnumerable<LdifAttribute> attributes)
        : base(dn)
    {
        Attributes = attributes?.ToArray() ?? throw new ArgumentNullException(nameof(attributes));
    }

    /// <summary>The attributes of the entry to add, in declaration order.</summary>
    public IReadOnlyList<LdifAttribute> Attributes { get; }
}

/// <summary>A change record with changetype: delete.</summary>
public sealed class LdifDeleteRecord : LdifChangeRecord
{
    /// <summary>Creates a delete change record for the given DN.</summary>
    public LdifDeleteRecord(string dn)
        : base(dn)
    {
    }
}

/// <summary>A change record with changetype: modify.</summary>
public sealed class LdifModifyRecord : LdifChangeRecord
{
    /// <summary>Creates a modify change record with the given DN and modifications.</summary>
    public LdifModifyRecord(string dn, params LdifModification[] modifications)
        : this(dn, (IEnumerable<LdifModification>)modifications)
    {
    }

    /// <summary>Creates a modify change record with the given DN and modifications.</summary>
    public LdifModifyRecord(string dn, IEnumerable<LdifModification> modifications)
        : base(dn)
    {
        Modifications = modifications?.ToArray() ?? throw new ArgumentNullException(nameof(modifications));
    }

    /// <summary>The modifications to apply, in declaration order.</summary>
    public IReadOnlyList<LdifModification> Modifications { get; }
}

/// <summary>One add/delete/replace operation within a modify change record.</summary>
public sealed class LdifModification
{
    /// <summary>Creates a modification of the given type, attribute, and values.</summary>
    public LdifModification(LdifModificationType type, string attributeName, params LdifValue[] values)
        : this(type, attributeName, (IEnumerable<LdifValue>)values)
    {
    }

    /// <summary>Creates a modification of the given type, attribute, and values.</summary>
    public LdifModification(LdifModificationType type, string attributeName, IEnumerable<LdifValue> values)
    {
        Type = type;
        AttributeName = attributeName ?? throw new ArgumentNullException(nameof(attributeName));
        Values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>The operation this modification performs.</summary>
    public LdifModificationType Type { get; }

    /// <summary>The attribute description the modification targets.</summary>
    public string AttributeName { get; }

    /// <summary>The operation's values; may be empty (e.g. delete-all-values).</summary>
    public IReadOnlyList<LdifValue> Values { get; }
}

/// <summary>The operation of one modify-record spec.</summary>
public enum LdifModificationType
{
    /// <summary>Add the given values to the attribute.</summary>
    Add,

    /// <summary>Delete the given values, or the whole attribute when no values are given.</summary>
    Delete,

    /// <summary>Replace all values of the attribute with the given ones.</summary>
    Replace,

    /// <summary>Modify-Increment extension (RFC 4525); supported by OpenLDAP.</summary>
    Increment,
}

/// <summary>A change record with changetype: moddn or modrdn.</summary>
public sealed class LdifModDnRecord : LdifChangeRecord
{
    /// <summary>Creates a moddn/modrdn change record.</summary>
    public LdifModDnRecord(string dn, string newRdn, bool deleteOldRdn, string? newSuperior = null)
        : base(dn)
    {
        NewRdn = newRdn ?? throw new ArgumentNullException(nameof(newRdn));
        DeleteOldRdn = deleteOldRdn;
        NewSuperior = newSuperior;
    }

    /// <summary>The entry's new relative distinguished name.</summary>
    public string NewRdn { get; }

    /// <summary>Whether the old RDN attribute values are removed from the entry.</summary>
    public bool DeleteOldRdn { get; }

    /// <summary>DN of the new parent entry; null keeps the current parent.</summary>
    public string? NewSuperior { get; }
}
