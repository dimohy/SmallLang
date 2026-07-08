using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed class SemanticCompiler(SmallLangProgram program)
{
    public BoundProgram Compile()
    {
        var functions = BindFunctions();
        var mainBindings = BindMain(functions);
        return new BoundProgram(functions, program.Statements, mainBindings);
    }

    private IReadOnlyDictionary<string, BoundFunction> BindFunctions()
    {
        var functions = new Dictionary<string, BoundFunction>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (functions.ContainsKey(function.Name))
            {
                throw Error(function.Line, function.Column, $"function '{function.Name}' already exists");
            }

            if (IsReservedName(function.Name))
            {
                throw Error(function.Line, function.Column, $"function name '{function.Name}' is reserved");
            }

            var inputType = function.InputType is null
                ? (BoundType?)null
                : ParseType(function.InputType, function.Line, function.Column);
            var returnType = ParseType(function.ReturnType, function.Line, function.Column);

            functions.Add(function.Name, new BoundFunction(
                function.Name,
                inputType,
                returnType,
                function.Body,
                function.Line,
                function.Column));
        }

        foreach (var function in functions.Values)
        {
            var bodyBindings = new Dictionary<string, BoundType>(StringComparer.Ordinal);
            if (function.InputType is { } inputType)
            {
                bodyBindings.Add("it", inputType);
            }

            var bodyType = InferExpression(
                function.Body,
                functions,
                bodyBindings,
                allowPrintCall: false,
                allowReadIntCall: false,
                allowFlowBindingTarget: false);
            if (bodyType != function.ReturnType)
            {
                throw Error(
                    function.Line,
                    function.Column,
                    $"function '{function.Name}' returns {FormatType(bodyType)} but declares {FormatType(function.ReturnType)}");
            }

            if (function.ReturnType == BoundType.Text && !IsPlainStringLiteral(function.Body))
            {
                throw Error(
                    function.Line,
                    function.Column,
                    "the first Text function body must be a plain string literal");
            }
        }

        return functions;
    }

    private IReadOnlyDictionary<string, BoundType> BindMain(IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var bindings = new Dictionary<string, BoundType>(StringComparer.Ordinal);
        BindStatements(program.Statements, functions, bindings);
        return bindings;
    }

    private static void BindStatements(
        IReadOnlyList<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> functions,
        Dictionary<string, BoundType> bindings)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    ValidateBindingName(binding.Name, binding.Line, binding.Column);
                    if (bindings.ContainsKey(binding.Name))
                    {
                        throw Error(binding.Line, binding.Column, $"binding '{binding.Name}' already exists in this scope");
                    }

                    var valueType = InferExpression(
                        binding.Value,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall: true,
                        allowFlowBindingTarget: false);
                    if (valueType == BoundType.Unit)
                    {
                        throw Error(binding.Line, binding.Column, "cannot bind a unit value");
                    }

                    bindings.Add(binding.Name, valueType);
                    break;
                case EachStatement each:
                    ValidateBindingName(each.ItemName, each.Line, each.Column);
                    if (bindings.ContainsKey(each.ItemName))
                    {
                        throw Error(each.Line, each.Column, $"binding '{each.ItemName}' already exists in this scope");
                    }

                    var startType = InferExpression(
                        each.Start,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall: true,
                        allowFlowBindingTarget: false);
                    if (startType != BoundType.Int)
                    {
                        throw Error(each.Start.Line, each.Start.Column, "range start must be an integer");
                    }

                    var endType = InferExpression(
                        each.End,
                        functions,
                        bindings,
                        allowPrintCall: false,
                        allowReadIntCall: true,
                        allowFlowBindingTarget: false);
                    if (endType != BoundType.Int)
                    {
                        throw Error(each.End.Line, each.End.Column, "range end must be an integer");
                    }

                    var bodyBindings = new Dictionary<string, BoundType>(bindings, StringComparer.Ordinal)
                    {
                        [each.ItemName] = BoundType.Int
                    };
                    BindStatements(each.Body, functions, bodyBindings);
                    break;
                case ExpressionStatement expressionStatement:
                    var effect = InferExpressionStatement(expressionStatement.Expression, functions, bindings);
                    if (effect is FlowBindingEffect bindingEffect)
                    {
                        ValidateBindingName(
                            bindingEffect.Name,
                            expressionStatement.Expression.Line,
                            expressionStatement.Expression.Column);
                        if (bindings.ContainsKey(bindingEffect.Name))
                        {
                            throw Error(
                                expressionStatement.Expression.Line,
                                expressionStatement.Expression.Column,
                                $"binding '{bindingEffect.Name}' already exists in this scope");
                        }

                        bindings.Add(bindingEffect.Name, bindingEffect.Type);
                    }

                    break;
                default:
                    throw new SmallLangException($"unsupported statement {statement.GetType().Name}");
            }
        }
    }

    private static FlowEffect InferExpressionStatement(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        if (expression is FlowExpression flow)
        {
            var result = InferFlowExpression(
                flow,
                functions,
                bindings,
                allowReadIntCall: true,
                allowFlowBindingTarget: true);
            if (result.Type != BoundType.Unit && result.Effect is NoFlowEffect)
            {
                throw Error(
                    expression.Line,
                    expression.Column,
                    "value-flow expression statements must end in print or bind their result");
            }

            return result.Effect;
        }

        var expressionType = InferExpression(
            expression,
            functions,
            bindings,
            allowPrintCall: true,
            allowReadIntCall: true,
            allowFlowBindingTarget: false);
        if (expressionType != BoundType.Unit)
        {
            throw Error(
                expression.Line,
                expression.Column,
                "only function calls with side effects are valid expression statements");
        }

        return FlowEffect.None;
    }

    private static BoundType InferExpression(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowPrintCall,
        bool allowReadIntCall,
        bool allowFlowBindingTarget)
    {
        return expression switch
        {
            StringExpression str => InferStringExpression(str, bindings),
            NumberExpression => BoundType.Int,
            NameExpression name => ResolveBindingType(name.Name, bindings, name.Line, name.Column),
            AddExpression add => InferAddExpression(add, functions, bindings, allowReadIntCall),
            MultiplyExpression multiply => InferMultiplyExpression(multiply, functions, bindings, allowReadIntCall),
            CallExpression call => InferCallExpression(call, functions, bindings, allowPrintCall, allowReadIntCall),
            FlowExpression flow => InferFlowExpression(
                flow,
                functions,
                bindings,
                allowReadIntCall,
                allowFlowBindingTarget).Type,
            _ => throw Error(expression.Line, expression.Column, "expected an expression value")
        };
    }

    private static BoundType InferStringExpression(
        StringExpression expression,
        IReadOnlyDictionary<string, BoundType> bindings)
    {
        foreach (var segment in expression.Segments)
        {
            if (segment is not InterpolationSegment interpolation)
            {
                continue;
            }

            if (interpolation.Path.Count != 1)
            {
                throw Error(expression.Line, expression.Column, "path interpolation is reserved until modules are specified");
            }

            _ = ResolveBindingType(interpolation.Path[0], bindings, expression.Line, expression.Column);
        }

        return BoundType.Text;
    }

    private static BoundType InferAddExpression(
        AddExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "+");
    }

    private static BoundType InferMultiplyExpression(
        MultiplyExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        return InferIntegerBinaryExpression(expression.Left, expression.Right, functions, bindings, allowReadIntCall, "*");
    }

    private static BoundType InferIntegerBinaryExpression(
        Expression leftExpression,
        Expression rightExpression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        string operatorText)
    {
        var left = InferExpression(
            leftExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        var right = InferExpression(
            rightExpression,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (left != BoundType.Int)
        {
            throw Error(leftExpression.Line, leftExpression.Column, $"left operand of '{operatorText}' must be an integer");
        }

        if (right != BoundType.Int)
        {
            throw Error(rightExpression.Line, rightExpression.Column, $"right operand of '{operatorText}' must be an integer");
        }

        return BoundType.Int;
    }

    private static FlowResult InferFlowExpression(
        FlowExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall,
        bool allowFlowBindingTarget)
    {
        var currentType = InferFlowSource(expression.Source, functions, bindings, allowReadIntCall);
        for (var i = 0; i < expression.Targets.Count; i++)
        {
            var target = expression.Targets[i];
            var isLast = i == expression.Targets.Count - 1;
            var path = string.Join('.', target);

            if (path is "print" or "println")
            {
                if (!isLast)
                {
                    throw Error(expression.Line, expression.Column, $"{path} must be the final value-flow target");
                }

                return new FlowResult(BoundType.Unit, FlowEffect.None);
            }

            if (path == "readInt")
            {
                if (!allowReadIntCall)
                {
                    throw Error(expression.Line, expression.Column, "readInt is only valid in main for the current runtime slice");
                }

                if (currentType != BoundType.Text)
                {
                    throw Error(
                        expression.Line,
                        expression.Column,
                        $"readInt expects Text but received {FormatType(currentType)}");
                }

                currentType = BoundType.Int;
                continue;
            }

            if (functions.TryGetValue(path, out var function))
            {
                if (function.InputType is null)
                {
                    throw Error(expression.Line, expression.Column, $"function '{path}' does not accept a flowed input");
                }

                if (currentType != function.InputType)
                {
                    throw Error(
                        expression.Line,
                        expression.Column,
                        $"function '{path}' expects {FormatType(function.InputType.Value)} but received {FormatType(currentType)}");
                }

                currentType = function.ReturnType;
                continue;
            }

            if (allowFlowBindingTarget && isLast && target.Count == 1)
            {
                return new FlowResult(BoundType.Unit, new FlowBindingEffect(target[0], currentType));
            }

            throw Error(expression.Line, expression.Column, $"unknown value-flow target '{path}'");
        }

        return new FlowResult(currentType, FlowEffect.None);
    }

    private static BoundType InferFlowSource(
        Expression source,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowReadIntCall)
    {
        if (source is NameExpression name && !bindings.ContainsKey(name.Name))
        {
            if (functions.TryGetValue(name.Name, out var function) && function.InputType is null)
            {
                return function.ReturnType;
            }
        }

        return InferExpression(
            source,
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
    }

    private static BoundType InferCallExpression(
        CallExpression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<string, BoundType> bindings,
        bool allowPrintCall,
        bool allowReadIntCall)
    {
        var path = string.Join('.', expression.Path);
        if (path is "print" or "println")
        {
            if (!allowPrintCall)
            {
                throw Error(expression.Line, expression.Column, $"{path} is only valid as an expression statement");
            }

            if (expression.Arguments.Count != 1)
            {
                throw Error(expression.Line, expression.Column, $"{path} expects exactly one argument");
            }

            _ = InferExpression(
                expression.Arguments[0],
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            return BoundType.Unit;
        }

        if (path == "readInt")
        {
            if (!allowReadIntCall)
            {
                throw Error(expression.Line, expression.Column, "readInt is only valid in main for the current runtime slice");
            }

            if (expression.Arguments.Count != 1)
            {
                throw Error(expression.Line, expression.Column, "readInt expects exactly one Text prompt");
            }

            var promptType = InferExpression(
                expression.Arguments[0],
                functions,
                bindings,
                allowPrintCall: false,
                allowReadIntCall,
                allowFlowBindingTarget: false);
            if (promptType != BoundType.Text)
            {
                throw Error(
                    expression.Arguments[0].Line,
                    expression.Arguments[0].Column,
                    $"readInt expects Text but received {FormatType(promptType)}");
            }

            return BoundType.Int;
        }

        if (!functions.TryGetValue(path, out var function))
        {
            throw Error(expression.Line, expression.Column, $"unknown function '{path}'");
        }

        if (function.InputType is null)
        {
            if (expression.Arguments.Count != 0)
            {
                throw Error(expression.Line, expression.Column, $"function '{path}' does not accept arguments");
            }

            return function.ReturnType;
        }

        if (expression.Arguments.Count != 1)
        {
            throw Error(expression.Line, expression.Column, $"function '{path}' expects exactly one argument");
        }

        var argumentType = InferExpression(
            expression.Arguments[0],
            functions,
            bindings,
            allowPrintCall: false,
            allowReadIntCall,
            allowFlowBindingTarget: false);
        if (argumentType != function.InputType)
        {
            throw Error(
                expression.Line,
                expression.Column,
                $"function '{path}' expects {FormatType(function.InputType.Value)} but received {FormatType(argumentType)}");
        }

        return function.ReturnType;
    }

    private static BoundType ResolveBindingType(
        string name,
        IReadOnlyDictionary<string, BoundType> bindings,
        int line,
        int column)
    {
        return bindings.TryGetValue(name, out var type)
            ? type
            : throw Error(line, column, $"unknown binding '{name}'");
    }

    private static void ValidateBindingName(string name, int line, int column)
    {
        if (IsReservedName(name))
        {
            throw Error(line, column, $"binding name '{name}' is reserved");
        }
    }

    private static bool IsReservedName(string name)
    {
        return name is "main" or "print" or "println" or "readInt" or "each" or "in" or "it";
    }

    private static BoundType ParseType(string typeName, int line, int column)
    {
        return typeName switch
        {
            "Text" => BoundType.Text,
            "Int" => BoundType.Int,
            _ => throw Error(line, column, $"unknown type '{typeName}'")
        };
    }

    private static string FormatType(BoundType type)
    {
        return type switch
        {
            BoundType.Unit => "Unit",
            BoundType.Text => "Text",
            BoundType.Int => "Int",
            _ => type.ToString()
        };
    }

    private static bool IsPlainStringLiteral(Expression expression)
    {
        return expression is StringExpression str
            && str.Segments.All(static segment => segment is TextSegment);
    }

    private static SmallLangException Error(int line, int column, string message)
    {
        return new SmallLangException($"semantic error at {line}:{column}: {message}");
    }

    private sealed record FlowResult(BoundType Type, FlowEffect Effect);

    private abstract record FlowEffect
    {
        public static FlowEffect None { get; } = new NoFlowEffect();
    }

    private sealed record NoFlowEffect : FlowEffect;

    private sealed record FlowBindingEffect(string Name, BoundType Type) : FlowEffect;
}
