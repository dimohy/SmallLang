using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Lexing;
using Sollang.Compiler.Parsing;

namespace Sollang.Compiler.Cli;

internal static class SourceFormatter
{
    public static int Run(string[] args)
    {
        var check = false;
        var stdin = false;
        var paths = new List<string>();
        foreach (var argument in args)
        {
            switch (argument)
            {
                case "--check":
                    check = true;
                    break;
                case "--stdin":
                    stdin = true;
                    break;
                default:
                    if (argument.StartsWith('-', StringComparison.Ordinal))
                    {
                        throw new SollangException($"unknown format option '{argument}'");
                    }
                    paths.Add(argument);
                    break;
            }
        }

        if (stdin)
        {
            if (paths.Count != 0 || check)
            {
                throw new SollangException("format --stdin cannot be combined with paths or --check");
            }
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Write(Format(Console.In.ReadToEnd()));
            return 0;
        }
        if (paths.Count == 0)
        {
            throw new SollangException("usage: sollang format [--check] <source.slg> ... | --stdin");
        }

        var changed = false;
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            var source = File.ReadAllText(fullPath, Encoding.UTF8);
            var formatted = Format(source);
            if (string.Equals(source, formatted, StringComparison.Ordinal))
            {
                continue;
            }
            changed = true;
            if (!check)
            {
                File.WriteAllText(fullPath, formatted, new UTF8Encoding(false));
            }
        }

        return check && changed ? 1 : 0;
    }

    public static string Format(string source)
    {
        Validate(source);
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var result = new StringBuilder(normalized.Length + 1);
        var depth = 0;
        var continuationBlockDepths = new List<int>();
        var inTripleString = false;

        var logicalLineCount = lines.Length;
        while (logicalLineCount > 0 && string.IsNullOrWhiteSpace(lines[logicalLineCount - 1]))
        {
            logicalLineCount--;
        }

        for (var index = 0; index < logicalLineCount; index++)
        {
            var original = lines[index];
            if (inTripleString)
            {
                result.AppendLine(original);
                ScanLine(original, ref inTripleString, out _, out _);
                continue;
            }

            var trimmed = original.Trim();
            if (trimmed.Length == 0)
            {
                result.AppendLine();
                continue;
            }

            ScanLine(trimmed, ref inTripleString, out var opens, out var closes);
            var leadingCloses = CountLeadingClosingBraces(trimmed);
            var lineDepth = Math.Max(0, depth - leadingCloses) + continuationBlockDepths.Count;
            var isContinuation = IsContinuation(trimmed);
            if (isContinuation)
            {
                lineDepth++;
            }
            result.Append(' ', lineDepth * 4).AppendLine(trimmed.TrimEnd());
            depth = Math.Max(0, depth + opens - closes);
            continuationBlockDepths.RemoveAll(blockDepth => blockDepth > depth);
            if (isContinuation && opens > closes)
            {
                continuationBlockDepths.Add(depth);
            }
        }

        var formatted = result.ToString().Replace(Environment.NewLine, "\n", StringComparison.Ordinal);
        Validate(formatted);
        return formatted;
    }

    private static bool IsContinuation(string line) =>
        line.StartsWith("->", StringComparison.Ordinal)
        || line.StartsWith("=>", StringComparison.Ordinal)
        || StartsWithWord(line, "and")
        || StartsWithWord(line, "or");

    private static bool StartsWithWord(string line, string word) =>
        line.StartsWith(word, StringComparison.Ordinal)
        && (line.Length == word.Length
            || (!char.IsLetterOrDigit(line[word.Length]) && line[word.Length] != '_'));

    private static int CountLeadingClosingBraces(string line)
    {
        var count = 0;
        foreach (var character in line)
        {
            if (character == '}') count++;
            else if (!char.IsWhiteSpace(character)) break;
        }
        return count;
    }

    private static void ScanLine(string line, ref bool inTripleString, out int opens, out int closes)
    {
        opens = 0;
        closes = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (index + 2 < line.Length && line.AsSpan(index, 3).SequenceEqual("\"\"\""))
            {
                inTripleString = !inTripleString;
                index += 2;
                continue;
            }
            if (inTripleString) continue;
            var character = line[index];
            if (!inString && character == '#') break;
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }
            if (character == '"') inString = true;
            else if (character == '{') opens++;
            else if (character == '}') closes++;
        }
    }

    private static void Validate(string source)
    {
        var tokens = new Lexer(source).Lex();
        _ = new Parser(tokens).Parse();
    }
}
