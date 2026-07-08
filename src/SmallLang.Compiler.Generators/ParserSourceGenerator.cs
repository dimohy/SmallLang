using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SmallLang.Compiler.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ParserSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidSpec = new(
        id: "SLANGGEN002",
        title: "Invalid SmallLang grammar specification",
        messageFormat: "{0}",
        category: "SmallLang.Parser",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var specs = context.AdditionalTextsProvider
            .Where(static text => Path.GetFileName(text.Path).Equals("smalllang.grammar", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) => GrammarSpec.Parse(
                text.Path,
                text.GetText(cancellationToken)?.ToString() ?? string.Empty));

        context.RegisterSourceOutput(specs, static (sourceProductionContext, spec) =>
        {
            if (spec.Error is not null)
            {
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(InvalidSpec, Location.None, spec.Error));
                return;
            }

            sourceProductionContext.AddSource(
                "GeneratedParser.g.cs",
                SourceText.From(ParserEmitter.Emit(spec), Encoding.UTF8));
        });
    }
}

internal sealed record GrammarSpec(
    string ParserType,
    string StartRule,
    ImmutableArray<GrammarRule> Rules,
    string? Error)
{
    private static readonly string[] RequiredRules =
    [
        "SourceFile",
        "FunctionDeclaration",
        "FunctionSignature",
        "MainBlock",
        "Statement",
        "EachStatement",
        "BindingStatement",
        "ExpressionStatement",
        "StatementEnd",
        "RangeExpression",
        "Expression",
        "FlowExpression",
        "AdditiveExpression",
        "MultiplicativeExpression",
        "PrimaryExpression",
        "CallExpression",
        "ArgumentList",
        "Path",
        "TypeName",
        "StringExpression",
        "NumberExpression",
        "NameExpression"
    ];

    public static GrammarSpec Parse(string path, string text)
    {
        var parserType = "SmallLang.Compiler.Parsing.Parser";
        var startRule = "SourceFile";
        var rules = ImmutableArray.CreateBuilder<GrammarRule>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = StripComment(lines[i]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("parser ", StringComparison.Ordinal))
            {
                parserType = line.Substring("parser ".Length).Trim();
                if (!IsQualifiedIdentifier(parserType))
                {
                    return Failure(path, lineNumber, $"invalid parser type '{parserType}'");
                }

                continue;
            }

            if (line.StartsWith("start ", StringComparison.Ordinal))
            {
                startRule = line.Substring("start ".Length).Trim();
                if (!IsIdentifier(startRule))
                {
                    return Failure(path, lineNumber, $"invalid start rule '{startRule}'");
                }

                continue;
            }

            if (line.StartsWith("rule ", StringComparison.Ordinal))
            {
                var parsed = ParseRule(line.Substring("rule ".Length), path, lineNumber);
                if (parsed.Error is not null)
                {
                    return parsed.Error;
                }

                if (!names.Add(parsed.Rule!.Name))
                {
                    return Failure(path, lineNumber, $"duplicate grammar rule '{parsed.Rule.Name}'");
                }

                rules.Add(parsed.Rule);
                continue;
            }

            return Failure(path, lineNumber, $"expected 'parser', 'start', or 'rule', got '{line}'");
        }

        if (startRule != "SourceFile")
        {
            return Failure(path, 0, "current parser generator requires 'start SourceFile'");
        }

        foreach (var required in RequiredRules)
        {
            if (!names.Contains(required))
            {
                return Failure(path, 0, $"grammar specification must declare rule '{required}'");
            }
        }

        return new GrammarSpec(parserType, startRule, rules.ToImmutable(), null);
    }

    private static GrammarParseResult ParseRule(string line, string path, int lineNumber)
    {
        var equal = line.IndexOf('=');
        if (equal < 0)
        {
            return GrammarParseResult.Fail(Failure(path, lineNumber, "rule must use '='"));
        }

        var name = line.Substring(0, equal).Trim();
        var production = line.Substring(equal + 1).Trim();
        if (!IsIdentifier(name))
        {
            return GrammarParseResult.Fail(Failure(path, lineNumber, $"invalid rule name '{name}'"));
        }

        if (production.Length == 0)
        {
            return GrammarParseResult.Fail(Failure(path, lineNumber, $"rule '{name}' has an empty production"));
        }

        return GrammarParseResult.Ok(new GrammarRule(name, production));
    }

    private static string StripComment(string line)
    {
        var comment = line.IndexOf('#');
        return comment >= 0 ? line.Substring(0, comment) : line;
    }

    private static bool IsQualifiedIdentifier(string value)
    {
        return value.Split('.').All(IsIdentifier);
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

    private static GrammarSpec Failure(string path, int line, string message)
    {
        var prefix = line > 0 ? $"{path}:{line}" : path;
        return new GrammarSpec(
            "SmallLang.Compiler.Parsing.Parser",
            "SourceFile",
            ImmutableArray<GrammarRule>.Empty,
            $"{prefix}: {message}");
    }
}

internal sealed record GrammarRule(string Name, string Production);

internal sealed record GrammarParseResult(GrammarRule? Rule, GrammarSpec? Error)
{
    public static GrammarParseResult Ok(GrammarRule rule) => new(rule, null);

    public static GrammarParseResult Fail(GrammarSpec error) => new(null, error);
}

internal static class ParserEmitter
{
    public static string Emit(GrammarSpec spec)
    {
        var lastDot = spec.ParserType.LastIndexOf('.');
        var ns = lastDot >= 0 ? spec.ParserType.Substring(0, lastDot) : "SmallLang.Compiler.Parsing";
        var typeName = lastDot >= 0 ? spec.ParserType.Substring(lastDot + 1) : spec.ParserType;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using SmallLang.Compiler.Diagnostics;");
        builder.AppendLine("using SmallLang.Compiler.Lexing;");
        builder.AppendLine("using SmallLang.Compiler.Syntax;");
        builder.AppendLine();
        builder.Append("namespace ").Append(ns).AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("// Grammar source:");
        foreach (var rule in spec.Rules)
        {
            builder.Append("//   ")
                .Append(rule.Name)
                .Append(" = ")
                .AppendLine(rule.Production);
        }

        builder.AppendLine();
        builder.Append("internal sealed class ").Append(typeName).AppendLine("(IReadOnlyList<Token> tokens)");
        builder.AppendLine("{");
        builder.AppendLine("    private int _index;");
        builder.AppendLine();
        builder.AppendLine("    public SmallLangProgram Parse()");
        builder.AppendLine("    {");
        builder.AppendLine("        // SourceFile = NewLine* FunctionDeclaration* MainBlock NewLine* End");
        builder.AppendLine("        var functions = new List<FunctionDeclaration>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        while (Check(TokenKind.Identifier) && CheckNext(TokenKind.Colon))");
        builder.AppendLine("        {");
        builder.AppendLine("            functions.Add(ParseFunctionDeclaration());");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var statements = ParseMainBlock();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.End);");
        builder.AppendLine("        return new SmallLangProgram(functions, statements);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private FunctionDeclaration ParseFunctionDeclaration()");
        builder.AppendLine("    {");
        builder.AppendLine("        // FunctionDeclaration = Identifier Colon FunctionSignature LeftBrace NewLine* Expression NewLine* RightBrace");
        builder.AppendLine("        var name = ExpectIdentifier();");
        builder.AppendLine("        Expect(TokenKind.Colon);");
        builder.AppendLine("        var inputType = ParseOptionalInputType();");
        builder.AppendLine("        Expect(TokenKind.Arrow);");
        builder.AppendLine("        var returnType = ExpectIdentifier();");
        builder.AppendLine("        Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        var body = ParseExpression();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new FunctionDeclaration(name.Text, inputType, returnType.Text, body, name.Line, name.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private string? ParseOptionalInputType()");
        builder.AppendLine("    {");
        builder.AppendLine("        // FunctionSignature = Arrow TypeName | TypeName Arrow TypeName");
        builder.AppendLine("        if (Check(TokenKind.Arrow))");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return ExpectIdentifier().Text;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private IReadOnlyList<Statement> ParseMainBlock()");
        builder.AppendLine("    {");
        builder.AppendLine("        // MainBlock = Identifier(\"main\") LeftBrace NewLine* Statement* RightBrace");
        builder.AppendLine("        var main = ExpectIdentifier(\"main\");");
        builder.AppendLine("        if (main.Text != \"main\")");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(main, \"expected 'main' block\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        var statements = new List<Statement>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            statements.Add(ParseStatement());");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return statements;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // Statement = EachStatement | BindingStatement | ExpressionStatement");
        builder.AppendLine("        if (CheckIdentifier(\"each\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseEachStatement();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Check(TokenKind.Identifier) && CheckNext(TokenKind.Equal))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseBindingStatement();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return ParseExpressionStatement();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseEachStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // EachStatement = Identifier(\"each\") Identifier Identifier(\"in\") RangeExpression LeftBrace NewLine* Statement* RightBrace");
        builder.AppendLine("        var each = ExpectIdentifier(\"each\");");
        builder.AppendLine("        var item = ExpectIdentifier();");
        builder.AppendLine("        ExpectIdentifier(\"in\");");
        builder.AppendLine("        var start = ParseExpression();");
        builder.AppendLine("        Expect(TokenKind.Range);");
        builder.AppendLine("        var end = ParseExpression();");
        builder.AppendLine("        Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        var body = new List<Statement>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            body.Add(ParseStatement());");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new EachStatement(item.Text, start, end, body, each.Line, each.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseBindingStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // BindingStatement = Identifier Equal Expression StatementEnd");
        builder.AppendLine("        var name = ExpectIdentifier();");
        builder.AppendLine("        Expect(TokenKind.Equal);");
        builder.AppendLine("        var expression = ParseExpression();");
        builder.AppendLine("        ExpectStatementEnd();");
        builder.AppendLine("        return new BindingStatement(name.Text, expression, name.Line, name.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseExpressionStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // ExpressionStatement = Expression StatementEnd");
        builder.AppendLine("        var expression = ParseExpression();");
        builder.AppendLine("        ExpectStatementEnd();");
        builder.AppendLine("        return new ExpressionStatement(expression);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // Expression = FlowExpression");
        builder.AppendLine("        return ParseFlowExpression();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseFlowExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // FlowExpression = AdditiveExpression (Arrow Path)*");
        builder.AppendLine("        var expression = ParseAdditiveExpression();");
        builder.AppendLine("        List<IReadOnlyList<string>>? targets = null;");
        builder.AppendLine("        while (Match(TokenKind.Arrow, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            var target = ExpectIdentifier();");
        builder.AppendLine("            var path = ParsePathAfterFirstIdentifier(target);");
        builder.AppendLine("            targets ??= [];");
        builder.AppendLine("            targets.Add(path);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return targets is null");
        builder.AppendLine("            ? expression");
        builder.AppendLine("            : new FlowExpression(expression, targets, expression.Line, expression.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseAdditiveExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // AdditiveExpression = MultiplicativeExpression (Plus MultiplicativeExpression)*");
        builder.AppendLine("        var expression = ParseMultiplicativeExpression();");
        builder.AppendLine("        while (Match(TokenKind.Plus, out var plus))");
        builder.AppendLine("        {");
        builder.AppendLine("            var right = ParseMultiplicativeExpression();");
        builder.AppendLine("            expression = new AddExpression(expression, right, plus.Line, plus.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseMultiplicativeExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // MultiplicativeExpression = PrimaryExpression (Star PrimaryExpression)*");
        builder.AppendLine("        var expression = ParsePrimaryExpression();");
        builder.AppendLine("        while (Match(TokenKind.Star, out var star))");
        builder.AppendLine("        {");
        builder.AppendLine("            var right = ParsePrimaryExpression();");
        builder.AppendLine("            expression = new MultiplyExpression(expression, right, star.Line, star.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParsePrimaryExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // PrimaryExpression = CallExpression | StringExpression | NumberExpression | NameExpression");
        builder.AppendLine("        if (Match(TokenKind.String, out var stringToken))");
        builder.AppendLine("        {");
        builder.AppendLine("            return new StringExpression(StringLiteralParser.ParseStringSegments(stringToken), stringToken.Line, stringToken.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.Number, out var numberToken))");
        builder.AppendLine("        {");
        builder.AppendLine("            return new NumberExpression(numberToken.Text, numberToken.Line, numberToken.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.Identifier, out var identifier))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParsePathStartingWith(identifier);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw Error(Peek(), \"expected expression\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParsePathStartingWith(Token identifier)");
        builder.AppendLine("    {");
        builder.AppendLine("        // Path = Identifier (Dot Identifier)*");
        builder.AppendLine("        var path = ParsePathAfterFirstIdentifier(identifier);");
        builder.AppendLine("        if (Match(TokenKind.LeftParen, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseCallExpression(identifier, path);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (path.Count != 1)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(identifier, \"path values are reserved for calls and interpolation\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new NameExpression(identifier.Text, identifier.Line, identifier.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private IReadOnlyList<string> ParsePathAfterFirstIdentifier(Token identifier)");
        builder.AppendLine("    {");
        builder.AppendLine("        // Path = Identifier (Dot Identifier)*");
        builder.AppendLine("        var path = new List<string> { identifier.Text };");
        builder.AppendLine("        while (Match(TokenKind.Dot, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            path.Add(ExpectIdentifier().Text);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return path;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private CallExpression ParseCallExpression(Token start, IReadOnlyList<string> path)");
        builder.AppendLine("    {");
        builder.AppendLine("        // CallExpression = Path LeftParen ArgumentList? RightParen");
        builder.AppendLine("        var arguments = new List<Expression>();");
        builder.AppendLine("        if (!Check(TokenKind.RightParen))");
        builder.AppendLine("        {");
        builder.AppendLine("            ParseArgumentList(arguments);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightParen);");
        builder.AppendLine("        return new CallExpression(path, arguments, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void ParseArgumentList(List<Expression> arguments)");
        builder.AppendLine("    {");
        builder.AppendLine("        // ArgumentList = Expression (Comma Expression)*");
        builder.AppendLine("        do");
        builder.AppendLine("        {");
        builder.AppendLine("            arguments.Add(ParseExpression());");
        builder.AppendLine("        }");
        builder.AppendLine("        while (Match(TokenKind.Comma, out _));");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void ExpectStatementEnd()");
        builder.AppendLine("    {");
        builder.AppendLine("        // StatementEnd = NewLine+ | lookahead(RightBrace) | lookahead(End)");
        builder.AppendLine("        if (Check(TokenKind.NewLine) || Check(TokenKind.RightBrace) || Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw Error(Peek(), \"expected end of statement\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void SkipNewLines()");
        builder.AppendLine("    {");
        builder.AppendLine("        while (Check(TokenKind.NewLine))");
        builder.AppendLine("        {");
        builder.AppendLine("            Advance();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Token Expect(TokenKind kind)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (Match(kind, out var token))");
        builder.AppendLine("        {");
        builder.AppendLine("            return token;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw Error(Peek(), $\"expected {kind}\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Token ExpectIdentifier(string? text = null)");
        builder.AppendLine("    {");
        builder.AppendLine("        var token = Expect(TokenKind.Identifier);");
        builder.AppendLine("        if (text is not null && token.Text != text)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(token, $\"expected '{text}'\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return token;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool Match(TokenKind kind, out Token token)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (Check(kind))");
        builder.AppendLine("        {");
        builder.AppendLine("            token = Advance();");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        token = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool Check(TokenKind kind)");
        builder.AppendLine("    {");
        builder.AppendLine("        return Peek().Kind == kind;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool CheckIdentifier(string text)");
        builder.AppendLine("    {");
        builder.AppendLine("        return Check(TokenKind.Identifier) && Peek().Text == text;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool CheckNext(TokenKind kind)");
        builder.AppendLine("    {");
        builder.AppendLine("        return _index + 1 < tokens.Count && tokens[_index + 1].Kind == kind;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Token Advance()");
        builder.AppendLine("    {");
        builder.AppendLine("        return tokens[_index++];");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Token Peek()");
        builder.AppendLine("    {");
        builder.AppendLine("        return tokens[_index];");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static SmallLangException Error(Token token, string message)");
        builder.AppendLine("    {");
        builder.AppendLine("        return new SmallLangException($\"parse error at {token.Line}:{token.Column}: {message}\");");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }
}
