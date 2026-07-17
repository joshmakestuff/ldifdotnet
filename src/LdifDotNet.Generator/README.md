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
var entries = new SchemaEntryGenerator(schema, options).Entries("inetOrgPerson", 100, "ou=people,dc=example,dc=com");
```

MUST attributes are always filled; MAY attributes per `OptionalAttributeFill`;
values come from your example pools, then well-known-attribute heuristics,
then syntax-aware generation.
