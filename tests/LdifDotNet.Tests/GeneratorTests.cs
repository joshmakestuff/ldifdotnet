using LdifDotNet.Generator;

namespace LdifDotNet.Tests;

public class GeneratorTests
{
    private static LdifGeneratorOptions SmallOptions(int seed = 1234) => new()
    {
        Seed = seed,
        PeopleCount = 20,
        GroupCount = 5,
    };

    [Fact]
    public void Same_seed_produces_identical_output()
    {
        string first = LdifWriter.WriteToString(new LdifGenerator(SmallOptions()).SampleDirectory());
        string second = LdifWriter.WriteToString(new LdifGenerator(SmallOptions()).SampleDirectory());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_produce_different_output()
    {
        string first = LdifWriter.WriteToString(new LdifGenerator(SmallOptions(seed: 1)).SampleDirectory());
        string second = LdifWriter.WriteToString(new LdifGenerator(SmallOptions(seed: 2)).SampleDirectory());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sample_directory_is_a_loadable_tree()
    {
        var records = new LdifGenerator(SmallOptions()).SampleDirectory();

        // base + 2 OUs + people + groups
        Assert.Equal(1 + 2 + 20 + 5, records.Count);

        var dns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            Assert.True(dns.Add(record.Dn), $"duplicate DN: {record.Dn}");

        // Parent-before-child order: every entry's parent must already be present
        // (the base entry's parent is outside the generated tree).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { records[0].Dn };
        foreach (var record in records.Skip(1))
        {
            string parent = ParentDn(record.Dn);
            Assert.True(seen.Contains(parent), $"entry '{record.Dn}' generated before its parent '{parent}'");
            seen.Add(record.Dn);
        }
    }

    [Fact]
    public void Group_members_reference_generated_people()
    {
        var records = new LdifGenerator(SmallOptions()).SampleDirectory();
        var peopleDns = records
            .Where(r => r.Dn.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Dn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = records.Where(r => r["objectClass"]!.Values.Any(v => v.AsString() == "groupOfNames")).ToList();

        Assert.Equal(5, groups.Count);
        foreach (var group in groups)
        {
            var members = group["member"]!.Values;
            Assert.NotEmpty(members);
            Assert.All(members, m => Assert.Contains(m.AsString(), peopleDns));
        }
    }

    [Fact]
    public void Dangling_ratio_zero_leaves_the_seeded_stream_untouched()
    {
        // The dangling draw is skipped entirely at 0, so output matches a generator
        // built before the option existed. Guards against a silent reroll for consumers
        // pinning a seed.
        var withoutOption = SmallOptions();
        var explicitZero = SmallOptions();
        explicitZero.DanglingMemberRatio = 0;

        Assert.Equal(
            LdifWriter.WriteToString(new LdifGenerator(withoutOption).SampleDirectory()),
            LdifWriter.WriteToString(new LdifGenerator(explicitZero).SampleDirectory()));
    }

    [Fact]
    public void Full_dangling_ratio_makes_every_member_unresolvable()
    {
        var options = SmallOptions();
        options.DanglingMemberRatio = 1.0;
        var records = new LdifGenerator(options).SampleDirectory();
        var existingDns = records.Select(r => r.Dn).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var members = GroupMembers(records);

        Assert.NotEmpty(members);
        foreach (string member in members)
        {
            Assert.DoesNotContain(member, existingDns);
            Assert.EndsWith("ou=people,dc=example,dc=com", member, StringComparison.OrdinalIgnoreCase);
            Assert.Null(Record.Exception(() => Dn.Parse(member)));   // dangling, but still a valid DN
        }
    }

    [Fact]
    public void Partial_dangling_ratio_mixes_resolvable_and_dangling_members()
    {
        var options = SmallOptions();
        options.DanglingMemberRatio = 0.5;
        options.GroupCount = 30;
        var records = new LdifGenerator(options).SampleDirectory();
        var existingDns = records.Select(r => r.Dn).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var members = GroupMembers(records);

        Assert.Contains(members, m => existingDns.Contains(m));
        Assert.Contains(members, m => !existingDns.Contains(m));
    }

    [Fact]
    public void Dangling_dns_stay_dangling_when_more_people_are_generated_later()
    {
        // Dangling uids are reserved in the same pool Person draws from; a person
        // generated afterwards must never make a dangling reference resolve.
        var options = SmallOptions();
        options.DanglingMemberRatio = 1.0;
        var generator = new LdifGenerator(options);
        const string PeopleDn = "ou=people,dc=example,dc=com";

        var people = generator.People(20, PeopleDn);
        var groups = generator.Groups(5, "ou=groups,dc=example,dc=com", people);
        var latecomers = generator.People(200, PeopleDn);

        var members = GroupMembers(groups).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(latecomers, p => Assert.DoesNotContain(p.Dn, members));
    }

    [Fact]
    public void Dangling_ratio_is_deterministic_under_seed()
    {
        static LdifGeneratorOptions Options()
        {
            var options = SmallOptions();
            options.DanglingMemberRatio = 0.4;
            return options;
        }

        Assert.Equal(
            LdifWriter.WriteToString(new LdifGenerator(Options()).SampleDirectory()),
            LdifWriter.WriteToString(new LdifGenerator(Options()).SampleDirectory()));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Dangling_ratio_outside_zero_to_one_throws(double ratio)
    {
        var options = SmallOptions();
        options.DanglingMemberRatio = ratio;

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new LdifGenerator(options));
        Assert.Contains("DanglingMemberRatio", ex.Message, StringComparison.Ordinal);
    }

    private static List<string> GroupMembers(IEnumerable<LdifContentRecord> records) =>
        records
            .Where(r => r["objectClass"]!.Values.Any(v => v.AsString() == "groupOfNames"))
            .SelectMany(r => r["member"]!.Values.Select(v => v.AsString()))
            .ToList();

    [Fact]
    public void People_have_core_inetorgperson_attributes()
    {
        var person = new LdifGenerator(SmallOptions()).Person("ou=people,dc=example,dc=com");

        Assert.StartsWith("uid=", person.Dn);
        Assert.Contains("inetOrgPerson", person["objectClass"]!.Values.Select(v => v.AsString()));
        foreach (string required in new[] { "uid", "cn", "sn", "givenName", "mail", "telephoneNumber" })
            Assert.NotNull(person[required]);
        Assert.EndsWith("@example.com", person["mail"]!.Values[0].AsString());
    }

    [Fact]
    public void Generated_uids_are_unique()
    {
        var generator = new LdifGenerator(SmallOptions());
        var people = generator.People(500, "ou=people,dc=example,dc=com");

        var uids = people.Select(p => p["uid"]!.Values[0].AsString()).ToList();
        Assert.Equal(uids.Count, uids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Sample_directory_round_trips_through_ldif()
    {
        var records = new LdifGenerator(SmallOptions()).SampleDirectory();

        string ldif = LdifWriter.WriteToString(records);
        var reparsed = LdifReader.Parse(ldif);

        Assert.Equal(records.Count, reparsed.Count);
        for (int i = 0; i < records.Count; i++)
            Assert.Equal(records[i].Dn, reparsed[i].Dn);
    }

    [Fact]
    public void Base_entry_handles_escaped_base_dn()
    {
        var options = new LdifGeneratorOptions { Seed = 1, BaseDn = @"o=Example\, Inc,dc=com" };
        var baseEntry = new LdifGenerator(options).SampleDirectory()[0];

        Assert.Equal(@"o=Example\, Inc,dc=com", baseEntry.Dn);
        // The o attribute value is the unescaped name — it must match the DN's RDN.
        Assert.Equal("Example, Inc", baseEntry["o"]!.Values[0].AsString());
    }

    [Fact]
    public void Multivalued_first_rdn_base_dn_throws()
    {
        var generator = new LdifGenerator(new LdifGeneratorOptions { Seed = 1, BaseDn = "dc=a+dc=b,dc=com" });

        Assert.Throws<InvalidOperationException>(() => generator.SampleDirectory());
    }

    [Fact]
    public void Malformed_base_dn_throws_at_construction() =>
        Assert.Throws<ArgumentException>(() => new LdifGenerator(new LdifGeneratorOptions { BaseDn = "garbage" }));

    private static string ParentDn(string dn)
    {
        for (int i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\')
            {
                i++;
                continue;
            }
            if (dn[i] == ',')
                return dn[(i + 1)..].TrimStart(' ');
        }
        return "";
    }
}
