using System.Globalization;
using System.Text;
using SLang.Compiler.Diagnostics;
using SLang.Compiler.Syntax;

namespace SLang.Compiler.Semantics;

internal sealed class SemanticCompiler(SlangProgram program)
{
    public byte[] CompileToStdoutBytes()
    {
        var bindings = new Dictionary<string, SemanticValue>(StringComparer.Ordinal);
        var output = new StringBuilder();

        foreach (var statement in program.Statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    if (bindings.ContainsKey(binding.Name))
                    {
                        throw Error(binding.Line, binding.Column, $"binding '{binding.Name}' already exists in this scope");
                    }

                    bindings.Add(binding.Name, EvaluateValue(binding.Value, bindings));
                    break;
                case ExpressionStatement expressionStatement:
                    CompileExpressionStatement(expressionStatement.Expression, bindings, output);
                    break;
                default:
                    throw new SlangException($"unsupported statement {statement.GetType().Name}");
            }
        }

        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static void CompileExpressionStatement(
        Expression expression,
        IReadOnlyDictionary<string, SemanticValue> bindings,
        StringBuilder output)
    {
        if (expression is not CallExpression call)
        {
            throw Error(expression.Line, expression.Column, "only function calls are valid expression statements");
        }

        var path = string.Join('.', call.Path);
        if (path != "print")
        {
            throw Error(call.Line, call.Column, $"unknown function '{path}'");
        }

        if (call.Arguments.Count != 1)
        {
            throw Error(call.Line, call.Column, "print expects exactly one argument");
        }

        output.Append(EvaluateValue(call.Arguments[0], bindings).ToDisplayString());
    }

    private static SemanticValue EvaluateValue(
        Expression expression,
        IReadOnlyDictionary<string, SemanticValue> bindings)
    {
        return expression switch
        {
            StringExpression str => new TextValue(EvaluateStringLiteral(str, bindings)),
            NumberExpression number => EvaluateNumber(number),
            NameExpression name => ResolveName(name.Name, bindings, name.Line, name.Column),
            AddExpression add => EvaluateAdd(add, bindings),
            _ => throw Error(expression.Line, expression.Column, "expected an expression value")
        };
    }

    private static IntegerValue EvaluateNumber(NumberExpression expression)
    {
        return long.TryParse(
            expression.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? new IntegerValue(value)
            : throw Error(expression.Line, expression.Column, $"integer literal '{expression.Text}' is out of range");
    }

    private static IntegerValue EvaluateAdd(
        AddExpression expression,
        IReadOnlyDictionary<string, SemanticValue> bindings)
    {
        var left = EvaluateValue(expression.Left, bindings);
        var right = EvaluateValue(expression.Right, bindings);
        if (left is not IntegerValue leftInteger)
        {
            throw Error(expression.Left.Line, expression.Left.Column, "left operand of '+' must be an integer");
        }

        if (right is not IntegerValue rightInteger)
        {
            throw Error(expression.Right.Line, expression.Right.Column, "right operand of '+' must be an integer");
        }

        try
        {
            return new IntegerValue(checked(leftInteger.Value + rightInteger.Value));
        }
        catch (OverflowException)
        {
            throw Error(expression.Line, expression.Column, "integer addition overflow");
        }
    }

    private static string EvaluateStringLiteral(
        StringExpression expression,
        IReadOnlyDictionary<string, SemanticValue> bindings)
    {
        var result = new StringBuilder();
        foreach (var segment in expression.Segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    result.Append(text.Text);
                    break;
                case InterpolationSegment interpolation:
                    if (interpolation.Path.Count != 1)
                    {
                        throw Error(expression.Line, expression.Column, "path interpolation is reserved until modules are specified");
                    }

                    result.Append(ResolveName(
                        interpolation.Path[0],
                        bindings,
                        expression.Line,
                        expression.Column).ToDisplayString());
                    break;
                default:
                    throw new SlangException($"unsupported string segment {segment.GetType().Name}");
            }
        }

        return result.ToString();
    }

    private static SemanticValue ResolveName(
        string name,
        IReadOnlyDictionary<string, SemanticValue> bindings,
        int line,
        int column)
    {
        return bindings.TryGetValue(name, out var value)
            ? value
            : throw Error(line, column, $"unknown binding '{name}'");
    }

    private static SlangException Error(int line, int column, string message)
    {
        return new SlangException($"semantic error at {line}:{column}: {message}");
    }
}
