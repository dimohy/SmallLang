using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeValue EmitFunctionCall(CallExpression expression)
    {
        var path = string.Join('.', expression.Path);
        if (!TryResolveFunction(expression.Path, out var function))
        {
            throw new SmallLangException($"unknown runtime function '{path}'");
        }

        if (TryGetRuntimeWrapperKind(function, out var wrapperKind))
        {
            return EmitRuntimeWrapperCall(expression, wrapperKind, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            _mainOk = EmitPrintArgument(expression.Arguments[0], _mainOk);
            if (function.Kind == BoundFunctionKind.RuntimePrintLine)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
            }

            return RuntimeUnit.Instance;
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadInt)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one Text prompt");
            }

            var prompt = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(prompt, BoundType.Text, path);
            return EmitReadIntPrompt(prompt);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"{path} does not accept arguments");
            }

            return EmitRuntimeNowMillisIntrinsic(path);
        }

        if (function.Kind is BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeOpenIntReader)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            var runtimeArgument = EmitExpression(expression.Arguments[0]);
            EmitRuntimeUnitIntrinsic(function, runtimeArgument, path);
            return RuntimeUnit.Instance;
        }

        if (function.Kind is BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeClosestInt)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            var runtimeArgument = EmitExpression(expression.Arguments[0]);
            return EmitRuntimeIntIntrinsic(function, runtimeArgument, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimeCloseIntWriter
            or BoundFunctionKind.RuntimeCloseIntReader)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"{path} does not accept arguments");
            }

            EmitRuntimeUnitIntrinsic(function, argument: null, path);
            return RuntimeUnit.Instance;
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SmallLangException($"unsupported runtime function kind '{function.Kind}'");
        }

        RuntimeValue? argument = null;
        if (function.InputType is null)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"function '{path}' does not accept arguments");
            }
        }
        else
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"function '{path}' expects exactly one argument");
            }

            argument = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(argument, function.InputType.Value, path);
        }

        return EmitFunctionCall(function, argument);
    }

    private RuntimeValue EmitRuntimeWrapperCall(
        CallExpression expression,
        BoundFunctionKind wrapperKind,
        string path)
    {
        if (expression.Arguments.Count != 1)
        {
            throw new SmallLangException($"{path} expects exactly one argument");
        }

        return wrapperKind switch
        {
            BoundFunctionKind.RuntimePrint => EmitRuntimePrintCall(expression.Arguments[0], appendNewLine: false),
            BoundFunctionKind.RuntimePrintLine => EmitRuntimePrintCall(expression.Arguments[0], appendNewLine: true),
            BoundFunctionKind.RuntimeReadInt => EmitReadIntPromptExpression(expression.Arguments[0]),
            BoundFunctionKind.RuntimeSeedRandom
                or BoundFunctionKind.RuntimeOpenIntWriter
                or BoundFunctionKind.RuntimeWriteInt
                or BoundFunctionKind.RuntimeOpenIntReader
                => EmitRuntimeUnitWrapperCall(expression.Arguments[0], wrapperKind, path),
            BoundFunctionKind.RuntimeRandomBelow
                or BoundFunctionKind.RuntimeClosestInt
                => EmitRuntimeIntWrapperCall(expression.Arguments[0], wrapperKind, path),
            _ => throw new SmallLangException($"unsupported runtime wrapper kind '{wrapperKind}'")
        };
    }

    private RuntimeUnit EmitRuntimeUnitWrapperCall(Expression argument, BoundFunctionKind kind, string path)
    {
        var value = EmitExpression(argument);
        EmitRuntimeUnitIntrinsic(kind, value, path);
        return RuntimeUnit.Instance;
    }

    private RuntimeInt EmitRuntimeIntWrapperCall(Expression argument, BoundFunctionKind kind, string path)
    {
        var value = EmitExpression(argument);
        return EmitRuntimeIntIntrinsic(kind, value, path);
    }

    private RuntimeUnit EmitRuntimePrintCall(Expression argument, bool appendNewLine)
    {
        _mainOk = EmitPrintArgument(argument, _mainOk);
        if (appendNewLine)
        {
            _mainOk = EmitWriteText("\n", _mainOk);
        }

        return RuntimeUnit.Instance;
    }

    private RuntimeValue EmitFlowFunctionCall(BoundFunction function, RuntimeValue argument)
    {
        if (function.InputType is null)
        {
            throw new SmallLangException($"function '{function.Name}' does not accept a flowed input");
        }

        EnsureRuntimeType(argument, function.InputType.Value, function.Name);
        return EmitFunctionCall(function, argument);
    }

    private RuntimeValue EmitInlineFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        if (_inlineFunctionStack.Any(candidate => ReferenceEquals(candidate, function)))
        {
            throw new SmallLangException($"recursive inline function '{function.Name}' is not supported in the current runtime slice");
        }

        var outerLocals = CaptureLocals();
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        _inlineFunctionStack.Add(function);
        try
        {
            if (function.InputType is null)
            {
                if (argument is not null)
                {
                    throw new SmallLangException($"function '{function.Name}' does not accept arguments");
                }
            }
            else
            {
                if (argument is null)
                {
                    throw new SmallLangException($"function '{function.Name}' expects exactly one argument");
                }

                EnsureRuntimeType(argument, function.InputType.Value, function.Name);
                _locals[function.InputName ?? "it"] = argument;
            }

            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            return value;
        }
        finally
        {
            _inlineFunctionStack.RemoveAt(_inlineFunctionStack.Count - 1);
            _currentFunctions = previousFunctions;
            RestoreLocals(outerLocals);
        }
    }

    private RuntimeValue EmitFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Text value");
            }

            EnsureRuntimeType(argument, BoundType.Text, function.Name);
            _mainOk = EmitWriteValue(argument, _mainOk);
            if (function.Kind == BoundFunctionKind.RuntimePrintLine)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
            }

            return RuntimeUnit.Instance;
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadInt)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Text prompt");
            }

            EnsureRuntimeType(argument, BoundType.Text, function.Name);
            return EmitReadIntPrompt(argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (argument is not null)
            {
                throw new SmallLangException($"{function.Name} does not accept an argument");
            }

            return EmitRuntimeNowMillisIntrinsic(function.Name);
        }

        if (function.Kind is BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeCloseIntWriter
            or BoundFunctionKind.RuntimeOpenIntReader
            or BoundFunctionKind.RuntimeCloseIntReader)
        {
            return EmitRuntimeUnitIntrinsic(function, argument, function.Name);
        }

        if (function.Kind is BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeClosestInt)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Int argument");
            }

            return EmitRuntimeIntIntrinsic(function, argument, function.Name);
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SmallLangException($"function '{function.Name}' does not produce a runtime value");
        }

        if (function.IsStandardLibrary || function.IsLocal)
        {
            return EmitInlineFunctionCall(function, argument);
        }

        return function.ReturnType switch
        {
            BoundType.Text => EmitTextFunctionCall(function, argument),
            BoundType.Int => EmitIntFunctionCall(function, argument),
            BoundType.Bool => EmitBoolFunctionCall(function, argument),
            _ => throw new SmallLangException($"unsupported function return type {function.ReturnType}")
        };
    }

    private RuntimeText EmitTextFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("text");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(aggregate, "%smalllang.text", SymbolForFunction(function.Name)[1..], arguments);

        var pointer = NextTemp("text_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.text {aggregate}, 0");

        var length = NextTemp("text_len");
        EmitAssign(length, $"extractvalue %smalllang.text {aggregate}, 1");

        return new RuntimeText(pointer, length);
    }

    private RuntimeInt EmitIntFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(value, "i64", SymbolForFunction(function.Name)[1..], arguments);
        return new RuntimeInt(value);
    }

    private RuntimeBool EmitBoolFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(value, "i1", SymbolForFunction(function.Name)[1..], arguments);
        return new RuntimeBool(value);
    }

    private static string FunctionCallArgumentList(BoundFunction function, RuntimeValue? argument)
    {
        if (function.InputType is null)
        {
            if (argument is not null)
            {
                throw new SmallLangException($"function '{function.Name}' does not accept arguments");
            }

            return "";
        }

        if (argument is null)
        {
            throw new SmallLangException($"function '{function.Name}' expects exactly one argument");
        }

        return argument switch
        {
            RuntimeInt integer when function.InputType == BoundType.Int => $"i64 {integer.ValueName}",
            RuntimeBool boolean when function.InputType == BoundType.Bool => $"i1 {boolean.ValueName}",
            _ => throw new SmallLangException($"function '{function.Name}' expects {function.InputType} but received {argument.Type}")
        };
    }

    private static void EnsureRuntimeType(RuntimeValue value, BoundType expected, string path)
    {
        if (value.Type != expected)
        {
            throw new SmallLangException($"function '{path}' expects {expected} but received {value.Type}");
        }
    }

    private bool TryGetRuntimePrinterKind(BoundFunction function, out BoundFunctionKind kind)
    {
        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            kind = function.Kind;
            return true;
        }

        if (TryGetRuntimeWrapperKind(function, out kind)
            && kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            return true;
        }

        kind = default;
        return false;
    }

    private bool TryGetRuntimeWrapperKind(BoundFunction function, out BoundFunctionKind kind)
    {
        if (!function.IsStandardLibrary
            || function.Body is not FlowExpression flow
            || flow.Source is not NameExpression name
            || name.Name != (function.InputName ?? "it")
            || flow.Targets.Count != 1
            || !TryResolveFunction(flow.Targets[0].Path, out var target))
        {
            kind = default;
            return false;
        }

        if (target.Kind is BoundFunctionKind.RuntimePrint
            or BoundFunctionKind.RuntimePrintLine
            or BoundFunctionKind.RuntimeReadInt
            or BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeOpenIntReader
            or BoundFunctionKind.RuntimeClosestInt)
        {
            kind = target.Kind;
            return true;
        }

        kind = default;
        return false;
    }

}

