using System.Text.RegularExpressions;

namespace LdifDotNet.Tests;

/// <summary>
/// The OpenLDAP release tag is pinned independently in two tools scripts and
/// restated in THIRD-PARTY-NOTICES.md; nothing structural forces the copies to
/// agree, so this test does — bumping one without the others fails the build.
/// </summary>
public partial class ToolingPinTests
{
    [GeneratedRegex(@"\$(?:Tag|OpenLdapTag)\s*=\s*'(?<tag>OPENLDAP_REL_ENG_[0-9_]+)'", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PinnedTag();

    private static string Tooling(string name) =>
        Path.Combine(AppContext.BaseDirectory, "tooling", name);

    private static string TagOf(string script)
    {
        var match = PinnedTag().Match(File.ReadAllText(Tooling(script)));
        Assert.True(match.Success, $"{script}: no pinned OpenLDAP release tag found");
        return match.Groups["tag"].Value;
    }

    [Fact]
    public void Fixture_and_schema_scripts_pin_the_same_openldap_tag() =>
        Assert.Equal(TagOf("get-openldap-fixtures.ps1"), TagOf("get-schemas.ps1"));

    [Fact]
    public void Third_party_notices_mention_the_pinned_tag() =>
        Assert.Contains(
            TagOf("get-openldap-fixtures.ps1"),
            File.ReadAllText(Tooling("THIRD-PARTY-NOTICES.md")));
}
