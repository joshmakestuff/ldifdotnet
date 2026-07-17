// Consumer smoke test: proves a clean project can restore the packed NuGet
// packages and exercise each one. Run after `dotnet pack -o packages` from the
// repository root; any failure throws and exits non-zero.
using LdifDotNet;
using LdifDotNet.Generator;
using LdifDotNet.Schema;

// LdifDotNet: parse → write → reparse round trip.
var parsed = LdifReader.Parse("dn: dc=example\ndc: example\n");
string written = LdifWriter.WriteToString(parsed);
var reparsed = LdifReader.Parse(written);
Check(reparsed.Count == 1 && reparsed[0].Dn == "dc=example", "core round trip");

// LdifDotNet.Schema: parse definitions.
var schema = LdapSchema.Parse(
    """
    attributetype ( 1.2.3.4.1 NAME 'cn' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )
    attributetype ( 1.2.3.4.2 NAME 'sn' SYNTAX 1.3.6.1.4.1.1466.115.121.1.15 )
    objectclass ( 1.2.3.4.3 NAME 'smokeThing' STRUCTURAL MUST ( cn $ sn ) )
    """);
Check(schema.FindObjectClass("smokeThing") is { Kind: LdapObjectClassKind.Structural }, "schema parse");

// LdifDotNet.Generator: deterministic schema-driven entries.
var generator = new SchemaEntryGenerator(schema, new SchemaGeneratorOptions { Seed = 42 });
var entries = generator.Entries("smokeThing", 3, "dc=example");
Check(entries.Count == 3 && entries.All(e => e["cn"] is not null), "schema-driven generation");

Console.WriteLine("consumer smoke: OK");

static void Check(bool condition, string what)
{
    if (!condition)
        throw new InvalidOperationException($"consumer smoke failed: {what}");
}
