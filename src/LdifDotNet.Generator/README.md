# LdifDotNet.Generator

Deterministic fake LDAP directory data, powered by Bogus. Same seed, same
output — always. Generated data is verified loadable into real OpenLDAP in CI.

```csharp
using LdifDotNet;
using LdifDotNet.Generator;

// A complete loadable tree: base entry, OUs, people, groups
var records = new LdifGenerator(new LdifGeneratorOptions
{
    Seed = 42,
    BaseDn = "dc=example,dc=com",
    PeopleCount = 100,
    GroupCount = 10,
}).SampleDirectory();
LdifWriter.WriteFile("seed.ldif", records);
```

Schema-driven generation works from real schema files (via LdifDotNet.Schema):

```csharp
var schema = LdapSchema.Load("core.schema", "cosine.schema", "inetorgperson.schema", "eduperson.schema");
var options = new SchemaGeneratorOptions { Seed = 42, OptionalAttributeFill = 1.0 };
options.AuxiliaryClasses.Add("eduPerson");
options.ExampleValues["eduPersonAffiliation"] = ["faculty", "student", "staff"];
options.Formatters["mail"] = "{{name.firstName}}.{{name.lastName}}@corp.example";
options.Formatters["employeeNumber"] = "EMP-{{randomizer.replacenumbers(#####)}}";
var entries = new SchemaEntryGenerator(schema, options).Entries("inetOrgPerson", 100, "ou=people,dc=example,dc=com");
```

MUST attributes are always filled; MAY attributes per `OptionalAttributeFill`;
values come from your formatters, then example pools, then
well-known-attribute heuristics, then syntax-aware generation.

`Formatters` templates use [Bogus handlebars
tokens](https://github.com/bchavez/Bogus#parse-handlebars) —
`{{dataset.method(args)}}`, case-insensitive; text outside tokens is emitted
verbatim (literal `{{`/`}}` cannot be expressed — the tokenizer owns them, and
its error may quote generated text). A formatter overrides all built-in
generation for its attribute and its output is not checked against the
attribute's syntax. Tokens must return scalar values and are stringified with
the invariant culture; they draw from the generator's seeded randomness (time
tokens from a fixed epoch), so seeded output stays deterministic per package
version regardless of machine culture. Malformed, non-scalar, and always-empty
templates fail construction; the dictionary is snapshotted at construction, and
a key naming a schema attribute covers all of its names (`surname` also formats
`sn`). Empty, whitespace-only, or control-character RDN values fail generation
loudly rather than emitting DNs a real server rejects. RDN collisions are
resolved by drawing fresh values; only text-safe syntaxes fall back to a `-n`
suffix, and structured syntaxes (e.g. INTEGER) fail rather than emit a
corrupted value.

### DN-valued attributes

Attributes whose syntax is Distinguished Name (`member`, `owner`, `seeAlso`,
`manager`, ...) are drawn from real DNs rather than filled with the parent DN,
so generated membership is something a consumer can actually traverse:

```csharp
var people = generator.Entries("inetOrgPerson", 100, "ou=people,dc=example,dc=com");
// No wiring needed: DNs already minted by this generator are the default source.
var groups = generator.Entries("groupOfNames", 10, "ou=groups,dc=example,dc=com");

// Or point an attribute at DNs you own:
options.DnPool["member"] = people.Select(p => p.Dn).ToList();
options.MaxDnValues = 8;         // values per multi-valued DN attribute, default 4
options.DanglingMemberRatio = 0.1; // 10% resolve to nothing, for referential-integrity testing
```

Sources, in order: the attribute's `DnPool`, then the DNs this generator has
already minted, then the parent DN — the last reached only before anything has
been minted. Pool values must parse as RFC 4514 DNs or construction fails.
`SINGLE-VALUE` attributes always get exactly one value whatever `MaxDnValues`
says, an entry never references itself, and a dangling DN's RDN value is
reserved so no later entry can make the reference resolve. `Formatters` and
`ExampleValues` still take precedence and stay single-valued.
