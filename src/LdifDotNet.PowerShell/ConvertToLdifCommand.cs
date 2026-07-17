using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Serializes record objects to a single LDIF string, mirroring ConvertTo-Json.
/// </summary>
[Cmdlet(VerbsData.ConvertTo, "Ldif")]
[OutputType(typeof(string))]
public sealed class ConvertToLdifCommand : PSCmdlet
{
    private readonly List<LdifRecord> _records = [];

    /// <summary>Records to serialize: LdifRecord objects, or hashtables/PSCustomObjects with a 'dn' key.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public object[] InputObject { get; set; } = [];

    /// <summary>Column at which output lines are folded. Default 76 per RFC 2849.</summary>
    [Parameter]
    [ValidateRange(2, int.MaxValue)]
    public int WrapColumn { get; set; } = 76;

    /// <summary>Disable folding entirely (like ldapsearch -o ldif-wrap=no).</summary>
    [Parameter]
    public SwitchParameter NoWrap { get; set; }

    /// <summary>Omit the leading "version: 1" line.</summary>
    [Parameter]
    public SwitchParameter NoVersionLine { get; set; }

    protected override void ProcessRecord()
    {
        foreach (object input in InputObject)
            _records.Add(LdifInput.ToRecord(input));
    }

    protected override void EndProcessing()
    {
        var options = new LdifWriterOptions
        {
            WrapColumn = NoWrap.IsPresent ? null : WrapColumn,
            IncludeVersionLine = !NoVersionLine.IsPresent,
        };
        WriteObject(LdifWriter.WriteToString(_records, options));
    }
}
