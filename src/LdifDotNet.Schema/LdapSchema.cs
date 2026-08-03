using System.Text;

namespace LdifDotNet.Schema;

/// <summary>
/// An aggregated set of LDAP schema definitions, loaded from one or more
/// slapd.conf-style schema files. Lookups are by name or OID, case-insensitive;
/// when definitions collide, the first-declared one wins.
/// </summary>
public sealed class LdapSchema
{
    private readonly List<LdapAttributeType> _attributeTypes;
    private readonly List<LdapObjectClass> _objectClasses;
    private readonly List<LdapSyntax> _syntaxes;
    private readonly List<LdapUnparsedDefinition> _unparsedDefinitions;
    private readonly Dictionary<string, LdapAttributeType> _attributeIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LdapObjectClass> _classIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LdapSyntax> _syntaxIndex = new(StringComparer.OrdinalIgnoreCase);

    private LdapSchema(
        List<LdapAttributeType> attributeTypes,
        List<LdapObjectClass> objectClasses,
        List<LdapSyntax> syntaxes,
        List<LdapUnparsedDefinition>? unparsedDefinitions = null)
    {
        _attributeTypes = attributeTypes;
        _objectClasses = objectClasses;
        _syntaxes = syntaxes;
        _unparsedDefinitions = unparsedDefinitions ?? [];

        foreach (var attributeType in attributeTypes)
        {
            _attributeIndex.TryAdd(attributeType.Oid, attributeType);
            foreach (string name in attributeType.Names)
                _attributeIndex.TryAdd(name, attributeType);
        }
        foreach (var objectClass in objectClasses)
        {
            _classIndex.TryAdd(objectClass.Oid, objectClass);
            foreach (string name in objectClass.Names)
                _classIndex.TryAdd(name, objectClass);
        }
        foreach (var syntax in syntaxes)
        {
            _syntaxIndex.TryAdd(syntax.Oid, syntax);
            foreach (string name in syntax.Names)
                _syntaxIndex.TryAdd(name, syntax);
        }
    }

    /// <summary>Loads and aggregates schema files in order (later files may reference earlier OID macros).</summary>
    public static LdapSchema Load(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var parser = new SchemaParser();
        var attributeTypes = new List<LdapAttributeType>();
        var objectClasses = new List<LdapObjectClass>();
        var syntaxes = new List<LdapSyntax>();

        foreach (string path in paths)
        {
            string text;
            try
            {
                text = File.ReadAllText(path, RfcGrammar.StrictUtf8);
            }
            catch (DecoderFallbackException)
            {
                throw new LdapSchemaParseException($"{Path.GetFileName(path)}: file is not valid UTF-8", lineNumber: 0);
            }
            try
            {
                parser.ParseInto(text, attributeTypes, objectClasses, syntaxes);
            }
            catch (LdapSchemaParseException e)
            {
                throw new LdapSchemaParseException($"{Path.GetFileName(path)}: {e.Message}", e.LineNumber);
            }
        }
        return new LdapSchema(attributeTypes, objectClasses, syntaxes);
    }

    /// <summary>Parses schema definitions from text in slapd.conf schema file format.</summary>
    public static LdapSchema Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var attributeTypes = new List<LdapAttributeType>();
        var objectClasses = new List<LdapObjectClass>();
        var syntaxes = new List<LdapSyntax>();
        new SchemaParser().ParseInto(text, attributeTypes, objectClasses, syntaxes);
        return new LdapSchema(attributeTypes, objectClasses, syntaxes);
    }

    /// <summary>
    /// Parses attributeTypes and objectClasses subschema values; equivalent to
    /// <see cref="ParseSubschema(IEnumerable{string}, IEnumerable{string}, IEnumerable{string})"/>
    /// with no ldapSyntaxes values.
    /// </summary>
    public static LdapSchema ParseSubschema(
        IEnumerable<string> attributeTypeDefinitions,
        IEnumerable<string> objectClassDefinitions)
    {
        return ParseSubschema(attributeTypeDefinitions, objectClassDefinitions, []);
    }

    /// <summary>
    /// Parses definition values as a server publishes them in its subschema
    /// subentry (RFC 4512 §4.2): each value is one bare parenthesized definition,
    /// e.g. "( 2.5.6.6 NAME 'person' ... )". Lenient, because a live server's
    /// schema cannot be fixed by the consumer: a definition that fails to parse
    /// is preserved in <see cref="UnparsedDefinitions"/> instead of failing the
    /// whole schema, and an unknown keyword inside a definition is skipped
    /// rather than failing that definition.
    /// </summary>
    public static LdapSchema ParseSubschema(
        IEnumerable<string> attributeTypeDefinitions,
        IEnumerable<string> objectClassDefinitions,
        IEnumerable<string> ldapSyntaxDefinitions)
    {
        ArgumentNullException.ThrowIfNull(attributeTypeDefinitions);
        ArgumentNullException.ThrowIfNull(objectClassDefinitions);
        ArgumentNullException.ThrowIfNull(ldapSyntaxDefinitions);

        var parser = new SchemaParser();
        var attributeTypes = new List<LdapAttributeType>();
        var objectClasses = new List<LdapObjectClass>();
        var syntaxes = new List<LdapSyntax>();
        var unparsed = new List<LdapUnparsedDefinition>();

        foreach (string definition in attributeTypeDefinitions)
        {
            if (definition is null)
                throw new ArgumentException("Definition values must not be null.", nameof(attributeTypeDefinitions));
            try
            {
                attributeTypes.Add(parser.ParseAttributeTypeDefinition(definition, lenient: true));
            }
            catch (LdapSchemaParseException e)
            {
                unparsed.Add(new LdapUnparsedDefinition(LdapSchemaDefinitionKind.AttributeType, definition, e.Message));
            }
        }
        foreach (string definition in objectClassDefinitions)
        {
            if (definition is null)
                throw new ArgumentException("Definition values must not be null.", nameof(objectClassDefinitions));
            try
            {
                objectClasses.Add(parser.ParseObjectClassDefinition(definition, lenient: true));
            }
            catch (LdapSchemaParseException e)
            {
                unparsed.Add(new LdapUnparsedDefinition(LdapSchemaDefinitionKind.ObjectClass, definition, e.Message));
            }
        }
        foreach (string definition in ldapSyntaxDefinitions)
        {
            if (definition is null)
                throw new ArgumentException("Definition values must not be null.", nameof(ldapSyntaxDefinitions));
            try
            {
                syntaxes.Add(parser.ParseSyntaxDefinition(definition, lenient: true));
            }
            catch (LdapSchemaParseException e)
            {
                unparsed.Add(new LdapUnparsedDefinition(LdapSchemaDefinitionKind.Syntax, definition, e.Message));
            }
        }
        return new LdapSchema(attributeTypes, objectClasses, syntaxes, unparsed);
    }

    /// <summary>All attribute types in declaration order.</summary>
    public IReadOnlyList<LdapAttributeType> AttributeTypes => _attributeTypes;

    /// <summary>All object classes in declaration order.</summary>
    public IReadOnlyList<LdapObjectClass> ObjectClasses => _objectClasses;

    /// <summary>All syntax definitions in declaration order.</summary>
    public IReadOnlyList<LdapSyntax> Syntaxes => _syntaxes;

    /// <summary>
    /// Definitions
    /// <see cref="ParseSubschema(IEnumerable{string}, IEnumerable{string}, IEnumerable{string})"/>
    /// could not parse, raw text preserved. Always empty for the strict
    /// <see cref="Load"/> and <see cref="Parse"/> paths, which throw on the
    /// first error instead.
    /// </summary>
    public IReadOnlyList<LdapUnparsedDefinition> UnparsedDefinitions => _unparsedDefinitions;

    /// <summary>Finds an attribute type by any of its names or its OID, or null.</summary>
    public LdapAttributeType? FindAttributeType(string nameOrOid)
    {
        ArgumentNullException.ThrowIfNull(nameOrOid);
        return _attributeIndex.GetValueOrDefault(nameOrOid);
    }

    /// <summary>Finds an object class by any of its names or its OID, or null.</summary>
    public LdapObjectClass? FindObjectClass(string nameOrOid)
    {
        ArgumentNullException.ThrowIfNull(nameOrOid);
        return _classIndex.GetValueOrDefault(nameOrOid);
    }

    /// <summary>
    /// Finds a syntax by OID (or a slapd-extension name), or null. A length
    /// bound is stripped before the lookup, so a raw SYNTAX reference like
    /// "1.3.6.1.4.1.1466.115.121.1.15{32768}" finds the syntax it names —
    /// OpenLDAP publishes bounded references, and the bound is not part of the
    /// syntax's identity.
    /// </summary>
    public LdapSyntax? FindSyntax(string nameOrOid)
    {
        ArgumentNullException.ThrowIfNull(nameOrOid);
        return _syntaxIndex.GetValueOrDefault(SchemaParser.StripLengthBound(nameOrOid, out _));
    }

    /// <summary>
    /// The attribute type's syntax OID, inherited through its SUP chain when
    /// the definition omits SYNTAX (RFC 4512 §2.5.1) — OpenLDAP publishes cn as
    /// "SUP name" with no SYNTAX at all. Cycle-guarded; a superior missing from
    /// this schema ends the walk. Null when no definition on the chain declares
    /// a syntax.
    /// </summary>
    public string? ResolveSyntaxOid(LdapAttributeType attributeType)
    {
        ArgumentNullException.ThrowIfNull(attributeType);

        var visited = new HashSet<LdapAttributeType>();
        for (var current = attributeType; current is not null && visited.Add(current);)
        {
            if (current.Syntax is not null)
                return current.Syntax;
            current = current.SuperiorName is { } superior ? FindAttributeType(superior) : null;
        }
        return null;
    }

    /// <summary>
    /// Attribute names the class requires (MUST), including those inherited through
    /// its superior chain. Superiors missing from this schema are skipped.
    /// </summary>
    public IReadOnlyList<string> RequiredAttributeNames(LdapObjectClass objectClass) =>
        CollectAttributeNames(objectClass, c => c.Must);

    /// <summary>
    /// Attribute names the class allows (MAY), including those inherited through
    /// its superior chain. Superiors missing from this schema are skipped.
    /// </summary>
    public IReadOnlyList<string> OptionalAttributeNames(LdapObjectClass objectClass) =>
        CollectAttributeNames(objectClass, c => c.May);

    private List<string> CollectAttributeNames(
        LdapObjectClass objectClass, Func<LdapObjectClass, IReadOnlyList<string>> selector)
    {
        ArgumentNullException.ThrowIfNull(objectClass);

        var result = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<LdapObjectClass>();
        var queue = new Queue<LdapObjectClass>();
        queue.Enqueue(objectClass);

        while (queue.TryDequeue(out var current))
        {
            if (!visited.Add(current))
                continue;
            foreach (string name in selector(current))
            {
                if (seenNames.Add(name))
                    result.Add(name);
            }
            foreach (string superiorName in current.SuperiorNames)
            {
                if (FindObjectClass(superiorName) is { } superior)
                    queue.Enqueue(superior);
            }
        }
        return result;
    }
}
