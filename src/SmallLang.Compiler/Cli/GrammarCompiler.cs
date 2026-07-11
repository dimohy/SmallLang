using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.Cli;

internal static class GrammarCompiler
{
    private const int Return = 0;
    private const int MatchToken = 1;
    private const int MatchKeyword = 2;
    private const int CallRule = 3;
    private const int Choice = 4;
    private const int Commit = 5;
    private const int Jump = 6;
    private const int LookaheadToken = 7;

    public static void Build(string[] args)
    {
        if (args.Length != 4 || args[2] != "-o")
        {
            throw new SmallLangException(
                "usage: smalllang grammar build <lexer-file> <grammar-file> -o <generated.sl>");
        }

        var lexerPath = Path.GetFullPath(args[0]);
        var grammarPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[3]);
        var lexer = ReadLexer(lexerPath);
        var grammar = ReadGrammar(grammarPath);
        var compiled = Compile(lexer, grammar);
        var sourceHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(File.ReadAllText(lexerPath) + "\n---grammar---\n" + File.ReadAllText(grammarPath))))
            .ToLowerInvariant();
        var source = EmitSlModule(compiled, lexerPath, grammarPath, sourceHash);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? Directory.GetCurrentDirectory());
        File.WriteAllText(outputPath, source, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outputPath} ({compiled.Program.Count} grammar words)");
    }

    private static LexerInput ReadLexer(string path)
    {
        if (!File.Exists(path)) throw new SmallLangException($"lexer specification not found: {path}");
        var names = new List<string>();
        var rules = new List<LexerRuleInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, number) in ReadMeaningfulLines(path))
        {
            var isToken = line.StartsWith("token ", StringComparison.Ordinal);
            var isSkip = line.StartsWith("skip ", StringComparison.Ordinal);
            if (!isToken && !isSkip) continue;
            var equal = line.IndexOf('=');
            if (equal < 0) throw SpecError(path, number, "lexer rule must use '='");
            var prefixLength = isToken ? "token ".Length : "skip ".Length;
            var name = line[prefixLength..equal].Trim();
            RequireIdentifier(path, number, name, "lexer rule");
            if (!seen.Add(name)) throw SpecError(path, number, $"duplicate lexer rule '{name}'");
            var tokenId = -1;
            if (isToken)
            {
                tokenId = names.Count;
                names.Add(name);
            }
            var pattern = line[(equal + 1)..].Trim();
            rules.Add(ParseLexerRule(path, number, name, tokenId, pattern));
        }
        if (names.Count == 0) throw new SmallLangException($"{path}: lexer has no token rules");
        return new LexerInput(names, rules);
    }

    private static LexerRuleInput ParseLexerRule(
        string path, int line, string name, int tokenId, string pattern)
    {
        if (pattern.Length >= 2 && pattern[0] == '"' && pattern[^1] == '"')
        {
            return new LexerRuleInput(name, tokenId, 7, pattern[1..^1]);
        }
        var kind = pattern switch
        {
            "whitespace" => 0,
            "line_comment" => 1,
            "identifier" => 2,
            "quoted_string" => 3,
            "number" => 4,
            "newline" => 5,
            "end" => 6,
            _ => throw SpecError(path, line, $"unknown lexer pattern '{pattern}'")
        };
        return new LexerRuleInput(name, tokenId, kind, null);
    }

    private static GrammarInput ReadGrammar(string path)
    {
        if (!File.Exists(path)) throw new SmallLangException($"grammar specification not found: {path}");
        var start = "";
        var rules = new List<RuleInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, number) in ReadMeaningfulLines(path))
        {
            if (line.StartsWith("parser ", StringComparison.Ordinal)) continue;
            if (line.StartsWith("start ", StringComparison.Ordinal))
            {
                start = line["start ".Length..].Trim();
                RequireIdentifier(path, number, start, "start rule");
                continue;
            }
            if (!line.StartsWith("rule ", StringComparison.Ordinal))
            {
                throw SpecError(path, number, "expected 'parser', 'start', or 'rule'");
            }
            var equal = line.IndexOf('=');
            if (equal < 0) throw SpecError(path, number, "grammar rule must use '='");
            var name = line["rule ".Length..equal].Trim();
            RequireIdentifier(path, number, name, "rule");
            if (!seen.Add(name)) throw SpecError(path, number, $"duplicate rule '{name}'");
            var production = line[(equal + 1)..].Trim();
            if (production.Length == 0) throw SpecError(path, number, $"rule '{name}' is empty");
            rules.Add(new RuleInput(name, production, number));
        }
        if (start.Length == 0) throw new SmallLangException($"{path}: missing start rule");
        if (!seen.Contains(start)) throw new SmallLangException($"{path}: unknown start rule '{start}'");
        return new GrammarInput(start, rules);
    }

    private static CompiledGrammar Compile(LexerInput lexer, GrammarInput grammar)
    {
        var tokens = lexer.TokenNames;
        var tokenIds = tokens.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
        var ruleIds = grammar.Rules.Select((rule, index) => (rule.Name, index))
            .ToDictionary(x => x.Name, x => x.index, StringComparer.Ordinal);
        var strings = new List<string>();
        var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int StringId(string value)
        {
            if (stringIds.TryGetValue(value, out var id)) return id;
            id = strings.Count;
            strings.Add(value);
            stringIds.Add(value, id);
            return id;
        }

        var expressions = grammar.Rules.Select(rule =>
            new ProductionParser(rule.Production, rule.Line, tokenIds, ruleIds, StringId).Parse()).ToArray();
        var program = new List<int>();
        var offsets = new List<int>();
        foreach (var expression in expressions)
        {
            offsets.Add(program.Count);
            Emit(expression, program);
            program.Add(Return);
        }
        VerifyProgram(program, offsets, tokens.Count, grammar.Rules.Count, strings.Count);
        var lexerLiterals = lexer.Rules.Where(rule => rule.Literal is not null)
            .Select(rule => rule.Literal!).Distinct(StringComparer.Ordinal).ToArray();
        var lexerLiteralIds = lexerLiterals.Select((value, index) => (value, index))
            .ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal);
        return new CompiledGrammar(tokens, grammar.Rules.Select(x => x.Name).ToArray(), strings,
            offsets, program, ruleIds[grammar.Start], lexer.Rules.Select(x => x.Name).ToArray(),
            lexer.Rules.Select(x => x.Kind).ToArray(), lexer.Rules.Select(x => x.TokenId).ToArray(),
            lexer.Rules.Select(x => x.Literal is null ? -1 : lexerLiteralIds[x.Literal]).ToArray(),
            lexerLiterals);
    }

    private static void VerifyProgram(
        IReadOnlyList<int> program,
        IReadOnlyList<int> ruleOffsets,
        int tokenCount,
        int ruleCount,
        int stringCount)
    {
        if (ruleOffsets.Count != ruleCount || ruleOffsets.Any(offset => offset < 0 || offset >= program.Count))
        {
            throw new SmallLangException("generated grammar has an invalid rule offset table");
        }
        for (var pc = 0; pc < program.Count;)
        {
            var op = program[pc++];
            switch (op)
            {
                case Return:
                    break;
                case MatchToken:
                case LookaheadToken:
                    RequireOperand(program, ref pc, value => value >= 0 && value < tokenCount, "token id");
                    break;
                case MatchKeyword:
                    RequireOperand(program, ref pc, value => value >= 0 && value < tokenCount, "keyword token id");
                    RequireOperand(program, ref pc, value => value >= 0 && value < stringCount, "keyword string id");
                    break;
                case CallRule:
                    RequireOperand(program, ref pc, value => value >= 0 && value < ruleCount, "rule id");
                    break;
                case Choice:
                case Commit:
                case Jump:
                    RequireOperand(program, ref pc, value => value >= 0 && value < program.Count, "instruction target");
                    break;
                default:
                    throw new SmallLangException($"generated grammar has unknown opcode {op}");
            }
        }
    }

    private static void RequireOperand(
        IReadOnlyList<int> program, ref int pc, Func<int, bool> predicate, string subject)
    {
        if (pc >= program.Count || !predicate(program[pc]))
        {
            throw new SmallLangException($"generated grammar has an invalid {subject}");
        }
        pc++;
    }

    private static void Emit(Node node, List<int> program)
    {
        switch (node)
        {
            case SequenceNode sequence:
                foreach (var item in sequence.Items) Emit(item, program);
                break;
            case ChoiceNode choice:
                EmitChoice(choice.Alternatives, 0, program);
                break;
            case OptionalNode optional:
            {
                program.Add(Choice);
                var target = program.Count;
                program.Add(0);
                Emit(optional.Item, program);
                program[target] = program.Count;
                break;
            }
            case RepeatNode repeat:
            {
                if (repeat.AtLeastOne) Emit(repeat.Item, program);
                var start = program.Count;
                program.Add(Choice);
                var target = program.Count;
                program.Add(0);
                Emit(repeat.Item, program);
                program.Add(Jump);
                program.Add(start);
                program[target] = program.Count;
                break;
            }
            case TokenNode token:
                program.Add(MatchToken);
                program.Add(token.TokenId);
                break;
            case KeywordNode keyword:
                program.Add(MatchKeyword);
                program.Add(keyword.TokenId);
                program.Add(keyword.StringId);
                break;
            case CallNode call:
                program.Add(CallRule);
                program.Add(call.RuleId);
                break;
            case LookaheadNode lookahead:
                program.Add(LookaheadToken);
                program.Add(lookahead.TokenId);
                break;
            default:
                throw new InvalidOperationException($"unknown grammar node {node.GetType().Name}");
        }
    }

    private static void EmitChoice(IReadOnlyList<Node> alternatives, int index, List<int> program)
    {
        if (index == alternatives.Count - 1)
        {
            Emit(alternatives[index], program);
            return;
        }
        program.Add(Choice);
        var nextTarget = program.Count;
        program.Add(0);
        Emit(alternatives[index], program);
        program.Add(Commit);
        var endTarget = program.Count;
        program.Add(0);
        program[nextTarget] = program.Count;
        EmitChoice(alternatives, index + 1, program);
        program[endTarget] = program.Count;
    }

    private static string EmitSlModule(
        CompiledGrammar grammar, string lexerPath, string grammarPath, string sourceHash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace syntax.generated.smalllang");
        builder.AppendLine();
        builder.AppendLine("# Generated by `smalllang grammar build`; do not edit by hand.");
        builder.AppendLine($"# Lexer source: {Path.GetFileName(lexerPath)}");
        builder.AppendLine($"# Grammar source: {Path.GetFileName(grammarPath)}");
        builder.AppendLine($"# Source SHA-256: {sourceHash}");
        builder.AppendLine("# Opcodes: 0 return, 1 token, 2 keyword, 3 call, 4 choice,");
        builder.AppendLine("# 5 commit, 6 jump, 7 token lookahead.");
        builder.AppendLine("# Lexer kinds: 0 whitespace, 1 line comment, 2 identifier,");
        builder.AppendLine("# 3 quoted string, 4 number, 5 newline, 6 end, 7 literal.");
        builder.AppendLine();
        EmitTextArrayFunction(builder, "tokenNames", grammar.TokenNames);
        EmitTextArrayFunction(builder, "lexerRuleNames", grammar.LexerRuleNames);
        EmitIntArrayFunction(builder, "lexerRuleKinds", grammar.LexerRuleKinds);
        EmitIntArrayFunction(builder, "lexerRuleTokens", grammar.LexerRuleTokens);
        EmitTextArrayFunction(builder, "lexerLiteralTexts", grammar.LexerLiteralTexts);
        EmitIntArrayFunction(builder, "lexerLiteralIndexes", grammar.LexerLiteralIndexes);
        EmitTextArrayFunction(builder, "ruleNames", grammar.RuleNames);
        EmitTextArrayFunction(builder, "keywordTexts", grammar.Strings);
        EmitIntArrayFunction(builder, "ruleOffsets", grammar.RuleOffsets);
        EmitIntArrayFunction(builder, "parserProgram", grammar.Program);
        builder.AppendLine($"public tokenCount: -> Int => {grammar.TokenNames.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"public ruleCount: -> Int => {grammar.RuleNames.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"public programWordCount: -> Int => {grammar.Program.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"public startRule: -> Int => {grammar.StartRule.ToString(CultureInfo.InvariantCulture)}");
        return builder.ToString();
    }

    private static void EmitTextArrayFunction(StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        builder.Append($"public {name}: -> [Text; ~] => [");
        builder.Append(string.Join(", ", values.Select(value => $"\"{value}\"")));
        builder.AppendLine(", ~]");
        builder.AppendLine();
    }

    private static void EmitIntArrayFunction(StringBuilder builder, string name, IReadOnlyList<int> values)
    {
        builder.Append($"public {name}: -> [Int; ~] => [");
        builder.Append(string.Join(", ", values.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        builder.AppendLine(", ~]");
        builder.AppendLine();
    }

    private static IEnumerable<(string Line, int Number)> ReadMeaningfulLines(string path)
    {
        var lines = File.ReadAllLines(path);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length > 0) yield return (line, index + 1);
        }
    }

    private static void RequireIdentifier(string path, int line, string value, string subject)
    {
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0]))
            || value.Skip(1).Any(ch => !(ch == '_' || char.IsLetterOrDigit(ch))))
        {
            throw SpecError(path, line, $"invalid {subject} name '{value}'");
        }
    }

    private static SmallLangException SpecError(string path, int line, string message) =>
        new($"{path}:{line}: {message}");

    private sealed record GrammarInput(string Start, IReadOnlyList<RuleInput> Rules);
    private sealed record RuleInput(string Name, string Production, int Line);
    private sealed record LexerInput(IReadOnlyList<string> TokenNames, IReadOnlyList<LexerRuleInput> Rules);
    private sealed record LexerRuleInput(string Name, int TokenId, int Kind, string? Literal);
    private sealed record CompiledGrammar(
        IReadOnlyList<string> TokenNames,
        IReadOnlyList<string> RuleNames,
        IReadOnlyList<string> Strings,
        IReadOnlyList<int> RuleOffsets,
        IReadOnlyList<int> Program,
        int StartRule,
        IReadOnlyList<string> LexerRuleNames,
        IReadOnlyList<int> LexerRuleKinds,
        IReadOnlyList<int> LexerRuleTokens,
        IReadOnlyList<int> LexerLiteralIndexes,
        IReadOnlyList<string> LexerLiteralTexts);

    private abstract record Node;
    private sealed record SequenceNode(IReadOnlyList<Node> Items) : Node;
    private sealed record ChoiceNode(IReadOnlyList<Node> Alternatives) : Node;
    private sealed record OptionalNode(Node Item) : Node;
    private sealed record RepeatNode(Node Item, bool AtLeastOne) : Node;
    private sealed record TokenNode(int TokenId) : Node;
    private sealed record KeywordNode(int TokenId, int StringId) : Node;
    private sealed record CallNode(int RuleId) : Node;
    private sealed record LookaheadNode(int TokenId) : Node;

    private sealed class ProductionParser(
        string text,
        int line,
        IReadOnlyDictionary<string, int> tokenIds,
        IReadOnlyDictionary<string, int> ruleIds,
        Func<string, int> getStringId)
    {
        private int _index;

        public Node Parse()
        {
            var result = ParseChoice();
            SkipSpaces();
            if (_index != text.Length) Fail($"unexpected '{text[_index]}'");
            return result;
        }

        private Node ParseChoice()
        {
            var alternatives = new List<Node> { ParseSequence() };
            while (TryTake('|')) alternatives.Add(ParseSequence());
            return alternatives.Count == 1 ? alternatives[0] : new ChoiceNode(alternatives);
        }

        private Node ParseSequence()
        {
            var items = new List<Node>();
            while (true)
            {
                SkipSpaces();
                if (_index == text.Length || text[_index] is ')' or '|') break;
                items.Add(ParseQuantified());
            }
            if (items.Count == 0) Fail("empty grammar sequence is not supported");
            return items.Count == 1 ? items[0] : new SequenceNode(items);
        }

        private Node ParseQuantified()
        {
            var node = ParsePrimary();
            SkipSpaces();
            if (_index == text.Length) return node;
            return text[_index] switch
            {
                '?' => TakeAnd(new OptionalNode(node)),
                '*' => TakeAnd(new RepeatNode(node, false)),
                '+' => TakeAnd(new RepeatNode(node, true)),
                _ => node
            };
        }

        private Node ParsePrimary()
        {
            SkipSpaces();
            if (TryTake('('))
            {
                var nested = ParseChoice();
                Require(')');
                return nested;
            }
            var name = ReadIdentifier();
            if (_index < text.Length && text[_index] == '(')
            {
                _index++;
                if (name == "lookahead")
                {
                    var token = ReadIdentifier();
                    Require(')');
                    return new LookaheadNode(RequireToken(token));
                }
                if (name == "Identifier")
                {
                    var value = ReadString();
                    Require(')');
                    return new KeywordNode(RequireToken(name), getStringId(value));
                }
                Fail($"unsupported grammar predicate '{name}(...)'");
            }
            if (ruleIds.TryGetValue(name, out var rule)) return new CallNode(rule);
            if (tokenIds.TryGetValue(name, out var tokenId)) return new TokenNode(tokenId);
            Fail($"unknown grammar symbol '{name}'");
            throw new InvalidOperationException("unreachable grammar parser state");
        }

        private string ReadIdentifier()
        {
            SkipSpaces();
            var start = _index;
            while (_index < text.Length && (text[_index] == '_' || char.IsLetterOrDigit(text[_index]))) _index++;
            if (start == _index) Fail("expected grammar symbol");
            return text[start.._index];
        }

        private string ReadString()
        {
            SkipSpaces();
            Require('"');
            var start = _index;
            while (_index < text.Length && text[_index] != '"') _index++;
            if (_index == text.Length) Fail("unterminated grammar string");
            var value = text[start.._index];
            _index++;
            return value;
        }

        private int RequireToken(string name) => tokenIds.TryGetValue(name, out var id)
            ? id
            : throw new SmallLangException($"grammar line {line}: unknown token '{name}'");

        private T TakeAnd<T>(T value)
        {
            _index++;
            return value;
        }

        private bool TryTake(char value)
        {
            SkipSpaces();
            if (_index >= text.Length || text[_index] != value) return false;
            _index++;
            return true;
        }

        private void Require(char value)
        {
            if (!TryTake(value)) Fail($"expected '{value}'");
        }

        private void SkipSpaces()
        {
            while (_index < text.Length && char.IsWhiteSpace(text[_index])) _index++;
        }

        private void Fail(string message) => throw new SmallLangException($"grammar line {line}: {message}");
    }
}
