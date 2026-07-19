using System.Text;

namespace LdifDotNet;

/// <summary>
/// Streaming, forward-only LDIF reader (RFC 2849). Tolerant of LF and CRLF line
/// endings, missing version lines, and mixed content/change records in one file.
/// Input must be valid UTF-8 (RFC 2849): file-based readers reject invalid bytes
/// as a parse error rather than silently substituting U+FFFD, which would
/// collapse distinct invalid inputs into one decoded value.
/// </summary>
public sealed class LdifReader : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TextReader _reader;
    private readonly bool _ownsReader;
    private int _lineNumber;
    private bool _firstBlock = true;

    /// <summary>Creates a reader over the given text; the reader does not take ownership of it.</summary>
    public LdifReader(TextReader reader)
        : this(reader, ownsReader: false)
    {
    }

    private LdifReader(TextReader reader, bool ownsReader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _ownsReader = ownsReader;
    }

    /// <summary>Reads the next record, or returns null at end of input.</summary>
    public LdifRecord? ReadRecord()
    {
        while (true)
        {
            var lines = ReadRecordLines();
            if (lines is null)
                return null;

            int start = 0;
            if (_firstBlock)
            {
                _firstBlock = false;
                if (LineName(lines[0]) is { } name && NameEquals(name, "version"))
                {
                    string version = ParseValueSpec(lines[0], AfterName(lines[0])).AsString().Trim();
                    if (version != "1")
                        throw new LdifParseException($"unsupported LDIF version '{version}'", lines[0].Number);
                    start = 1;
                }
            }

            if (start < lines.Count)
                return ParseRecord(lines, start);
            // The block held only the version line; the first record follows.
        }
    }

    /// <summary>Lazily reads all records from an LDIF file. The file must be valid
    /// UTF-8; invalid bytes are a parse error (see the class summary).</summary>
    public static IEnumerable<LdifRecord> ReadFile(string path)
    {
        using var reader = new LdifReader(new StreamReader(path, StrictUtf8), ownsReader: true);
        foreach (var record in reader.ReadAll())
            yield return record;
    }

    /// <summary>Lazily reads all records from a reader.</summary>
    public static IEnumerable<LdifRecord> Read(TextReader reader)
    {
        using var ldifReader = new LdifReader(reader);
        foreach (var record in ldifReader.ReadAll())
            yield return record;
    }

    /// <summary>Parses all records from an LDIF string.</summary>
    public static IReadOnlyList<LdifRecord> Parse(string ldif)
    {
        using var reader = new LdifReader(new StringReader(ldif), ownsReader: true);
        return reader.ReadAll().ToList();
    }

    private IEnumerable<LdifRecord> ReadAll()
    {
        while (ReadRecord() is { } record)
            yield return record;
    }

    /// <summary>Releases the underlying reader when this instance owns it (file-based readers).</summary>
    public void Dispose()
    {
        if (_ownsReader)
            _reader.Dispose();
    }

    private readonly record struct LogicalLine(string Text, int Number);

    /// <summary>
    /// Reads the next block of logical (unfolded) lines, dropping comments and their
    /// continuations. Returns null at end of input.
    /// </summary>
    private List<LogicalLine>? ReadRecordLines()
    {
        var lines = new List<LogicalLine>();
        string? current = null;
        StringBuilder? folded = null; // created lazily on the first continuation of `current`
        int currentNumber = 0;
        bool inComment = false;

        // Appending continuations to a string would copy the whole accumulated
        // line each time — quadratic for a large value folded into many short
        // physical lines. Unfolded lines stay as the string the reader returned.
        void CommitCurrent()
        {
            if (current is null)
                return;
            lines.Add(new LogicalLine(folded?.ToString() ?? current, currentNumber));
            folded = null;
        }

        while (true)
        {
            string? physical = ReadPhysicalLine();
            if (physical is null)
                break;
            _lineNumber++;

            if (physical.Length == 0)
            {
                if (current is not null || lines.Count > 0)
                    break; // record separator
                inComment = false;
                continue; // leading blank lines
            }

            if (physical[0] == '#')
            {
                inComment = true;
                continue;
            }

            if (physical[0] == ' ')
            {
                if (inComment)
                    continue; // continuation of a folded comment
                if (current is null)
                    throw new LdifParseException("continuation line with nothing to continue", _lineNumber);
                folded ??= new StringBuilder(current);
                folded.Append(physical, 1, physical.Length - 1);
                continue;
            }

            inComment = false;
            CommitCurrent();
            current = physical;
            currentNumber = _lineNumber;
        }

        CommitCurrent();
        return lines.Count == 0 ? null : lines;
    }

    /// <summary>
    /// Reads one physical line, surfacing invalid UTF-8 from a strict decoder
    /// (see <see cref="ReadFile"/>) as a parse error. The decoder works on
    /// buffered blocks, so the reported line is where decoding stopped — the
    /// invalid byte is at or after it.
    /// </summary>
    private string? ReadPhysicalLine()
    {
        try
        {
            return _reader.ReadLine();
        }
        catch (DecoderFallbackException)
        {
            throw new LdifParseException("input is not valid UTF-8 (RFC 2849 requires it)", _lineNumber + 1);
        }
    }

    private LdifRecord ParseRecord(List<LogicalLine> lines, int i)
    {
        var dnLine = lines[i];
        if (LineName(dnLine) is not { } dnName || !NameEquals(dnName, "dn"))
            throw new LdifParseException("expected 'dn:' to start a record", dnLine.Number);
        var dnValue = ParseValueSpec(dnLine, AfterName(dnLine));
        if (dnValue.IsUrl)
            throw new LdifParseException("a DN cannot be a URL reference", dnLine.Number);
        string dn = DecodeDnField(dnValue, "dn", dnLine.Number);
        i++;

        // Per RFC 2849 a change record is dn, then controls, then changetype. Only
        // treat "control" lines as controls when a changetype actually follows;
        // otherwise they are ordinary attributes of a content record.
        int afterControls = i;
        while (afterControls < lines.Count && LineName(lines[afterControls]) is { } n && NameEquals(n, "control"))
            afterControls++;

        if (afterControls < lines.Count
            && LineName(lines[afterControls]) is { } ctName
            && NameEquals(ctName, "changetype"))
        {
            var controls = new List<LdifControl>();
            for (int c = i; c < afterControls; c++)
                controls.Add(ParseControl(lines[c]));

            var ctLine = lines[afterControls];
            string changeType = ParseValueSpec(ctLine, AfterName(ctLine)).AsString().Trim().ToLowerInvariant();
            i = afterControls + 1;

            return changeType switch
            {
                "add" => new LdifAddRecord(dn, ParseAttributes(lines, i)) { Controls = controls },
                "delete" => ParseDelete(dn, controls, lines, i),
                "modify" => new LdifModifyRecord(dn, ParseModifications(lines, i)) { Controls = controls },
                "modrdn" or "moddn" => ParseModDn(dn, controls, lines, i),
                _ => throw new LdifParseException($"unknown changetype '{changeType}'", ctLine.Number),
            };
        }

        return new LdifContentRecord(dn, ParseAttributes(lines, i));
    }

    private static LdifDeleteRecord ParseDelete(string dn, List<LdifControl> controls, List<LogicalLine> lines, int i)
    {
        if (i < lines.Count)
            throw new LdifParseException("unexpected content after 'changetype: delete'", lines[i].Number);
        return new LdifDeleteRecord(dn) { Controls = controls };
    }

    private LdifModDnRecord ParseModDn(string dn, List<LdifControl> controls, List<LogicalLine> lines, int i)
    {
        string newRdn = ExpectValue(lines, ref i, "newrdn", dnField: true);
        string deleteOldRdn = ExpectValue(lines, ref i, "deleteoldrdn").Trim();
        bool delete = deleteOldRdn switch
        {
            "0" => false,
            "1" => true,
            _ => throw new LdifParseException($"deleteoldrdn must be 0 or 1, got '{deleteOldRdn}'", lines[i - 1].Number),
        };

        string? newSuperior = null;
        if (i < lines.Count && LineName(lines[i]) is { } n && NameEquals(n, "newsuperior"))
        {
            newSuperior = DecodeDnField(ParseValueSpec(lines[i], AfterName(lines[i])), "newsuperior", lines[i].Number);
            i++;
        }

        if (i < lines.Count)
            throw new LdifParseException("unexpected content after modrdn record", lines[i].Number);
        return new LdifModDnRecord(dn, newRdn, delete, newSuperior) { Controls = controls };
    }

    private string ExpectValue(List<LogicalLine> lines, ref int i, string expectedName, bool dnField = false)
    {
        if (i >= lines.Count || LineName(lines[i]) is not { } name || !NameEquals(name, expectedName))
        {
            int number = i < lines.Count ? lines[i].Number : _lineNumber;
            throw new LdifParseException($"expected '{expectedName}:'", number);
        }
        var line = lines[i];
        var parsed = ParseValueSpec(line, AfterName(line));
        i++;
        return dnField ? DecodeDnField(parsed, expectedName, line.Number) : parsed.AsString();
    }

    /// <summary>
    /// Converts a DN-valued field (dn, newrdn, newsuperior) to text. RFC 2849
    /// requires base64-encoded distinguished-name fields to be valid UTF-8, so
    /// invalid sequences are a parse error rather than silent U+FFFD replacement.
    /// </summary>
    private static string DecodeDnField(LdifValue value, string field, int lineNumber)
    {
        if (value.BinaryOctets is not { } bytes)
            return value.AsString();
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new LdifParseException($"base64 '{field}' value is not valid UTF-8", lineNumber);
        }
    }

    private static List<LdifModification> ParseModifications(List<LogicalLine> lines, int i)
    {
        var mods = new List<LdifModification>();
        while (i < lines.Count)
        {
            if (lines[i].Text.TrimEnd() == "-")
            {
                i++;
                continue;
            }

            var opLine = lines[i];
            if (LineName(opLine) is not { } opName)
                throw new LdifParseException("expected 'add:', 'delete:' or 'replace:' in modify record", opLine.Number);
            var type = opName.ToLowerInvariant() switch
            {
                "add" => LdifModificationType.Add,
                "delete" => LdifModificationType.Delete,
                "replace" => LdifModificationType.Replace,
                "increment" => LdifModificationType.Increment,
                _ => throw new LdifParseException($"unknown modify operation '{opName}'", opLine.Number),
            };
            string attributeName = ParseValueSpec(opLine, AfterName(opLine)).AsString().Trim();
            i++;

            var values = new List<LdifValue>();
            while (i < lines.Count && lines[i].Text.TrimEnd() != "-")
            {
                var line = lines[i];
                if (LineName(line) is null)
                    throw new LdifParseException("expected 'name: value' line in modify record", line.Number);
                values.Add(ParseValueSpec(line, AfterName(line)));
                i++;
            }
            if (i < lines.Count)
                i++; // consume the "-" terminator

            mods.Add(new LdifModification(type, attributeName, values));
        }
        return mods;
    }

    private static List<LdifAttribute> ParseAttributes(List<LogicalLine> lines, int start)
    {
        // Values of the same attribute may be interleaved with others in the file;
        // group them by name (case-insensitive) preserving first-appearance order.
        var order = new List<string>();
        var byName = new Dictionary<string, List<LdifValue>>(StringComparer.OrdinalIgnoreCase);

        for (int i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            if (LineName(line) is not { } name)
                throw new LdifParseException("expected 'name: value' line", line.Number);
            if (!byName.TryGetValue(name, out var values))
            {
                values = [];
                byName[name] = values;
                order.Add(name);
            }
            values.Add(ParseValueSpec(line, AfterName(line)));
        }

        return order.ConvertAll(name => new LdifAttribute(name, byName[name]));
    }

    private static LdifControl ParseControl(LogicalLine line)
    {
        string rest = line.Text[AfterName(line)..];
        if (rest.StartsWith(':'))
            rest = rest[1..];
        rest = rest.TrimStart(' ');

        int end = 0;
        while (end < rest.Length && rest[end] != ' ' && rest[end] != ':')
            end++;
        string oid = rest[..end];
        if (oid.Length == 0)
            throw new LdifParseException("control is missing an OID", line.Number);
        rest = rest[end..];

        bool? criticality = null;
        if (rest.StartsWith(' '))
        {
            rest = rest.TrimStart(' ');
            if (rest is "true"
                || rest.StartsWith("true ", StringComparison.Ordinal)
                || rest.StartsWith("true:", StringComparison.Ordinal))
            {
                criticality = true;
                rest = rest[4..].TrimStart(' ');
            }
            else if (rest is "false"
                || rest.StartsWith("false ", StringComparison.Ordinal)
                || rest.StartsWith("false:", StringComparison.Ordinal))
            {
                criticality = false;
                rest = rest[5..].TrimStart(' ');
            }
            else if (rest.Length > 0 && rest[0] != ':')
            {
                throw new LdifParseException("control criticality must be 'true' or 'false'", line.Number);
            }
        }

        LdifValue? value = null;
        if (rest.StartsWith(':'))
            value = ParseValueSpec(new LogicalLine(rest, line.Number), 0);
        else if (rest.Length > 0)
            throw new LdifParseException("malformed control line", line.Number);

        return new LdifControl(oid, criticality, value);
    }

    /// <summary>Parses a value-spec: ": text", ":: base64" or ":&lt; url" starting at <paramref name="colon"/>.</summary>
    private static LdifValue ParseValueSpec(LogicalLine line, int colon)
    {
        string text = line.Text;
        if (colon >= text.Length || text[colon] != ':')
            throw new LdifParseException("expected ':' after attribute name", line.Number);

        if (colon + 1 < text.Length && text[colon + 1] == ':')
        {
            string base64 = text[(colon + 2)..].Trim();
            try
            {
                return LdifValue.FromOwnedBytes(base64.Length == 0 ? [] : Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                throw new LdifParseException("invalid base64 value", line.Number);
            }
        }

        if (colon + 1 < text.Length && text[colon + 1] == '<')
        {
            string url = text[(colon + 2)..].Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new LdifParseException($"invalid URL '{url}'", line.Number);
            return LdifValue.FromUrl(uri);
        }

        return LdifValue.FromString(text[(colon + 1)..].TrimStart(' '));
    }

    /// <summary>The attribute name of a logical line, or null if it has no ':' separator.</summary>
    private static string? LineName(LogicalLine line)
    {
        int colon = line.Text.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 ? null : line.Text[..colon];
    }

    private static int AfterName(LogicalLine line) => line.Text.IndexOf(':', StringComparison.Ordinal);

    private static bool NameEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
