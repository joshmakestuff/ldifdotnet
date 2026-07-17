using LdifDotNet.Schema;

namespace LdifDotNet.Tests;

public class SchemaParserTests
{
    public static TheoryData<string> SchemaFiles()
    {
        var data = new TheoryData<string>();
        foreach (string file in SchemaCorpusTests.AllSchemaFiles())
            data.Add(file);
        return data;
    }

    /// <summary>
    /// The parser must handle the entire vendored corpus, and must find exactly as
    /// many definitions as a naive line scan does — nothing silently dropped.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void Parses_every_schema_in_corpus(string relativePath)
    {
        string path = Fixtures.PathOf(relativePath);
        var schema = LdapSchema.Load(path);
        var (expectedAttributeTypes, expectedObjectClasses) = SchemaCorpusTests.CountDefinitions(path);

        Assert.Equal(expectedAttributeTypes, schema.AttributeTypes.Count);
        Assert.Equal(expectedObjectClasses, schema.ObjectClasses.Count);
        Assert.All(schema.AttributeTypes, a => Assert.NotEqual("", a.Oid));
        Assert.All(schema.ObjectClasses, c => Assert.NotEqual("", c.Oid));
    }

    [Theory]
    [InlineData(@"owner\27s path\5Croot", "owner's path\\root")]  // RFC 4512 QQ + QS (upper)
    [InlineData(@"lower\5ccase", "lower\\case")]                  // QS, lower-case hex
    [InlineData(@"no escapes here", "no escapes here")]
    public void Quoted_string_escapes_are_decoded(string escaped, string expected)
    {
        var schema = LdapSchema.Parse(
            $"attributetype ( 1.2.3.4 NAME 'testAttr' DESC '{escaped}' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )");

        Assert.Equal(expected, schema.AttributeTypes[0].Description);
    }

    [Fact]
    public void Extension_values_decode_quoted_string_escapes()
    {
        var schema = LdapSchema.Parse(
            @"attributetype ( 1.2.3.4 NAME 'testAttr' X-ORIGIN 'somebody\27s draft' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )");

        Assert.Equal("somebody's draft", schema.AttributeTypes[0].Extensions["X-ORIGIN"][0]);
    }

    [Theory]
    [InlineData(@"bad \00 escape")]   // hex pair outside the RFC 4512 set
    [InlineData(@"bad \x escape")]    // not a hex pair
    [InlineData(@"truncated \2")]     // one char after the backslash
    [InlineData(@"truncated \")]      // nothing after the backslash
    public void Malformed_quoted_string_escapes_are_rejected(string value)
    {
        Assert.Throws<LdapSchemaParseException>(() => LdapSchema.Parse(
            $"attributetype ( 1.2.3.4 NAME 'testAttr' DESC '{value}' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )"));
    }

    [Fact]
    public void Core_schema_person_class_is_parsed_correctly()
    {
        var schema = LoadOpenLdap("core.schema");

        var person = schema.FindObjectClass("person");
        Assert.NotNull(person);
        Assert.Equal("2.5.6.6", person.Oid);
        Assert.Equal(LdapObjectClassKind.Structural, person.Kind);
        Assert.Contains("top", person.SuperiorNames);
        Assert.Contains("sn", person.Must);
        Assert.Contains("cn", person.Must);
        Assert.Contains("telephoneNumber", person.May);
    }

    [Fact]
    public void Inetorgperson_inherits_through_superior_chain()
    {
        var schema = LoadOpenLdap("core.schema", "cosine.schema", "inetorgperson.schema");

        var inetOrgPerson = schema.FindObjectClass("inetOrgPerson");
        Assert.NotNull(inetOrgPerson);
        Assert.Equal("2.16.840.1.113730.3.2.2", inetOrgPerson.Oid);
        Assert.Contains("organizationalPerson", inetOrgPerson.SuperiorNames);

        var required = schema.RequiredAttributeNames(inetOrgPerson);
        Assert.Contains("sn", required);
        Assert.Contains("cn", required);

        var optional = schema.OptionalAttributeNames(inetOrgPerson);
        Assert.Contains("displayName", optional);          // own MAY
        Assert.Contains("telephoneNumber", optional);      // inherited from person
    }

    [Fact]
    public void Attribute_type_details_are_parsed()
    {
        var schema = LoadOpenLdap("core.schema");

        // Note: cn, description, name etc. are NOT in core.schema — slapd hardcodes
        // that "system schema". sn is the multi-name attribute that is present.
        var sn = schema.FindAttributeType("sn");
        Assert.NotNull(sn);
        Assert.Equal("2.5.4.4", sn.Oid);
        Assert.Contains("surname", sn.Names);
        Assert.Equal("name", sn.SuperiorName);

        var businessCategory = schema.FindAttributeType("businessCategory");
        Assert.NotNull(businessCategory);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", businessCategory.Syntax);
        Assert.Equal(128, businessCategory.SyntaxLength);
        Assert.Equal("caseIgnoreMatch", businessCategory.Equality);
    }

    [Fact]
    public void Eduperson_schema_is_parsed()
    {
        var schema = LdapSchema.Load(Fixtures.PathOf("schemas/contrib/eduperson.schema"));

        var eduPerson = schema.FindObjectClass("eduPerson");
        Assert.NotNull(eduPerson);
        Assert.Equal(LdapObjectClassKind.Auxiliary, eduPerson.Kind);

        var principalName = schema.FindAttributeType("eduPersonPrincipalName");
        Assert.NotNull(principalName);
        Assert.True(principalName.SingleValue);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.15", principalName.Syntax);
    }

    [Fact]
    public void Contrib_unix_schemas_are_parsed()
    {
        var rfc2307bis = LdapSchema.Load(Fixtures.PathOf("schemas/contrib/rfc2307bis.schema"));
        var posixAccount = rfc2307bis.FindObjectClass("posixAccount");
        Assert.NotNull(posixAccount);
        Assert.Contains("uidNumber", posixAccount.Must);
        var ipNetworkNumber = rfc2307bis.FindAttributeType("ipNetworkNumber");
        Assert.NotNull(ipNetworkNumber);
        Assert.True(ipNetworkNumber.SingleValue);
        Assert.Equal("1.3.6.1.4.1.1466.115.121.1.26", ipNetworkNumber.Syntax);

        var sudo = LdapSchema.Load(Fixtures.PathOf("schemas/contrib/sudo.schema"));
        var sudoRole = sudo.FindObjectClass("sudoRole");
        Assert.NotNull(sudoRole);
        Assert.Equal(LdapObjectClassKind.Structural, sudoRole.Kind);
        Assert.Contains("sudoUser", sudoRole.May);

        var lpk = LdapSchema.Load(Fixtures.PathOf("schemas/contrib/openssh-lpk.schema"));
        var ldapPublicKey = lpk.FindObjectClass("ldapPublicKey");
        Assert.NotNull(ldapPublicKey);
        Assert.Equal(LdapObjectClassKind.Auxiliary, ldapPublicKey.Kind);
        Assert.NotNull(lpk.FindAttributeType("sshPublicKey"));
    }

    [Fact]
    public void Lookup_is_case_insensitive_and_first_wins()
    {
        var schema = LoadOpenLdap("core.schema");

        Assert.NotNull(schema.FindObjectClass("person"));
        Assert.Same(schema.FindObjectClass("PERSON"), schema.FindObjectClass("person"));
        Assert.NotNull(schema.FindAttributeType("sn"));
        Assert.Same(schema.FindAttributeType("SN"), schema.FindAttributeType("surname"));
    }

    private static LdapSchema LoadOpenLdap(params string[] names) =>
        LdapSchema.Load([.. names.Select(n => Fixtures.PathOf("schemas/openldap", n))]);
}
