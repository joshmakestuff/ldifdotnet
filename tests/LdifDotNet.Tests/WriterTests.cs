using System.Text;

namespace LdifDotNet.Tests;

public class WriterTests
{
    [Fact]
    public void Writes_simple_content_record()
    {
        var record = new LdifContentRecord(
            "cn=Babs Jensen,dc=example,dc=com",
            new LdifAttribute("cn", "Babs Jensen"),
            new LdifAttribute("objectClass", "person"));

        Assert.Equal(
            "version: 1\n" +
            "dn: cn=Babs Jensen,dc=example,dc=com\n" +
            "cn: Babs Jensen\n" +
            "objectClass: person\n",
            LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Separates_records_with_a_blank_line()
    {
        var records = new[]
        {
            new LdifContentRecord("dc=a", new LdifAttribute("dc", "a")),
            new LdifContentRecord("dc=b", new LdifAttribute("dc", "b")),
        };

        Assert.Equal(
            "version: 1\ndn: dc=a\ndc: a\n\ndn: dc=b\ndc: b\n",
            LdifWriter.WriteToString(records));
    }

    [Theory]
    [InlineData("café")]           // non-ASCII
    [InlineData(" leading space")]
    [InlineData(":starts with colon")]
    [InlineData("<starts with less-than")]
    [InlineData("trailing space ")]
    [InlineData("embedded\nnewline")]
    public void Base64_encodes_unsafe_values(string unsafeValue)
    {
        var record = new LdifContentRecord("dc=x", new LdifAttribute("description", unsafeValue));

        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(unsafeValue));
        Assert.Contains($"description:: {expected}", LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Binary_values_are_always_base64()
    {
        byte[] bytes = [1, 2, 3, 250];
        var record = new LdifContentRecord("dc=x", new LdifAttribute("data", LdifValue.FromBytes(bytes)));

        Assert.Contains($"data:: {Convert.ToBase64String(bytes)}", LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Url_values_are_written_as_references()
    {
        var record = new LdifContentRecord(
            "dc=x",
            new LdifAttribute("jpegphoto", LdifValue.FromUrl(new Uri("file:///photos/x.jpg"))));

        Assert.Contains("jpegphoto:< file:///photos/x.jpg", LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Empty_values_are_written_with_bare_colon()
    {
        var record = new LdifContentRecord("dc=x", new LdifAttribute("seeAlso", ""));

        Assert.Contains("\nseeAlso:\n", LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Base64_encodes_unsafe_dn()
    {
        var record = new LdifContentRecord("ou=営業部,o=Airius", new LdifAttribute("ou", "x"));

        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("ou=営業部,o=Airius"));
        Assert.Contains($"dn:: {expected}", LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Folds_long_lines_at_wrap_column_and_round_trips()
    {
        string longValue = new('a', 200);
        var record = new LdifContentRecord("dc=x", new LdifAttribute("description", longValue));

        string ldif = LdifWriter.WriteToString([record]);

        foreach (string line in ldif.TrimEnd('\n').Split('\n'))
            Assert.True(line.Length <= 76, $"line exceeds wrap column: {line.Length} chars");
        Assert.Contains("\n ", ldif); // folding happened

        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(ldif)));
        Assert.Equal(longValue, reparsed["description"]!.Values[0].AsString());
    }

    [Fact]
    public void Null_wrap_column_disables_folding()
    {
        string longValue = new('a', 200);
        var record = new LdifContentRecord("dc=x", new LdifAttribute("description", longValue));

        string ldif = LdifWriter.WriteToString([record], new LdifWriterOptions { WrapColumn = null });

        Assert.DoesNotContain("\n ", ldif);
        Assert.Contains($"description: {longValue}\n", ldif);
    }

    [Fact]
    public void Version_line_can_be_disabled()
    {
        var record = new LdifContentRecord("dc=x", new LdifAttribute("dc", "x"));

        string ldif = LdifWriter.WriteToString([record], new LdifWriterOptions { IncludeVersionLine = false });

        Assert.StartsWith("dn: dc=x\n", ldif);
    }

    [Fact]
    public void Writes_modify_change_record()
    {
        var record = new LdifModifyRecord(
            "cn=Paula Jensen,dc=example,dc=com",
            new LdifModification(LdifModificationType.Add, "postaladdress", "123 Anystreet"),
            new LdifModification(LdifModificationType.Delete, "description"),
            new LdifModification(LdifModificationType.Replace, "telephonenumber", "+1 408 555 1234", "+1 408 555 5678"));

        Assert.Equal(
            "version: 1\n" +
            "dn: cn=Paula Jensen,dc=example,dc=com\n" +
            "changetype: modify\n" +
            "add: postaladdress\n" +
            "postaladdress: 123 Anystreet\n" +
            "-\n" +
            "delete: description\n" +
            "-\n" +
            "replace: telephonenumber\n" +
            "telephonenumber: +1 408 555 1234\n" +
            "telephonenumber: +1 408 555 5678\n" +
            "-\n",
            LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Writes_delete_record_with_control()
    {
        var record = new LdifDeleteRecord("ou=Product Development, dc=airius, dc=com")
        {
            Controls = [new LdifControl("1.2.840.113556.1.4.805", criticality: true)],
        };

        Assert.Equal(
            "version: 1\n" +
            "dn: ou=Product Development, dc=airius, dc=com\n" +
            "control: 1.2.840.113556.1.4.805 true\n" +
            "changetype: delete\n",
            LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Writes_modrdn_record()
    {
        var record = new LdifModDnRecord(
            "ou=PD Accountants, ou=Product Development, dc=airius, dc=com",
            "ou=Product Development Accountants",
            deleteOldRdn: false,
            newSuperior: "ou=Accounting, dc=airius, dc=com");

        Assert.Equal(
            "version: 1\n" +
            "dn: ou=PD Accountants, ou=Product Development, dc=airius, dc=com\n" +
            "changetype: modrdn\n" +
            "newrdn: ou=Product Development Accountants\n" +
            "deleteoldrdn: 0\n" +
            "newsuperior: ou=Accounting, dc=airius, dc=com\n",
            LdifWriter.WriteToString([record]));
    }
}
