namespace LdifDotNet.Schema;

/// <summary>Thrown when a schema definition cannot be parsed.</summary>
public sealed class LdapSchemaParseException : Exception
{
    /// <summary>Creates the exception for a failure at the given 1-based line number.</summary>
    public LdapSchemaParseException(string message, int lineNumber)
        : base(message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based line number where the failing definition starts.</summary>
    public int LineNumber { get; }
}
