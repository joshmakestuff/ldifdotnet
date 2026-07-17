#pragma warning disable MA0048 // Deliberate: the writer's options type is colocated with it

using System.Text;

namespace LdifDotNet;

/// <summary>
/// Forward-only LDIF writer (RFC 2849). Emits strictly conformant output:
/// base64-encodes values that are not safe strings and folds long lines.
/// Lines are always terminated with LF. Records that cannot be serialized as
/// valid RFC 2849 are rejected before anything is written: a document may not
/// mix content and change records, content and add records need at least one
/// attribute, and attribute descriptions and control OIDs must match the RFC
/// grammar. The record model itself is deliberately permissive; this writer is
/// the enforcement point.
/// </summary>
public sealed class LdifWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly int? _wrapColumn;
    private readonly bool _includeVersionLine;
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
    }

    /// <summary>Writes one record, validating first that it can be serialized as
    /// strict RFC 2849 within this document (see the class summary).</summary>
    public void WriteRecord(LdifRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);

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
    private static void ValidateRecord(LdifRecord record)
    {
        if (record is LdifChangeRecord change)
        {
            foreach (var control in change.Controls)
            {
                if (!IsNumericOid(control.Oid))
                    throw new ArgumentException($"Control OID '{control.Oid}' is not a valid numeric OID (RFC 2849 ldap-oid).", nameof(record));
            }
        }

        switch (record)
        {
            case LdifContentRecord content:
                if (content.Attributes.Count == 0)
                    throw new ArgumentException("A content record must have at least one attribute (RFC 2849 ldif-attrval-record).", nameof(record));
                if (FirstInvalidAttributeName(content.Attributes) is { } badContentName)
                    throw new ArgumentException($"'{badContentName}' is not a valid attribute description (RFC 2849 AttributeDescription).", nameof(record));
                break;

            case LdifAddRecord add:
                if (add.Attributes.Count == 0)
                    throw new ArgumentException("An add change record must have at least one attribute (RFC 2849 change-add).", nameof(record));
                if (FirstInvalidAttributeName(add.Attributes) is { } badAddName)
                    throw new ArgumentException($"'{badAddName}' is not a valid attribute description (RFC 2849 AttributeDescription).", nameof(record));
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
}
