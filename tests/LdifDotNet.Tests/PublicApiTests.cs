using System.Reflection;
using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace LdifDotNet.Tests;

public class PublicApiTests
{
    /// <summary>
    /// The approved files are the API contracts. If a test here fails, a public
    /// surface changed: review the diff, then re-approve by running tests once with
    /// the environment variable UPDATE_PUBLIC_API=1 and committing the updated file.
    /// </summary>
    [Fact]
    public void Core_public_api_matches_approved_contract() =>
        AssertApproved(typeof(LdifRecord).Assembly, "PublicApi.approved.txt");

    [Fact]
    public void Generator_public_api_matches_approved_contract() =>
        AssertApproved(typeof(Generator.LdifGenerator).Assembly, "PublicApi.Generator.approved.txt");

    private static void AssertApproved(Assembly assembly, string approvedFileName)
    {
        string current = assembly
            .GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false })
            .ReplaceLineEndings("\n").Trim();

        string approvedFile = Path.Combine(SourceDirectory(), approvedFileName);

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
