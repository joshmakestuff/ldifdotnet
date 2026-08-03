#pragma warning disable MA0048 // Deliberate: the generator's options type is colocated with it

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Bogus;
using LdifDotNet.Schema;

namespace LdifDotNet.Generator;

/// <summary>
/// Generates fake entries for arbitrary LDAP schemas: MUST attributes are always
/// filled, MAY attributes per <see cref="SchemaGeneratorOptions.OptionalAttributeFill"/>.
/// Values come from (in priority order) user-supplied format templates
/// (<see cref="SchemaGeneratorOptions.Formatters"/>), example pools, well-known
/// attribute-name heuristics (only when compatible with the attribute's declared
/// syntax), then the attribute's syntax OID. Required attributes whose syntax has
/// no supported generator fall back to free text, which a server may reject;
/// supply an <see cref="SchemaGeneratorOptions.ExampleValues"/> pool for those.
/// </summary>
public sealed partial class SchemaEntryGenerator
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

    /// <summary>
    /// Validated snapshot of <see cref="SchemaGeneratorOptions.Formatters"/>,
    /// expanded to cover every schema name of each keyed attribute. A snapshot
    /// keeps the fail-at-construction contract airtight: mutating the options
    /// dictionary after construction cannot smuggle in an unvalidated template.
    /// </summary>
    private readonly Dictionary<string, string> _formatters;

    private readonly Dictionary<string, HashSet<string>> _usedRdnValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _nextRdnSuffix = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a generator for the given schema; null options use the defaults.</summary>
    public SchemaEntryGenerator(LdapSchema schema, SchemaGeneratorOptions? options = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _options = options ?? new SchemaGeneratorOptions();
        if (_options.OptionalAttributeFill is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "OptionalAttributeFill must be between 0 and 1.");
        _faker = FakerFactory.Create(_options.Locale, _options.Seed);
        _formatters = ValidateFormatters(_schema, _options);
    }

    /// <summary>
    /// Fails fast on unusable formatter templates and returns the validated,
    /// alias-expanded snapshot. Probing uses a throwaway faker so validation can
    /// never perturb the real generator's random stream.
    /// </summary>
    private static Dictionary<string, string> ValidateFormatters(LdapSchema schema, SchemaGeneratorOptions options)
    {
        var expanded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.Formatters.Count == 0)
            return expanded;

        var probe = FakerFactory.Create(options.Locale, seed: 0);
        var originalCulture = PinInvariantCulture();
        try
        {
            foreach (var (attribute, template) in options.Formatters)
            {
                ValidateTemplate(attribute, template);
                // Register the template under the key and, when the key names a
                // schema attribute, under all of that attribute's names — so a
                // formatter keyed by an alias (e.g. "surname") still applies to
                // the name the object class uses (e.g. "sn") instead of being
                // silently ignored.
                Register(attribute, template);
                if (schema.FindAttributeType(attribute) is { } definition)
                {
                    // The OID too: an object class may list the attribute
                    // numerically (MUST 1.2.3.4), and that string becomes the
                    // generated attribute's name.
                    Register(definition.Oid, template);
                    foreach (string name in definition.Names)
                        Register(name, template);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
        return expanded;

        void ValidateTemplate(string attribute, string template)
        {
            if (string.IsNullOrEmpty(template))
                throw new ArgumentException($"Formatter for attribute '{attribute}' has a null or empty template.", nameof(options));

            if (FindNonScalarToken(template) is { } bad)
            {
                throw new ArgumentException(
                    $"Formatter template for attribute '{attribute}' uses token '{bad.Token}', which returns" +
                    $" {bad.TypeName}, not a scalar value: \"{template}\". Use a scalar token (e.g. lorem.word, not lorem.words).",
                    nameof(options));
            }

            string probeOutput;
            try
            {
                probeOutput = ParseTemplate(probe, template);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The tokenizer dispatches by reflection, so the exception surface
                // is unbounded (KeyNotFoundException from a bad IBAN country code,
                // wrapped TargetInvocationException, ...). The probe is a sandboxed
                // throwaway faker; nothing is worth letting through unwrapped.
                var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                throw new ArgumentException(
                    $"Formatter template for attribute '{attribute}' is invalid: \"{template}\" ({cause.Message})",
                    nameof(options), cause);
            }

            if (string.IsNullOrWhiteSpace(probeOutput))
            {
                throw new ArgumentException(
                    $"Formatter template for attribute '{attribute}' produced an empty or whitespace-only value" +
                    $" on a probe draw: \"{template}\".", nameof(options));
            }
        }

        void Register(string name, string template)
        {
            if (expanded.TryGetValue(name, out string? existing))
            {
                if (!string.Equals(existing, template, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Formatters contains conflicting templates for attribute '{name}' — aliases of one schema attribute share one formatter.",
                        nameof(options));
                }
            }
            else
            {
                expanded[name] = template;
            }
        }
    }

    [GeneratedRegex(
        @"\{\{\s*(?<category>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatterToken();

    /// <summary>
    /// Finds a template token whose Bogus dataset method cannot return a scalar,
    /// deciding on the method's declared return type rather than sniffing rendered
    /// output (which false-positives on literal text and misses non-System types).
    /// Categories resolve like Bogus's own registration (dataset type name,
    /// case-insensitive); unresolvable tokens are left to the probe parse, which
    /// throws for genuinely unknown ones — so this check cannot false-reject.
    /// </summary>
    private static (string Token, string TypeName)? FindNonScalarToken(string template)
    {
        foreach (Match match in FormatterToken().Matches(template))
        {
            string category = match.Groups["category"].Value;
            string method = match.Groups["method"].Value;
            var dataset = Array.Find(
                typeof(Faker).GetProperties(BindingFlags.Public | BindingFlags.Instance),
                p => p.PropertyType.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (dataset is null)
                continue;
            var overloads = dataset.PropertyType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.Equals(method, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (overloads.Count > 0 && !overloads.Exists(m => IsScalar(m.ReturnType)))
                return ($"{category}.{method}", overloads[0].ReturnType.Name);
        }
        return null;
    }

    /// <summary>
    /// Types whose default rendering is a stable single-line value under the
    /// invariant culture. Bogus 35.6.5's dataset methods also return DateOnly,
    /// TimeOnly, IPAddress, IPEndPoint and Version — all render cleanly
    /// ("05/04/1999", "133.207.206.30", "1.9.5.1"); arrays, Currency, Exception
    /// and other objects stringify as type names or multi-line text and stay
    /// rejected.
    /// </summary>
    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(string) || type.IsPrimitive || type.IsEnum
            || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(Guid)
            || type == typeof(DateOnly) || type == typeof(TimeOnly)
            || type == typeof(System.Net.IPAddress) || type == typeof(System.Net.IPEndPoint)
            || type == typeof(Version);
    }

    /// <summary>
    /// The single blessed <c>Faker.Parse</c> call site (RS0030 bans it elsewhere).
    /// Callers must hold the invariant-culture scope: the Bogus tokenizer
    /// stringifies non-string token results with the current culture, which would
    /// make seeded output vary by machine culture (under ar-SA, {{date.past}}
    /// renders an Umm al-Qura calendar date).
    /// </summary>
    private static string ParseTemplate(Faker faker, string template)
    {
#pragma warning disable RS0030 // Sole blessed call site; culture is pinned by the caller's scope
        return faker.Parse(template);
#pragma warning restore RS0030
    }

    /// <summary>
    /// Pins the invariant culture for a generation scope; the caller restores the
    /// returned culture in a finally block. Restoring by assignment pins the
    /// AsyncLocal-backed culture even where the thread was inheriting a default —
    /// not fixable through the public API; accepted.
    /// </summary>
    private static CultureInfo PinInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return original;
    }

    /// <summary>Generates one entry of the given structural class under <paramref name="parentDn"/>.</summary>
    public LdifContentRecord Entry(string objectClassName, string parentDn)
    {
        ArgumentException.ThrowIfNullOrEmpty(objectClassName);
        ArgumentException.ThrowIfNullOrEmpty(parentDn);

        // One culture pin per entry: every Bogus call in the body (not just
        // formatter templates) then stringifies culture-invariantly.
        var originalCulture = PinInvariantCulture();
        try
        {
            return EntryCore(objectClassName, parentDn);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private LdifContentRecord EntryCore(string objectClassName, string parentDn)
    {
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

        return new LdifContentRecord($"{rdnAttribute}={Dn.EscapeValue(rdnValue)},{parentDn}", attributes);

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

    /// <summary>How many fresh draws to attempt on an RDN collision before falling back.</summary>
    private const int RdnRegenerationAttempts = 20;

    /// <summary>Syntaxes for which a "-n" uniqueness suffix stays lexically valid.</summary>
    private static readonly HashSet<string> SuffixSafeSyntaxes = new(StringComparer.Ordinal)
    {
        SyntaxPrefix + "15", // Directory String
        SyntaxPrefix + "26", // IA5 String
        SyntaxPrefix + "40", // Octet String
        SyntaxPrefix + "41", // Postal Address
        SyntaxPrefix + "44", // Printable String
        SyntaxPrefix + "50", // Telephone Number
    };

    private string UniqueRdnValue(string rdnAttribute, string parentDn)
    {
        string key = $"{parentDn}\n{rdnAttribute}";
        var used = _usedRdnValues.TryGetValue(key, out var set)
            ? set
            : _usedRdnValues[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string candidate = NextCandidate();
        if (used.Add(candidate))
            return candidate;

        // A fresh draw usually resolves a collision for random sources and keeps
        // the value inside its declared syntax, where a "-n" suffix would corrupt
        // e.g. an INTEGER uidNumber into a server-rejected "1000-2".
        for (int attempt = 0; attempt < RdnRegenerationAttempts; attempt++)
        {
            string retry = NextCandidate();
            if (used.Add(retry))
                return retry;
        }

        var (_, syntax) = ResolveSyntax(rdnAttribute);
        if (syntax is not null && !SuffixSafeSyntaxes.Contains(syntax))
        {
            throw new InvalidOperationException(
                $"Cannot generate a unique RDN value for '{rdnAttribute}': {RdnRegenerationAttempts} draws collided and a" +
                " uniqueness suffix would violate the attribute's declared syntax. Widen the formatter or example pool value space.");
        }

        // Resume from the last suffix minted for this candidate — restarting at 2
        // per entry is quadratic when a formatter yields a constant RDN value.
        string suffixKey = $"{key}\n{candidate}";
        int suffix = _nextRdnSuffix.TryGetValue(suffixKey, out int next) ? next : 2;
        string value;
        do
        {
            value = $"{candidate}-{suffix}";
            suffix++;
        }
        while (!used.Add(value));
        _nextRdnSuffix[suffixKey] = suffix;
        return value;

        string NextCandidate()
        {
            // GenerateValue never returns null for a required attribute.
            string generated = GenerateValue(rdnAttribute, parentDn, required: true)!.Value.AsString();
            // RFC 4514 can represent empty and whitespace-only RDN values, so the
            // writer's DN validation passes them — but a real server rejects them;
            // a control character (e.g. a newline from {{lorem.paragraphs}}) would
            // hide inside a base64-encoded dn line. Fail loud instead.
            if (string.IsNullOrWhiteSpace(generated) || generated.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    $"Generated RDN value for '{rdnAttribute}' is empty, whitespace-only, or contains control characters; configure the attribute's formatter or example pool to produce a printable value.");
            }
            return generated;
        }
    }

    private LdifValue? GenerateValue(string attributeName, string parentDn, bool required)
    {
        if (_formatters.TryGetValue(attributeName, out string? template))
            return LdifValue.FromString(ParseTemplate(_faker, template));

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
        return (definition is not null, definition is null ? null : _schema.ResolveSyntaxOid(definition));
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
        var timestamp = FakerFactory.GenerationEpoch
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
    /// values are drawn from the pool instead of being synthesized — unless a
    /// <see cref="Formatters"/> template exists for the attribute, which takes
    /// precedence. An empty or whitespace-only pool value drawn for the RDN
    /// attribute fails generation with <see cref="InvalidOperationException"/>.
    /// </summary>
    public IDictionary<string, IReadOnlyList<string>> ExampleValues { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-attribute value format templates (case-insensitive names), e.g.
    /// <c>"{{name.firstName}}.{{name.lastName}}@corp.example"</c> or
    /// <c>"EMP-{{randomizer.replacenumbers(#####)}}"</c>. Tokens use Bogus
    /// handlebars syntax (<c>{{dataset.method(args)}}</c>, case-insensitive);
    /// text outside tokens is emitted verbatim, except that literal
    /// <c>{{</c>/<c>}}</c> cannot be expressed. A matching formatter overrides
    /// all built-in generation for that attribute, including
    /// <see cref="ExampleValues"/>, and its output is not checked against the
    /// attribute's declared syntax — the template author owns validity. Tokens
    /// must return scalar values and are stringified with the invariant culture;
    /// they draw from the generator's seeded randomness (time tokens from a fixed
    /// epoch), so seeded output remains deterministic per package version
    /// regardless of machine culture. Malformed, non-scalar, and always-empty
    /// templates fail generator construction. The generator snapshots this
    /// dictionary at construction (later mutation has no effect) and applies a
    /// key naming a schema attribute to all of that attribute's names, so a
    /// formatter keyed <c>"surname"</c> also covers <c>sn</c>.
    /// </summary>
    public IDictionary<string, string> Formatters { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
