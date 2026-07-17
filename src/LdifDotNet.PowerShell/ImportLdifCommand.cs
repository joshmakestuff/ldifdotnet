using System.Management.Automation;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Reads records from LDIF files, streaming each record into the pipeline as it
/// is parsed. Paths support wildcards and PowerShell-relative locations.
/// </summary>
[Cmdlet(VerbsData.Import, "Ldif")]
[OutputType(typeof(LdifRecord))]
public sealed class ImportLdifCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath", "FullName")]
    public string[] Path { get; set; } = [];

    protected override void ProcessRecord()
    {
        foreach (string path in Path)
        {
            foreach (string resolved in GetResolvedProviderPathFromPSPath(path, out _))
            {
                try
                {
                    foreach (var record in LdifReader.ReadFile(resolved))
                        WriteObject(record);
                }
                catch (LdifParseException exception)
                {
                    WriteError(new ErrorRecord(
                        exception, "InvalidLdif", ErrorCategory.ParserError, resolved));
                }
            }
        }
    }
}
