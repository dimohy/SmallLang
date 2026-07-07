using SLang.Compiler.Diagnostics;
using SLang.Compiler.Lexing;
using SLang.Compiler.Syntax;

namespace SLang.Compiler.Parsing;

internal static class StringLiteralParser
{
    public static IReadOnlyList<StringSegment> ParseStringSegments(Token token)
    {
        var segments = new List<StringSegment>();
        var text = token.Text;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '}')
            {
                throw ErrorAt(token, "unexpected '}' in string literal");
            }

            if (text[i] != '{')
            {
                continue;
            }

            if (i > start)
            {
                segments.Add(new TextSegment(text[start..i]));
            }

            var close = text.IndexOf('}', i + 1);
            if (close < 0)
            {
                throw ErrorAt(token, "unterminated interpolation in string literal");
            }

            var pathText = text[(i + 1)..close];
            var path = ParseInterpolationPath(pathText, token);
            segments.Add(new InterpolationSegment(path));
            i = close;
            start = i + 1;
        }

        if (start < text.Length)
        {
            segments.Add(new TextSegment(text[start..]));
        }

        return segments;
    }

    private static IReadOnlyList<string> ParseInterpolationPath(string text, Token token)
    {
        if (text.Length == 0)
        {
            throw ErrorAt(token, "empty interpolation is not allowed");
        }

        var parts = text.Split('.');
        foreach (var part in parts)
        {
            if (!IsIdentifier(part))
            {
                throw ErrorAt(token, $"invalid interpolation path '{{{text}}}'");
            }
        }

        return parts;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0])))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!(value[i] == '_' || char.IsLetterOrDigit(value[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static SlangException ErrorAt(Token token, string message)
    {
        return new SlangException($"parse error at {token.Line}:{token.Column}: {message}");
    }
}
