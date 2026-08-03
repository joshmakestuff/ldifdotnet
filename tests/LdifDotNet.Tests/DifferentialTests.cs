#pragma warning disable MA0048 // Deliberate: the gating attribute is colocated with the tests it gates

using System.Diagnostics;
using System.Globalization;
using LdifDotNet.Generator;
using LdifDotNet.Schema;

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

    private static string SchemaDir =>
        Environment.GetEnvironmentVariable("LDIF_SCHEMA_DIR") ?? "/etc/ldap/schema";

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
        var records = new LdifGenerator(new LdifGeneratorOptions
        {
            Seed = 20260717,
            PeopleCount = 50,
            GroupCount = 8,
        }).SampleDirectory();

        AssertLoadsAndRoundTrips(
            records,
            [$"{SchemaDir}/core.schema", $"{SchemaDir}/cosine.schema", $"{SchemaDir}/inetorgperson.schema"]);
    }

    [DifferentialFact]
    public void Schema_generated_eduperson_entries_load_into_openldap()
    {
        string[] schemaFiles =
        [
            $"{SchemaDir}/core.schema",
            $"{SchemaDir}/cosine.schema",
            $"{SchemaDir}/inetorgperson.schema",
            Fixtures.PathOf("schemas/contrib/eduperson.schema"),
        ];

        var options = new SchemaGeneratorOptions { Seed = 424242, OptionalAttributeFill = 1.0 };
        options.AuxiliaryClasses.Add("eduPerson");
        var generator = new SchemaEntryGenerator(LdapSchema.Load(schemaFiles), options);

        var records = new List<LdifContentRecord>
        {
            new("dc=example,dc=com",
                new LdifAttribute("objectClass", "top", "dcObject", "organization"),
                new LdifAttribute("dc", "example"),
                new LdifAttribute("o", "Example Org")),
            new("ou=people,dc=example,dc=com",
                new LdifAttribute("objectClass", "top", "organizationalUnit"),
                new LdifAttribute("ou", "people")),
        };
        records.AddRange(generator.Entries("inetOrgPerson", 25, "ou=people,dc=example,dc=com"));

        AssertLoadsAndRoundTrips(records, schemaFiles);
    }

    /// <summary>
    /// Reads the subschema subentry from a running slapd — the input shape
    /// LdapSchema.ParseSubschema exists for, and one slapcat cannot produce
    /// (cn=Subschema is synthesized at runtime, not stored in the database).
    /// Every definition a real server publishes must parse.
    /// </summary>
    [DifferentialFact]
    public void Live_subschema_parses_completely()
    {
        string work = Directory.CreateTempSubdirectory("ldifdotnet-subschema").FullName;
        string confFile = WriteSlapdConf(work,
            [$"{SchemaDir}/core.schema", $"{SchemaDir}/cosine.schema", $"{SchemaDir}/inetorgperson.schema"]);
        string url = "ldapi://" + Uri.EscapeDataString(Path.Combine(work, "ldapi"));

        // slapd's parent process exits only after the listener is up, so a zero
        // exit code means the server is already answering.
        var slapd = Run(Tool("slapd"), "-f", confFile, "-h", url);
        Assert.True(slapd.ExitCode == 0, $"slapd failed to start:\n{slapd.StdOut}{slapd.StdErr}");
        try
        {
            var search = Run(Tool("ldapsearch"), "-LL", "-H", url, "-x", "-s", "base",
                "-b", "cn=Subschema", "(objectClass=subschema)", "attributeTypes", "objectClasses");
            Assert.True(search.ExitCode == 0, $"ldapsearch failed:\n{search.StdErr}");

            // Dogfood: ldapsearch answers in LDIF, so our own reader unfolds it.
            var entry = Assert.IsType<LdifContentRecord>(Assert.Single(LdifReader.Parse(search.StdOut)));
            var attributeTypes = entry["attributeTypes"];
            var objectClasses = entry["objectClasses"];
            Assert.NotNull(attributeTypes);
            Assert.NotNull(objectClasses);

            var schema = LdapSchema.ParseSubschema(
                attributeTypes.Values.Select(v => v.AsString()),
                objectClasses.Values.Select(v => v.AsString()));

            Assert.True(schema.UnparsedDefinitions.Count == 0,
                "definitions a live server published failed to parse:\n" + string.Join(
                    "\n", schema.UnparsedDefinitions.Select(u => $"{u.Error}: {u.Definition}")));
            Assert.True(schema.AttributeTypes.Count > 200,
                $"expected the full published attribute set, got {schema.AttributeTypes.Count}");

            // The shape schema files never contain: cn published with SUP and no SYNTAX.
            var cn = schema.FindAttributeType("cn");
            Assert.NotNull(cn);
            Assert.Equal("name", cn.SuperiorName);
            Assert.Null(cn.Syntax);
        }
        finally
        {
            StopSlapd(Path.Combine(work, "slapd.pid"));
        }
    }

    private static void AssertLoadsAndRoundTrips(
        IReadOnlyList<LdifContentRecord> records, IEnumerable<string> schemaIncludes)
    {
        string work = Directory.CreateTempSubdirectory("ldifdotnet-differential").FullName;
        string confFile = WriteSlapdConf(work, schemaIncludes);

        string dataFile = Path.Combine(work, "data.ldif");
        // slapadd (unlike ldapadd) rejects the RFC 2849 version line — a real
        // OpenLDAP behavior this harness surfaced on its first run.
        LdifWriter.WriteFile(dataFile, records, new LdifWriterOptions { IncludeVersionLine = false });

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

    /// <summary>Writes a minimal slapd.conf into <paramref name="work"/> and returns its path.</summary>
    private static string WriteSlapdConf(string work, IEnumerable<string> schemaIncludes)
    {
        string databaseDir = Path.Combine(work, "db");
        Directory.CreateDirectory(databaseDir);
        string modulePath = Environment.GetEnvironmentVariable("LDIF_SLAPD_MODULEPATH") ?? "/usr/lib/ldap";

        string confFile = Path.Combine(work, "slapd.conf");
        string includes = string.Join('\n', schemaIncludes.Select(path => $"include {path}"));
        File.WriteAllText(confFile, $"""
            {includes}
            modulepath {modulePath}
            moduleload back_mdb
            pidfile {Path.Combine(work, "slapd.pid")}
            database mdb
            suffix "dc=example,dc=com"
            rootdn "cn=admin,dc=example,dc=com"
            directory {databaseDir}

            """);
        return confFile;
    }

    /// <summary>Kills the slapd whose pid file startup wrote; tolerates it being gone already.</summary>
    private static void StopSlapd(string pidFile)
    {
        if (!File.Exists(pidFile)
            || !int.TryParse(File.ReadAllText(pidFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return;
        }
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill();
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
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
            UseShellExecute = false,
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
