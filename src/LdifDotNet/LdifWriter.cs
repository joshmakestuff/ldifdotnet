using System.Text;

namespace LdifDotNet;

/// <summary>
/// Forward-only LDIF writer (RFC 2849). Emits strictly conformant output:
/// base64-encodes values that are not safe strings and folds long lines.
/// Lines are always terminated with LF.
/// </summary>
public sealed class LdifWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly LdifWriterOptions _options;
    private bool _firstRecord = true;

    public LdifWriter(TextWriter writer, LdifWriterOptions? options = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options ?? new LdifWriterOptions();
        if (_options.WrapColumn is { } wrap && wrap < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "WrapColumn must be at least 2, or null to disable folding.");
    }

    public void WriteRecord(LdifRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_firstRecord)
        {
            if (_options.IncludeVersionLine)
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

    public void Dispose() => _writer.Flush();

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
        if (_options.WrapColumn is not { } wrap || line.Length <= wrap)
        {
            _writer.Write(line);
            _writer.Write('\n');
            return;
        }

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

/// <summary>Options controlling LDIF output.</summary>
public sealed class LdifWriterOptions
{
    /// <summary>
    /// Column at which output lines are folded. Default 76 per RFC 2849.
    /// Set to null to disable folding entirely (like ldapsearch -o ldif-wrap=no);
    /// such output is not strictly RFC-conformant but is widely accepted.
    /// </summary>
    public int? WrapColumn { get; set; } = 76;

    /// <summary>Whether to emit "version: 1" before the first record. Default true.</summary>
    public bool IncludeVersionLine { get; set; } = true;
}
