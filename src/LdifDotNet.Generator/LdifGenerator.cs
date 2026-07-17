using Bogus;

namespace LdifDotNet.Generator;

/// <summary>
/// Generates realistic fake directory data as LDIF records, powered by Bogus.
/// Deterministic when <see cref="LdifGeneratorOptions.Seed"/> is set.
/// </summary>
public sealed class LdifGenerator
{
    private readonly LdifGeneratorOptions _options;
    private readonly Faker _faker;
    private readonly string _mailDomain;
    private readonly HashSet<string> _usedUids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedGroupNames = new(StringComparer.OrdinalIgnoreCase);

    public LdifGenerator(LdifGeneratorOptions? options = null)
    {
        _options = options ?? new LdifGeneratorOptions();
        _faker = new Faker(_options.Locale);
        if (_options.Seed is { } seed)
            _faker.Random = new Randomizer(seed);

        var dcComponents = _options.BaseDn
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.StartsWith("dc=", StringComparison.OrdinalIgnoreCase))
            .Select(part => part[3..])
            .ToList();
        _mailDomain = dcComponents.Count > 0
            ? string.Join('.', dcComponents)
            : _faker.Internet.DomainName();
    }

    /// <summary>
    /// Generates a complete loadable tree in parent-before-child order: the base
    /// entry, ou=people and ou=groups, then people and groups per the options.
    /// </summary>
    public IReadOnlyList<LdifContentRecord> SampleDirectory()
    {
        string peopleDn = $"ou=people,{_options.BaseDn}";
        string groupsDn = $"ou=groups,{_options.BaseDn}";

        var records = new List<LdifContentRecord>
        {
            BaseEntry(),
            OrganizationalUnit("people", _options.BaseDn),
            OrganizationalUnit("groups", _options.BaseDn),
        };
        var people = People(_options.PeopleCount, peopleDn);
        records.AddRange(people);
        records.AddRange(Groups(_options.GroupCount, groupsDn, people));
        return records;
    }

    /// <summary>Generates an inetOrgPerson entry under <paramref name="parentDn"/>.</summary>
    public LdifContentRecord Person(string parentDn)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentDn);

        string givenName = _faker.Name.FirstName();
        string surname = _faker.Name.LastName();
        string commonName = $"{givenName} {surname}";
        string uid = UniqueUid(givenName, surname);

        return new LdifContentRecord(
            $"uid={Rdn.Escape(uid)},{parentDn}",
            new LdifAttribute("objectClass", "top", "person", "organizationalPerson", "inetOrgPerson"),
            new LdifAttribute("uid", uid),
            new LdifAttribute("cn", commonName),
            new LdifAttribute("sn", surname),
            new LdifAttribute("givenName", givenName),
            new LdifAttribute("displayName", commonName),
            new LdifAttribute("mail", $"{uid}@{_mailDomain}"),
            new LdifAttribute("telephoneNumber", _faker.Phone.PhoneNumber()),
            new LdifAttribute("title", _faker.Name.JobTitle()),
            new LdifAttribute("employeeNumber", _faker.Random.ReplaceNumbers("######")),
            new LdifAttribute("l", _faker.Address.City()));
    }

    /// <summary>Generates <paramref name="count"/> person entries under <paramref name="parentDn"/>.</summary>
    public IReadOnlyList<LdifContentRecord> People(int count, string parentDn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var people = new List<LdifContentRecord>(count);
        for (int i = 0; i < count; i++)
            people.Add(Person(parentDn));
        return people;
    }

    /// <summary>
    /// Generates a groupOfNames entry under <paramref name="parentDn"/> whose members
    /// are randomly chosen from <paramref name="memberPool"/> (at least one; groupOfNames
    /// requires a member).
    /// </summary>
    public LdifContentRecord Group(string parentDn, IReadOnlyList<LdifContentRecord> memberPool)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentDn);
        ArgumentNullException.ThrowIfNull(memberPool);
        if (memberPool.Count == 0)
            throw new ArgumentException("groupOfNames requires at least one member; the pool is empty.", nameof(memberPool));

        string name = UniqueGroupName();
        int memberCount = _faker.Random.Int(1, Math.Min(memberPool.Count, 12));
        var members = _faker.PickRandom(memberPool, memberCount);

        return new LdifContentRecord(
            $"cn={Rdn.Escape(name)},{parentDn}",
            new LdifAttribute("objectClass", "top", "groupOfNames"),
            new LdifAttribute("cn", name),
            new LdifAttribute("description", _faker.Company.CatchPhrase()),
            new LdifAttribute("member", members.Select(m => LdifValue.FromString(m.Dn))));
    }

    /// <summary>Generates <paramref name="count"/> group entries under <paramref name="parentDn"/>.</summary>
    public IReadOnlyList<LdifContentRecord> Groups(int count, string parentDn, IReadOnlyList<LdifContentRecord> memberPool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var groups = new List<LdifContentRecord>(count);
        for (int i = 0; i < count; i++)
            groups.Add(Group(parentDn, memberPool));
        return groups;
    }

    private LdifContentRecord BaseEntry()
    {
        string baseDn = _options.BaseDn;
        int comma = baseDn.IndexOf(',', StringComparison.Ordinal);
        string firstRdn = comma < 0 ? baseDn : baseDn[..comma];
        int equals = firstRdn.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
            throw new InvalidOperationException($"Base DN '{baseDn}' does not start with a valid RDN.");
        string attribute = firstRdn[..equals].Trim();
        string value = firstRdn[(equals + 1)..].Trim();

        return attribute.ToLowerInvariant() switch
        {
            "dc" => new LdifContentRecord(
                baseDn,
                new LdifAttribute("objectClass", "top", "dcObject", "organization"),
                new LdifAttribute("dc", value),
                new LdifAttribute("o", _faker.Company.CompanyName())),
            "o" => new LdifContentRecord(
                baseDn,
                new LdifAttribute("objectClass", "top", "organization"),
                new LdifAttribute("o", value)),
            "ou" => new LdifContentRecord(
                baseDn,
                new LdifAttribute("objectClass", "top", "organizationalUnit"),
                new LdifAttribute("ou", value)),
            _ => throw new InvalidOperationException($"Base DN must start with dc=, o= or ou=; got '{attribute}='."),
        };
    }

    private static LdifContentRecord OrganizationalUnit(string name, string parentDn) =>
        new(
            $"ou={Rdn.Escape(name)},{parentDn}",
            new LdifAttribute("objectClass", "top", "organizationalUnit"),
            new LdifAttribute("ou", name));

    private string UniqueUid(string givenName, string surname)
    {
        string candidate = Sanitize(_faker.Internet.UserName(givenName, surname).ToLowerInvariant());
        if (candidate.Length == 0)
            candidate = "user";
        string uid = candidate;
        for (int suffix = 2; !_usedUids.Add(uid); suffix++)
            uid = $"{candidate}{suffix}";
        return uid;

        static string Sanitize(string value) =>
            new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
    }

    private string UniqueGroupName()
    {
        string candidate = _faker.Commerce.Department(1);
        string name = candidate;
        for (int suffix = 2; !_usedGroupNames.Add(name); suffix++)
            name = $"{candidate} {suffix}";
        return name;
    }
}
