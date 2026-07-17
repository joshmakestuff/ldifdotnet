using System.Text;

namespace LdifDotNet;

/// <summary>
/// A single attribute value. LDIF values are octet strings; this type tracks whether
/// the value is textual (safe string), binary (base64-encoded in LDIF), or a URL
/// reference (":&lt;" in LDIF, content not loaded). Equality compares octets, so a
/// value read from base64 equals the same text read directly.
/// </summary>
public readonly struct LdifValue : IEquatable<LdifValue>
{
    private readonly string? _text;
    private readonly byte[]? _bytes;
    private readonly Uri? _url;

    private LdifValue(string? text, byte[]? bytes, Uri? url)
    {
        _text = text;
        _bytes = bytes;
        _url = url;
    }

    public static LdifValue FromString(string value) =>
        new(value ?? throw new ArgumentNullException(nameof(value)), null, null);

    /// <summary>Creates a binary value. The input array is copied, so later mutation
    /// of <paramref name="value"/> cannot affect this value's bytes, equality, or hash.</summary>
    public static LdifValue FromBytes(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(null, [.. value], null);
    }

    /// <summary>Creates a binary value that takes ownership of <paramref name="value"/>
    /// without copying. The caller must not retain or mutate the array; used by the
    /// reader for freshly decoded base64 buffers.</summary>
    internal static LdifValue FromOwnedBytes(byte[] value) => new(null, value, null);

    public static LdifValue FromUrl(Uri url) =>
        new(null, null, url ?? throw new ArgumentNullException(nameof(url)));

    public static implicit operator LdifValue(string value) => FromString(value);

    /// <summary>True when the value was constructed from (or parsed as) raw bytes.</summary>
    public bool IsBinary => _bytes is not null;

    /// <summary>True when the value is a URL reference whose content has not been loaded.</summary>
    public bool IsUrl => _url is not null;

    /// <summary>The referenced URL, or null when the value is not a URL reference.</summary>
    public Uri? Url => _url;

    /// <summary>The value as text. Binary values are decoded as UTF-8.</summary>
    public string AsString() => IsUrl
        ? throw new InvalidOperationException("Value is a URL reference; its content is not loaded. Check IsUrl and use Url.")
        : _text ?? Encoding.UTF8.GetString(_bytes ?? []);

    /// <summary>The value as raw bytes. Textual values are encoded as UTF-8.
    /// The returned array is a copy; mutating it cannot affect this value.</summary>
    public byte[] AsBytes() => IsUrl
        ? throw new InvalidOperationException("Value is a URL reference; its content is not loaded. Check IsUrl and use Url.")
        : _bytes is not null ? [.. _bytes] : Encoding.UTF8.GetBytes(_text ?? "");

    private byte[] Octets() => _bytes ?? Encoding.UTF8.GetBytes(_text ?? "");

    public bool Equals(LdifValue other)
    {
        if (IsUrl || other.IsUrl)
            return IsUrl && other.IsUrl && _url == other._url;
        return Octets().AsSpan().SequenceEqual(other.Octets());
    }

    public override bool Equals(object? obj) => obj is LdifValue other && Equals(other);

    public static bool operator ==(LdifValue left, LdifValue right) => left.Equals(right);

    public static bool operator !=(LdifValue left, LdifValue right) => !left.Equals(right);

    public override int GetHashCode()
    {
        if (IsUrl)
            return _url!.GetHashCode();
        var hash = new HashCode();
        hash.AddBytes(Octets());
        return hash.ToHashCode();
    }

    public override string ToString() => IsUrl ? _url!.OriginalString : AsString();
}
