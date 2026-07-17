# LdifDotNet.Schema

Parser for LDAP schema definitions: RFC 4512 `attributetype` / `objectclass`
descriptions in slapd.conf schema-file format, including `objectidentifier`
OID macros. Dependency-free.

```csharp
using LdifDotNet.Schema;

var schema = LdapSchema.Load("core.schema", "cosine.schema", "inetorgperson.schema");

var person = schema.FindObjectClass("inetOrgPerson");
var required = schema.RequiredAttributeNames(person);   // MUST, inherited through SUP chain
var optional = schema.OptionalAttributeNames(person);   // MAY, inherited through SUP chain

var sn = schema.FindAttributeType("surname");            // lookup by any name or OID
Console.WriteLine(sn.Syntax);                            // 1.3.6.1.4.1.1466.115.121.1.15
```

Proven against OpenLDAP's complete shipped schema set plus eduPerson,
rfc2307bis, sudo, and openssh-lpk.
