#pragma warning disable MA0048 // Deliberate: the writer's options type is colocated with it

using System.Text;

namespace LdifDotNet;

/// <summary>
/// Forward-only LDIF writer (RFC 2849). Emits strictly conformant output:
/// base64-encodes values that are not safe strings and folds long lines.
/// Lines are always terminated with LF. Records that cannot be serialized as
/// valid RFC 2849 are rejected before anything is written: a document may not
/// mix content and change records, content and add records need at least one
/// attribute, attribute descriptions and control OIDs must match the RFC
/// grammar, URL values must not contain characters a url line cannot carry
/// (control characters, leading/trailing spaces), a content record may not
/// begin with attributes that would read back as a change record ("control"
/// lines then "changetype"), and DN-valued fields must parse as RFC 4514
/// (unless <see cref="LdifWriterOptions.ValidateDns"/> is disabled). The record
/// model itself is deliberately permissive; this writer is the enforcement point.
/// </summary>
public sealed class LdifWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly int? _wrapColumn;
    private readonly bool _includeVersionLine;
    private readonly bool _validateDns;
    private bool _firstRecord = true;
    private bool? _changeDocument;

    /// <summary>Creates a writer over the given text writer; options are snapshotted, and
    /// the writer does not take ownership of <paramref name="writer"/>.</summary>
    public LdifWriter(TextWriter writer, LdifWriterOptions? options = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        options ??= new LdifWriterOptions();
        if (options.WrapColumn is { } wrap && wrap < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "WrapColumn must be at least 2, or null to disable folding.");
        _wrapColumn = options.WrapColumn;
        _includeVersionLine = options.IncludeVersionLine;
        _validateDns = options.ValidateDns;
    }

    /// <summary>Writes one record, validating first that it can be serialized as
    /// strict RFC 2849 within this document (see the class summary).</summary>
    public void WriteRecord(LdifRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record, _validateDns);

        bool isChange = record is LdifChangeRecord;
        if (_changeDocument is { } changeDocument && changeDocument != isChange)
        {
            throw new InvalidOperationException(
                $"An LDIF document contains either content records or change records, never both (RFC 2849 ldif-file); this document already contains {(changeDocument ? "change" : "content")} records.");
        }
        _changeDocument = isChange;

        if (_firstRecord)
        {
            if (_includeVersionLine)
                WriteFolded("version: 1");
            _firstRecord = false;
        }
        else
        {
            _writer.Write('\n');
        }

        WriteValueLine("dn", LdifValue.FromString(record.Dn));

        switch (record)
        {
            case LdifContentRecord content:
                WriteAttributes(content.Attributes);
                break;

            case LdifAddRecord add:
                WriteControls(add);
                WriteFolded("changetype: add");
                WriteAttributes(add.Attributes);
                break;

            case LdifDeleteRecord delete:
                WriteControls(delete);
                WriteFolded("changetype: delete");
                break;

            case LdifModifyRecord modify:
                WriteControls(modify);
                WriteFolded("changetype: modify");
                foreach (var mod in modify.Modifications)
                {
                    string op = mod.Type switch
                    {
                        LdifModificationType.Add => "add",
                        LdifModificationType.Delete => "delete",
                        LdifModificationType.Replace => "replace",
                        LdifModificationType.Increment => "increment",
                        _ => throw new ArgumentException($"Unknown modification type {mod.Type}.", nameof(record)),
                    };
                    WriteFolded($"{op}: {mod.AttributeName}");
                    foreach (var value in mod.Values)
                        WriteValueLine(mod.AttributeName, value);
                    WriteFolded("-");
                }
                break;

            case LdifModDnRecord modDn:
                WriteControls(modDn);
                WriteFolded("changetype: modrdn");
                WriteValueLine("newrdn", LdifValue.FromString(modDn.NewRdn));
                WriteFolded($"deleteoldrdn: {(modDn.DeleteOldRdn ? 1 : 0)}");
                if (modDn.NewSuperior is not null)
                    WriteValueLine("newsuperior", LdifValue.FromString(modDn.NewSuperior));
                break;

            default:
                throw new ArgumentException($"Unknown record type {record.GetType()}.", nameof(record));
        }
    }

    /// <summary>Writes all records to a file (UTF-8, LF line endings).</summary>
    public static void WriteFile(string path, IEnumerable<LdifRecord> records, LdifWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        using var streamWriter = new StreamWriter(path);
        using var writer = new LdifWriter(streamWriter, options);
        foreach (var record in records)
            writer.WriteRecord(record);
    }

    /// <summary>Writes all records to an LDIF string.</summary>
    public static string WriteToString(IEnumerable<LdifRecord> records, LdifWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        using var stringWriter = new StringWriter();
        using (var writer = new LdifWriter(stringWriter, options))
        {
            foreach (var record in records)
                writer.WriteRecord(record);
        }

        return stringWriter.ToString();
    }

    /// <summary>Flushes the underlying writer; ownership stays with the caller.</summary>
    public void Dispose() => _writer.Flush();

    /// <summary>
    /// Rejects records the writer could only serialize as invalid RFC 2849.
    /// Runs before any output so a failed record never leaves partial lines.
    /// </summary>
    private static void ValidateRecord(LdifRecord record, bool validateDns)
    {
        if (validateDns)
            ValidateDnFields(record);

        if (record is LdifChangeRecord change)
        {
            foreach (var control in change.Controls)
            {
                if (!IsNumericOid(control.Oid))
                    throw new ArgumentException($"Control OID '{control.Oid}' is not a valid numeric OID (RFC 2849 ldap-oid).", nameof(record));
            }
        }

        if (FirstUnserializableUrlChar(AllValues(record)) is { } badUrlChar)
        {
            throw new ArgumentException(
                $"A URL value contains a control character or leading/trailing space (U+{(int)badUrlChar:X4}); a url line has no escape or base64 form, so the record cannot be serialized as valid RFC 2849. Percent-encode it (e.g. %0A for a newline, %20 for a space).",
                nameof(record));
        }

        switch (record)
        {
            case LdifContentRecord content:
                if (content.Attributes.Count == 0)
                    throw new ArgumentException("A content record must have at least one attribute (RFC 2849 ldif-attrval-record).", nameof(record));
                if (FirstInvalidAttributeName(content.Attributes) is { } badContentName)
                    throw new ArgumentException($"'{badContentName}' is not a valid attribute description (RFC 2849 AttributeDescription).", nameof(record));
                if (FirstEmptyValuedAttributeName(content.Attributes) is { } emptyContentName)
                    throw new ArgumentException($"Attribute '{emptyContentName}' has no values; each attribute needs at least one attrval-spec (RFC 2849).", nameof(record));
                if (WouldReadBackAsChangeRecord(content.Attributes))
                {
                    throw new ArgumentException(
                        "A content record whose leading attributes are 'control' lines followed by 'changetype' reads back as a change record; RFC 2849 gives the writer no way to mark the difference. Reorder the attributes or use a change record type.",
                        nameof(record));
                }
                break;

            case LdifAddRecord add:
                if (add.Attributes.Count == 0)
                    throw new ArgumentException("An add change record must have at least one attribute (RFC 2849 change-add).", nameof(record));
                if (FirstInvalidAttributeName(add.Attributes) is { } badAddName)
                    throw new ArgumentException($"'{badAddName}' is not a valid attribute description (RFC 2849 AttributeDescription).", nameof(record));
                if (FirstEmptyValuedAttributeName(add.Attributes) is { } emptyAddName)
                    throw new ArgumentException($"Attribute '{emptyAddName}' has no values; each attribute needs at least one attrval-spec (RFC 2849).", nameof(record));
                break;

            case LdifModifyRecord modify:
                foreach (var mod in modify.Modifications)
                {
                    if (mod.Type is not (LdifModificationType.Add or LdifModificationType.Delete or LdifModificationType.Replace or LdifModificationType.Increment))
                        throw new ArgumentException($"Unknown modification type {mod.Type}.", nameof(record));
                    if (!IsAttributeDescription(mod.AttributeName))
                        throw new ArgumentException($"'{mod.AttributeName}' is not a valid attribute description (RFC 2849 AttributeDescription).", nameof(record));
                }
                break;

            case LdifDeleteRecord or LdifModDnRecord:
                break;

            default:
                throw new ArgumentException($"Unknown record type {record.GetType()}.", nameof(record));
        }
    }

    /// <summary>
    /// Validates every DN-valued field through <see cref="Dn.Parse"/> — one
    /// definition of "valid DN" shared with the front-door validators. newrdn is
    /// additionally required to be exactly one RDN (RFC 2849 rdn), not a full DN.
    /// </summary>
    private static void ValidateDnFields(LdifRecord record)
    {
        ValidateDn(record.Dn, "dn");
        if (record is LdifModDnRecord modDn)
        {
            if (ParseDnField(modDn.NewRdn, "newrdn").Count != 1)
                throw new ArgumentException($"newrdn '{modDn.NewRdn}' must be a single RDN, not a multi-component DN.", nameof(record));
            if (modDn.NewSuperior is { } newSuperior)
                ValidateDn(newSuperior, "newsuperior");
        }
    }

    private static void ValidateDn(string value, string field) => ParseDnField(value, field);

    private static IReadOnlyList<RelativeDistinguishedName> ParseDnField(string value, string field)
    {
        try
        {
            return Dn.Parse(value);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException(
                $"The {field} '{value}' is not a valid RFC 4514 DN: {e.Message} Set LdifWriterOptions.ValidateDns = false to write it verbatim.",
                nameof(value),
                e);
        }
    }

    /// <summary>
    /// Mirrors the reader's record detection: after the dn line, zero or more
    /// "control" lines followed by a "changetype" line make a change record. A
    /// content record whose leading attributes serialize to that shape cannot
    /// round-trip (it would come back as a different record type), so the writer
    /// rejects it. Agreement with <see cref="LdifReader"/> is pinned by the
    /// writer/reader round-trip tests.
    /// </summary>
    private static bool WouldReadBackAsChangeRecord(IReadOnlyList<LdifAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Name.Equals("control", StringComparison.OrdinalIgnoreCase))
                continue;
            return attribute.Name.Equals("changetype", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>Every value the record would emit: attribute values, modification
    /// values, and control values. DN-valued fields are strings, not values.</summary>
    private static IEnumerable<LdifValue> AllValues(LdifRecord record)
    {
        if (record is LdifChangeRecord change)
        {
            foreach (var control in change.Controls)
            {
                if (control.Value is { } value)
                    yield return value;
            }
        }

        IReadOnlyList<LdifAttribute> attributes = record switch
        {
            LdifContentRecord content => content.Attributes,
            LdifAddRecord add => add.Attributes,
            _ => [],
        };
        foreach (var attribute in attributes)
            foreach (var value in attribute.Values)
                yield return value;

        if (record is LdifModifyRecord modify)
        {
            foreach (var mod in modify.Modifications)
                foreach (var value in mod.Values)
                    yield return value;
        }
    }

    /// <summary>
    /// The first character that makes a URL value unserializable, or null. A url
    /// line has no escape or base64 form, so a control character (a newline would
    /// inject document structure) or a leading/trailing space (the reader trims
    /// both) cannot be carried. Interior spaces and non-ASCII are emitted as
    /// given; percent-encode them for strictly conformant RFC 2849 output.
    /// </summary>
    private static char? FirstUnserializableUrlChar(IEnumerable<LdifValue> values)
    {
        foreach (var value in values)
        {
            if (!value.IsUrl)
                continue;
            string url = value.Url!.OriginalString;
            foreach (char c in url)
            {
                if (c is < ' ' or '\x7F')
                    return c;
            }
            if (url.StartsWith(' ') || url.EndsWith(' '))
                return ' ';
        }
        return null;
    }

    private static string? FirstInvalidAttributeName(IReadOnlyList<LdifAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (!IsAttributeDescription(attribute.Name))
                return attribute.Name;
        }
        return null;
    }

    /// <summary>
    /// The name of the first attribute with no values, or null. An empty-valued
    /// attribute emits no lines, so it would be silently dropped from strict output.
    /// </summary>
    private static string? FirstEmptyValuedAttributeName(IReadOnlyList<LdifAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Values.Count == 0)
                return attribute.Name;
        }
        return null;
    }

    /// <summary>
    /// RFC 2849 AttributeDescription: a numeric OID or a descr (ALPHA then
    /// ALPHA / DIGIT / "-"), followed by zero or more non-empty ";option" parts.
    /// </summary>
    private static bool IsAttributeDescription(string name)
    {
        string[] parts = name.Split(';');
        if (!IsNumericOid(parts[0]) && !IsDescr(parts[0]))
            return false;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                return false;
            foreach (char c in parts[i])
            {
                if (!IsAttrTypeChar(c))
                    return false;
            }
        }
        return true;
    }

    private static bool IsDescr(string text)
    {
        if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
            return false;
        foreach (char c in text)
        {
            if (!IsAttrTypeChar(c))
                return false;
        }
        return true;
    }

    private static bool IsAttrTypeChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';

    /// <summary>RFC 2849 ldap-oid: 1*DIGIT *("." 1*DIGIT).</summary>
    private static bool IsNumericOid(string text)
    {
        bool expectDigit = true;
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
                expectDigit = false;
            else if (c == '.' && !expectDigit)
                expectDigit = true;
            else
                return false;
        }
        return text.Length > 0 && !expectDigit;
    }

    private void WriteAttributes(IReadOnlyList<LdifAttribute> attributes)
    {
        foreach (var attribute in attributes)
            foreach (var value in attribute.Values)
                WriteValueLine(attribute.Name, value);
    }

    private void WriteControls(LdifChangeRecord record)
    {
        foreach (var control in record.Controls)
        {
            var line = new StringBuilder("control: ").Append(control.Oid);
            if (control.Criticality is { } criticality)
                line.Append(' ').Append(criticality ? "true" : "false");
            if (control.Value is { } value)
                line.Append(RenderValueSpec(value));
            WriteFolded(line.ToString());
        }
    }

    private void WriteValueLine(string name, LdifValue value) =>
        WriteFolded(name + RenderValueSpec(value));

    private static string RenderValueSpec(LdifValue value)
    {
        if (value.IsUrl)
            return $":< {value.Url!.OriginalString}";
        if (value.IsBinary)
            return $":: {Convert.ToBase64String(value.AsBytes())}";

        string text = value.AsString();
        if (text.Length == 0)
            return ":";
        if (!IsSafeString(text))
            return $":: {Convert.ToBase64String(value.AsBytes())}";
        return $": {text}";
    }

    /// <summary>
    /// RFC 2849 SAFE-STRING: ASCII without NUL/CR/LF, not starting with space, ':'
    /// or '&lt;'. Values ending with a space are treated as unsafe per note 8.
    /// </summary>
    private static bool IsSafeString(string text)
    {
        char first = text[0];
        if (first is ' ' or ':' or '<')
            return false;
        if (text[^1] == ' ')
            return false;
        foreach (char c in text)
        {
            if (c is '\0' or '\r' or '\n' || c > 0x7F)
                return false;
        }
        return true;
    }

    private void WriteFolded(string line)
    {
        if (_wrapColumn is not { } wrap || line.Length <= wrap)
        {
            _writer.Write(line);
            _writer.Write('\n');
            return;
        }

        // The constructor guarantees wrap >= 2; the loop below relies on a
        // positive increment (wrap - 1) for forward progress.
        System.Diagnostics.Debug.Assert(wrap >= 2, "WrapColumn snapshot must be >= 2.");
        _writer.Write(line.AsSpan(0, wrap));
        _writer.Write('\n');
        for (int position = wrap; position < line.Length; position += wrap - 1)
        {
            _writer.Write(' ');
            _writer.Write(line.AsSpan(position, Math.Min(wrap - 1, line.Length - position)));
            _writer.Write('\n');
        }
    }
}

/// <summary>
/// Options controlling LDIF output. Values are snapshotted by the
/// <see cref="LdifWriter"/> constructor; changes after construction have no
/// effect on an existing writer.
/// </summary>
public sealed class LdifWriterOptions
{
    /// <summary>
    /// Column at which output lines are folded. Default 76 per RFC 2849.
    /// Set to null to disable folding entirely (like ldapsearch -o ldif-wrap=no);
    /// such output is not strictly RFC-conformant but is widely accepted.
    /// </summary>
    public int? WrapColumn { get; init; } = 76;

    /// <summary>Whether to emit "version: 1" before the first record. Default true.</summary>
    public bool IncludeVersionLine { get; init; } = true;

    /// <summary>
    /// Whether DN-valued fields (dn, newrdn, newsuperior) must parse under
    /// <see cref="Dn.Parse"/> before a record is written. Default true: RFC 2849
    /// requires a distinguishedName there, and an invalid DN is typically only
    /// discovered later by the consuming server. Disable to write DNs verbatim —
    /// e.g. to round-trip foreign LDIF whose DNs use constructs
    /// <see cref="Dn.Parse"/> does not support, such as BER hexstring values.
    /// </summary>
    public bool ValidateDns { get; init; } = true;
}
