# LdifDotNet.Schema

Parser for LDAP schema definitions: RFC 4512 `attributetype` / `objectclass`
descriptions in slapd.conf schema-file format, including `objectidentifier`
OID macros — and the bare definition values a live server publishes in its
subschema subentry. Dependency-free.

```csharp
using LdifDotNet.Schema;

var schema = LdapSchema.Load("core.schema", "cosine.schema", "inetorgperson.schema");

var person = schema.FindObjectClass("inetOrgPerson");
var required = schema.RequiredAttributeNames(person);   // MUST, inherited through SUP chain
var optional = schema.OptionalAttributeNames(person);   // MAY, inherited through SUP chain

var sn = schema.FindAttributeType("surname");            // lookup by any name or OID
Console.WriteLine(sn.Syntax);                            // 1.3.6.1.4.1.1466.115.121.1.15
```

Schema read from a live server's `cn=Subschema` entry parses leniently — the
consumer cannot fix a server's schema, so a definition that fails to parse is
preserved raw in `UnparsedDefinitions` instead of failing the whole schema:

```csharp
// attributeTypes / objectClasses values fetched from the subschema subentry
var schema = LdapSchema.ParseSubschema(attributeTypeValues, objectClassValues);
foreach (var bad in schema.UnparsedDefinitions)
    Console.WriteLine($"unparsed {bad.Kind}: {bad.Error}");

// Strict single-definition parsing is also available:
var cn = LdapAttributeType.Parse("( 2.5.4.3 NAME ( 'cn' 'commonName' ) SUP name )");
```

Proven against OpenLDAP's complete shipped schema set plus eduPerson,
rfc2307bis, sudo, and openssh-lpk — and, for subschema input, against the
`cn=Subschema` entry a real OpenLDAP 2.6 server publishes.
