using System.Diagnostics;
using LdifDotNet.Generator;

namespace LdifDotNet.Tests;

/// <summary>
/// Runs only where real OpenLDAP tools are installed (the differential CI job
/// sets LDIF_DIFFERENTIAL=1); skipped everywhere else.
/// </summary>
public sealed class DifferentialFactAttribute : FactAttribute
{
    public DifferentialFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LDIF_DIFFERENTIAL") != "1")
            Skip = "Differential tests need OpenLDAP tools; set LDIF_DIFFERENTIAL=1 to enable.";
    }
}

/// <summary>
/// Differential tests against a real OpenLDAP installation: our writer's output
/// must be accepted by slapadd, and slapcat's output must parse back through our
/// reader to semantically identical entries.
/// </summary>
public class DifferentialTests
{
    /// <summary>Attributes slapd generates; not part of what we wrote.</summary>
    private static readonly HashSet<string> OperationalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "structuralObjectClass", "entryUUID", "entryCSN", "creatorsName",
        "createTimestamp", "modifiersName", "modifyTimestamp", "contextCSN",
    };

    [DifferentialFact]
    public void Openldap_version_matches_pinned_claim()
    {
        string expected = Environment.GetEnvironmentVariable("LDIF_EXPECTED_OPENLDAP") ?? "2.6";
        var result = Run(Tool("slapd"), "-V");

        Assert.Contains($"slapd {expected}", result.StdOut + result.StdErr);
    }

    [DifferentialFact]
    public void Generated_directory_loads_into_openldap_and_round_trips()
    {
        string work = Directory.CreateTempSubdirectory("ldifdotnet-differential").FullName;
        string databaseDir = Path.Combine(work, "db");
        Directory.CreateDirectory(databaseDir);
        string schemaDir = Environment.GetEnvironmentVariable("LDIF_SCHEMA_DIR") ?? "/etc/ldap/schema";
        string modulePath = Environment.GetEnvironmentVariable("LDIF_SLAPD_MODULEPATH") ?? "/usr/lib/ldap";

        string confFile = Path.Combine(work, "slapd.conf");
        File.WriteAllText(confFile, $"""
            include {schemaDir}/core.schema
            include {schemaDir}/cosine.schema
            include {schemaDir}/inetorgperson.schema
            modulepath {modulePath}
            moduleload back_mdb
            database mdb
            suffix "dc=example,dc=com"
            rootdn "cn=admin,dc=example,dc=com"
            directory {databaseDir}

            """);

        var records = new LdifGenerator(new LdifGeneratorOptions
        {
            Seed = 20260717,
            PeopleCount = 50,
            GroupCount = 8,
        }).SampleDirectory();

        string dataFile = Path.Combine(work, "data.ldif");
        LdifWriter.WriteFile(dataFile, records);

        var slapadd = Run(Tool("slapadd"), "-f", confFile, "-l", dataFile);
        Assert.True(slapadd.ExitCode == 0, $"slapadd rejected our writer's LDIF:\n{slapadd.StdErr}");

        var slapcat = Run(Tool("slapcat"), "-f", confFile);
        Assert.True(slapcat.ExitCode == 0, $"slapcat failed:\n{slapcat.StdErr}");

        var exported = LdifReader.Parse(slapcat.StdOut).Cast<LdifContentRecord>().ToList();
        Assert.Equal(records.Count, exported.Count);

        var expectedByDn = records.ToDictionary(r => r.Dn, Fingerprint, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in exported)
        {
            Assert.True(
                expectedByDn.TryGetValue(entry.Dn, out string? expected),
                $"slapcat exported unexpected DN '{entry.Dn}'");
            Assert.Equal(expected, Fingerprint(entry));
        }
    }

    /// <summary>
    /// Canonical, order-independent rendering of an entry's user attributes so
    /// entries can be compared across slapd's attribute reordering.
    /// </summary>
    private static string Fingerprint(LdifContentRecord record)
    {
        var attributes = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var attribute in record.Attributes)
        {
            if (OperationalAttributes.Contains(attribute.Name))
                continue;
            string key = attribute.Name.ToLowerInvariant();
            if (!attributes.TryGetValue(key, out var values))
                attributes[key] = values = [];
            values.AddRange(attribute.Values.Select(v => Convert.ToBase64String(v.AsBytes())));
        }

        foreach (var values in attributes.Values)
            values.Sort(StringComparer.Ordinal);
        return string.Join("\n", attributes.Select(kv => $"{kv.Key}: {string.Join(" | ", kv.Value)}"));
    }

    private static string Tool(string name)
    {
        string sbin = $"/usr/sbin/{name}";
        return File.Exists(sbin) ? sbin : name;
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var stdOut = process.StandardOutput.ReadToEndAsync();
        var stdErr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdOut.Result, stdErr.Result);
    }
}
