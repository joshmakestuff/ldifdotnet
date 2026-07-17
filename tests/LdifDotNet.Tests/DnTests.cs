namespace LdifDotNet.Tests;

public class DnTests
{
    [Theory]
    [InlineData("Smith, Jr.", "Smith\\, Jr.")]
    [InlineData("#hash", "\\#hash")]
    [InlineData(" leading", "\\ leading")]
    [InlineData("trailing ", "trailing\\ ")]
    [InlineData("a+b<c>d;e\"f\\g", "a\\+b\\<c\\>d\\;e\\\"f\\\\g")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void EscapeValue_follows_rfc4514(string value, string expected) =>
        Assert.Equal(expected, Dn.EscapeValue(value));

    [Fact]
    public void EscapeValue_hex_escapes_nul() =>
        Assert.Equal("a\\00b", Dn.EscapeValue("a\0b"));

    [Theory]
    [InlineData("Smith, Jr.")]
    [InlineData("#hash")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("a+b<c>d;e\"f\\g")]
    [InlineData("plain")]
    [InlineData("a\0b")]
    [InlineData("Lučić")]
    [InlineData("   ")]
    public void Escape_then_unescape_round_trips(string value) =>
        Assert.Equal(value, Dn.UnescapeValue(Dn.EscapeValue(value)));

    [Fact]
    public void UnescapeValue_decodes_multibyte_hex_run() =>
        Assert.Equal("Lučić", Dn.UnescapeValue("Lu\\C4\\8Di\\C4\\87"));

    [Fact]
    public void UnescapeValue_decodes_hex_control_char() =>
        Assert.Equal("Before\rAfter", Dn.UnescapeValue("Before\\0dAfter"));

    [Fact]
    public void UnescapeValue_returns_hexstring_verbatim() =>
        Assert.Equal("#04024869", Dn.UnescapeValue("#04024869"));

    [Theory]
    [InlineData("dangling\\")]
    [InlineData("bad\\Xhex")]
    [InlineData("halfhex\\C")]
    public void UnescapeValue_rejects_malformed_escapes(string value) =>
        Assert.Throws<ArgumentException>(() => Dn.UnescapeValue(value));

    [Fact]
    public void Rdn_escapes_the_value() =>
        Assert.Equal("cn=Smith\\, Jr.", Dn.Rdn("cn", "Smith, Jr."));

    [Fact]
    public void Combine_joins_parts_and_skips_empties() =>
        Assert.Equal(
            "uid=jsmith,ou=people,dc=example,dc=com",
            Dn.Combine(Dn.Rdn("uid", "jsmith"), Dn.Rdn("ou", "people"), "", "dc=example,dc=com"));

    [Fact]
    public void Compose_of_awkward_value_parses_back()
    {
        string dn = Dn.Combine(Dn.Rdn("cn", "Doe, John + Co."), "dc=example,dc=com");
        var rdns = Dn.Parse(dn);

        Assert.Equal("cn", rdns[0].Type);
        Assert.Equal("Doe, John + Co.", rdns[0].Value);
        Assert.Equal("example", rdns[1].Value);
    }

    [Fact]
    public void Parse_simple_dn()
    {
        var rdns = Dn.Parse("UID=jsmith,DC=example,DC=net");

        Assert.Equal(3, rdns.Count);
        Assert.Equal(new AttributeTypeAndValue("UID", "jsmith"), rdns[0].SoleAttribute);
        Assert.Equal(new AttributeTypeAndValue("DC", "example"), rdns[1].SoleAttribute);
        Assert.Equal(new AttributeTypeAndValue("DC", "net"), rdns[2].SoleAttribute);
    }

    [Fact]
    public void Parse_represents_multivalued_rdn()
    {
        var rdns = Dn.Parse("OU=Sales+CN=J.  Smith,DC=example,DC=net");

        Assert.True(rdns[0].IsMultiValued);
        Assert.Equal(
            new[] { new AttributeTypeAndValue("OU", "Sales"), new AttributeTypeAndValue("CN", "J.  Smith") },
            rdns[0].Attributes);
        Assert.False(rdns[1].IsMultiValued);
    }

    [Fact]
    public void Single_accessors_throw_on_multivalued_rdn()
    {
        var rdn = Dn.Parse("OU=Sales+CN=Smith,DC=x")[0];

        Assert.Throws<InvalidOperationException>(() => rdn.SoleAttribute);
        Assert.Throws<InvalidOperationException>(() => rdn.Value);
    }

    [Fact]
    public void Parse_unescapes_special_characters() =>
        Assert.Equal(
            "James \"Jim\" Smith, III",
            Dn.Parse("CN=James \\\"Jim\\\" Smith\\, III,DC=example,DC=net")[0].Value);

    [Fact]
    public void Parse_decodes_hex_escapes() =>
        Assert.Equal("Before\rAfter", Dn.Parse("CN=Before\\0dAfter,DC=example,DC=net")[0].Value);

    [Fact]
    public void Parse_keeps_hexstring_value_verbatim()
    {
        var rdns = Dn.Parse("1.3.6.1.4.1.1466.0=#04024869");

        Assert.Equal("1.3.6.1.4.1.1466.0", rdns[0].Type);
        Assert.Equal("#04024869", rdns[0].Value);
    }

    [Fact]
    public void Parse_tolerates_insignificant_whitespace()
    {
        var rdns = Dn.Parse("uid=jsmith, ou=people , dc=example");

        Assert.Equal(["jsmith", "people", "example"], rdns.Select(r => r.Value));
    }

    [Fact]
    public void Parse_preserves_escaped_trailing_space()
    {
        var rdns = Dn.Parse("cn=trailing\\ ,dc=x");

        Assert.Equal("trailing ", rdns[0].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_empty_yields_no_rdns(string dn) =>
        Assert.Empty(Dn.Parse(dn));

    [Theory]
    [InlineData("novalue,dc=x")]
    [InlineData("=orphan,dc=x")]
    public void Parse_rejects_malformed_components(string dn) =>
        Assert.Throws<ArgumentException>(() => Dn.Parse(dn));

    [Fact]
    public void Rdn_round_trips_through_ToString()
    {
        var rdn = Dn.Parse("cn=Smith\\, Jr.,dc=x")[0];

        Assert.Equal("cn=Smith\\, Jr.", rdn.ToString());
    }
}
