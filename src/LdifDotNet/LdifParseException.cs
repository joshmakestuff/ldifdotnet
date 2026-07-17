namespace LdifDotNet;

/// <summary>Thrown when LDIF input cannot be parsed.</summary>
public sealed class LdifParseException : Exception
{
    public LdifParseException(string message, int lineNumber)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based physical line number in the input where the error was detected.</summary>
    public int LineNumber { get; }
}
