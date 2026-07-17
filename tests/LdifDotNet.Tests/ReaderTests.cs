namespace LdifDotNet.Tests;

public class ReaderTests
{
    private static readonly string InvalidUtf8Base64 = Convert.ToBase64String([0xFF, 0xFE, 0xFD]);

    [Fact]
    public void Rejects_base64_dn_with_invalid_utf8()
    {
        string ldif = $"dn:: {InvalidUtf8Base64}\ncn: x\n";

        var ex = Assert.Throws<LdifParseException>(() => LdifReader.Parse(ldif));
        Assert.Contains("UTF-8", ex.Message);
        Assert.Equal(1, ex.LineNumber);
    }

    [Fact]
    public void Rejects_base64_newrdn_with_invalid_utf8()
    {
        string ldif =
            "dn: dc=x\n" +
            "changetype: modrdn\n" +
            $"newrdn:: {InvalidUtf8Base64}\n" +
            "deleteoldrdn: 0\n";

        var ex = Assert.Throws<LdifParseException>(() => LdifReader.Parse(ldif));
        Assert.Contains("newrdn", ex.Message);
        Assert.Equal(3, ex.LineNumber);
    }

    [Fact]
    public void Rejects_base64_newsuperior_with_invalid_utf8()
    {
        string ldif =
            "dn: dc=x\n" +
            "changetype: modrdn\n" +
            "newrdn: dc=y\n" +
            "deleteoldrdn: 0\n" +
            $"newsuperior:: {InvalidUtf8Base64}\n";

        var ex = Assert.Throws<LdifParseException>(() => LdifReader.Parse(ldif));
        Assert.Contains("newsuperior", ex.Message);
        Assert.Equal(5, ex.LineNumber);
    }

    [Fact]
    public void Valid_base64_dn_decodes_as_utf8()
    {
        string ldif = "dn:: b3U95Za25qWt6YOoLG89QWlyaXVz\nou: x\n"; // ou=営業部,o=Airius

        var record = Assert.Single(LdifReader.Parse(ldif));
        Assert.Equal("ou=営業部,o=Airius", record.Dn);
    }

    [Fact]
    public void Rejects_relative_url_reference()
    {
        string ldif = "dn: dc=x\njpegPhoto:< relative/photo.jpg\n";

        Assert.Throws<LdifParseException>(() => LdifReader.Parse(ldif));
    }

    [Fact]
    public void Absolute_url_reference_round_trips()
    {
        var record = new LdifContentRecord(
            "dc=x",
            new LdifAttribute("jpegPhoto", LdifValue.FromUrl(new Uri("file:///photos/x.jpg"))));

        string written = LdifWriter.WriteToString([record]);
        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(written)));

        Assert.Equal(new Uri("file:///photos/x.jpg"), reparsed["jpegPhoto"]!.Values[0].Url);
    }
}
