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
    public void ReadFile_rejects_invalid_utf8_bytes()
    {
        string dir = Directory.CreateTempSubdirectory("ldifdotnet-reader-tests").FullName;
        try
        {
            // Same bytes Rejects_base64_dn_with_invalid_utf8 uses: the plain-text
            // and base64 paths must agree that invalid UTF-8 is a parse error,
            // not a silent U+FFFD substitution.
            string path = Path.Combine(dir, "invalid.ldif");
            File.WriteAllBytes(path, [.. "dn: cn="u8, 0xFF, 0xFE, 0xFD, .. ",dc=x\ncn: z\n"u8]);

            var ex = Assert.Throws<LdifParseException>(() => LdifReader.ReadFile(path).ToList());
            Assert.Contains("UTF-8", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFile_reads_valid_multibyte_utf8()
    {
        string dir = Directory.CreateTempSubdirectory("ldifdotnet-reader-tests").FullName;
        try
        {
            string path = Path.Combine(dir, "valid.ldif");
            File.WriteAllBytes(path, "dn: dc=x\ndescription: 営業部\n"u8.ToArray());

            var record = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.ReadFile(path).ToList()));
            Assert.Equal("営業部", record["description"]!.Values[0].AsString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
