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
        "NamespaceDeclaration",
        "ImportDeclaration",
        "FunctionDeclaration",
        "FunctionSignature",
        "FunctionBody",
        "BlockFunctionBody",
        "MainBlock",
        "Statement",
        "BlockFunctionCallStatement",
        "EachStatement",
        "BindingStatement",
        "ExpressionStatement",
        "StatementEnd",
        "RangeExpression",
        "Expression",
        "FlowExpression",
        "RangeOrLogicalExpression",
        "IfFlowTarget",
        "WhenFlowTarget",
        "FoldFlowTarget",
        "WhenExpression",
        "WhenArm",
        "WhenCondition",
        "SubjectWhenArm",
        "SubjectWhenCondition",
        "WhenArmBody",
        "BlockBody",
        "LogicalOrExpression",
        "LogicalAndExpression",
        "EqualityExpression",
        "ComparisonExpression",
        "AdditiveExpression",
        "MultiplicativeExpression",
        "UnaryExpression",
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
        builder.AppendLine("using System;");
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
        builder.Append("internal sealed class ").Append(typeName).AppendLine("(IReadOnlyList<Token> tokens, bool isStandardLibrary = false)");
        builder.AppendLine("{");
        builder.AppendLine("    private int _index;");
        builder.AppendLine("    private IReadOnlyList<string> _namespacePath = [];");
        builder.AppendLine("    private readonly Dictionary<string, IReadOnlyList<string>> _imports = new(StringComparer.Ordinal);");
        builder.AppendLine();
        builder.AppendLine("    public SmallLangProgram Parse()");
        builder.AppendLine("    {");
        builder.AppendLine("        // SourceFile = NewLine* NamespaceDeclaration? ImportDeclaration* FunctionDeclaration* (MainBlock | Statement*) NewLine* End");
        builder.AppendLine("        var functions = new List<FunctionDeclaration>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        if (CheckIdentifier(\"namespace\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            ParseNamespaceDeclaration();");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        while (CheckIdentifier(\"import\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            ParseImportDeclaration();");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        while (IsFunctionDeclarationStart())");
        builder.AppendLine("        {");
        builder.AppendLine("            functions.Add(ParseFunctionDeclaration(isLocal: false));");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var statements = CheckIdentifier(\"main\")");
        builder.AppendLine("            ? ParseMainBlock()");
        builder.AppendLine("            : ParseTopLevelStatements();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.End);");
        builder.AppendLine("        return new SmallLangProgram(functions, statements);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public Expression ParseExpressionFragment()");
        builder.AppendLine("    {");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        var expression = ParseExpression();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.End);");
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void ParseNamespaceDeclaration()");
        builder.AppendLine("    {");
        builder.AppendLine("        // NamespaceDeclaration = Identifier(\"namespace\") Path StatementEnd");
        builder.AppendLine("        ExpectIdentifier(\"namespace\");");
        builder.AppendLine("        var start = ExpectIdentifier();");
        builder.AppendLine("        _namespacePath = ParsePathAfterFirstIdentifier(start);");
        builder.AppendLine("        ExpectStatementEnd();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void ParseImportDeclaration()");
        builder.AppendLine("    {");
        builder.AppendLine("        // ImportDeclaration = Identifier(\"import\") Path (Identifier(\"as\") Identifier)? StatementEnd");
        builder.AppendLine("        var importToken = ExpectIdentifier(\"import\");");
        builder.AppendLine("        var start = ExpectIdentifier();");
        builder.AppendLine("        var path = ParsePathAfterFirstIdentifier(start);");
        builder.AppendLine("        var alias = path[path.Count - 1];");
        builder.AppendLine("        if (CheckIdentifier(\"as\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            ExpectIdentifier(\"as\");");
        builder.AppendLine("            alias = ExpectIdentifier().Text;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (_imports.ContainsKey(alias))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(importToken, $\"import alias '{alias}' already exists\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        _imports.Add(alias, path);");
        builder.AppendLine("        ExpectStatementEnd();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private FunctionDeclaration ParseFunctionDeclaration(bool isLocal)");
        builder.AppendLine("    {");
        builder.AppendLine("        // FunctionDeclaration = Path Identifier? Colon FunctionSignature (BlockFunctionBody | FunctionBody)");
        builder.AppendLine("        var name = ExpectIdentifier();");
        builder.AppendLine("        var namePath = ParsePathAfterFirstIdentifier(name);");
        builder.AppendLine("        var functionName = QualifyFunctionName(namePath, isLocal);");
        builder.AppendLine("        Token? inputName = null;");
        builder.AppendLine("        if (Check(TokenKind.Identifier) && CheckNext(TokenKind.Colon))");
        builder.AppendLine("        {");
        builder.AppendLine("            inputName = ExpectIdentifier();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.Colon);");
        builder.AppendLine("        var inputType = ParseOptionalInputType();");
        builder.AppendLine("        if (inputName is not null && inputType is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(inputName.Value, \"function input name requires an input type\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.Arrow);");
        builder.AppendLine("        var returnType = ExpectIdentifier();");
        builder.AppendLine("        Token? blockInputName = null;");
        builder.AppendLine("        Token? blockInputType = null;");
        builder.AppendLine("        var localFunctions = new List<FunctionDeclaration>();");
        builder.AppendLine("        Expression? body = null;");
        builder.AppendLine("        var blockBody = new List<Statement>();");
        builder.AppendLine("        var isIntrinsic = false;");
        builder.AppendLine("        if (CheckIdentifier(\"block\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            // BlockFunctionBody = Identifier(\"block\") Identifier Colon TypeName LeftBrace NewLine* Statement* RightBrace");
        builder.AppendLine("            ExpectIdentifier(\"block\");");
        builder.AppendLine("            blockInputName = ExpectIdentifier();");
        builder.AppendLine("            Expect(TokenKind.Colon);");
        builder.AppendLine("            blockInputType = ExpectIdentifier();");
        builder.AppendLine("            Expect(TokenKind.LeftBrace);");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("            while (!Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("            {");
        builder.AppendLine("                blockBody.Add(ParseStatement());");
        builder.AppendLine("                SkipNewLines();");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            Expect(TokenKind.RightBrace);");
        builder.AppendLine("        }");
        builder.AppendLine("        else if (Match(TokenKind.Equal, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            ExpectIdentifier(\"intrinsic\");");
        builder.AppendLine("            isIntrinsic = true;");
        builder.AppendLine("        }");
        builder.AppendLine("        else if (Match(TokenKind.FatArrow, out _) || Match(TokenKind.Arrow, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            body = ParseExpression();");
        builder.AppendLine("        }");
        builder.AppendLine("        else");
        builder.AppendLine("        {");
        builder.AppendLine("            Expect(TokenKind.LeftBrace);");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("            while (IsFunctionDeclarationStart())");
        builder.AppendLine("            {");
        builder.AppendLine("                localFunctions.Add(ParseFunctionDeclaration(isLocal: true));");
        builder.AppendLine("                SkipNewLines();");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            body = ParseExpression();");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("            Expect(TokenKind.RightBrace);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new FunctionDeclaration(functionName, inputName?.Text, inputType, returnType.Text, blockInputName?.Text, blockInputType?.Text, localFunctions, body, blockBody, name.Line, name.Column, isIntrinsic, isStandardLibrary);");
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
        builder.AppendLine("    private IReadOnlyList<Statement> ParseTopLevelStatements()");
        builder.AppendLine("    {");
        builder.AppendLine("        // Source files may omit main; remaining top-level statements become the main body.");
        builder.AppendLine("        var statements = new List<Statement>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            statements.Add(ParseStatement());");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return statements;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // Statement = BlockFunctionCallStatement | EachStatement | BindingStatement | ExpressionStatement");
        builder.AppendLine("        if (CheckIdentifier(\"each\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseEachStatement();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (TryParseBlockFunctionCallStatement(out var blockFunctionCallStatement))");
        builder.AppendLine("        {");
        builder.AppendLine("            return blockFunctionCallStatement;");
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
        builder.AppendLine("    private bool TryParseBlockFunctionCallStatement(out Statement statement)");
        builder.AppendLine("    {");
        builder.AppendLine("        // BlockFunctionCallStatement = RangeOrLogicalExpression Arrow Path Identifier? LeftBrace NewLine* Statement* RightBrace");
        builder.AppendLine("        statement = null!;");
        builder.AppendLine();
        builder.AppendLine("        var saved = _index;");
        builder.AppendLine();
        builder.AppendLine("        var source = ParseRangeOrLogicalExpression();");
        builder.AppendLine();
        builder.AppendLine("        if (!Match(TokenKind.Arrow, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var targetToken = ExpectIdentifier();");
        builder.AppendLine("        if (targetToken.Text is \"if\" or \"when\" or \"fold\")");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var target = ResolveImportedPath(ParsePathAfterFirstIdentifier(targetToken));");
        builder.AppendLine("        var itemName = \"it\";");
        builder.AppendLine("        var usesDefaultItemName = true;");
        builder.AppendLine("        if (!Check(TokenKind.Identifier) && !Check(TokenKind.LeftBrace))");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Check(TokenKind.Identifier))");
        builder.AppendLine("        {");
        builder.AppendLine("            var item = ExpectIdentifier();");
        builder.AppendLine("            itemName = item.Text;");
        builder.AppendLine("            usesDefaultItemName = false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (!Check(TokenKind.LeftBrace))");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
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
        builder.AppendLine("        statement = new BlockFunctionCallStatement(source, target, itemName, body, targetToken.Line, targetToken.Column, usesDefaultItemName);");
        builder.AppendLine("        return true;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseEachStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // EachStatement = Identifier(\"each\") Identifier Identifier(\"in\") RangeExpression LeftBrace NewLine* Statement* RightBrace");
        builder.AppendLine("        var each = ExpectIdentifier(\"each\");");
        builder.AppendLine("        var item = ExpectIdentifier();");
        builder.AppendLine("        ExpectIdentifier(\"in\");");
        builder.AppendLine("        var source = ParseRangeExpression();");
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
        builder.AppendLine("        return new BlockFunctionCallStatement(source, new[] { \"each\" }, item.Text, body, each.Line, each.Column, false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool TryParseRangeExpression(out RangeExpression range)");
        builder.AppendLine("    {");
        builder.AppendLine("        // RangeExpression = LogicalOrExpression Range LogicalOrExpression");
        builder.AppendLine("        var saved = _index;");
        builder.AppendLine("        range = null!;");
        builder.AppendLine("        var start = ParseLogicalOrExpression();");
        builder.AppendLine("        if (!Match(TokenKind.Range, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var end = ParseLogicalOrExpression();");
        builder.AppendLine("        range = new RangeExpression(start, end, start.Line, start.Column);");
        builder.AppendLine("        return true;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private RangeExpression ParseRangeExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // RangeExpression = LogicalOrExpression Range LogicalOrExpression");
        builder.AppendLine("        var start = ParseLogicalOrExpression();");
        builder.AppendLine("        Expect(TokenKind.Range);");
        builder.AppendLine("        var end = ParseLogicalOrExpression();");
        builder.AppendLine("        return new RangeExpression(start, end, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseBindingStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // BindingStatement = Identifier Equal Expression StatementEnd");
        builder.AppendLine("        var name = ExpectIdentifier();");
        builder.AppendLine("        Expect(TokenKind.Equal);");
        builder.AppendLine("        var expression = ParseExpression();");
        builder.AppendLine("        ExpectStatementEnd();");
        builder.AppendLine("        return new BindingStatement(name.Text, expression, name.Line, name.Column, IsMutable: false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Statement ParseExpressionStatement()");
        builder.AppendLine("    {");
        builder.AppendLine("        // ExpressionStatement = Expression StatementEnd");
        builder.AppendLine("        var expression = ParseExpression();");
        builder.AppendLine("        if (Match(TokenKind.FatArrow, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            var isMutable = false;");
        builder.AppendLine("            if (CheckIdentifier(\"mut\"))");
        builder.AppendLine("            {");
        builder.AppendLine("                ExpectIdentifier(\"mut\");");
        builder.AppendLine("                isMutable = true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var name = ExpectIdentifier();");
        builder.AppendLine("            ExpectStatementEnd();");
        builder.AppendLine("            return new BindingStatement(name.Text, expression, name.Line, name.Column, isMutable);");
        builder.AppendLine("        }");
        builder.AppendLine();
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
        builder.AppendLine("        // FlowExpression = RangeOrLogicalExpression (Arrow (Path FlowTargetCall? | IfFlowTarget | WhenFlowTarget | FoldFlowTarget))*");
        builder.AppendLine("        var expression = ParseRangeOrLogicalExpression();");
        builder.AppendLine("        List<FlowTarget>? targets = null;");
        builder.AppendLine("        while (Match(TokenKind.Arrow, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            var target = ExpectIdentifier();");
        builder.AppendLine("            if (target.Text == \"if\")");
        builder.AppendLine("            {");
        builder.AppendLine("                expression = BuildFlowExpression(expression, targets);");
        builder.AppendLine("                targets = null;");
        builder.AppendLine("                var thenBody = ParseBlockBody();");
        builder.AppendLine("                BlockBody? elseBody = null;");
        builder.AppendLine("                if (CheckIdentifier(\"else\"))");
        builder.AppendLine("                {");
        builder.AppendLine("                    ExpectIdentifier(\"else\");");
        builder.AppendLine("                    elseBody = ParseBlockBody();");
        builder.AppendLine("                }");
        builder.AppendLine();
                builder.AppendLine("                expression = new IfExpression(expression, thenBody, elseBody, target.Line, target.Column);");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            if (target.Text == \"when\")");
                builder.AppendLine("            {");
                builder.AppendLine("                expression = BuildFlowExpression(expression, targets);");
                builder.AppendLine("                targets = null;");
                builder.AppendLine("                expression = ParseSubjectWhenExpression(expression, target);");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            if (target.Text == \"fold\")");
                builder.AppendLine("            {");
                builder.AppendLine("                expression = BuildFlowExpression(expression, targets);");
                builder.AppendLine("                targets = null;");
                builder.AppendLine("                expression = ParseFoldExpression(expression, target);");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine();
                builder.AppendLine("            var path = ResolveImportedPath(ParsePathAfterFirstIdentifier(target));");
        builder.AppendLine("            var usesCallSyntax = false;");
        builder.AppendLine("            if (Match(TokenKind.LeftParen, out _))");
                builder.AppendLine("            {");
                builder.AppendLine("                usesCallSyntax = true;");
                builder.AppendLine("                var arguments = ParseOptionalArgumentList();");
                builder.AppendLine("                Expect(TokenKind.RightParen);");
                builder.AppendLine("                targets ??= [];");
                builder.AppendLine("                targets.Add(new FlowTarget(path, arguments, usesCallSyntax, target.Line, target.Column));");
                builder.AppendLine("                continue;");
                builder.AppendLine("            }");
                builder.AppendLine("            targets ??= [];");
                builder.AppendLine("            targets.Add(new FlowTarget(path, [], usesCallSyntax, target.Line, target.Column));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return BuildFlowExpression(expression, targets);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseRangeOrLogicalExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // RangeOrLogicalExpression = RangeExpression | LogicalOrExpression");
        builder.AppendLine("        var expression = ParseLogicalOrExpression();");
        builder.AppendLine("        if (!Match(TokenKind.Range, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            return expression;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var end = ParseLogicalOrExpression();");
        builder.AppendLine("        return new RangeExpression(expression, end, expression.Line, expression.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static Expression BuildFlowExpression(Expression expression, List<FlowTarget>? targets)");
        builder.AppendLine("    {");
        builder.AppendLine("        return targets is null");
        builder.AppendLine("            ? expression");
        builder.AppendLine("            : new FlowExpression(expression, targets, expression.Line, expression.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private FoldExpression ParseFoldExpression(Expression source, Token fold)");
        builder.AppendLine("    {");
        builder.AppendLine("        // FoldFlowTarget = Identifier(\"fold\") Expression Identifier Comma Identifier LeftBrace BlockBody RightBrace");
        builder.AppendLine("        var initial = ParseExpression();");
        builder.AppendLine("        var accumulator = ExpectIdentifier();");
        builder.AppendLine("        Expect(TokenKind.Comma);");
        builder.AppendLine("        var item = ExpectIdentifier();");
        builder.AppendLine("        var body = ParseBlockBody();");
        builder.AppendLine("        return new FoldExpression(source, initial, accumulator.Text, item.Text, body, fold.Line, fold.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private BlockBody ParseBlockBody()");
        builder.AppendLine("    {");
        builder.AppendLine("        // BlockBody = NewLine* Statement* Expression? NewLine*");
        builder.AppendLine("        var start = Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        var statements = new List<Statement>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (TryParseBlockValue(out var value))");
        builder.AppendLine("            {");
        builder.AppendLine("                Expect(TokenKind.RightBrace);");
        builder.AppendLine("                return new BlockBody(statements, value, start.Line, start.Column);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            statements.Add(ParseStatement());");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new BlockBody(statements, null, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool TryParseBlockValue(out Expression value)");
        builder.AppendLine("    {");
        builder.AppendLine("        var saved = _index;");
        builder.AppendLine("        value = ParseExpression();");
        builder.AppendLine("        if (value is FlowExpression)");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            value = null!;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        if (Check(TokenKind.RightBrace))");
        builder.AppendLine("        {");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        _index = saved;");
        builder.AppendLine("        value = null!;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseLogicalOrExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // LogicalOrExpression = LogicalAndExpression (Identifier(\"or\") LogicalAndExpression)*");
        builder.AppendLine("        var expression = ParseLogicalAndExpression();");
        builder.AppendLine("        while (CheckIdentifier(\"or\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            var op = ExpectIdentifier(\"or\");");
        builder.AppendLine("            var right = ParseLogicalAndExpression();");
        builder.AppendLine("            expression = new OrExpression(expression, right, op.Line, op.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseLogicalAndExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // LogicalAndExpression = EqualityExpression (Identifier(\"and\") EqualityExpression)*");
        builder.AppendLine("        var expression = ParseEqualityExpression();");
        builder.AppendLine("        while (CheckIdentifier(\"and\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            var op = ExpectIdentifier(\"and\");");
        builder.AppendLine("            var right = ParseEqualityExpression();");
        builder.AppendLine("            expression = new AndExpression(expression, right, op.Line, op.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseEqualityExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // EqualityExpression = ComparisonExpression ((EqualEqual | BangEqual) ComparisonExpression)*");
        builder.AppendLine("        var expression = ParseComparisonExpression();");
        builder.AppendLine("        while (Match(TokenKind.EqualEqual, out var equal) || Match(TokenKind.BangEqual, out equal))");
        builder.AppendLine("        {");
        builder.AppendLine("            var op = equal.Kind == TokenKind.EqualEqual ? ComparisonOperator.Equal : ComparisonOperator.NotEqual;");
        builder.AppendLine("            var right = ParseComparisonExpression();");
        builder.AppendLine("            expression = new CompareExpression(expression, op, right, equal.Line, equal.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseComparisonExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // ComparisonExpression = AdditiveExpression ((Less | LessEqual | Greater | GreaterEqual) AdditiveExpression)*");
        builder.AppendLine("        var expression = ParseAdditiveExpression();");
        builder.AppendLine("        while (TryParseComparisonOperator(out var op, out var token))");
        builder.AppendLine("        {");
        builder.AppendLine("            var right = ParseAdditiveExpression();");
        builder.AppendLine("            expression = new CompareExpression(expression, op, right, token.Line, token.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool TryParseComparisonOperator(out ComparisonOperator op, out Token token)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (Match(TokenKind.Less, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.Less;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.LessEqual, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.LessOrEqual;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.Greater, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.Greater;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.GreaterEqual, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.GreaterOrEqual;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        op = default;");
        builder.AppendLine("        token = default;");
        builder.AppendLine("        return false;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseAdditiveExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // AdditiveExpression = MultiplicativeExpression ((Plus | Minus) MultiplicativeExpression)*");
        builder.AppendLine("        var expression = ParseMultiplicativeExpression();");
        builder.AppendLine("        while (Match(TokenKind.Plus, out var op) || Match(TokenKind.Minus, out op))");
        builder.AppendLine("        {");
        builder.AppendLine("            var right = ParseMultiplicativeExpression();");
        builder.AppendLine("            expression = op.Kind == TokenKind.Plus");
        builder.AppendLine("                ? new AddExpression(expression, right, op.Line, op.Column)");
        builder.AppendLine("                : new SubtractExpression(expression, right, op.Line, op.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseMultiplicativeExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // MultiplicativeExpression = UnaryExpression ((Star | Slash | Percent) UnaryExpression)*");
        builder.AppendLine("        var expression = ParseUnaryExpression();");
        builder.AppendLine("        while (Match(TokenKind.Star, out var op) || Match(TokenKind.Slash, out op) || Match(TokenKind.Percent, out op))");
        builder.AppendLine("        {");
        builder.AppendLine("            var right = ParseUnaryExpression();");
        builder.AppendLine("            expression = op.Kind switch");
        builder.AppendLine("            {");
        builder.AppendLine("                TokenKind.Star => new MultiplyExpression(expression, right, op.Line, op.Column),");
        builder.AppendLine("                TokenKind.Slash => new DivideExpression(expression, right, op.Line, op.Column),");
        builder.AppendLine("                TokenKind.Percent => new ModuloExpression(expression, right, op.Line, op.Column),");
        builder.AppendLine("                _ => throw Error(op, \"expected multiplicative operator\")");
        builder.AppendLine("            };");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseUnaryExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // UnaryExpression = Identifier(\"not\") UnaryExpression | Minus UnaryExpression | PrimaryExpression");
        builder.AppendLine("        if (CheckIdentifier(\"not\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            var op = ExpectIdentifier(\"not\");");
        builder.AppendLine("            return new NotExpression(ParseUnaryExpression(), op.Line, op.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.Minus, out var minus))");
        builder.AppendLine("        {");
        builder.AppendLine("            return new NegateExpression(ParseUnaryExpression(), minus.Line, minus.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return ParsePrimaryExpression();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParsePrimaryExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // PrimaryExpression = PostfixExpression");
        builder.AppendLine("        var expression = ParseAtomExpression();");
        builder.AppendLine("        while (Match(TokenKind.LeftBracket, out var bracket))");
        builder.AppendLine("        {");
        builder.AppendLine("            var index = ParseExpression();");
        builder.AppendLine("            Expect(TokenKind.RightBracket);");
        builder.AppendLine("            expression = new IndexExpression(expression, index, bracket.Line, bracket.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return expression;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseAtomExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // AtomExpression = WhenExpression | CallExpression | ArrayExpression | DictionaryExpression | StringExpression | NumberExpression | NameExpression | LeftParen Expression RightParen");
        builder.AppendLine("        if (CheckIdentifier(\"when\"))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseWhenExpression();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.LeftParen, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            var expression = ParseExpression();");
        builder.AppendLine("            Expect(TokenKind.RightParen);");
            builder.AppendLine("            return expression;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Check(TokenKind.LeftBracket))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseArrayExpression();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Check(TokenKind.LeftBrace))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseDictionaryExpression();");
        builder.AppendLine("        }");
        builder.AppendLine();
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
        builder.AppendLine("            if (identifier.Text == \"true\" || identifier.Text == \"false\")");
        builder.AppendLine("            {");
        builder.AppendLine("                return new BoolExpression(identifier.Text == \"true\", identifier.Line, identifier.Column);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return ParsePathStartingWith(identifier);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw Error(Peek(), \"expected expression\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseArrayExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // ArrayExpression = '[' (Range | ArrayItems) ']'");
        builder.AppendLine("        var start = Expect(TokenKind.LeftBracket);");
        builder.AppendLine("        if (Match(TokenKind.Range, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            Expect(TokenKind.RightBracket);");
        builder.AppendLine("            return new ArrayLiteralExpression([], IsDynamic: true, start.Line, start.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Check(TokenKind.RightBracket))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(Peek(), \"empty array literals require '..' for a dynamic array\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var first = ParseExpression();");
        builder.AppendLine("        if (Match(TokenKind.Semicolon, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            var count = Expect(TokenKind.Number);");
        builder.AppendLine("            Expect(TokenKind.RightBracket);");
        builder.AppendLine("            if (!int.TryParse(count.Text, out var parsedCount) || parsedCount < 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw Error(count, \"array repeat count must be a non-negative integer literal\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return new ArrayRepeatExpression(first, parsedCount, start.Line, start.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var elements = new List<Expression> { first };");
        builder.AppendLine("        var isDynamic = false;");
        builder.AppendLine("        while (Match(TokenKind.Comma, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (Match(TokenKind.Range, out _))");
        builder.AppendLine("            {");
        builder.AppendLine("                isDynamic = true;");
        builder.AppendLine("                break;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (Check(TokenKind.RightBracket))");
        builder.AppendLine("            {");
        builder.AppendLine("                break;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            elements.Add(ParseExpression());");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightBracket);");
        builder.AppendLine("        return new ArrayLiteralExpression(elements, isDynamic, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private DictionaryLiteralExpression ParseDictionaryExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // DictionaryExpression = '{' DictionaryEntry (',' DictionaryEntry)* ','? '}'");
        builder.AppendLine("        var start = Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        if (Check(TokenKind.RightBrace))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(Peek(), \"empty dictionary literals are reserved until typed empty dictionaries exist\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var entries = new List<DictionaryEntryExpression>();");
        builder.AppendLine("        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            var key = ParseExpression();");
        builder.AppendLine("            Expect(TokenKind.Colon);");
        builder.AppendLine("            var value = ParseExpression();");
        builder.AppendLine("            entries.Add(new DictionaryEntryExpression(key, value));");
        builder.AppendLine("            if (!Match(TokenKind.Comma, out _))");
        builder.AppendLine("            {");
        builder.AppendLine("                break;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (Check(TokenKind.RightBrace))");
        builder.AppendLine("            {");
        builder.AppendLine("                break;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new DictionaryLiteralExpression(entries, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private WhenExpression ParseWhenExpression()");
        builder.AppendLine("    {");
        builder.AppendLine("        // WhenExpression = Identifier(\"when\") LeftBrace NewLine* WhenArm+ Identifier(\"else\") WhenArmBody NewLine* RightBrace");
        builder.AppendLine("        var when = ExpectIdentifier(\"when\");");
        builder.AppendLine("        Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        var arms = new List<WhenArm>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!CheckIdentifier(\"else\") && !Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            var condition = ParseWhenCondition();");
        builder.AppendLine("            var body = ParseWhenArmBody();");
        builder.AppendLine("            arms.Add(new WhenArm(condition, body, condition.Line, condition.Column));");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (arms.Count == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(when, \"when requires at least one condition arm\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        ExpectIdentifier(\"else\");");
        builder.AppendLine("        var elseBody = ParseWhenArmBody();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new WhenExpression(null, arms, elseBody, when.Line, when.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private WhenExpression ParseSubjectWhenExpression(Expression subject, Token when)");
        builder.AppendLine("    {");
        builder.AppendLine("        // WhenFlowTarget = Identifier(\"when\") LeftBrace NewLine* SubjectWhenArm+ Identifier(\"else\") WhenArmBody NewLine* RightBrace");
        builder.AppendLine("        Expect(TokenKind.LeftBrace);");
        builder.AppendLine("        var arms = new List<WhenArm>();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine();
        builder.AppendLine("        while (!CheckIdentifier(\"else\") && !Check(TokenKind.RightBrace) && !Check(TokenKind.End))");
        builder.AppendLine("        {");
        builder.AppendLine("            var condition = ParseSubjectWhenCondition();");
        builder.AppendLine("            var body = ParseWhenArmBody();");
        builder.AppendLine("            arms.Add(new WhenArm(condition, body, condition.Line, condition.Column));");
        builder.AppendLine("            SkipNewLines();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (arms.Count == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw Error(when, \"when requires at least one condition arm\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        ExpectIdentifier(\"else\");");
        builder.AppendLine("        var elseBody = ParseWhenArmBody();");
        builder.AppendLine("        SkipNewLines();");
        builder.AppendLine("        Expect(TokenKind.RightBrace);");
        builder.AppendLine("        return new WhenExpression(subject, arms, elseBody, when.Line, when.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseWhenCondition()");
        builder.AppendLine("    {");
        builder.AppendLine("        // WhenCondition = SubjectWhenCondition | LogicalOrExpression");
        builder.AppendLine("        var saved = _index;");
        builder.AppendLine("        if (TryParseSubjectWhenCondition(out var subjectCondition))");
        builder.AppendLine("        {");
        builder.AppendLine("            return subjectCondition;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        _index = saved;");
        builder.AppendLine("        return ParseLogicalOrExpression();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private BlockBody ParseWhenArmBody()");
        builder.AppendLine("    {");
        builder.AppendLine("        // WhenArmBody = FatArrow Expression | Arrow Expression | LeftBrace BlockBody RightBrace");
        builder.AppendLine("        if (Match(TokenKind.FatArrow, out var arrow) || Match(TokenKind.Arrow, out arrow))");
        builder.AppendLine("        {");
        builder.AppendLine("            return new BlockBody([], ParseExpression(), arrow.Line, arrow.Column);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return ParseBlockBody();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParseSubjectWhenCondition()");
        builder.AppendLine("    {");
        builder.AppendLine("        // SubjectWhenCondition = (EqualEqual | BangEqual | Less | LessEqual | Greater | GreaterEqual) Expression | RangeExpression");
        builder.AppendLine("        if (TryParseSubjectWhenCondition(out var condition))");
        builder.AppendLine("        {");
        builder.AppendLine("            return condition;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        throw Error(Peek(), \"expected subject comparison operator or range in value-flow when arm\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool TryParseSubjectWhenCondition(out Expression condition)");
        builder.AppendLine("    {");
        builder.AppendLine("        var saved = _index;");
        builder.AppendLine("        if (TryParseSubjectWhenOperator(out var op, out var token))");
        builder.AppendLine("        {");
        builder.AppendLine("            condition = new SubjectCompareExpression(op, ParseLogicalOrExpression(), token.Line, token.Column);");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var start = ParseLogicalOrExpression();");
        builder.AppendLine("        if (!Match(TokenKind.Range, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            _index = saved;");
        builder.AppendLine("            condition = null!;");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var end = ParseLogicalOrExpression();");
        builder.AppendLine("        condition = new SubjectRangeExpression(start, end, start.Line, start.Column);");
        builder.AppendLine("        return true;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool TryParseSubjectWhenOperator(out ComparisonOperator op, out Token token)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (Match(TokenKind.EqualEqual, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.Equal;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (Match(TokenKind.BangEqual, out token))");
        builder.AppendLine("        {");
        builder.AppendLine("            op = ComparisonOperator.NotEqual;");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return TryParseComparisonOperator(out op, out token);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private Expression ParsePathStartingWith(Token identifier)");
        builder.AppendLine("    {");
        builder.AppendLine("        // Path = Identifier (Dot Identifier)*");
        builder.AppendLine("        var path = ParsePathAfterFirstIdentifier(identifier);");
        builder.AppendLine("        if (Match(TokenKind.LeftParen, out _))");
        builder.AppendLine("        {");
        builder.AppendLine("            return ParseCallExpression(identifier, ResolveImportedPath(path));");
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
        builder.AppendLine("    private string QualifyFunctionName(IReadOnlyList<string> path, bool isLocal)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (isLocal || path.Count != 1 || _namespacePath.Count == 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            return string.Join('.', path);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var qualified = new List<string>(_namespacePath.Count + 1);");
        builder.AppendLine("        foreach (var segment in _namespacePath)");
        builder.AppendLine("        {");
        builder.AppendLine("            qualified.Add(segment);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        qualified.Add(path[0]);");
        builder.AppendLine("        return string.Join('.', qualified);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private IReadOnlyList<string> ResolveImportedPath(IReadOnlyList<string> path)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (path.Count == 0 || !_imports.TryGetValue(path[0], out var importedPath))");
        builder.AppendLine("        {");
        builder.AppendLine("            return path;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var resolved = new List<string>(importedPath.Count + path.Count - 1);");
        builder.AppendLine("        foreach (var segment in importedPath)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolved.Add(segment);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        for (var i = 1; i < path.Count; i++)");
        builder.AppendLine("        {");
        builder.AppendLine("            resolved.Add(path[i]);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return resolved;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private CallExpression ParseCallExpression(Token start, IReadOnlyList<string> path)");
        builder.AppendLine("    {");
        builder.AppendLine("        // CallExpression = Path LeftParen ArgumentList? RightParen");
        builder.AppendLine("        var arguments = ParseOptionalArgumentList();");
        builder.AppendLine("        Expect(TokenKind.RightParen);");
        builder.AppendLine("        return new CallExpression(path, arguments, start.Line, start.Column);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private IReadOnlyList<Expression> ParseOptionalArgumentList()");
        builder.AppendLine("    {");
        builder.AppendLine("        var arguments = new List<Expression>();");
        builder.AppendLine("        if (!Check(TokenKind.RightParen))");
        builder.AppendLine("        {");
        builder.AppendLine("            ParseArgumentList(arguments);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return arguments;");
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
        builder.AppendLine("    private bool IsFunctionDeclarationStart()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!Check(TokenKind.Identifier))");
        builder.AppendLine("        {");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var offset = 1;");
        builder.AppendLine("        while (CheckAhead(offset, TokenKind.Dot))");
        builder.AppendLine("        {");
        builder.AppendLine("            offset++;");
        builder.AppendLine("            if (!CheckAhead(offset, TokenKind.Identifier))");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            offset++;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return CheckAhead(offset, TokenKind.Colon)");
        builder.AppendLine("            || (CheckAhead(offset, TokenKind.Identifier) && CheckAhead(offset + 1, TokenKind.Colon));");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool CheckNext(TokenKind kind)");
        builder.AppendLine("    {");
        builder.AppendLine("        return _index + 1 < tokens.Count && tokens[_index + 1].Kind == kind;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private bool CheckAhead(int offset, TokenKind kind)");
        builder.AppendLine("    {");
        builder.AppendLine("        return _index + offset < tokens.Count && tokens[_index + offset].Kind == kind;");
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
