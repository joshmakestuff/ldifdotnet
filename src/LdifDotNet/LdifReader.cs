namespace LdifDotNet;

/// <summary>
/// Streaming, forward-only LDIF reader (RFC 2849). Tolerant of LF and CRLF line endings.
/// </summary>
public sealed class LdifReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly bool _ownsReader;

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
        throw new NotImplementedException();
    }

    /// <summary>Lazily reads all records from an LDIF file.</summary>
    public static IEnumerable<LdifRecord> ReadFile(string path)
    {
        using var reader = new LdifReader(new StreamReader(path), ownsReader: true);
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

    public void Dispose()
    {
        if (_ownsReader)
            _reader.Dispose();
    }
}
