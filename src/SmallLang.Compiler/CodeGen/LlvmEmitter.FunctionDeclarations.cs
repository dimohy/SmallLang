using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitUserFunctions()
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in _program.Functions.Values)
        {
            if (function.Kind != BoundFunctionKind.User
                || function.IsStandardLibrary
                || function.IsLocal
                || !emitted.Add(function.Name))
            {
                continue;
            }

            switch (function.ReturnType)
            {
                case BoundType.Text:
                    EmitTextFunction(function);
                    break;
                case BoundType.Int:
                    EmitIntFunction(function);
                    break;
                case BoundType.Bool:
                    EmitBoolFunction(function);
                    break;
                default:
                    throw new SmallLangException($"unsupported function return type {function.ReturnType}");
            }
        }
    }

    private void EmitTextFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        _locals.Clear();
        try
        {
            EmitFunctionLine($"define internal %smalllang.text {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            BindFunctionParameter(function);

            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.Text, function.Name);
            var text = (RuntimeText)value;
            var aggregate0 = NextTemp("text_ret");
            EmitAssign(aggregate0, $"insertvalue %smalllang.text poison, ptr {text.PointerName}, 0");
            var aggregate1 = NextTemp("text_ret");
            EmitAssign(aggregate1, $"insertvalue %smalllang.text {aggregate0}, i64 {text.LengthName}, 1");
            EmitRet("%smalllang.text", aggregate1);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitIntFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        _locals.Clear();
        try
        {
            EmitFunctionLine($"define internal i64 {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            BindFunctionParameter(function);

            var value = EmitIntExpression(function.Body);
            EmitRet("i64", value.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitBoolFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        _locals.Clear();
        try
        {
            EmitFunctionLine($"define internal i1 {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            BindFunctionParameter(function);

            var value = EmitBoolExpression(function.Body);
            EmitRet("i1", value.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private static string ParameterListForFunction(BoundFunction function)
    {
        return function.InputType switch
        {
            null => "",
            BoundType.Int => "i64 %it",
            BoundType.Bool => "i1 %it",
            _ => throw new SmallLangException("only Int and Bool function input is supported in the current runtime slice")
        };
    }

    private void BindFunctionParameter(BoundFunction function)
    {
        switch (function.InputType)
        {
            case null:
                return;
            case BoundType.Int:
                _locals.Add(function.InputName ?? "it", new RuntimeInt("%it"));
                return;
            case BoundType.Bool:
                _locals.Add(function.InputName ?? "it", new RuntimeBool("%it"));
                return;
            default:
                throw new SmallLangException("only Int and Bool function input is supported in the current runtime slice");
        }
    }

}

