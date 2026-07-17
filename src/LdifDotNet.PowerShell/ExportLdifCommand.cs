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

    /// <summary>Records to write: LdifRecord objects, or hashtables/PSCustomObjects with a 'dn' key.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public object[] InputObject { get; set; } = [];

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
        _stream = CreateTempSibling(_destination, out _tempPath);
        _writer = new LdifWriter(_stream, options);
    }

    /// <summary>
    /// Creates a uniquely named temporary file next to the destination and opens it
    /// with <see cref="FileMode.CreateNew"/>, so it can never truncate an existing
    /// file and concurrent exports never share a path. Same-directory placement keeps
    /// the final replace on one volume (atomic).
    /// </summary>
    private static StreamWriter CreateTempSibling(string destination, out string tempPath)
    {
        string directory = System.IO.Path.GetDirectoryName(destination) ?? ".";
        string name = System.IO.Path.GetFileName(destination);
        for (int attempt = 0; ; attempt++)
        {
            string candidate = System.IO.Path.Combine(
                directory, $"{name}.{System.IO.Path.GetRandomFileName()}.tmp");
            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                tempPath = candidate;
                return new StreamWriter(stream);
            }
            catch (IOException) when (File.Exists(candidate) && attempt < 8)
            {
                // Astronomically unlikely name collision; try another name.
            }
        }
    }

    protected override void ProcessRecord()
    {
        if (_writer is null)
            return; // -WhatIf, or the user declined confirmation
        foreach (object input in InputObject)
            _writer.WriteRecord(LdifInput.ToRecord(input));
    }

    protected override void EndProcessing()
    {
        if (_writer is null)
            return;
        CloseStreams();

        if (NoClobber.IsPresent)
        {
            // Non-overwriting move: atomically fails if the destination was created
            // while the pipeline ran, closing the TOCTOU gap in the -NoClobber check.
            try
            {
                File.Move(_tempPath!, _destination!);
            }
            catch (IOException) when (File.Exists(_destination))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new IOException($"The file '{_destination}' already exists and -NoClobber was specified."),
                    "FileExists",
                    ErrorCategory.ResourceExists,
                    _destination));
            }
        }
        else
        {
            File.Move(_tempPath!, _destination!, overwrite: true);
        }

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
