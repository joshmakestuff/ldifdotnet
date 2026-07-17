using System.Management.Automation;
using System.Text;

namespace LdifDotNet.PowerShell;

/// <summary>
/// Parses LDIF text into record objects. Pipeline input is accumulated line by
/// line (so "Get-Content file | ConvertFrom-Ldif" works) and parsed at the end,
/// mirroring ConvertFrom-Json.
/// </summary>
[Cmdlet(VerbsData.ConvertFrom, "Ldif")]
[OutputType(typeof(LdifRecord))]
public sealed class ConvertFromLdifCommand : PSCmdlet
{
    private readonly StringBuilder _input = new();

    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [AllowEmptyString]
    public string[] InputObject { get; set; } = [];

    protected override void ProcessRecord()
    {
        foreach (string text in InputObject)
        {
            _input.Append(text);
            _input.Append('\n');
        }
    }

    protected override void EndProcessing()
    {
        try
        {
            foreach (var record in LdifReader.Parse(_input.ToString()))
                WriteObject(LdifView.AsPipelineObject(record));
        }
        catch (LdifParseException exception)
        {
            ThrowTerminatingError(new ErrorRecord(
                exception, "InvalidLdif", ErrorCategory.ParserError, targetObject: null));
        }
    }
}
