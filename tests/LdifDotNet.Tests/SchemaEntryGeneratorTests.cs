using System.Globalization;
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
            .Distinct(StringComparer.Ordinal)
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
        Assert.True(
            int.TryParse(entry["uidNumber"]!.Values[0].AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out _),
            "uidNumber must be an integer");
        Assert.True(
            int.TryParse(entry["gidNumber"]!.Values[0].AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out _),
            "gidNumber must be an integer");
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

    [Fact]
    public void Non_structural_primary_class_throws()
    {
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/rfc2307bis.schema"));

        var ex = Assert.Throws<ArgumentException>(() => generator.Entry("posixAccount", ParentDn));
        Assert.Contains("structural", ex.Message);
    }

    [Fact]
    public void Non_auxiliary_class_in_auxiliary_list_throws()
    {
        var options = new SchemaGeneratorOptions { Seed = 1 };
        options.AuxiliaryClasses.Add("person"); // structural, not auxiliary
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entry("inetOrgPerson", ParentDn));
        Assert.Contains("auxiliary", ex.Message);
    }

    [Fact]
    public void Rdn_attribute_must_be_allowed_by_selected_classes()
    {
        var options = new SchemaGeneratorOptions { Seed = 1, RdnAttribute = "uidNumber" };
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entry("person", ParentDn));
        Assert.Contains("uidNumber", ex.Message);
    }

    [Fact]
    public void Heuristic_yields_to_incompatible_declared_syntax()
    {
        // A custom schema reuses the well-known name "description" with INTEGER
        // syntax; the free-text heuristic must not win over the declared syntax.
        var schema = LdapSchema.Parse(
            "attributetype ( 1.2.3.4.1 NAME 'cn' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n" +
            "attributetype ( 1.2.3.4.2 NAME 'description' EQUALITY integerMatch SYNTAX 1.3.6.1.4.1.1466.115.121.1.27 )\n" +
            "objectclass ( 1.2.3.4.3 NAME 'testThing' STRUCTURAL MUST ( cn $ description ) )\n");
        var generator = new SchemaEntryGenerator(schema, new SchemaGeneratorOptions { Seed = 21 });

        var entry = generator.Entry("testThing", ParentDn);

        Assert.True(
            int.TryParse(entry["description"]!.Values[0].AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out _),
            "description with INTEGER syntax must get an integer value, not the free-text heuristic");
    }

    [Fact]
    public void Formatter_overrides_pool_and_heuristic()
    {
        var options = new SchemaGeneratorOptions { Seed = 23, OptionalAttributeFill = 1.0 };
        options.AuxiliaryClasses.Add("eduPerson");
        options.ExampleValues["eduPersonAffiliation"] = ["faculty"];
        options.Formatters["eduPersonAffiliation"] = "affiliate-{{randomizer.replacenumbers(##)}}";
        options.Formatters["cn"] = "{{name.lastName}} (generated)";
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/eduperson.schema"), options);

        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.Matches("^affiliate-[0-9]{2}$", entry["eduPersonAffiliation"]!.Values[0].AsString());
        Assert.EndsWith(" (generated)", entry["cn"]!.Values[0].AsString());
    }

    [Fact]
    public void Formatter_output_is_not_syntax_gated()
    {
        var options = new SchemaGeneratorOptions { Seed = 5 };
        options.AuxiliaryClasses.Add("posixAccount");
        options.Formatters["uidNumber"] = "not-a-number";
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/rfc2307bis.schema"), options);

        var entry = generator.Entry("account", ParentDn);

        // The template author owns validity; INTEGER syntax must not veto the value.
        Assert.Equal("not-a-number", entry["uidNumber"]!.Values[0].AsString());
    }

    [Fact]
    public void Formatter_applies_to_rdn_with_uniqueness_suffix()
    {
        var options = new SchemaGeneratorOptions { Seed = 2, RdnAttribute = "cn", OptionalAttributeFill = 0 };
        options.Formatters["CN"] = "Fixed Name";   // key case differs from the attribute on purpose

        var entries = new SchemaEntryGenerator(CoreSchemas(), options).Entries("person", 3, ParentDn);

        Assert.Equal($"cn=Fixed Name,{ParentDn}", entries[0].Dn);
        Assert.Equal($"cn=Fixed Name-2,{ParentDn}", entries[1].Dn);
        Assert.Equal($"cn=Fixed Name-3,{ParentDn}", entries[2].Dn);
    }

    [Fact]
    public void Formatter_rdn_value_is_dn_escaped()
    {
        var options = new SchemaGeneratorOptions { Seed = 2, RdnAttribute = "cn", OptionalAttributeFill = 0 };
        options.Formatters["cn"] = "Doe, John";

        var entry = new SchemaEntryGenerator(CoreSchemas(), options).Entry("person", ParentDn);

        Assert.StartsWith("cn=Doe\\, John,", entry.Dn);
    }

    [Fact]
    public void Pattern_tokens_generate_and_literal_hash_is_preserved()
    {
        var options = new SchemaGeneratorOptions { Seed = 19, OptionalAttributeFill = 1.0 };
        options.Formatters["employeeNumber"] = "EMP#{{randomizer.replacenumbers(#####)}}";
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.Matches("^EMP#[0-9]{5}$", entry["employeeNumber"]!.Values[0].AsString());
    }

    [Fact]
    public void Formatters_preserve_seeded_determinism_including_date_tokens()
    {
        SchemaGeneratorOptions Options()
        {
            var options = new SchemaGeneratorOptions { Seed = 31, OptionalAttributeFill = 0.5 };
            options.Formatters["mail"] = "{{name.firstName}}.{{name.lastName}}@corp.example";
            options.Formatters["description"] = "hired {{date.past}}";
            return options;
        }

        string first = LdifWriter.WriteToString(
            new SchemaEntryGenerator(CoreSchemas(), Options()).Entries("inetOrgPerson", 20, ParentDn));
        string second = LdifWriter.WriteToString(
            new SchemaEntryGenerator(CoreSchemas(), Options()).Entries("inetOrgPerson", 20, ParentDn));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Date_tokens_derive_from_fixed_epoch_not_the_clock()
    {
        var options = new SchemaGeneratorOptions { Seed = 8, OptionalAttributeFill = 0 };
        options.Formatters["sn"] = "{{date.past}}";
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        string value = generator.Entry("person", ParentDn)["sn"]!.Values[0].AsString();

        // Within a year before the pinned 2000-01-01 epoch — never today-relative.
        Assert.Matches("1999|2000", value);
    }

    [Fact]
    public void Unknown_formatter_token_fails_construction()
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["cn"] = "{{no.suchThing}}";

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("cn", ex.Message);
        Assert.Contains("{{no.suchThing}}", ex.Message);
    }

    [Fact]
    public void Unclosed_formatter_token_fails_construction()
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["cn"] = "{{name.lastName";

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("cn", ex.Message);
    }

    [Fact]
    public void Empty_formatter_template_fails_construction()
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["cn"] = "";

        Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
    }

    [Fact]
    public void Validating_extra_formatters_does_not_perturb_generation()
    {
        SchemaGeneratorOptions Options(bool extra)
        {
            var options = new SchemaGeneratorOptions { Seed = 41, OptionalAttributeFill = 0 };
            options.Formatters["cn"] = "{{name.fullName}}";
            if (extra)
                options.Formatters["seeAlso"] = "{{name.lastName}}"; // validated but never generated at fill 0
            return options;
        }

        string without = LdifWriter.WriteToString(
            new SchemaEntryGenerator(CoreSchemas(), Options(extra: false)).Entries("person", 10, ParentDn));
        string with = LdifWriter.WriteToString(
            new SchemaEntryGenerator(CoreSchemas(), Options(extra: true)).Entries("person", 10, ParentDn));

        Assert.Equal(without, with);
    }

    [Fact]
    public void Seeded_formatter_output_is_culture_invariant()
    {
        SchemaGeneratorOptions Options()
        {
            var options = new SchemaGeneratorOptions { Seed = 47, OptionalAttributeFill = 1.0 };
            options.Formatters["description"] = "hired {{date.past}} balance {{finance.amount}}";
            return options;
        }

        string RunUnder(string cultureName)
        {
            var original = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            try
            {
                return LdifWriter.WriteToString(
                    new SchemaEntryGenerator(CoreSchemas(), Options()).Entries("inetOrgPerson", 5, ParentDn));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // ar-SA formats dates in the Umm al-Qura calendar and uses U+066B as the
        // decimal separator — the strongest counterexample if stringification ever
        // reverts to the current culture.
        Assert.Equal(RunUnder("en-US"), RunUnder("ar-SA"));
    }

    [Fact]
    public void Non_scalar_formatter_token_fails_construction()
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["cn"] = "{{lorem.words}}";   // string[] — stringifies as "System.String[]"

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("cn", ex.Message);
        Assert.Contains("non-scalar", ex.Message);
    }

    [Fact]
    public void Empty_formatter_rdn_value_fails_loud()
    {
        var options = new SchemaGeneratorOptions { Seed = 1, RdnAttribute = "cn" };
        options.Formatters["cn"] = "{{lorem.letter(0)}}";   // validates fine, always produces ""
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entry("person", ParentDn));
        Assert.Contains("cn", ex.Message);
    }

    [Fact]
    public void Empty_pool_rdn_value_fails_loud()
    {
        var options = new SchemaGeneratorOptions { Seed = 1, RdnAttribute = "cn" };
        options.ExampleValues["cn"] = [""];
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entry("person", ParentDn));
        Assert.Contains("cn", ex.Message);
    }

    [Fact]
    public void Nul_in_rdn_value_is_hex_escaped()
    {
        var options = new SchemaGeneratorOptions { Seed = 1, RdnAttribute = "cn" };
        options.ExampleValues["cn"] = ["bad\0name"];
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entry = generator.Entry("person", ParentDn);

        Assert.StartsWith("cn=bad\\00name,", entry.Dn);
        Assert.DoesNotContain('\0', entry.Dn);
    }
}
