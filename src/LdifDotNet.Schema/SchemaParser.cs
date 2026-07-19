using System.Globalization;
using System.Text;

namespace LdifDotNet.Schema;

/// <summary>
/// Parses slapd.conf-style schema files: "attributetype (...)" and
/// "objectclass (...)" directives in RFC 4512 description syntax, plus
/// "objectidentifier" OID macros. Other slapd directives are ignored.
/// </summary>
internal sealed class SchemaParser
{
    private readonly Dictionary<string, string> _oidMacros = new(StringComparer.OrdinalIgnoreCase);

    public void ParseInto(string text, List<LdapAttributeType> attributeTypes, List<LdapObjectClass> objectClasses)
    {
        foreach (var (directive, lineNumber) in Directives(text))
        {
            int space = directive.IndexOfAny([' ', '\t']);
            string keyword = (space < 0 ? directive : directive[..space]).ToLowerInvariant();
            string body = space < 0 ? "" : directive[space..];

            switch (keyword)
            {
                case "attributetype" or "attributetypes":
                    attributeTypes.Add(ParseAttributeType(new Cursor(body, lineNumber)));
                    break;
                case "objectclass" or "objectclasses":
                    objectClasses.Add(ParseObjectClass(new Cursor(body, lineNumber)));
                    break;
                case "objectidentifier":
                    ParseOidMacro(body, lineNumber);
                    break;
            }
        }
    }

    /// <summary>
    /// Assembles logical directives: a directive starts at column 0; lines that
    /// begin with whitespace continue it; '#' lines are comments; blank lines end it.
    /// </summary>
    private static IEnumerable<(string Text, int Line)> Directives(string text)
    {
        string? current = null;
        int startLine = 0, lineNumber = 0;

        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNumber++;
            if (raw.StartsWith('#'))
                continue;
            if (raw.Length == 0 || string.IsNullOrWhiteSpace(raw))
            {
                if (current is not null)
                {
                    yield return (current, startLine);
                    current = null;
                }
                continue;
            }
            if (char.IsWhiteSpace(raw[0]))
            {
                if (current is not null)
                    current += " " + raw.TrimStart();
                continue;
            }
            if (current is not null)
                yield return (current, startLine);
            current = raw;
            startLine = lineNumber;
        }

        if (current is not null)
            yield return (current, startLine);
    }

    private void ParseOidMacro(string body, int lineNumber)
    {
        string[] parts = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new LdapSchemaParseException($"line {lineNumber}: malformed objectidentifier directive", lineNumber);
        string resolved = ExpandOidMacros(parts[1]);
        if (!RfcGrammar.IsNumericOid(resolved))
        {
            throw new LdapSchemaParseException(
                $"line {lineNumber}: objectidentifier '{parts[0]}' resolves to '{resolved}', which is not a numeric OID or a declared macro reference",
                lineNumber);
        }
        _oidMacros[parts[0]] = resolved;
    }

    /// <summary>
    /// Resolves the OID token of a definition: macro references expand, and the
    /// result must be a numeric OID. An undeclared macro fails here — slapd would
    /// reject it at load, so passing it downstream as a bogus OID string would
    /// just relocate the error.
    /// </summary>
    private string ResolveOid(Cursor cursor)
    {
        string oid = cursor.ReadValue();
        string resolved = ExpandOidMacros(oid);
        if (!RfcGrammar.IsNumericOid(resolved))
            throw cursor.Error($"OID '{oid}' is not a numeric OID or a declared objectidentifier macro reference");
        return resolved;
    }

    /// <summary>Expands "macro" or "macro:suffix" references against declared OID macros.</summary>
    private string ExpandOidMacros(string oid)
    {
        int colon = oid.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            string prefix = oid[..colon];
            if (_oidMacros.TryGetValue(prefix, out string? baseOid))
                return $"{baseOid}.{oid[(colon + 1)..]}";
        }
        else if (_oidMacros.TryGetValue(oid, out string? macroOid))
        {
            return macroOid;
        }
        return oid;
    }

    private LdapAttributeType ParseAttributeType(Cursor cursor)
    {
        cursor.Expect(TokenKind.LParen);
        var result = new LdapAttributeType { Oid = ResolveOid(cursor) };
        var extensions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var token = cursor.Next();
            if (token.Kind == TokenKind.RParen)
                break;
            if (token.Kind != TokenKind.Word)
                throw cursor.Error($"unexpected token '{token.Value}' in attributetype");

            switch (token.Value.ToUpperInvariant())
            {
                case "NAME": result.Names = cursor.ReadValueList(); break;
                case "DESC": result.Description = cursor.ReadValue(); break;
                case "OBSOLETE": result.Obsolete = true; break;
                case "SUP": result.SuperiorName = cursor.ReadValue(); break;
                case "EQUALITY": result.Equality = cursor.ReadValue(); break;
                case "ORDERING": result.Ordering = cursor.ReadValue(); break;
                case "SUBSTR" or "SUBSTRINGS": result.Substring = cursor.ReadValue(); break;
                case "SYNTAX":
                    string syntax = cursor.ReadValue();
                    int brace = syntax.IndexOf('{', StringComparison.Ordinal);
                    if (brace >= 0 && syntax.EndsWith('}')
                        && int.TryParse(syntax[(brace + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int length))
                    {
                        result.SyntaxLength = length;
                        syntax = syntax[..brace];
                    }
                    result.Syntax = syntax;
                    break;
                case "SINGLE-VALUE": result.SingleValue = true; break;
                case "COLLECTIVE": result.Collective = true; break;
                case "NO-USER-MODIFICATION": result.NoUserModification = true; break;
                case "USAGE": result.Usage = cursor.ReadValue(); break;
                default:
                    if (token.Value.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                        extensions[token.Value] = cursor.ReadValueList();
                    else
                        throw cursor.Error($"unexpected keyword '{token.Value}' in attributetype");
                    break;
            }
        }

        result.Extensions = extensions;
        return result;
    }

    private LdapObjectClass ParseObjectClass(Cursor cursor)
    {
        cursor.Expect(TokenKind.LParen);
        var result = new LdapObjectClass { Oid = ResolveOid(cursor) };
        var extensions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var token = cursor.Next();
            if (token.Kind == TokenKind.RParen)
                break;
            if (token.Kind != TokenKind.Word)
                throw cursor.Error($"unexpected token '{token.Value}' in objectclass");

            switch (token.Value.ToUpperInvariant())
            {
                case "NAME": result.Names = cursor.ReadValueList(); break;
                case "DESC": result.Description = cursor.ReadValue(); break;
                case "OBSOLETE": result.Obsolete = true; break;
                case "SUP": result.SuperiorNames = cursor.ReadValueList(); break;
                case "ABSTRACT": result.Kind = LdapObjectClassKind.Abstract; break;
                case "STRUCTURAL": result.Kind = LdapObjectClassKind.Structural; break;
                case "AUXILIARY": result.Kind = LdapObjectClassKind.Auxiliary; break;
                case "MUST": result.Must = cursor.ReadValueList(); break;
                case "MAY": result.May = cursor.ReadValueList(); break;
                default:
                    if (token.Value.StartsWith("X-", StringComparison.OrdinalIgnoreCase))
                        extensions[token.Value] = cursor.ReadValueList();
                    else
                        throw cursor.Error($"unexpected keyword '{token.Value}' in objectclass");
                    break;
            }
        }

        result.Extensions = extensions;
        return result;
    }

    private enum TokenKind
    {
        LParen,
        RParen,
        Dollar,
        Word,
        Quoted,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    private sealed class Cursor(string text, int lineNumber)
    {
        private int _position;
        private Token? _peeked;

        public Token Next()
        {
            if (_peeked is { } token)
            {
                _peeked = null;
                return token;
            }
            return Read();
        }

        public Token Peek()
        {
            _peeked ??= Read();
            return _peeked.Value;
        }

        /// <summary>A single value: a bare word or a quoted string.</summary>
        public string ReadValue()
        {
            var token = Next();
            if (token.Kind is not (TokenKind.Word or TokenKind.Quoted))
                throw Error($"expected a value, got '{token.Value}'");
            return token.Value;
        }

        /// <summary>A single value or a parenthesized list ('$'-separated or space-separated).</summary>
        public List<string> ReadValueList()
        {
            if (Peek().Kind != TokenKind.LParen)
                return [ReadValue()];

            Next();
            var values = new List<string>();
            while (true)
            {
                var token = Next();
                if (token.Kind == TokenKind.RParen)
                    return values;
                if (token.Kind == TokenKind.Dollar)
                    continue;
                if (token.Kind is not (TokenKind.Word or TokenKind.Quoted))
                    throw Error($"unexpected token '{token.Value}' in value list");
                values.Add(token.Value);
            }
        }

        public void Expect(TokenKind kind)
        {
            var token = Next();
            if (token.Kind != kind)
                throw Error($"expected {kind}, got '{token.Value}'");
        }

        public LdapSchemaParseException Error(string message) =>
            new($"line {lineNumber}: {message}", lineNumber);

        /// <summary>
        /// Decodes RFC 4512 qdstring escapes: "\27" is an apostrophe and
        /// "\5C"/"\5c" a backslash. These are the only escapes the grammar
        /// defines; any other use of '\' in a quoted string is malformed.
        /// </summary>
        private string DecodeQuotedString(string raw)
        {
            if (!raw.Contains('\\', StringComparison.Ordinal))
                return raw;

            var result = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }
                if (i + 2 >= raw.Length)
                    throw Error("truncated escape sequence in quoted string");
                string hex = raw.Substring(i + 1, 2);
                result.Append(hex switch
                {
                    "27" => '\'',
                    "5C" or "5c" => '\\',
                    _ => throw Error($"invalid escape '\\{hex}' in quoted string (RFC 4512 defines \\27 and \\5C)"),
                });
                i += 2;
            }
            return result.ToString();
        }

        private Token Read()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
                _position++;
            if (_position >= text.Length)
                return new Token(TokenKind.End, "<end>");

            char c = text[_position];
            switch (c)
            {
                case '(':
                    _position++;
                    return new Token(TokenKind.LParen, "(");
                case ')':
                    _position++;
                    return new Token(TokenKind.RParen, ")");
                case '$':
                    _position++;
                    return new Token(TokenKind.Dollar, "$");
                case '\'':
                    int close = text.IndexOf('\'', _position + 1);
                    if (close < 0)
                        throw Error("unterminated quoted string");
                    string quoted = DecodeQuotedString(text[(_position + 1)..close]);
                    _position = close + 1;
                    return new Token(TokenKind.Quoted, quoted);
                default:
                    int start = _position;
                    while (_position < text.Length
                        && !char.IsWhiteSpace(text[_position])
                        && text[_position] is not ('(' or ')' or '$' or '\''))
                    {
                        _position++;
                    }
                    return new Token(TokenKind.Word, text[start.._position]);
            }
        }
    }
}
