namespace LdifDotNet;

/// <summary>Thrown when LDIF input cannot be parsed.</summary>
public sealed class LdifParseException : Exception
{
    /// <summary>Creates the exception for a failure at the given 1-based physical line number.</summary>
    public LdifParseException(string message, int lineNumber)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based physical line number in the input where the error was detected.</summary>
    public int LineNumber { get; }
}
