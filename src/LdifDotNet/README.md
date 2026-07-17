# LdifDotNet

Pure managed LDIF (RFC 2849) reader and writer for .NET. No native dependencies;
compatibility with OpenLDAP is verified in CI against a real `slapd`.

```csharp
using LdifDotNet;

// Streaming read: content and change records come back as typed objects
foreach (var record in LdifReader.ReadFile("dump.ldif"))
{
    if (record is LdifContentRecord entry)
        Console.WriteLine($"{entry.Dn}: {entry["cn"]?.Values[0]}");
}

// Strict RFC output: automatic base64 for unsafe values, 76-column folding
string ldif = LdifWriter.WriteToString(records);

// ldapsearch-style unwrapped output
string unwrapped = LdifWriter.WriteToString(records, new LdifWriterOptions { WrapColumn = null });
```

Handles folding, comments, base64 values and DNs, URL value references
(never auto-resolved), all changetypes, controls, and OpenLDAP's
modify-increment extension (RFC 4525).
