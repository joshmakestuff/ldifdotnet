namespace LdifDotNet.Tests;

/// <summary>
/// Every fixture file — RFC 2849 examples and the vendored OpenLDAP corpus — must
/// parse, and parse(write(parse(file))) must be structurally identical.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<string> LdifFiles()
    {
        var data = new TheoryData<string>();
        foreach (string file in Fixtures.AllLdifFiles())
            data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(LdifFiles))]
    public void Fixture_round_trips(string relativePath)
    {
        var original = LdifReader.ReadFile(Fixtures.PathOf(relativePath)).ToList();
        Assert.NotEmpty(original);

        string written = LdifWriter.WriteToString(original);
        var reparsed = LdifReader.Parse(written);

        Assert.Equal(original.Count, reparsed.Count);
        for (int i = 0; i < original.Count; i++)
            AssertRecordsEqual(original[i], reparsed[i]);
    }

    // Regression for quadratic unfolding: a 2 MB value folds into ~28k physical
    // lines; per-continuation string concatenation would copy ~28 GB here.
    [Fact]
    public void Large_folded_value_round_trips()
    {
        string large = new('a', 2_000_000);
        var record = new LdifContentRecord("dc=x", new LdifAttribute("description", large));

        string written = LdifWriter.WriteToString([record]);
        var reparsed = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(written)));

        Assert.Equal(large, reparsed["description"]!.Values[0].AsString());
    }

    [Fact]
    public void Fixture_discovery_finds_both_corpora()
    {
        var files = Fixtures.AllLdifFiles().ToList();
        Assert.Contains(files, f => f.StartsWith("rfc2849", StringComparison.Ordinal));
        Assert.Contains(files, f => f.StartsWith("openldap", StringComparison.Ordinal));
        Assert.True(files.Count > 40, $"expected both corpora, found only {files.Count} files");
    }

    private static void AssertRecordsEqual(LdifRecord expected, LdifRecord actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Dn, actual.Dn);

        switch (expected)
        {
            case LdifContentRecord content:
                AssertAttributesEqual(content.Attributes, ((LdifContentRecord)actual).Attributes);
                break;
            case LdifAddRecord add:
                AssertAttributesEqual(add.Attributes, ((LdifAddRecord)actual).Attributes);
                AssertControlsEqual(add, (LdifChangeRecord)actual);
                break;
            case LdifDeleteRecord delete:
                AssertControlsEqual(delete, (LdifChangeRecord)actual);
                break;
            case LdifModifyRecord modify:
                var actualModify = (LdifModifyRecord)actual;
                Assert.Equal(modify.Modifications.Count, actualModify.Modifications.Count);
                for (int i = 0; i < modify.Modifications.Count; i++)
                {
                    var e = modify.Modifications[i];
                    var a = actualModify.Modifications[i];
                    Assert.Equal(e.Type, a.Type);
                    Assert.Equal(e.AttributeName, a.AttributeName, ignoreCase: true);
                    Assert.Equal(e.Values, a.Values);
                }
                AssertControlsEqual(modify, actualModify);
                break;
            case LdifModDnRecord modDn:
                var actualModDn = (LdifModDnRecord)actual;
                Assert.Equal(modDn.NewRdn, actualModDn.NewRdn);
                Assert.Equal(modDn.DeleteOldRdn, actualModDn.DeleteOldRdn);
                Assert.Equal(modDn.NewSuperior, actualModDn.NewSuperior);
                AssertControlsEqual(modDn, actualModDn);
                break;
            default:
                Assert.Fail($"unhandled record type {expected.GetType()}");
                break;
        }
    }

    private static void AssertAttributesEqual(
        IReadOnlyList<LdifAttribute> expected, IReadOnlyList<LdifAttribute> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name, ignoreCase: true);
            Assert.Equal(expected[i].Values, actual[i].Values);
        }
    }

    private static void AssertControlsEqual(LdifChangeRecord expected, LdifChangeRecord actual)
    {
        Assert.Equal(expected.Controls.Count, actual.Controls.Count);
        for (int i = 0; i < expected.Controls.Count; i++)
        {
            Assert.Equal(expected.Controls[i].Oid, actual.Controls[i].Oid);
            Assert.Equal(expected.Controls[i].Criticality, actual.Controls[i].Criticality);
            Assert.Equal(expected.Controls[i].Value, actual.Controls[i].Value);
        }
    }
}
