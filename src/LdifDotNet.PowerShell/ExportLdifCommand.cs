using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Writes records to an LDIF file, streaming: each pipeline record is written as
/// it arrives rather than buffered. Output goes to a sibling temporary file that
/// replaces the destination only after the pipeline completes, so a failed or
/// interrupted export never truncates or partially overwrites an existing file.
/// Supports -WhatIf and -Confirm; -NoClobber refuses to replace an existing file.
/// </summary>
[Cmdlet(VerbsData.Export, "Ldif", SupportsShouldProcess = true)]
public sealed class ExportLdifCommand : PSCmdlet, IDisposable
{
    private string? _destination;
    private string? _tempPath;
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

    /// <summary>Fail instead of overwriting an existing destination file.</summary>
    [Parameter]
    public SwitchParameter NoClobber { get; set; }

    protected override void BeginProcessing()
    {
        _destination = GetUnresolvedProviderPathFromPSPath(Path);
        if (!ShouldProcess(_destination))
            return;

        if (NoClobber.IsPresent && File.Exists(_destination))
        {
            ThrowTerminatingError(new ErrorRecord(
                new IOException($"The file '{_destination}' already exists and -NoClobber was specified."),
                "FileExists",
                ErrorCategory.ResourceExists,
                _destination));
        }

        var options = new LdifWriterOptions
        {
            WrapColumn = NoWrap.IsPresent ? null : 76,
            IncludeVersionLine = !NoVersionLine.IsPresent,
        };
        _tempPath = _destination + ".tmp";
        _stream = new StreamWriter(_tempPath);
        _writer = new LdifWriter(_stream, options);
    }

    protected override void ProcessRecord()
    {
        if (_writer is null)
            return; // -WhatIf, or the user declined confirmation
        foreach (var record in InputObject)
            _writer.WriteRecord(record);
    }

    protected override void EndProcessing()
    {
        if (_writer is null)
            return;
        CloseStreams();
        File.Move(_tempPath!, _destination!, overwrite: true);
        _tempPath = null;
    }

    protected override void StopProcessing() => Dispose();

    public void Dispose()
    {
        CloseStreams();
        if (_tempPath is { } abandoned)
        {
            // The pipeline failed or was stopped before completion: the destination
            // was never touched, so only the temporary file needs best-effort cleanup.
            _tempPath = null;
            try
            {
                File.Delete(abandoned);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void CloseStreams()
    {
        _writer?.Dispose();
        _stream?.Dispose();
        _writer = null;
        _stream = null;
    }
}
