namespace LdifDotNet.Schema;

/// <summary>The kind of schema definition a subschema value was presented as.</summary>
public enum LdapSchemaDefinitionKind
{
    /// <summary>An attribute type description (RFC 4512 §4.1.2), from attributeTypes values.</summary>
    AttributeType,

    /// <summary>An object class description (RFC 4512 §4.1.1), from objectClasses values.</summary>
    ObjectClass,
}
