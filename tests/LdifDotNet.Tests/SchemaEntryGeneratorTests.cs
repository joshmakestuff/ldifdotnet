using LdifDotNet.Generator;
using LdifDotNet.Schema;

namespace LdifDotNet.Tests;

public class SchemaEntryGeneratorTests
{
    private const string ParentDn = "ou=people,dc=example,dc=com";

    private static LdapSchema CoreSchemas(params string[] extra) =>
        LdapSchema.Load([
            Fixtures.PathOf("schemas/openldap/core.schema"),
            Fixtures.PathOf("schemas/openldap/cosine.schema"),
            Fixtures.PathOf("schemas/openldap/inetorgperson.schema"),
            .. extra.Select(e => Fixtures.PathOf(e)),
        ]);

    [Fact]
    public void Same_seed_produces_identical_output()
    {
        var schema = CoreSchemas("schemas/contrib/eduperson.schema");
        SchemaGeneratorOptions Options()
        {
            var options = new SchemaGeneratorOptions { Seed = 99, OptionalAttributeFill = 0.5 };
            options.AuxiliaryClasses.Add("eduPerson");
            return options;
        }

        string first = LdifWriter.WriteToString(
            new SchemaEntryGenerator(schema, Options()).Entries("inetOrgPerson", 20, ParentDn));
        string second = LdifWriter.WriteToString(
            new SchemaEntryGenerator(schema, Options()).Entries("inetOrgPerson", 20, ParentDn));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Inetorgperson_entries_have_chain_and_required_attributes()
    {
        var generator = new SchemaEntryGenerator(CoreSchemas(), new SchemaGeneratorOptions { Seed = 7 });
        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.StartsWith("uid=", entry.Dn);   // uid preferred for the RDN
        var objectClasses = entry["objectClass"]!.Values.Select(v => v.AsString()).ToList();
        Assert.Equal(["top", "person", "organizationalPerson", "inetOrgPerson"], objectClasses);

        // MUST of person, inherited through the chain — even though cn/sn syntax
        // lives in slapd's hardcoded system schema.
        Assert.NotNull(entry["cn"]);
        Assert.NotNull(entry["sn"]);
    }

    [Fact]
    public void Zero_fill_generates_only_required_attributes()
    {
        var generator = new SchemaEntryGenerator(
            CoreSchemas(), new SchemaGeneratorOptions { Seed = 7, OptionalAttributeFill = 0 });
        var entry = generator.Entry("person", "dc=example,dc=com");

        var names = entry.Attributes.Select(a => a.Name).ToList();
        Assert.Equal(["objectClass", "cn", "sn"], names);
    }

    [Fact]
    public void Full_fill_includes_auxiliary_may_attributes()
    {
        var options = new SchemaGeneratorOptions { Seed = 11, OptionalAttributeFill = 1.0 };
        options.AuxiliaryClasses.Add("eduPerson");
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/eduperson.schema"), options);

        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.Contains("eduPerson", entry["objectClass"]!.Values.Select(v => v.AsString()));
        Assert.NotNull(entry["eduPersonAffiliation"]);
        Assert.Single(entry["eduPersonPrincipalName"]!.Values);   // SINGLE-VALUE respected
    }

    [Fact]
    public void Example_value_pools_steer_generation()
    {
        var options = new SchemaGeneratorOptions { Seed = 3, OptionalAttributeFill = 1.0 };
        options.AuxiliaryClasses.Add("eduPerson");
        options.ExampleValues["eduPersonAffiliation"] = ["faculty", "student", "staff"];
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/eduperson.schema"), options);

        var entries = generator.Entries("inetOrgPerson", 25, ParentDn);

        var affiliations = entries
            .Select(e => e["eduPersonAffiliation"]!.Values[0].AsString())
            .Distinct()
            .ToList();
        Assert.All(affiliations, a => Assert.Contains(a, (string[])["faculty", "student", "staff"]));
        Assert.True(affiliations.Count > 1, "expected the pool to be sampled, not a single value");
    }

    [Fact]
    public void Posix_account_attributes_are_syntax_valid()
    {
        var options = new SchemaGeneratorOptions { Seed = 5 };
        options.AuxiliaryClasses.Add("posixAccount");
        var generator = new SchemaEntryGenerator(
            CoreSchemas("schemas/contrib/rfc2307bis.schema"), options);

        var entry = generator.Entry("account", ParentDn);

        Assert.StartsWith("uid=", entry.Dn);
        Assert.True(int.TryParse(entry["uidNumber"]!.Values[0].AsString(), out _), "uidNumber must be an integer");
        Assert.True(int.TryParse(entry["gidNumber"]!.Values[0].AsString(), out _), "gidNumber must be an integer");
        Assert.StartsWith("/", entry["homeDirectory"]!.Values[0].AsString());
    }

    [Fact]
    public void Rdn_values_are_unique_per_parent()
    {
        var generator = new SchemaEntryGenerator(CoreSchemas(), new SchemaGeneratorOptions { Seed = 13 });
        var entries = generator.Entries("inetOrgPerson", 300, ParentDn);

        Assert.Equal(entries.Count, entries.Select(e => e.Dn).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Entries_round_trip_through_ldif()
    {
        var options = new SchemaGeneratorOptions { Seed = 17, OptionalAttributeFill = 1.0 };
        options.AuxiliaryClasses.Add("eduPerson");
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/eduperson.schema"), options);
        var entries = generator.Entries("inetOrgPerson", 10, ParentDn);

        var reparsed = LdifReader.Parse(LdifWriter.WriteToString(entries));

        Assert.Equal(entries.Count, reparsed.Count);
        for (int i = 0; i < entries.Count; i++)
            Assert.Equal(entries[i].Dn, reparsed[i].Dn);
    }

    [Fact]
    public void Unknown_object_class_throws()
    {
        var generator = new SchemaEntryGenerator(CoreSchemas());
        Assert.Throws<ArgumentException>(() => generator.Entry("noSuchClass", ParentDn));
    }
}
