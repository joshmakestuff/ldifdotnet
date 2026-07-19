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
    private readonly Dictionary<string, LdapAttributeType> _attributeIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LdapObjectClass> _classIndex = new(StringComparer.OrdinalIgnoreCase);

    private LdapSchema(List<LdapAttributeType> attributeTypes, List<LdapObjectClass> objectClasses)
    {
        _attributeTypes = attributeTypes;
        _objectClasses = objectClasses;

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
    }

    /// <summary>Loads and aggregates schema files in order (later files may reference earlier OID macros).</summary>
    public static LdapSchema Load(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var parser = new SchemaParser();
        var attributeTypes = new List<LdapAttributeType>();
        var objectClasses = new List<LdapObjectClass>();

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
                parser.ParseInto(text, attributeTypes, objectClasses);
            }
            catch (LdapSchemaParseException e)
            {
                throw new LdapSchemaParseException($"{Path.GetFileName(path)}: {e.Message}", e.LineNumber);
            }
        }
        return new LdapSchema(attributeTypes, objectClasses);
    }

    /// <summary>Parses schema definitions from text in slapd.conf schema file format.</summary>
    public static LdapSchema Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var attributeTypes = new List<LdapAttributeType>();
        var objectClasses = new List<LdapObjectClass>();
        new SchemaParser().ParseInto(text, attributeTypes, objectClasses);
        return new LdapSchema(attributeTypes, objectClasses);
    }

    /// <summary>All attribute types in declaration order.</summary>
    public IReadOnlyList<LdapAttributeType> AttributeTypes => _attributeTypes;

    /// <summary>All object classes in declaration order.</summary>
    public IReadOnlyList<LdapObjectClass> ObjectClasses => _objectClasses;

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
