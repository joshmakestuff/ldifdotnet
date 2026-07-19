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

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_wrap_column_below_two(int wrap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LdifWriter(new StringWriter(), new LdifWriterOptions { WrapColumn = wrap }));
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

    [Fact]
    public void Rejects_change_record_after_content_record()
    {
        using var writer = new LdifWriter(new StringWriter());
        writer.WriteRecord(new LdifContentRecord("dc=a", new LdifAttribute("dc", "a")));

        var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteRecord(new LdifDeleteRecord("dc=b")));
        Assert.Contains("never both", ex.Message);
    }

    [Fact]
    public void Rejects_content_record_after_change_record()
    {
        using var writer = new LdifWriter(new StringWriter());
        writer.WriteRecord(new LdifDeleteRecord("dc=a"));

        Assert.Throws<InvalidOperationException>(() =>
            writer.WriteRecord(new LdifContentRecord("dc=b", new LdifAttribute("dc", "b"))));
    }

    [Fact]
    public void Rejects_content_record_with_no_attributes()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LdifWriter.WriteToString([new LdifContentRecord("dc=x")]));
        Assert.Contains("at least one attribute", ex.Message);
    }

    [Fact]
    public void Rejects_add_record_with_no_attributes()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LdifWriter.WriteToString([new LdifAddRecord("dc=x")]));
        Assert.Contains("at least one attribute", ex.Message);
    }

    [Fact]
    public void Rejects_content_record_with_an_empty_valued_attribute()
    {
        // A valid attribute is present too, so the empty one would be silently dropped.
        var record = new LdifContentRecord("dc=x",
            new LdifAttribute("dc", "x"),
            new LdifAttribute("cn"));

        var ex = Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
        Assert.Contains("no values", ex.Message);
    }

    [Fact]
    public void Rejects_add_record_with_an_empty_valued_attribute()
    {
        var record = new LdifAddRecord("dc=x",
            new LdifAttribute("dc", "x"),
            new LdifAttribute("cn"));

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Empty_string_value_is_still_valid()
    {
        // One empty-string value is a valid attrval-spec (bare colon); zero values is not.
        string ldif = LdifWriter.WriteToString([new LdifContentRecord("dc=x", new LdifAttribute("seeAlso", ""))]);
        Assert.Contains("\nseeAlso:\n", ldif);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1cn")]            // descr must start with a letter
    [InlineData("cn name")]        // space
    [InlineData("sn_2")]           // underscore
    [InlineData("cn;")]            // empty option
    [InlineData(";binary")]        // empty attribute type
    [InlineData("cn;lang=en")]     // '=' not an option char
    [InlineData("2.5.4.")]         // trailing dot
    [InlineData("2..5")]           // empty OID arc
    public void Rejects_invalid_attribute_descriptions(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LdifWriter.WriteToString([new LdifContentRecord("dc=x", new LdifAttribute(name, "v"))]));
        Assert.Contains("AttributeDescription", ex.Message);
    }

    [Theory]
    [InlineData("cn")]
    [InlineData("userCertificate;binary")]
    [InlineData("2.5.4.3")]
    [InlineData("x-custom-attr")]
    [InlineData("description;lang-en;binary")]
    public void Accepts_valid_attribute_descriptions(string name)
    {
        string ldif = LdifWriter.WriteToString([new LdifContentRecord("dc=x", new LdifAttribute(name, "v"))]);
        Assert.Contains($"{name}: v\n", ldif);
    }

    [Fact]
    public void Rejects_modification_with_invalid_attribute_name()
    {
        var record = new LdifModifyRecord("dc=x", new LdifModification(LdifModificationType.Add, "bad name", "v"));

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Theory]
    [InlineData("not-an-oid")]
    [InlineData("1..2")]
    [InlineData("")]
    public void Rejects_invalid_control_oid(string oid)
    {
        var record = new LdifDeleteRecord("dc=x") { Controls = [new LdifControl(oid)] };

        var ex = Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
        Assert.Contains("numeric OID", ex.Message);
    }

    [Theory]
    [InlineData("file:///tmp/a\nb")]      // LF: would inject document structure
    [InlineData("file:///tmp/a\rb")]      // CR
    [InlineData("file:///tmp/a\r\nb")]    // CRLF
    [InlineData("file:///tmp/a\tb")]      // TAB
    [InlineData("file:///tmp/a\0b")]      // NUL
    [InlineData("file:///tmp/a\u007Fb")]  // DEL
    [InlineData(" file:///tmp/ab")]       // leading space: the reader trims it
    [InlineData("file:///tmp/ab ")]       // trailing space: the reader trims it
    public void Rejects_url_value_a_url_line_cannot_carry(string reference)
    {
        var record = new LdifContentRecord("dc=x",
            new LdifAttribute("jpegPhoto", LdifValue.FromUrl(new Uri(reference))));

        var ex = Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
        Assert.Contains("URL value", ex.Message);
    }

    [Fact]
    public void Rejects_url_with_control_character_in_add_record()
    {
        var record = new LdifAddRecord("dc=x",
            new LdifAttribute("jpegPhoto", LdifValue.FromUrl(new Uri("file:///a\nb"))));

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Rejects_url_with_control_character_in_modification_value()
    {
        var record = new LdifModifyRecord("dc=x",
            new LdifModification(LdifModificationType.Add, "jpegPhoto", LdifValue.FromUrl(new Uri("file:///a\nb"))));

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Rejects_url_with_control_character_in_control_value()
    {
        var record = new LdifDeleteRecord("dc=x")
        {
            Controls = [new LdifControl("1.2.3", criticality: null, LdifValue.FromUrl(new Uri("file:///a\nb")))],
        };

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Theory]
    [InlineData("file:///photos/a b.jpg")]    // interior space is tolerated
    [InlineData("file:///photos/café.jpg")]  // non-ASCII is tolerated
    [InlineData("file:///photos/a%0Ab.jpg")]  // percent-encoded control stays literal text
    public void Tolerated_url_values_round_trip(string reference)
    {
        var record = new LdifContentRecord("dc=x",
            new LdifAttribute("jpegPhoto", LdifValue.FromUrl(new Uri(reference))));

        string ldif = LdifWriter.WriteToString([record]);
        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(ldif)));
        Assert.Equal(reference, reparsed["jpegPhoto"]!.Values[0].Url!.OriginalString);
    }

    [Theory]
    [InlineData("changetype")]
    [InlineData("CHANGETYPE")]  // the reader matches names case-insensitively
    public void Rejects_content_record_that_would_read_back_as_change_record(string name)
    {
        var record = new LdifContentRecord("cn=x,dc=y", new LdifAttribute(name, "delete"));

        var ex = Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
        Assert.Contains("change record", ex.Message);
    }

    [Fact]
    public void Rejects_content_record_with_control_attributes_then_changetype()
    {
        // Mirrors the reader: leading "control" lines are skipped before the
        // changetype check, so this shape also reads back as a change record.
        var record = new LdifContentRecord("cn=x,dc=y",
            new LdifAttribute("control", "1.2.3"),
            new LdifAttribute("changetype", "add"),
            new LdifAttribute("cn", "x"));

        Assert.Throws<ArgumentException>(() => LdifWriter.WriteToString([record]));
    }

    [Fact]
    public void Changetype_attribute_after_another_attribute_round_trips_as_content()
    {
        var record = new LdifContentRecord("cn=x,dc=y",
            new LdifAttribute("cn", "x"),
            new LdifAttribute("changetype", "delete"));

        string ldif = LdifWriter.WriteToString([record]);

        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(ldif)));
        Assert.Equal("delete", reparsed["changetype"]!.Values[0].AsString());
    }

    [Fact]
    public void Control_attributes_without_changetype_round_trip_as_content()
    {
        var record = new LdifContentRecord("cn=x,dc=y",
            new LdifAttribute("control", "v"),
            new LdifAttribute("cn", "x"));

        string ldif = LdifWriter.WriteToString([record]);

        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(ldif)));
        Assert.Equal("v", reparsed["control"]!.Values[0].AsString());
    }

    [Fact]
    public void Changetype_attribute_inside_add_record_round_trips_as_attribute()
    {
        // In an add record the real "changetype: add" line comes first, so a
        // literal changetype attribute is unambiguous and stays an attribute.
        var record = new LdifAddRecord("cn=x,dc=y",
            new LdifAttribute("changetype", "weird"),
            new LdifAttribute("cn", "x"));

        string ldif = LdifWriter.WriteToString([record]);

        var reparsed = Assert.IsType<LdifAddRecord>(Assert.Single(LdifReader.Parse(ldif)));
        Assert.Equal(2, reparsed.Attributes.Count);
        Assert.Equal("weird", reparsed.Attributes[0].Values[0].AsString());
    }

    [Fact]
    public void Invalid_record_writes_nothing()
    {
        var output = new StringWriter();
        using var writer = new LdifWriter(output);

        Assert.Throws<ArgumentException>(() => writer.WriteRecord(new LdifContentRecord("dc=x")));
        Assert.Equal("", output.ToString());

        writer.WriteRecord(new LdifContentRecord("dc=x", new LdifAttribute("dc", "x")));
        Assert.StartsWith("version: 1\n", output.ToString());
    }
}
