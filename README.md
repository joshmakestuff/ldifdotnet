# ldifdotnet

Pure managed .NET implementation of LDIF ([RFC 2849](https://www.rfc-editor.org/rfc/rfc2849)) —
no native OpenLDAP binding. Compatibility with OpenLDAP is proven continuously:
CI round-trips OpenLDAP's own test corpus and loads generated data into a real
`slapd` on every push.

| Package | What it does |
|---|---|
| `LdifDotNet` | Read and write LDIF: streaming `LdifReader`/`LdifWriter`, typed content and change records |
| `LdifDotNet.Schema` | Parse LDAP schema files (RFC 4512 / slapd.conf format) into a queryable model |
| `LdifDotNet.Generator` | Deterministic fake directory data (Bogus-powered), including schema-driven generation |
| `LdifDotNet.PowerShell` | `ConvertFrom-Ldif` / `ConvertTo-Ldif` / `Import-Ldif` / `Export-Ldif` cmdlets (PowerShell 7.6+, via PSGallery) |

## Quick start

```csharp
using LdifDotNet;

foreach (var record in LdifReader.ReadFile("dump.ldif"))
    Console.WriteLine(record.Dn);

var entry = new LdifContentRecord("cn=Babs,dc=example,dc=com",
    new LdifAttribute("objectClass", "top", "person"),
    new LdifAttribute("cn", "Babs"),
    new LdifAttribute("sn", "Jensen"));
File.WriteAllText("out.ldif", LdifWriter.WriteToString([entry]));
```

```powershell
Import-Ldif dump.ldif | Where-Object Dn -like '*ou=people*' | ConvertTo-Ldif -NoWrap
```

```csharp
// Fake data for any schema — point it at the same .schema files slapd uses
var schema = LdapSchema.Load("core.schema", "cosine.schema", "inetorgperson.schema", "eduperson.schema");
var options = new SchemaGeneratorOptions { Seed = 42, OptionalAttributeFill = 0.5 };
options.AuxiliaryClasses.Add("eduPerson");
var people = new SchemaEntryGenerator(schema, options).Entries("inetOrgPerson", 100, "ou=people,dc=example,dc=com");
```

Targets .NET 10 (current LTS). Licensed under the
[OpenLDAP Public License v2.8](LICENSE) (SPDX: `OLDAP-2.8`).
