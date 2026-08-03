namespace LdifDotNet.Tests;

/// <summary>
/// Guards the vendored LDAP schema corpus (see tools/get-schemas.ps1): the files
/// the schema-driven generator work builds on must be present and contain real
/// attributetype/objectclass definitions.
/// </summary>
public class SchemaCorpusTests
{
    public static TheoryData<string> SchemaFiles()
    {
        var data = new TheoryData<string>();
        foreach (string file in AllSchemaFiles())
            data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(SchemaFiles))]
    public void Schema_file_contains_definitions(string relativePath)
    {
        var (attributeTypes, objectClasses, syntaxes) = CountDefinitions(Fixtures.PathOf(relativePath));

        Assert.True(
            attributeTypes + objectClasses + syntaxes > 0,
            $"{relativePath} contains no schema definitions — corrupt fetch?");
    }

    [Fact]
    public void Corpus_contains_expected_schema_sets()
    {
        var files = AllSchemaFiles().ToList();

        foreach (string expected in new[]
        {
            "schemas/openldap/core.schema", "schemas/openldap/cosine.schema",
            "schemas/openldap/inetorgperson.schema", "schemas/openldap/nis.schema",
            "schemas/openldap/msuser.schema",
            "schemas/contrib/eduperson.schema", "schemas/contrib/rfc2307bis.schema",
            "schemas/contrib/sudo.schema", "schemas/contrib/openssh-lpk.schema",
        })
        {
            Assert.Contains(expected, files);
        }

        int totalAttributeTypes = 0, totalObjectClasses = 0;
        foreach (string file in files)
        {
            var (attributeTypes, objectClasses, _) = CountDefinitions(Fixtures.PathOf(file));
            totalAttributeTypes += attributeTypes;
            totalObjectClasses += objectClasses;
        }

        Assert.True(totalAttributeTypes > 250, $"expected a rich corpus, found only {totalAttributeTypes} attribute types");
        Assert.True(totalObjectClasses > 50, $"expected a rich corpus, found only {totalObjectClasses} object classes");
    }

    internal static IEnumerable<string> AllSchemaFiles() =>
        Directory.EnumerateFiles(Fixtures.PathOf("schemas"), "*.schema", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Fixtures.Root, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal);

    internal static (int AttributeTypes, int ObjectClasses, int Syntaxes) CountDefinitions(string path)
    {
        int attributeTypes = 0, objectClasses = 0, syntaxes = 0;
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("attributetype", StringComparison.OrdinalIgnoreCase))
                attributeTypes++;
            else if (trimmed.StartsWith("objectclass", StringComparison.OrdinalIgnoreCase))
                objectClasses++;
            else if (trimmed.StartsWith("ldapsyntax", StringComparison.OrdinalIgnoreCase))
                syntaxes++;
        }
        return (attributeTypes, objectClasses, syntaxes);
    }
}
