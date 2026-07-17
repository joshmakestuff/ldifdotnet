using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace LdifDotNet.Tests;

public class PublicApiTests
{
    /// <summary>
    /// The approved file is the API contract. If this test fails, the public surface
    /// changed: review the diff, then re-approve by running tests once with the
    /// environment variable UPDATE_PUBLIC_API=1 and committing the updated file.
    /// </summary>
    [Fact]
    public void Public_api_surface_matches_approved_contract()
    {
        string current = typeof(LdifRecord).Assembly
            .GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false })
            .ReplaceLineEndings("\n").Trim();

        string approvedFile = Path.Combine(SourceDirectory(), "PublicApi.approved.txt");

        if (Environment.GetEnvironmentVariable("UPDATE_PUBLIC_API") == "1")
            File.WriteAllText(approvedFile, current + "\n");

        string approved = File.Exists(approvedFile)
            ? File.ReadAllText(approvedFile).ReplaceLineEndings("\n").Trim()
            : "";

        Assert.Equal(approved, current);
    }

    private static string SourceDirectory([CallerFilePath] string thisFile = "") =>
        Path.GetDirectoryName(thisFile)!;
}
