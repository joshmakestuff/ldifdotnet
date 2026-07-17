using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Builds the pipeline view of an LDIF content record: the real record wrapped in a
/// <see cref="PSObject"/> whose attributes appear as PowerShell-friendly properties —
/// a scalar for a single value, an array for several; a <see cref="string"/>,
/// <c>byte[]</c>, or <see cref="System.Uri"/> per value kind; matched case-insensitively
/// like LDAP. The record stays the PSObject's base object, so piping the view back into
/// Export-Ldif / ConvertTo-Ldif rebinds the original record and round-trips losslessly.
/// </summary>
internal static class LdifView
{
    internal const string ContentTypeName = "LdifDotNet.PowerShell.LdifEntry";

    /// <summary>
    /// Wraps a content record in its friendly view; change records (add/delete/modify/
    /// moddn) pass through unchanged so their typed shape and existing behavior are kept.
    /// </summary>
    public static object AsPipelineObject(LdifRecord record) =>
        record is LdifContentRecord content ? ForContent(content) : record;

    public static PSObject ForContent(LdifContentRecord record)
    {
        var view = PSObject.AsPSObject(record);
        view.TypeNames.Insert(0, ContentTypeName);

        // Merge the values of same-named attributes (case-insensitive) so $entry.cn
        // yields every cn value regardless of how the LDIF grouped them on the wire.
        var order = new List<string>();
        var byName = new Dictionary<string, List<LdifValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in record.Attributes)
        {
            if (!byName.TryGetValue(attribute.Name, out var values))
            {
                values = [];
                byName[attribute.Name] = values;
                order.Add(attribute.Name);
            }
            values.AddRange(attribute.Values);
        }

        foreach (string name in order)
        {
            // Never shadow a base member (Dn, Attributes, ...); those keep their meaning.
            if (view.Members[name] is not null)
                continue;
            view.Properties.Add(new PSNoteProperty(name, Project(byName[name])));
        }

        return view;
    }

    private static object? Project(List<LdifValue> values) =>
        values.Count == 1 ? ProjectOne(values[0]) : values.Select(ProjectOne).ToArray();

    private static object? ProjectOne(LdifValue value) =>
        value.IsUrl ? value.Url
        : value.IsBinary ? value.AsBytes()
        : value.AsString();
}
