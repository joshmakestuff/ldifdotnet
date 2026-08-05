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

    [Theory]
    [InlineData("{{lorem.words}}", "String[]")]          // array
    [InlineData("{{finance.currency}}", "Currency")]     // object, non-System namespace (escaped the old sniff)
    [InlineData("{{system.exception}}", "Exception")]    // multi-line ToString (escaped the old sniff)
    public void Non_scalar_tokens_rejected_by_return_type(string template, string returnType)
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["cn"] = template;

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("cn", ex.Message);
        Assert.Contains(returnType, ex.Message);
    }

    [Fact]
    public void Literal_text_mentioning_type_names_is_allowed()
    {
        // The replaced output-sniffing guard false-positived on this template.
        var options = new SchemaGeneratorOptions { Seed = 6, OptionalAttributeFill = 1.0 };
        options.Formatters["description"] = "migrated from System.String[] `docs` {{lorem.word}}";
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.StartsWith("migrated from System.String[] `docs` ", entry["description"]!.Values[0].AsString());
    }

    [Fact]
    public void Always_empty_template_fails_construction()
    {
        var options = new SchemaGeneratorOptions { Seed = 1 };
        options.Formatters["cn"] = "{{lorem.letter(0)}}";   // scalar token, always renders ""

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("cn", ex.Message);
        Assert.Contains("empty", ex.Message);
    }

    [Theory]
    [InlineData("")]           // empty: RFC 4514 representable, servers reject
    [InlineData(" ")]          // whitespace-only: escapes to "cn=\ ", servers reject
    [InlineData("a\nb")]       // control char: would hide inside a base64-encoded dn line
    [InlineData("bad\0name")]  // NUL: escapable as \00 but still server-rejected
    public void Empty_whitespace_or_control_rdn_value_fails_loud(string poolValue)
    {
        var options = new SchemaGeneratorOptions { Seed = 1, RdnAttribute = "cn" };
        options.ExampleValues["cn"] = [poolValue];
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entry("person", ParentDn));
        Assert.Contains("cn", ex.Message);
    }

    [Fact]
    public void Formatter_mutation_after_construction_has_no_effect()
    {
        SchemaGeneratorOptions Options()
        {
            var options = new SchemaGeneratorOptions { Seed = 53, OptionalAttributeFill = 0.5 };
            options.Formatters["cn"] = "{{name.fullName}}";
            return options;
        }

        var mutated = Options();
        var generator = new SchemaEntryGenerator(CoreSchemas(), mutated);
        mutated.Formatters["cn"] = "{{no.suchThing}}";      // invalid — must not be read
        mutated.Formatters["sn"] = "{{lorem.words}}";       // non-scalar — must not be read
        string fromMutated = LdifWriter.WriteToString(generator.Entries("inetOrgPerson", 10, ParentDn));

        string fromClean = LdifWriter.WriteToString(
            new SchemaEntryGenerator(CoreSchemas(), Options()).Entries("inetOrgPerson", 10, ParentDn));

        Assert.Equal(fromClean, fromMutated);
    }

    [Fact]
    public void Formatter_alias_key_applies_to_canonical_attribute()
    {
        // core.schema defines 2.5.4.4 NAME ( 'sn' 'surname' ); person requires "sn".
        var options = new SchemaGeneratorOptions { Seed = 9, OptionalAttributeFill = 0 };
        options.Formatters["surname"] = "ALIASED {{name.lastName}}";
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entry = generator.Entry("person", ParentDn);

        Assert.StartsWith("ALIASED ", entry["sn"]!.Values[0].AsString());
    }

    [Fact]
    public void Conflicting_alias_formatters_fail_construction()
    {
        var options = new SchemaGeneratorOptions();
        options.Formatters["sn"] = "{{name.lastName}}";
        options.Formatters["surname"] = "{{name.firstName}}";

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("conflicting", ex.Message);
    }

    [Fact]
    public void Unusual_probe_exception_is_wrapped_naming_attribute()
    {
        // Bogus throws KeyNotFoundException for an unknown IBAN country code — a
        // reflection-dispatched surface the old four-type catch filter let escape.
        var options = new SchemaGeneratorOptions();
        options.Formatters["mail"] = "{{finance.iban(true,ZZ)}}";

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("mail", ex.Message);
        Assert.Contains("{{finance.iban(true,ZZ)}}", ex.Message);
    }

    [Fact]
    public void Literal_close_brace_fails_construction_naming_attribute()
    {
        // The Bogus tokenizer owns {{ and }}; a literal }} cannot be expressed and
        // must fail construction with the attribute named, not leak a raw error
        // quoting generated text.
        var options = new SchemaGeneratorOptions();
        options.Formatters["description"] = "closing }} brace {{lorem.word}}";

        var ex = Assert.Throws<ArgumentException>(() => new SchemaEntryGenerator(CoreSchemas(), options));
        Assert.Contains("description", ex.Message);
    }

    [Fact]
    public void Oid_referenced_attribute_uses_name_keyed_formatter()
    {
        // A legal object class may list an attribute by OID; the generated
        // attribute then carries the OID as its name, and a formatter keyed by
        // the attribute's name must still apply.
        var schema = LdapSchema.Parse(
            "attributetype ( 1.2.3.4.1 NAME 'cn' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n" +
            "attributetype ( 1.2.3.4.2 NAME 'empId' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n" +
            "objectclass ( 1.2.3.4.9 NAME 'oidThing' STRUCTURAL MUST ( cn $ 1.2.3.4.2 ) )\n");
        var options = new SchemaGeneratorOptions { Seed = 4, RdnAttribute = "cn" };
        options.Formatters["empId"] = "EMP-{{randomizer.replacenumbers(####)}}";
        var generator = new SchemaEntryGenerator(schema, options);

        var entry = generator.Entry("oidThing", ParentDn);

        Assert.Matches("^EMP-[0-9]{4}$", entry["1.2.3.4.2"]!.Values[0].AsString());
    }

    [Theory]
    [InlineData("{{date.pastdateonly}}", "^[0-9]{2}/[0-9]{2}/[0-9]{4}$")]     // DateOnly, invariant culture
    [InlineData("{{internet.ipaddress}}", "^[0-9.]+$")]                        // IPAddress
    [InlineData("{{system.version}}", "^[0-9.]+$")]                            // Version
    public void Value_type_tokens_are_accepted(string template, string pattern)
    {
        var options = new SchemaGeneratorOptions { Seed = 12, OptionalAttributeFill = 1.0 };
        options.Formatters["description"] = template;
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entry = generator.Entry("inetOrgPerson", ParentDn);

        Assert.Matches(pattern, entry["description"]!.Values[0].AsString());
    }

    [Fact]
    public void Numeric_rdn_collision_fails_instead_of_corrupting()
    {
        // A "-2" suffix on a declared-INTEGER RDN would be a server-rejected
        // value; with a constant formatter no regeneration can help, so
        // generation fails. (Only a declared syntax can trigger this — attributes
        // slapd hardcodes outside schema files, like uidNumber in the stock
        // fixtures, have unknowable syntax and keep the suffix fallback.)
        var schema = LdapSchema.Parse(
            "attributetype ( 1.2.3.5.1 NAME 'cn' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n" +
            "attributetype ( 1.2.3.5.2 NAME 'memberCount' EQUALITY integerMatch SYNTAX 1.3.6.1.4.1.1466.115.121.1.27 )\n" +
            "objectclass ( 1.2.3.5.9 NAME 'intThing' STRUCTURAL MUST ( cn $ memberCount ) )\n");
        var options = new SchemaGeneratorOptions { Seed = 3, RdnAttribute = "memberCount", OptionalAttributeFill = 0 };
        options.Formatters["memberCount"] = "1000";
        var generator = new SchemaEntryGenerator(schema, options);

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Entries("intThing", 2, ParentDn));
        Assert.Contains("memberCount", ex.Message);
    }

    [Fact]
    public void Rdn_collision_regenerates_syntax_valid_values()
    {
        // A random numeric RDN formatter with a small value space must resolve
        // collisions by drawing again — never by appending a "-n" suffix.
        var options = new SchemaGeneratorOptions { Seed = 14, RdnAttribute = "uidNumber", OptionalAttributeFill = 0 };
        options.AuxiliaryClasses.Add("posixAccount");
        options.Formatters["uidNumber"] = "{{randomizer.number(1,5)}}";
        var generator = new SchemaEntryGenerator(CoreSchemas("schemas/contrib/rfc2307bis.schema"), options);

        var entries = generator.Entries("account", 4, ParentDn);

        var values = entries.Select(e => e.Dn.Split(',')[0]["uidNumber=".Length..]).ToList();
        Assert.All(values, v => Assert.Matches("^[0-9]+$", v));
        Assert.Equal(4, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Constant_rdn_formatter_suffix_sequence_is_preserved()
    {
        var options = new SchemaGeneratorOptions { Seed = 2, RdnAttribute = "cn", OptionalAttributeFill = 0 };
        options.Formatters["cn"] = "Fixed Name";
        var generator = new SchemaEntryGenerator(CoreSchemas(), options);

        var entries = generator.Entries("person", 500, ParentDn);

        Assert.Equal($"cn=Fixed Name,{ParentDn}", entries[0].Dn);
        Assert.Equal($"cn=Fixed Name-2,{ParentDn}", entries[1].Dn);
        Assert.Equal($"cn=Fixed Name-500,{ParentDn}", entries[499].Dn);
        Assert.Equal(500, entries.Select(e => e.Dn).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact(Skip = "Known defect — see #65. Unskip with the system-schema syntax fallback.")]
    public void Dn_valued_attributes_are_parseable_dns()
    {
        // member/owner declare SUP distinguishedName, whose definition lives in
        // slapd's system schema and is commented out of core.schema. The SUP chain
        // yields no syntax, so generation falls through to free text and emits
        // values slapadd rejects.
        var generator = new SchemaEntryGenerator(CoreSchemas(), new SchemaGeneratorOptions { Seed = 7, OptionalAttributeFill = 1.0 });

        var entry = generator.Entry("groupOfNames", "ou=groups,dc=example,dc=com");

        foreach (string attribute in (string[])["member", "owner"])
        {
            foreach (var value in entry[attribute]?.Values ?? [])
                Assert.Null(Record.Exception(() => Dn.Parse(value.AsString())));
        }
    }
}
