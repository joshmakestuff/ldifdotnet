using System.Collections;
using System.Globalization;
using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Converts a pipeline object into an <see cref="LdifRecord"/> for the write cmdlets.
/// An existing record (including the friendly view from Import-Ldif, whose base object
/// is the real record) is used as-is; a hashtable or PSCustomObject becomes a content
/// record — its <c>dn</c> key is the DN and every other key an attribute, with values
/// projected back the way <see cref="LdifView"/> projects them out (scalar or array;
/// string, byte[], or Uri).
/// </summary>
internal static class LdifInput
{
    public static LdifRecord ToRecord(object? input)
    {
        object? value = (input as PSObject)?.BaseObject ?? input;
        if (value is LdifRecord record)
            return record;
        if (value is IDictionary dictionary)
            return FromPairs(Entries(dictionary));
        if (input is PSObject bag)
            return FromPairs(bag.Properties.Select(p => (p.Name, (object?)p.Value)));
        throw new ArgumentException(
            $"Cannot convert '{value?.GetType().Name ?? "null"}' to an LDIF record; pass an LdifRecord, a hashtable, or a PSCustomObject with a 'dn' key.",
            nameof(input));
    }

    private static IEnumerable<(string Name, object? Value)> Entries(IDictionary dictionary)
    {
        foreach (DictionaryEntry entry in dictionary)
            yield return (entry.Key?.ToString() ?? "", entry.Value);
    }

    private static LdifContentRecord FromPairs(IEnumerable<(string Name, object? Value)> pairs)
    {
        string? dn = null;
        var attributes = new List<LdifAttribute>();
        foreach (var (name, value) in pairs)
        {
            if (name.Equals("dn", StringComparison.OrdinalIgnoreCase))
            {
                dn = value is PSObject p ? p.BaseObject?.ToString() : value?.ToString();
                continue;
            }

            // A null or empty value list means the key contributes no attribute; the
            // strict writer rejects a zero-value attribute, so we simply omit it.
            var values = ToValues(value);
            if (values.Count > 0)
                attributes.Add(new LdifAttribute(name, values));
        }

        if (string.IsNullOrEmpty(dn))
            throw new ArgumentException("A hashtable or PSCustomObject record must have a non-empty 'dn' key.", nameof(pairs));

        return new LdifContentRecord(dn, attributes);
    }

    private static List<LdifValue> ToValues(object? value)
    {
        var result = new List<LdifValue>();
        object? v = value is PSObject p ? p.BaseObject : value;
        switch (v)
        {
            case null:
                break;
            case byte[] bytes:
                result.Add(LdifValue.FromBytes(bytes));
                break;
            case string text:
                result.Add(LdifValue.FromString(text));
                break;
            case Uri url:
                result.Add(LdifValue.FromUrl(url));
                break;
            case IEnumerable sequence:
                foreach (object? item in sequence)
                    result.AddRange(ToValues(item));
                break;
            default:
                result.Add(LdifValue.FromString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""));
                break;
        }
        return result;
    }
}
