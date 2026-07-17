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
    public LdifControl(string oid, bool? criticality = null, LdifValue? value = null)
    {
        Oid = oid ?? throw new ArgumentNullException(nameof(oid));
        Criticality = criticality;
        Value = value;
    }

    public string Oid { get; }

    public bool? Criticality { get; }

    public LdifValue? Value { get; }
}

/// <summary>A change record with changetype: add.</summary>
public sealed class LdifAddRecord : LdifChangeRecord
{
    public LdifAddRecord(string dn, params LdifAttribute[] attributes)
        : this(dn, (IEnumerable<LdifAttribute>)attributes)
    {
    }

    public LdifAddRecord(string dn, IEnumerable<LdifAttribute> attributes)
        : base(dn)
    {
        Attributes = attributes?.ToArray() ?? throw new ArgumentNullException(nameof(attributes));
    }

    public IReadOnlyList<LdifAttribute> Attributes { get; }
}

/// <summary>A change record with changetype: delete.</summary>
public sealed class LdifDeleteRecord : LdifChangeRecord
{
    public LdifDeleteRecord(string dn)
        : base(dn)
    {
    }
}

/// <summary>A change record with changetype: modify.</summary>
public sealed class LdifModifyRecord : LdifChangeRecord
{
    public LdifModifyRecord(string dn, params LdifModification[] modifications)
        : this(dn, (IEnumerable<LdifModification>)modifications)
    {
    }

    public LdifModifyRecord(string dn, IEnumerable<LdifModification> modifications)
        : base(dn)
    {
        Modifications = modifications?.ToArray() ?? throw new ArgumentNullException(nameof(modifications));
    }

    public IReadOnlyList<LdifModification> Modifications { get; }
}

/// <summary>One add/delete/replace operation within a modify change record.</summary>
public sealed class LdifModification
{
    public LdifModification(LdifModificationType type, string attributeName, params LdifValue[] values)
        : this(type, attributeName, (IEnumerable<LdifValue>)values)
    {
    }

    public LdifModification(LdifModificationType type, string attributeName, IEnumerable<LdifValue> values)
    {
        Type = type;
        AttributeName = attributeName ?? throw new ArgumentNullException(nameof(attributeName));
        Values = values?.ToArray() ?? throw new ArgumentNullException(nameof(values));
    }

    public LdifModificationType Type { get; }

    public string AttributeName { get; }

    public IReadOnlyList<LdifValue> Values { get; }
}

public enum LdifModificationType
{
    Add,
    Delete,
    Replace,
}

/// <summary>A change record with changetype: moddn or modrdn.</summary>
public sealed class LdifModDnRecord : LdifChangeRecord
{
    public LdifModDnRecord(string dn, string newRdn, bool deleteOldRdn, string? newSuperior = null)
        : base(dn)
    {
        NewRdn = newRdn ?? throw new ArgumentNullException(nameof(newRdn));
        DeleteOldRdn = deleteOldRdn;
        NewSuperior = newSuperior;
    }

    public string NewRdn { get; }

    public bool DeleteOldRdn { get; }

    public string? NewSuperior { get; }
}
