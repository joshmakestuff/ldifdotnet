namespace LdifDotNet.Schema;

/// <summary>
/// A definition value that
/// <see cref="LdapSchema.ParseSubschema(IEnumerable{string}, IEnumerable{string}, IEnumerable{string})"/>
/// could not parse, preserved raw so nothing is silently dropped. A live
/// server's schema cannot be fixed by the consumer, so one malformed or
/// vendor-specific definition degrades into this bucket instead of failing the
/// whole schema.
/// </summary>
public sealed class LdapUnparsedDefinition
{
    internal LdapUnparsedDefinition(LdapSchemaDefinitionKind kind, string definition, string error)
    {
        Kind = kind;
        Definition = definition;
        Error = error;
    }

    /// <summary>Which definition kind the value was presented as.</summary>
    public LdapSchemaDefinitionKind Kind { get; }

    /// <summary>The raw definition text, exactly as supplied.</summary>
    public string Definition { get; }

    /// <summary>Why the definition could not be parsed.</summary>
    public string Error { get; }

    /// <summary>Returns the raw definition text.</summary>
    public override string ToString() => Definition;
}
