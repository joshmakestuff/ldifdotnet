using LdifDotNet.Schema;

namespace LdifDotNet.Tests;

/// <summary>
/// Per-definition parsing (LdapAttributeType.Parse / LdapObjectClass.Parse) and
/// the lenient LdapSchema.ParseSubschema aggregate for subschema-subentry input.
/// Sample definitions are verbatim from what OpenLDAP 2.6 publishes in
/// cn=Subschema, including shapes schema files never contain (cn published with
/// SUP and no SYNTAX; bounded syntax OIDs like {32768}).
/// </summary>
public class SubschemaParseTests
{
    private const string NameDefinition =
        "( 2.5.4.41 NAME 'name' DESC 'RFC4519: common supertype of name attributes' "
        + "EQUALITY caseIgnoreMatch SUBSTR caseIgnoreSubstringsMatch "
        + "SYNTAX 1.3.6.1.4.1.1466.115.121.1.15{32768} )";

    private const string CnDefinition =
        "( 2.5.4.3 NAME ( 'cn' 'commonName' ) "
        + "DESC 'RFC4519: common name(s) for which the entity is known by' SUP name )";

    private const string TopDefinition =
        "( 2.5.6.0 NAME 'top' DESC 'top of the superclass chain' ABSTRACT MUST objectClass )";

    private const string PersonDefinition =
        "( 2.5.6.6 NAME 'person' DESC 'RFC2256: a person' SUP top STRUCTURAL "
        + "MUST ( sn $ cn ) MAY ( userPassword $ telephoneNumber $ seeAlso $ description ) )";

    [Fact]
    public void Attribute_type_parses_from_bare_definition()
    {
        var name = LdapAttributeType.Parse(NameDefinition);

        Assert.Equal("2.5.4.41", name.Oid);
        Assert.Equal("name", name.Name);
        Assert.Equal("caseIgnoreMatch", name.Equality);
        Assert.Equal("caseIgnoreSubstringsMatch", name.Substring);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", name.Syntax);
        Assert.Equal(32768, name.SyntaxLength);
    }

    [Fact]
    public void Attribute_type_published_with_sup_and_no_syntax_parses()
    {
        // OpenLDAP publishes cn this way; the syntax lives on the supertype.
        var cn = LdapAttributeType.Parse(CnDefinition);

        Assert.Equal("2.5.4.3", cn.Oid);
        Assert.Equal(["cn", "commonName"], cn.Names);
        Assert.Equal("name", cn.SuperiorName);
        Assert.Null(cn.Syntax);
    }

    [Fact]
    public void Object_class_parses_from_bare_definition()
    {
        var person = LdapObjectClass.Parse(PersonDefinition);

        Assert.Equal("2.5.6.6", person.Oid);
        Assert.Equal("person", person.Name);
        Assert.Equal(["top"], person.SuperiorNames);
        Assert.Equal(LdapObjectClassKind.Structural, person.Kind);
        Assert.Equal(["sn", "cn"], person.Must);
        Assert.Equal(["userPassword", "telephoneNumber", "seeAlso", "description"], person.May);
    }

    [Fact]
    public void X_extensions_are_captured_not_skipped()
    {
        var type = LdapAttributeType.Parse(
            "( 2.16.840.1.113730.3.1.1 NAME 'carLicense' DESC 'vehicle license or registration plate' "
            + "EQUALITY caseIgnoreMatch SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 X-ORIGIN 'RFC 2798' )");

        Assert.Equal(["RFC 2798"], type.Extensions["X-ORIGIN"]);
    }

    [Fact]
    public void Qdstring_escapes_decode_in_bare_definitions()
    {
        var type = LdapAttributeType.Parse(
            @"( 1.2.3 NAME 'x' DESC 'the entity\27s name' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )");

        Assert.Equal("the entity's name", type.Description);
    }

    [Fact]
    public void Strict_parse_rejects_unknown_keyword()
    {
        var ex = Assert.Throws<LdapSchemaParseException>(() => LdapAttributeType.Parse(
            "( 1.2.3 NAME 'x' VENDORFLAG SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"));

        Assert.Contains("VENDORFLAG", ex.Message);
        // A bare definition has no file location; the message is not prefixed with one.
        Assert.DoesNotContain("line", ex.Message);
    }

    [Fact]
    public void Strict_parse_rejects_trailing_text_after_definition() =>
        Assert.Throws<LdapSchemaParseException>(() => LdapObjectClass.Parse(
            "( 2.5.6.0 NAME 'top' ABSTRACT MUST objectClass ) trailing"));

    [Fact]
    public void Strict_parse_rejects_non_numeric_oid() =>
        Assert.Throws<LdapSchemaParseException>(() => LdapAttributeType.Parse(
            "( notAnOid NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"));

    [Fact]
    public void Bare_definitions_have_no_oid_macro_context()
    {
        // objectidentifier macros are a slapd.conf file concept; RFC 4512 bare
        // definitions require a numeric OID.
        Assert.Throws<LdapSchemaParseException>(() => LdapAttributeType.Parse(
            "( someMacro:1 NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"));
    }

    [Fact]
    public void Strict_parse_rejects_missing_open_paren() =>
        Assert.Throws<LdapSchemaParseException>(() => LdapAttributeType.Parse(
            "2.5.4.3 NAME 'cn' SUP name"));

    [Fact]
    public void Parse_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => LdapAttributeType.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => LdapObjectClass.Parse(null!));
    }

    [Fact]
    public void Subschema_round_trips_realistic_openldap_definitions()
    {
        var schema = LdapSchema.ParseSubschema(
            [NameDefinition, CnDefinition],
            [TopDefinition, PersonDefinition]);

        Assert.Empty(schema.UnparsedDefinitions);
        Assert.Equal(2, schema.AttributeTypes.Count);
        Assert.Equal(2, schema.ObjectClasses.Count);

        // Lookup by any name, and inheritance queries, work as for file input.
        Assert.Same(schema.FindAttributeType("cn"), schema.FindAttributeType("commonName"));
        var person = schema.FindObjectClass("person");
        Assert.NotNull(person);
        Assert.Contains("objectClass", schema.RequiredAttributeNames(person));
        Assert.Contains("sn", schema.RequiredAttributeNames(person));
    }

    [Fact]
    public void Subschema_buckets_unparseable_definition_and_keeps_the_rest()
    {
        const string garbage = "( 1.2.3 NAME 'broken";

        var schema = LdapSchema.ParseSubschema([NameDefinition, garbage], [PersonDefinition]);

        Assert.Single(schema.AttributeTypes);
        Assert.Single(schema.ObjectClasses);
        var unparsed = Assert.Single(schema.UnparsedDefinitions);
        Assert.Equal(LdapSchemaDefinitionKind.AttributeType, unparsed.Kind);
        Assert.Equal(garbage, unparsed.Definition);
        Assert.NotEmpty(unparsed.Error);
    }

    [Fact]
    public void Subschema_buckets_object_class_with_its_kind()
    {
        var schema = LdapSchema.ParseSubschema([], ["not even a definition"]);

        Assert.Empty(schema.ObjectClasses);
        var unparsed = Assert.Single(schema.UnparsedDefinitions);
        Assert.Equal(LdapSchemaDefinitionKind.ObjectClass, unparsed.Kind);
        Assert.Equal("not even a definition", unparsed.Definition);
    }

    [Fact]
    public void Subschema_skips_unknown_flag_keyword_before_a_real_keyword()
    {
        // The hard case: a valueless vendor keyword directly before NAME. Eating
        // NAME as the flag's value would silently drop the name.
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 VENDORFLAG NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"],
            []);

        Assert.Empty(schema.UnparsedDefinitions);
        var type = Assert.Single(schema.AttributeTypes);
        Assert.Equal("x", type.Name);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", type.Syntax);
    }

    [Fact]
    public void Subschema_skips_unknown_quoted_and_parenthesized_values()
    {
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 NAME 'x' VENDORKEY 'ignored' VENDORLIST ( a $ b ) SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"],
            []);

        Assert.Empty(schema.UnparsedDefinitions);
        var type = Assert.Single(schema.AttributeTypes);
        Assert.Equal("x", type.Name);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", type.Syntax);
    }

    [Fact]
    public void Subschema_treats_bare_word_after_unknown_keyword_as_next_keyword()
    {
        // An unknown keyword's bare-word "value" is indistinguishable from the
        // next keyword, so it is treated as one and skipped in turn when
        // unknown. This input parses identically under the alternative
        // (consume-one-token) heuristic; what distinguishes the two is the
        // flag-before-keyword shape, pinned by
        // Subschema_skips_unknown_flag_keyword_before_a_real_keyword — under
        // consume-one, VENDORFLAG would eat NAME and fail the definition.
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 VENDORKEY vendorvalue NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"],
            []);

        Assert.Empty(schema.UnparsedDefinitions);
        var type = Assert.Single(schema.AttributeTypes);
        Assert.Equal("x", type.Name);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", type.Syntax);
    }

    [Fact]
    public void Subschema_bare_value_colliding_with_a_keyword_is_the_known_blind_spot()
    {
        // The chosen heuristic's blind spot, documented rather than hidden: a
        // vendor keyword whose bare value literally equals a standard keyword
        // makes that keyword parse as real — here NAME captures the word
        // SYNTAX, and the OID that follows skips as an unknown flag. The
        // consume-one-token alternative handles this shape but corrupts the
        // flag-before-keyword shape instead; each heuristic has exactly one of
        // the two blind spots, this one has not been observed in real server
        // output, and the flag shape is the realistic vendor pattern.
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 VENDORKEY NAME SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"],
            []);

        Assert.Empty(schema.UnparsedDefinitions);
        var type = Assert.Single(schema.AttributeTypes);
        Assert.Equal(["SYNTAX"], type.Names);
        Assert.Null(type.Syntax);
    }

    [Fact]
    public void Subschema_still_captures_x_extensions()
    {
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 X-ORIGIN ( 'RFC 4519' 'user defined' ) )"],
            []);

        Assert.Empty(schema.UnparsedDefinitions);
        Assert.Equal(["RFC 4519", "user defined"], Assert.Single(schema.AttributeTypes).Extensions["X-ORIGIN"]);
    }

    [Fact]
    public void Subschema_buckets_trailing_text_rather_than_ignoring_it()
    {
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3 NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 ) trailing"],
            []);

        Assert.Empty(schema.AttributeTypes);
        Assert.Single(schema.UnparsedDefinitions);
    }

    [Fact]
    public void Subschema_with_empty_input_is_empty()
    {
        var schema = LdapSchema.ParseSubschema([], []);

        Assert.Empty(schema.AttributeTypes);
        Assert.Empty(schema.ObjectClasses);
        Assert.Empty(schema.UnparsedDefinitions);
    }

    [Fact]
    public void Subschema_null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => LdapSchema.ParseSubschema(null!, []));
        Assert.Throws<ArgumentNullException>(() => LdapSchema.ParseSubschema([], null!));
        Assert.Throws<ArgumentException>(() => LdapSchema.ParseSubschema([null!], []));
        Assert.Throws<ArgumentException>(() => LdapSchema.ParseSubschema([], [null!]));
    }

    [Fact]
    public void Ldap_syntax_parses_from_bare_definition()
    {
        // Verbatim OpenLDAP 2.6 shapes: only an explicit 'TRUE' asserts a flag.
        var audio = LdapSyntax.Parse(
            "( 1.3.6.1.4.1.1466.115.121.1.4 DESC 'Audio' X-NOT-HUMAN-READABLE 'TRUE' )");
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.4", audio.Oid);
        Assert.Equal("Audio", audio.Description);
        Assert.True(audio.NotHumanReadable);
        Assert.False(audio.BinaryTransferRequired);

        var certificate = LdapSyntax.Parse(
            "( 1.3.6.1.4.1.1466.115.121.1.8 DESC 'Certificate' "
            + "X-BINARY-TRANSFER-REQUIRED 'TRUE' X-NOT-HUMAN-READABLE 'TRUE' )");
        Assert.True(certificate.NotHumanReadable);
        Assert.True(certificate.BinaryTransferRequired);
        Assert.Equal(["TRUE"], certificate.Extensions["X-BINARY-TRANSFER-REQUIRED"]);

        var directoryString = LdapSyntax.Parse(
            "( 1.3.6.1.4.1.1466.115.121.1.15 DESC 'Directory String' )");
        Assert.False(directoryString.NotHumanReadable);
        Assert.False(directoryString.BinaryTransferRequired);
        Assert.Empty(directoryString.Extensions);
    }

    [Fact]
    public void Ldap_syntax_published_false_is_not_an_assertion()
    {
        var syntax = LdapSyntax.Parse(
            "( 1.2.3 DESC 'x' X-NOT-HUMAN-READABLE 'FALSE' )");

        Assert.False(syntax.NotHumanReadable);
        // The extension value itself stays available for consumers that care.
        Assert.Equal(["FALSE"], syntax.Extensions["X-NOT-HUMAN-READABLE"]);
    }

    [Fact]
    public void Ldap_syntax_true_is_case_insensitive()
    {
        var syntax = LdapSyntax.Parse("( 1.2.3 DESC 'x' X-NOT-HUMAN-READABLE 'true' )");

        Assert.True(syntax.NotHumanReadable);
    }

    [Fact]
    public void Ldap_syntax_strict_parse_rejects_bad_input()
    {
        Assert.Throws<LdapSchemaParseException>(() => LdapSyntax.Parse(
            "( 1.2.3 DESC 'x' VENDORFLAG )"));
        Assert.Throws<LdapSchemaParseException>(() => LdapSyntax.Parse(
            "( 1.2.3 DESC 'x' ) trailing"));
        Assert.Throws<LdapSchemaParseException>(() => LdapSyntax.Parse(
            "( notAnOid DESC 'x' )"));
        Assert.Throws<ArgumentNullException>(() => LdapSyntax.Parse(null!));
    }

    [Fact]
    public void Subschema_buckets_bad_syntax_with_its_kind()
    {
        var schema = LdapSchema.ParseSubschema([], [], ["( 1.2.3 DESC 'unterminated"]);

        Assert.Empty(schema.Syntaxes);
        var unparsed = Assert.Single(schema.UnparsedDefinitions);
        Assert.Equal(LdapSchemaDefinitionKind.Syntax, unparsed.Kind);
    }

    [Fact]
    public void Subschema_skips_unknown_keywords_in_syntax_definitions()
    {
        var schema = LdapSchema.ParseSubschema(
            [], [], ["( 1.2.3 VENDORFLAG DESC 'x' VENDORKEY 'v' )"]);

        Assert.Empty(schema.UnparsedDefinitions);
        Assert.Equal("x", Assert.Single(schema.Syntaxes).Description);
    }

    [Fact]
    public void Find_syntax_strips_length_bounds()
    {
        var schema = LdapSchema.ParseSubschema(
            [], [], ["( 1.3.6.1.4.1.1466.115.121.1.15 DESC 'Directory String' )"]);

        var syntax = schema.FindSyntax("1.3.6.1.4.1.1466.115.121.1.15");
        Assert.NotNull(syntax);
        // A raw bounded SYNTAX reference finds the same syntax: the {bound} is
        // not part of the OID's identity.
        Assert.Same(syntax, schema.FindSyntax("1.3.6.1.4.1.1466.115.121.1.15{32768}"));
        Assert.Null(schema.FindSyntax("1.2.3"));
    }

    [Fact]
    public void Resolve_syntax_oid_walks_the_sup_chain()
    {
        var schema = LdapSchema.ParseSubschema([NameDefinition, CnDefinition], []);
        var cn = schema.FindAttributeType("cn");
        var name = schema.FindAttributeType("name");
        Assert.NotNull(cn);
        Assert.NotNull(name);

        // cn declares no SYNTAX; it inherits Directory String through SUP name.
        Assert.Null(cn.Syntax);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", schema.ResolveSyntaxOid(cn));
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", schema.ResolveSyntaxOid(name));
    }

    [Fact]
    public void Resolve_syntax_oid_returns_null_for_missing_superior()
    {
        var schema = LdapSchema.ParseSubschema([CnDefinition], []);
        var cn = schema.FindAttributeType("cn");
        Assert.NotNull(cn);

        Assert.Null(schema.ResolveSyntaxOid(cn));
    }

    [Fact]
    public void Resolve_syntax_oid_survives_a_sup_cycle()
    {
        // A malformed schema with a SUP loop must terminate with null, not hang.
        var schema = LdapSchema.ParseSubschema(
            ["( 1.2.3.1 NAME 'a' SUP b )", "( 1.2.3.2 NAME 'b' SUP a )"],
            []);
        var a = schema.FindAttributeType("a");
        Assert.NotNull(a);

        Assert.Null(schema.ResolveSyntaxOid(a));
    }

    [Fact]
    public void Real_openldap_subschema_capture_parses_completely()
    {
        // The fixture is a real slapd 2.6 server's cn=Subschema answer (see
        // tests/fixtures/subschema/README.md), read back through our own LDIF
        // reader — folded lines, base64, and all — then through the lenient
        // subschema parser. Every published definition must parse.
        string fixturePath = Fixtures.PathOf("subschema", "openldap-2.6.ldif");

        // The "2.6" in the filename is a claim; the capture script's first-line
        // witness is what keeps it honest (a recapture from a drifted image
        // fails here instead of silently relabeling another version as 2.6).
        string witness = File.ReadLines(fixturePath).First();
        Assert.StartsWith("# Captured from:", witness, StringComparison.Ordinal);
        Assert.Contains("slapd 2.6.", witness, StringComparison.Ordinal);

        var entry = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.ReadFile(fixturePath)));
        Assert.Equal("cn=Subschema", entry.Dn);

        var schema = LdapSchema.ParseSubschema(
            ValuesOf(entry, "attributeTypes"),
            ValuesOf(entry, "objectClasses"),
            ValuesOf(entry, "ldapSyntaxes"));

        Assert.Empty(schema.UnparsedDefinitions);
        Assert.True(schema.AttributeTypes.Count > 200,
            $"expected the full published attribute set, got {schema.AttributeTypes.Count}");
        Assert.True(schema.ObjectClasses.Count > 50,
            $"expected the full published class set, got {schema.ObjectClasses.Count}");
        Assert.True(schema.Syntaxes.Count > 25,
            $"expected the full published syntax set, got {schema.Syntaxes.Count}");

        // Shapes a schema file never contains: cn published as SUP name with no
        // SYNTAX, and bounded syntax OIDs stripped to the bare OID.
        var cn = schema.FindAttributeType("commonName");
        Assert.NotNull(cn);
        Assert.Same(cn, schema.FindAttributeType("cn"));
        Assert.Equal("name", cn.SuperiorName);
        Assert.Null(cn.Syntax);

        var name = schema.FindAttributeType("name");
        Assert.NotNull(name);
        Assert.NotNull(name.Syntax);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", name.Syntax);
        Assert.Equal(32768, name.SyntaxLength);

        // Real-data SUP-chain resolution: cn's syntax comes from name, and the
        // resolved OID finds the published Directory String syntax definition.
        Assert.Equal(name.Syntax, schema.ResolveSyntaxOid(cn));
        var directoryString = schema.FindSyntax(name.Syntax);
        Assert.NotNull(directoryString);
        Assert.Equal("Directory String", directoryString.Description);

        // How a real server declares octet-carrying syntaxes.
        var audio = schema.FindSyntax("1.3.6.1.4.1.1466.115.121.1.4");
        Assert.NotNull(audio);
        Assert.True(audio.NotHumanReadable);
        var certificate = schema.FindSyntax("1.3.6.1.4.1.1466.115.121.1.8");
        Assert.NotNull(certificate);
        Assert.True(certificate.BinaryTransferRequired);

        var person = schema.FindObjectClass("person");
        Assert.NotNull(person);
        Assert.Equal(["sn", "cn"], person.Must);
        Assert.Contains("objectClass", schema.RequiredAttributeNames(person));
    }

    private static IEnumerable<string> ValuesOf(LdifContentRecord entry, string attribute)
    {
        var values = entry[attribute];
        Assert.NotNull(values);
        return values.Values.Select(v => v.AsString());
    }

    [Fact]
    public void Strict_file_parsing_reports_no_unparsed_definitions()
    {
        var schema = LdapSchema.Parse(
            "attributetype ( 1.2.3 NAME 'x' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n");

        Assert.Empty(schema.UnparsedDefinitions);
    }

    [Fact]
    public void Strict_file_parsing_still_rejects_unknown_keywords()
    {
        // Leniency belongs to subschema input only; a schema file is a build
        // input you fix, and slapd itself rejects unknown keywords.
        Assert.Throws<LdapSchemaParseException>(() => LdapSchema.Parse(
            "attributetype ( 1.2.3 NAME 'x' VENDORFLAG SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )\n"));
    }
}
