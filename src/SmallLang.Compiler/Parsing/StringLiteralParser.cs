using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Lexing;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Parsing;

internal static class StringLiteralParser
{
    public static IReadOnlyList<StringSegment> ParseStringSegments(Token token)
    {
        if (token.IsRawString)
        {
            return [new TextSegment(NormalizeRawString(token))];
        }

        var segments = new List<StringSegment>();
        var text = token.Text;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '$' || i + 1 >= text.Length)
            {
                continue;
            }

            var next = text[i + 1];
            if (IsIdentifierStart(next))
            {
                if (i > start)
                {
                    segments.Add(new TextSegment(DecodeEscapes(text[start..i])));
                }

                var nameStart = i + 1;
                var nameEnd = nameStart + 1;
                while (nameEnd < text.Length && IsIdentifierPart(text[nameEnd]))
                {
                    nameEnd++;
                }

                var name = text[nameStart..nameEnd];
                segments.Add(new InterpolationSegment(new NameExpression(name, token.Line, token.Column + nameStart)));
                i = nameEnd - 1;
                start = nameEnd;
                continue;
            }

            if (next != '(')
            {
                continue;
            }

            if (i > start)
            {
                segments.Add(new TextSegment(DecodeEscapes(text[start..i])));
            }

            var close = FindExpressionInterpolationClose(text, i + 2, token);
            var expressionText = text[(i + 2)..close];
            if (string.IsNullOrWhiteSpace(expressionText))
            {
                throw ErrorAt(token, "empty interpolation expression is not allowed");
            }

            segments.Add(new InterpolationSegment(ParseInterpolationExpression(expressionText, token)));
            i = close;
            start = i + 1;
        }

        if (start < text.Length)
        {
            segments.Add(new TextSegment(DecodeEscapes(text[start..])));
        }

        return segments;
    }

    private static string NormalizeRawString(Token token)
    {
        var text = token.Text;
        var contentStart = text.StartsWith("\r\n", StringComparison.Ordinal) ? 2
            : text.StartsWith('\n') ? 1
            : 0;
        if (contentStart == 0)
        {
            return text;
        }

        var closingLineStart = text.LastIndexOf('\n');
        if (closingLineStart < contentStart)
        {
            throw ErrorAt(token, "a multiline raw string closing delimiter must begin on a new line");
        }

        var indentation = text[(closingLineStart + 1)..];
        if (indentation.Any(static c => c is not (' ' or '\t')))
        {
            throw ErrorAt(token, "only indentation may precede a multiline raw string closing delimiter");
        }

        var bodyEnd = closingLineStart;
        if (bodyEnd > contentStart && text[bodyEnd - 1] == '\r')
        {
            bodyEnd--;
        }

        var body = text[contentStart..bodyEnd];
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                lines[i] = string.Empty;
                continue;
            }

            if (!lines[i].StartsWith(indentation, StringComparison.Ordinal))
            {
                throw ErrorAt(token, "raw string content must match the closing delimiter indentation");
            }

            lines[i] = lines[i][indentation.Length..];
        }

        return string.Join('\n', lines);
    }

    private static string DecodeEscapes(string text)
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                result.Append(text[i]);
                continue;
            }

            var escaped = text[i + 1];
            switch (escaped)
            {
                case 'n':
                    result.Append('\n');
                    i++;
                    break;
                case 'r':
                    result.Append('\r');
                    i++;
                    break;
                case 't':
                    result.Append('\t');
                    i++;
                    break;
                case '\\':
                    result.Append('\\');
                    i++;
                    break;
                default:
                    result.Append('\\');
                    break;
            }
        }

        return result.ToString();
    }

    private static int FindExpressionInterpolationClose(string text, int start, Token token)
    {
        var depth = 1;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                i = FindStringClose(text, i + 1, token);
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c != ')')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return i;
            }
        }

        throw ErrorAt(token, "unterminated interpolation expression in string literal");
    }

    private static int FindStringClose(string text, int start, Token token)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                return i;
            }
        }

        throw ErrorAt(token, "unterminated string literal inside interpolation expression");
    }

    private static Expression ParseInterpolationExpression(string text, Token token)
    {
        try
        {
            return new Parser(new Lexer(text).Lex()).ParseExpressionFragment();
        }
        catch (SmallLangException ex)
        {
            throw ErrorAt(token, $"invalid interpolation expression '$({text})': {ex.Message}");
        }
    }

    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || char.IsLetter(c);
    }

    private static bool IsIdentifierPart(char c)
    {
        return c == '_' || char.IsLetterOrDigit(c);
    }

    private static SmallLangException ErrorAt(Token token, string message)
    {
        return new SmallLangException($"parse error at {token.Line}:{token.Column}: {message}");
    }
}
