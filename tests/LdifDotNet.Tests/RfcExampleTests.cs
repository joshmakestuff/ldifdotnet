using System.Text;

namespace LdifDotNet.Tests;

/// <summary>Golden tests: the worked examples from RFC 2849 itself.</summary>
public class RfcExampleTests
{
    private static IReadOnlyList<LdifRecord> ReadExample(int number) =>
        LdifReader.ReadFile(Fixtures.PathOf("rfc2849", $"example{number}.ldif")).ToList();

    [Fact]
    public void Example1_two_simple_entries()
    {
        var records = ReadExample(1);

        Assert.Equal(2, records.Count);
        var barbara = Assert.IsType<LdifContentRecord>(records[0]);
        Assert.Equal("cn=Barbara Jensen, ou=Product Development, dc=airius, dc=com", barbara.Dn);
        Assert.Equal(3, barbara["objectclass"]!.Values.Count);
        Assert.Equal(3, barbara["cn"]!.Values.Count);
        Assert.Equal("A big sailing fan.", barbara["description"]!.Values[0].AsString());

        var bjorn = Assert.IsType<LdifContentRecord>(records[1]);
        Assert.Equal("cn=Bjorn Jensen, ou=Accounting, dc=airius, dc=com", bjorn.Dn);
        Assert.Equal("+1 408 555 1212", bjorn["telephonenumber"]!.Values[0].AsString());
    }

    [Fact]
    public void Example2_folded_value_is_unfolded()
    {
        var records = ReadExample(2);

        var record = Assert.IsType<LdifContentRecord>(Assert.Single(records));
        Assert.Equal(
            "Babs is a big sailing fan, and travels extensively in search of perfect sailing conditions.",
            record["description"]!.Values[0].AsString());
    }

    [Fact]
    public void Example3_base64_value_is_decoded()
    {
        var records = ReadExample(3);

        var record = Assert.IsType<LdifContentRecord>(Assert.Single(records));
        var description = record["description"]!.Values[0];
        Assert.True(description.IsBinary);
        string text = description.AsString();
        Assert.StartsWith("What a careful reader you are!", text);
        Assert.Contains("a control character in it (a CR).\r", text);
        Assert.EndsWith("you should really get out more.", text);
    }

    [Fact]
    public void Example4_base64_dns_and_utf8_values()
    {
        var records = ReadExample(4);

        Assert.Equal(2, records.Count);
        string expectedOuDn = Encoding.UTF8.GetString(Convert.FromBase64String("b3U95Za25qWt6YOoLG89QWlyaXVz"));
        Assert.Equal(expectedOuDn, records[0].Dn);
        Assert.StartsWith("ou=", records[0].Dn);

        var person = Assert.IsType<LdifContentRecord>(records[1]);
        Assert.Equal("rogasawara", person["uid"]!.Values[0].AsString());
        // Language-tagged attribute options are preserved as part of the name.
        Assert.NotNull(person["givenname;lang-ja"]);
        Assert.Equal("Rodney Ogasawara", person["cn;lang-en"]!.Values[0].AsString());
    }

    [Fact]
    public void Example5_url_value_reference()
    {
        var records = ReadExample(5);

        var record = Assert.IsType<LdifContentRecord>(Assert.Single(records));
        var photo = record["jpegphoto"]!.Values[0];
        Assert.True(photo.IsUrl);
        Assert.Equal(new Uri("file:///usr/local/directory/photos/hjensen.jpg"), photo.Url);
        Assert.Equal(2, record["cn"]!.Values.Count);
    }

    [Fact]
    public void Example6_change_records()
    {
        var records = ReadExample(6);

        Assert.Equal(6, records.Count);

        var add = Assert.IsType<LdifAddRecord>(records[0]);
        Assert.Equal("cn=Fiona Jensen, ou=Marketing, dc=airius, dc=com", add.Dn);
        Assert.True(add.Attributes.First(a => a.Name == "jpegphoto").Values[0].IsUrl);

        Assert.IsType<LdifDeleteRecord>(records[1]);

        var rename = Assert.IsType<LdifModDnRecord>(records[2]);
        Assert.Equal("cn=Paula Jensen", rename.NewRdn);
        Assert.True(rename.DeleteOldRdn);
        Assert.Null(rename.NewSuperior);

        var move = Assert.IsType<LdifModDnRecord>(records[3]);
        Assert.False(move.DeleteOldRdn);
        Assert.Equal("ou=Accounting, dc=airius, dc=com", move.NewSuperior);

        var modify = Assert.IsType<LdifModifyRecord>(records[4]);
        Assert.Equal(4, modify.Modifications.Count);
        Assert.Equal(LdifModificationType.Add, modify.Modifications[0].Type);
        Assert.Equal("postaladdress", modify.Modifications[0].AttributeName);
        Assert.Single(modify.Modifications[0].Values);
        Assert.Equal(LdifModificationType.Delete, modify.Modifications[1].Type);
        Assert.Empty(modify.Modifications[1].Values);
        Assert.Equal(LdifModificationType.Replace, modify.Modifications[2].Type);
        Assert.Equal(2, modify.Modifications[2].Values.Count);
        Assert.Equal(LdifModificationType.Delete, modify.Modifications[3].Type);
        Assert.Equal("+1 408 555 9876", modify.Modifications[3].Values[0].AsString());

        var emptyReplace = Assert.IsType<LdifModifyRecord>(records[5]);
        Assert.Equal(2, emptyReplace.Modifications.Count);
        Assert.All(emptyReplace.Modifications, m => Assert.Empty(m.Values));
    }

    [Fact]
    public void Example7_change_record_with_control()
    {
        var records = ReadExample(7);

        var delete = Assert.IsType<LdifDeleteRecord>(Assert.Single(records));
        var control = Assert.Single(delete.Controls);
        Assert.Equal("1.2.840.113556.1.4.805", control.Oid);
        Assert.True(control.Criticality);
        Assert.Null(control.Value);
    }
}
