namespace LdifDotNet;

/// <summary>
/// Forward-only LDIF writer (RFC 2849). Emits strictly conformant output:
/// base64-encodes values that are not safe strings and folds long lines.
/// </summary>
public sealed class LdifWriter : IDisposable
{
    private readonly TextWriter _writer;
    private readonly LdifWriterOptions _options;

    public LdifWriter(TextWriter writer, LdifWriterOptions? options = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _options = options ?? new LdifWriterOptions();
    }

    public void WriteRecord(LdifRecord record)
    {
        throw new NotImplementedException();
    }

    /// <summary>Writes all records to a file.</summary>
    public static void WriteFile(string path, IEnumerable<LdifRecord> records, LdifWriterOptions? options = null)
    {
        using var streamWriter = new StreamWriter(path);
        using var writer = new LdifWriter(streamWriter, options);
        foreach (var record in records)
            writer.WriteRecord(record);
    }

    /// <summary>Writes all records to an LDIF string.</summary>
    public static string WriteToString(IEnumerable<LdifRecord> records, LdifWriterOptions? options = null)
    {
        using var stringWriter = new StringWriter();
        using (var writer = new LdifWriter(stringWriter, options))
        {
            foreach (var record in records)
                writer.WriteRecord(record);
        }

        return stringWriter.ToString();
    }

    public void Dispose()
    {
    }
}

/// <summary>Options controlling LDIF output.</summary>
public sealed class LdifWriterOptions
{
    /// <summary>Column at which output lines are folded. Default 76 per RFC 2849.</summary>
    public int WrapColumn { get; set; } = 76;

    /// <summary>Whether to emit "version: 1" before the first record. Default true.</summary>
    public bool IncludeVersionLine { get; set; } = true;
}
