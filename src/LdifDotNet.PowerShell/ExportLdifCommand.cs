using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Writes records to an LDIF file, streaming: each pipeline record is written as
/// it arrives rather than buffered.
/// </summary>
[Cmdlet(VerbsData.Export, "Ldif")]
public sealed class ExportLdifCommand : PSCmdlet, IDisposable
{
    private StreamWriter? _stream;
    private LdifWriter? _writer;

    [Parameter(Mandatory = true, Position = 0)]
    public string Path { get; set; } = "";

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public LdifRecord[] InputObject { get; set; } = [];

    /// <summary>Disable folding entirely (like ldapsearch -o ldif-wrap=no).</summary>
    [Parameter]
    public SwitchParameter NoWrap { get; set; }

    /// <summary>Omit the leading "version: 1" line (e.g. for slapadd input).</summary>
    [Parameter]
    public SwitchParameter NoVersionLine { get; set; }

    protected override void BeginProcessing()
    {
        var options = new LdifWriterOptions
        {
            WrapColumn = NoWrap.IsPresent ? null : 76,
            IncludeVersionLine = !NoVersionLine.IsPresent,
        };
        _stream = new StreamWriter(GetUnresolvedProviderPathFromPSPath(Path));
        _writer = new LdifWriter(_stream, options);
    }

    protected override void ProcessRecord()
    {
        foreach (var record in InputObject)
            _writer!.WriteRecord(record);
    }

    protected override void EndProcessing() => Dispose();

    protected override void StopProcessing() => Dispose();

    public void Dispose()
    {
        _writer?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
    }
}
