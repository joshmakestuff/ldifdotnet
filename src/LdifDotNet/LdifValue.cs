using System.Text;

namespace LdifDotNet;

/// <summary>
/// A single attribute value. LDIF values are octet strings; this type tracks whether
/// the value is textual (safe string) or binary (base64-encoded in LDIF).
/// </summary>
public readonly struct LdifValue : IEquatable<LdifValue>
{
    private readonly string? _text;
    private readonly byte[]? _bytes;

    private LdifValue(string? text, byte[]? bytes)
    {
        _text = text;
        _bytes = bytes;
    }

    public static LdifValue FromString(string value) =>
        new(value ?? throw new ArgumentNullException(nameof(value)), null);

    public static LdifValue FromBytes(byte[] value) =>
        new(null, value ?? throw new ArgumentNullException(nameof(value)));

    public static implicit operator LdifValue(string value) => FromString(value);

    /// <summary>True when the value was constructed from (or parsed as) raw bytes.</summary>
    public bool IsBinary => _bytes is not null;

    /// <summary>The value as text. Binary values are decoded as UTF-8.</summary>
    public string AsString() => _text ?? Encoding.UTF8.GetString(_bytes ?? []);

    /// <summary>The value as raw bytes. Textual values are encoded as UTF-8.</summary>
    public byte[] AsBytes() => _bytes ?? Encoding.UTF8.GetBytes(_text ?? "");

    public bool Equals(LdifValue other) =>
        IsBinary == other.IsBinary && AsBytes().AsSpan().SequenceEqual(other.AsBytes());

    public override bool Equals(object? obj) => obj is LdifValue other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(AsBytes());
        return hash.ToHashCode();
    }

    public override string ToString() => AsString();
}
