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
