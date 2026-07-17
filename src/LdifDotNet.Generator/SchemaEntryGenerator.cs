#pragma warning disable MA0048 // Deliberate: the generator's options type is colocated with it

using System.Globalization;
using Bogus;
using LdifDotNet.Schema;

namespace LdifDotNet.Generator;

/// <summary>
/// Generates fake entries for arbitrary LDAP schemas: MUST attributes are always
/// filled, MAY attributes per <see cref="SchemaGeneratorOptions.OptionalAttributeFill"/>.
/// Values come from (in priority order) user-supplied example pools, well-known
/// attribute-name heuristics (only when compatible with the attribute's declared
/// syntax), then the attribute's syntax OID. Required attributes whose syntax has
/// no supported generator fall back to free text, which a server may reject;
/// supply an <see cref="SchemaGeneratorOptions.ExampleValues"/> pool for those.
/// </summary>
public sealed class SchemaEntryGenerator
{
    /// <summary>
    /// Syntaxes we can generate valid values for. MAY attributes with other
    /// syntaxes (certificates, delivery methods, ...) are skipped rather than
    /// risk emitting values a real server would reject.
    /// </summary>
    private const string SyntaxPrefix = "1.3.6.1.4.1.1466.115.121.1.";

    private readonly LdapSchema _schema;
    private readonly SchemaGeneratorOptions _options;
    private readonly Faker _faker;
    private readonly Dictionary<string, HashSet<string>> _usedRdnValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a generator for the given schema; null options use the defaults.</summary>
    public SchemaEntryGenerator(LdapSchema schema, SchemaGeneratorOptions? options = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _options = options ?? new SchemaGeneratorOptions();
        if (_options.OptionalAttributeFill is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "OptionalAttributeFill must be between 0 and 1.");
        _faker = new Faker(_options.Locale);
        if (_options.Seed is { } seed)
            _faker.Random = new Randomizer(seed);
    }

    /// <summary>Generates one entry of the given structural class under <paramref name="parentDn"/>.</summary>
    public LdifContentRecord Entry(string objectClassName, string parentDn)
    {
        ArgumentException.ThrowIfNullOrEmpty(objectClassName);
        ArgumentException.ThrowIfNullOrEmpty(parentDn);

        var primary = ResolveClass(objectClassName);
        if (primary.Kind != LdapObjectClassKind.Structural)
            throw new ArgumentException($"Object class '{primary.Name}' is {primary.Kind}; the primary class of an entry must be structural.", nameof(objectClassName));

        var classes = new List<LdapObjectClass> { primary };
        foreach (string auxiliaryName in _options.AuxiliaryClasses)
        {
            var auxiliary = ResolveClass(auxiliaryName);
            if (auxiliary.Kind != LdapObjectClassKind.Auxiliary)
                throw new InvalidOperationException($"AuxiliaryClasses contains '{auxiliary.Name}', which is {auxiliary.Kind}, not auxiliary.");
            classes.Add(auxiliary);
        }

        var objectClassValues = ObjectClassChain(classes);
        var required = CollectNames(classes, _schema.RequiredAttributeNames);
        var optional = CollectNames(classes, _schema.OptionalAttributeNames)
            .Where(name => !required.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        string rdnAttribute = _options.RdnAttribute is { } configuredRdn
            ? ValidateRdnAttribute(configuredRdn, required, optional)
            : PickRdnAttribute(required, optional);
        string rdnValue = UniqueRdnValue(rdnAttribute, parentDn);

        var attributes = new List<LdifAttribute>
        {
            new("objectClass", objectClassValues.Select(LdifValue.FromString)),
            new(rdnAttribute, rdnValue),
        };

        foreach (string name in required)
        {
            if (IsHandled(name, rdnAttribute))
                continue;
            if (GenerateValue(name, parentDn, required: true) is { } value)
                attributes.Add(new LdifAttribute(name, value));
        }
        foreach (string name in optional)
        {
            if (IsHandled(name, rdnAttribute) || _faker.Random.Double() >= _options.OptionalAttributeFill)
                continue;
            if (GenerateValue(name, parentDn, required: false) is { } value)
                attributes.Add(new LdifAttribute(name, value));
        }

        return new LdifContentRecord($"{rdnAttribute}={Rdn.Escape(rdnValue)},{parentDn}", attributes);

        static bool IsHandled(string name, string rdnAttribute) =>
            name.Equals("objectClass", StringComparison.OrdinalIgnoreCase)
            || name.Equals(rdnAttribute, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Generates <paramref name="count"/> entries under <paramref name="parentDn"/>.</summary>
    public IReadOnlyList<LdifContentRecord> Entries(string objectClassName, int count, string parentDn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var entries = new List<LdifContentRecord>(count);
        for (int i = 0; i < count; i++)
            entries.Add(Entry(objectClassName, parentDn));
        return entries;
    }

    private LdapObjectClass ResolveClass(string name) =>
        _schema.FindObjectClass(name)
        ?? throw new ArgumentException($"Object class '{name}' is not defined in the schema.", nameof(name));

    /// <summary>Superior-chain object class names, most-general first (top, ..., class, auxiliaries).</summary>
    private List<string> ObjectClassChain(List<LdapObjectClass> classes)
    {
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var objectClass in classes)
            Add(objectClass.Name);
        return chain;

        void Add(string name)
        {
            if (!seen.Add(name))
                return;
            if (_schema.FindObjectClass(name) is { } definition)
            {
                foreach (string superior in definition.SuperiorNames)
                    Add(superior);
                seen.Add(definition.Name);
                chain.Add(definition.Name);
            }
            else
            {
                // e.g. "top", hardcoded in slapd and absent from schema files
                chain.Add(name);
            }
        }
    }

    private static List<string> CollectNames(
        List<LdapObjectClass> classes, Func<LdapObjectClass, IReadOnlyList<string>> selector)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var objectClass in classes)
            foreach (string name in selector(objectClass))
                if (seen.Add(name))
                    result.Add(name);
        return result;
    }

    private static string ValidateRdnAttribute(string configured, List<string> required, List<string> optional)
    {
        if (!required.Contains(configured, StringComparer.OrdinalIgnoreCase)
            && !optional.Contains(configured, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RdnAttribute '{configured}' is neither required nor allowed by the selected object classes.");
        }
        return configured;
    }

    private static string PickRdnAttribute(List<string> required, List<string> optional)
    {
        foreach (string preferred in new[] { "uid", "cn" })
        {
            if (required.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                || optional.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }
        return required.FirstOrDefault(n => !n.Equals("objectClass", StringComparison.OrdinalIgnoreCase))
            ?? "cn";
    }

    private string UniqueRdnValue(string rdnAttribute, string parentDn)
    {
        string candidate = GenerateValue(rdnAttribute, parentDn, required: true)?.AsString() ?? "entry";
        var used = _usedRdnValues.TryGetValue($"{parentDn}\n{rdnAttribute}", out var set)
            ? set
            : _usedRdnValues[$"{parentDn}\n{rdnAttribute}"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string value = candidate;
        for (int suffix = 2; !used.Add(value); suffix++)
            value = $"{candidate}-{suffix}";
        return value;
    }

    private LdifValue? GenerateValue(string attributeName, string parentDn, bool required)
    {
        if (_options.ExampleValues.TryGetValue(attributeName, out var pool) && pool.Count > 0)
            return LdifValue.FromString(_faker.PickRandom<string>(pool));

        var (found, syntax) = ResolveSyntax(attributeName);

        // A well-known name only gets its heuristic value when the schema's declared
        // syntax (if any) accepts it — a custom attribute reusing a familiar name
        // with an incompatible syntax must not receive a plausible-looking invalid value.
        if (HeuristicValue(attributeName) is { } heuristic
            && (syntax is null || HeuristicMatchesSyntax(heuristic, syntax)))
        {
            return LdifValue.FromString(heuristic);
        }

        if (syntax is not null)
        {
            if (SyntaxValue(syntax, parentDn) is { } value)
                return value;
            // Syntax we cannot generate valid values for: never risk it on optionals.
            return required ? LdifValue.FromString(FreeText()) : (LdifValue?)null;
        }

        // No syntax anywhere in the SUP chain. A definition that exists but inherits
        // its syntax from slapd's hardcoded system schema (e.g. SUP name) is a
        // DirectoryString in practice; a name with no definition at all is only
        // generated when the schema forces it (MUST).
        return found || required ? LdifValue.FromString(FreeText()) : (LdifValue?)null;
    }

    /// <summary>Well-known attribute names get realistic values regardless of syntax.</summary>
    private string? HeuristicValue(string attributeName) => attributeName.ToLowerInvariant() switch
    {
        "cn" or "commonname" or "displayname" => _faker.Name.FullName(),
        "sn" or "surname" => _faker.Name.LastName(),
        "givenname" => _faker.Name.FirstName(),
        "uid" or "username" => SanitizeUid(_faker.Internet.UserName().ToLowerInvariant()),
        "mail" or "rfc822mailbox" or "email" => _faker.Internet.Email().ToLowerInvariant(),
        "telephonenumber" or "mobile" or "homephone" or "facsimiletelephonenumber" or "pager"
            => _faker.Phone.PhoneNumber(),
        "o" or "organizationname" => _faker.Company.CompanyName(),
        "ou" or "organizationalunitname" => _faker.Commerce.Department(1),
        "l" or "localityname" => _faker.Address.City(),
        "st" or "stateorprovincename" => _faker.Address.State(),
        "street" or "streetaddress" => _faker.Address.StreetAddress(),
        "postalcode" => _faker.Address.ZipCode(),
        "description" => _faker.Company.CatchPhrase(),
        "title" => _faker.Name.JobTitle(),
        "employeenumber" => _faker.Random.ReplaceNumbers("######"),
        "uidnumber" or "gidnumber" => _faker.Random.Int(1000, 60000).ToString(CultureInfo.InvariantCulture),
        "homedirectory" => $"/home/{SanitizeUid(_faker.Internet.UserName().ToLowerInvariant())}",
        "loginshell" => _faker.PickRandom("/bin/bash", "/bin/zsh", "/bin/sh"),
        "userpassword" => _faker.Internet.Password(),
        _ => null,
    };

    /// <summary>
    /// Whether a heuristic value is lexically valid for the given syntax OID.
    /// Permissive for free-form syntaxes, strict for structured ones; a syntax we
    /// cannot judge rejects the heuristic so generation falls through to
    /// <see cref="SyntaxValue"/> (or is skipped) rather than risk invalid output.
    /// </summary>
    private static bool HeuristicMatchesSyntax(string value, string syntax)
    {
        if (!syntax.StartsWith(SyntaxPrefix, StringComparison.Ordinal))
            return true; // non-standard syntax family: heuristic is no worse than free text

        return syntax[SyntaxPrefix.Length..] switch
        {
            "15" => true,                                                   // Directory String: any UTF-8
            "40" => true,                                                   // Octet String: any octets
            "41" => true,                                                   // Postal Address: dstring lines
            "26" => value.All(char.IsAscii),                                // IA5 String
            "27" => IsInteger(value),                                       // INTEGER
            "7" => value is "TRUE" or "FALSE",                              // Boolean
            "36" => value.All(c => char.IsAsciiDigit(c) || c == ' '),       // Numeric String
            "44" or "50" or "22" => value.All(IsPrintableChar),             // Printable / Telephone / Facsimile
            "11" => value.Length == 2 && value.All(IsPrintableChar),        // Country String
            _ => false,                                                     // structured syntax we cannot judge
        };
    }

    private static bool IsInteger(string value)
    {
        int start = value.StartsWith('-') ? 1 : 0;
        return value.Length > start && value.Skip(start).All(char.IsAsciiDigit);
    }

    /// <summary>RFC 4517 PrintableCharacter.</summary>
    private static bool IsPrintableChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '\'' or '(' or ')' or '+' or ',' or '-' or '.' or '/' or ':' or '?' or '=' or ' ';

    /// <summary>Resolves an attribute's syntax OID, walking the SUP chain.</summary>
    private (bool Found, string? Syntax) ResolveSyntax(string attributeName)
    {
        var definition = _schema.FindAttributeType(attributeName);
        bool found = definition is not null;
        for (int depth = 0; definition is not null && depth < 20; depth++)
        {
            if (definition.Syntax is not null)
                return (true, definition.Syntax);
            definition = definition.SuperiorName is { } superior
                ? _schema.FindAttributeType(superior)
                : null;
        }
        return (found, null);
    }

    private LdifValue? SyntaxValue(string syntax, string parentDn)
    {
        if (!syntax.StartsWith(SyntaxPrefix, StringComparison.Ordinal))
            return null;

        return syntax[SyntaxPrefix.Length..] switch
        {
            "15" => (LdifValue?)LdifValue.FromString(FreeText()),                        // Directory String
            "26" => LdifValue.FromString(_faker.Internet.DomainWord()),                  // IA5 String
            "27" => LdifValue.FromString(_faker.Random.Int(0, 100000).ToString(CultureInfo.InvariantCulture)), // INTEGER
            "7" => LdifValue.FromString(_faker.Random.Bool() ? "TRUE" : "FALSE"),        // Boolean
            "12" => LdifValue.FromString(parentDn),                                      // DN
            "24" => LdifValue.FromString(RandomTimestamp()),                             // Generalized Time
            "36" => LdifValue.FromString(_faker.Random.ReplaceNumbers("########")),      // Numeric String
            "41" => LdifValue.FromString($"{_faker.Address.StreetAddress()} $ {_faker.Address.City()}"), // Postal Address
            "50" => LdifValue.FromString(_faker.Phone.PhoneNumber()),                    // Telephone Number
            "44" => LdifValue.FromString(_faker.Random.AlphaNumeric(10)),                // Printable String
            "11" => LdifValue.FromString(_faker.Address.CountryCode()),                  // Country String
            "40" => LdifValue.FromBytes(_faker.Random.Bytes(16)),                        // Octet String
            _ => null,
        };
    }

    private string FreeText() => string.Join(' ', _faker.Lorem.Words(2));

    /// <summary>Deterministic timestamp — derived from the seeded RNG, never the clock.</summary>
    private string RandomTimestamp()
    {
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddSeconds(_faker.Random.Long(0, 30L * 365 * 24 * 3600));
        return timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";
    }

    private static string SanitizeUid(string value)
    {
        string sanitized = new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        return sanitized.Length > 0 ? sanitized : "user";
    }
}

/// <summary>Options controlling schema-driven generation.</summary>
public sealed class SchemaGeneratorOptions
{
    /// <summary>
    /// Seed for deterministic output. The same seed, schema, options and package
    /// version always produce the same entries. Null uses a random seed.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>Bogus locale for generated values. Default "en".</summary>
    public string Locale { get; set; } = "en";

    /// <summary>Fraction (0..1) of allowed (MAY) attributes to fill. Default 0.25.</summary>
    public double OptionalAttributeFill { get; set; } = 0.25;

    /// <summary>RDN attribute to use; null picks uid, then cn, then the first required attribute.</summary>
    public string? RdnAttribute { get; set; }

    /// <summary>Auxiliary object classes to mix into every entry (e.g. "eduPerson", "posixAccount").</summary>
    public IList<string> AuxiliaryClasses { get; } = [];

    /// <summary>
    /// Per-attribute example value pools (case-insensitive names). When present,
    /// values are drawn from the pool instead of being synthesized.
    /// </summary>
    public IDictionary<string, IReadOnlyList<string>> ExampleValues { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
