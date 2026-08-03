namespace LdifDotNet.Tests;

public class AttributeDescriptionTests
{
    [Theory]
    [InlineData("cn", "cn")]
    [InlineData("cn;lang-en", "cn")]
    [InlineData("cn;lang-en;binary", "cn")]
    [InlineData("userCertificate;binary", "userCertificate")]
    [InlineData("2.5.4.3;binary", "2.5.4.3")]
    public void TypeOf_strips_options(string description, string expected) =>
        Assert.Equal(expected, AttributeDescription.TypeOf(description));

    [Theory]
    [InlineData("userCertificate;binary", "binary", true)]
    [InlineData("userCertificate;BINARY", "binary", true)] // options are case-insensitive (RFC 4512 §2.5)
    [InlineData("userCertificate;binary", "BINARY", true)]
    [InlineData("cn;lang-en;binary", "binary", true)]
    [InlineData("cn;lang-en;binary", "lang-en", true)]
    [InlineData("cn;lang-en", "binary", false)]
    [InlineData("cn", "binary", false)]
    [InlineData("cn;binary", "bin", false)] // whole-option match, never a prefix match
    public void HasOption_matches_whole_options_case_insensitively(string description, string option, bool expected) =>
        Assert.Equal(expected, AttributeDescription.HasOption(description, option));

    [Theory]
    [InlineData("cn", true)]
    [InlineData("2.5.4.3", true)]
    [InlineData("01.2.3", true)] // RFC 2849 ldap-oid allows leading zeros — deliberate, see RfcGrammar.IsNumericOid
    [InlineData("userCertificate;binary", true)]
    [InlineData("cn;lang-en;binary", true)]
    [InlineData("", false)]
    [InlineData(";binary", false)]
    [InlineData("cn;", false)]
    [InlineData("9cn", false)] // descr must start with a letter, and this is no numeric OID
    [InlineData("cn name", false)]
    [InlineData("cn;läng", false)] // ASCII only
    public void IsValid_matches_rfc2849_attribute_description(string description, bool expected) =>
        Assert.Equal(expected, AttributeDescription.IsValid(description));

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => AttributeDescription.TypeOf(null!));
        Assert.Throws<ArgumentNullException>(() => AttributeDescription.HasOption(null!, "binary"));
        Assert.Throws<ArgumentNullException>(() => AttributeDescription.HasOption("cn", null!));
        Assert.Throws<ArgumentNullException>(() => AttributeDescription.IsValid(null!));
    }
}
