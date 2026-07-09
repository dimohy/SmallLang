using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeValue EmitFlowSource(Expression source)
    {
        return EmitExpression(source);
    }

    private string EmitPrintFlowSource(Expression expression, string ok)
    {
        if (expression is StringExpression str)
        {
            return EmitPrintArgument(str, ok);
        }

        var value = EmitFlowSource(expression);
        return EmitWriteValue(value, ok);
    }

    private RuntimeInt EmitReadIntPrompt(RuntimeValue prompt)
    {
        EnsureRuntimeType(prompt, BoundType.Text, "sys.io.readInt");
        _mainOk = EmitWriteValue(prompt, _mainOk);
        return EmitReadIntAfterPrompt();
    }

    private RuntimeInt EmitReadIntPromptExpression(Expression prompt)
    {
        _mainOk = EmitPrintArgument(prompt, _mainOk);
        return EmitReadIntAfterPrompt();
    }

    private RuntimeInt EmitReadIntAfterPrompt()
    {
        var result = NextTemp("read_int");
        EmitCall(result, "%smalllang.read_int_result", "smalllang_read_i64", "ptr %stdin, ptr %read");

        var value = NextTemp("read_value");
        EmitAssign(value, $"extractvalue %smalllang.read_int_result {result}, 0");

        var ok = NextTemp("read_ok");
        EmitAssign(ok, $"extractvalue %smalllang.read_int_result {result}, 1");

        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        return new RuntimeInt(value);
    }

    private RuntimeInt EmitRuntimeNowMillisIntrinsic(string path)
    {
        _ = path;
        var value = NextTemp("now_ms");
        EmitCall(value, "i64", "smalllang_now_millis", "");
        return new RuntimeInt(value);
    }

    private void EmitReturnIfReadFailed(string readOk)
    {
        var isOk = NextTemp("read_is_ok");
        var failLabel = NextLabel("read_fail");
        var continueLabel = NextLabel("read_continue");

        EmitCompare(isOk, "ne", "i32", readOk, "0");
        EmitConditionalBranch(isOk, continueLabel, failLabel);
        EmitLabel(failLabel);
        EmitRet("i32", "1");
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
    }

    private RuntimeUnit EmitRuntimeUnitIntrinsic(BoundFunction function, RuntimeValue? argument, string path)
    {
        return EmitRuntimeUnitIntrinsic(function.Kind, argument, path);
    }

    private RuntimeUnit EmitRuntimeUnitIntrinsic(BoundFunctionKind kind, RuntimeValue? argument, string path)
    {
        var ok = kind switch
        {
            BoundFunctionKind.RuntimeSeedRandom => EmitRuntimeIntStatusCall(
                "smalllang_seed_random",
                argument,
                path),
            BoundFunctionKind.RuntimeOpenIntWriter => EmitRuntimeTextStatusCall(
                "smalllang_open_write_i64_file",
                argument,
                path),
            BoundFunctionKind.RuntimeWriteInt => EmitRuntimeIntStatusCall(
                "smalllang_write_i64_file",
                argument,
                path),
            BoundFunctionKind.RuntimeCloseIntWriter => EmitRuntimeNoArgumentStatusCall(
                "smalllang_close_write_i64_file",
                argument,
                path),
            BoundFunctionKind.RuntimeOpenIntReader => EmitRuntimeTextStatusCall(
                "smalllang_platform_open_read_file",
                argument,
                path),
            BoundFunctionKind.RuntimeCloseIntReader => EmitRuntimeNoArgumentStatusCall(
                "smalllang_platform_close_read_file",
                argument,
                path),
            _ => throw new SmallLangException($"unsupported runtime unit intrinsic '{kind}'")
        };

        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        return RuntimeUnit.Instance;
    }

    private string EmitRuntimeNoArgumentStatusCall(string functionName, RuntimeValue? argument, string path)
    {
        if (argument is not null)
        {
            throw new SmallLangException($"{path} does not accept an argument");
        }

        var ok = NextTemp("runtime_ok");
        EmitCall(ok, "i32", functionName, "");
        return ok;
    }

    private string EmitRuntimeTextStatusCall(string functionName, RuntimeValue? argument, string path)
    {
        if (argument is not RuntimeText text)
        {
            throw new SmallLangException($"{path} expects Text");
        }

        var ok = NextTemp("runtime_ok");
        EmitCall(ok, "i32", functionName, $"ptr {text.PointerName}, i64 {text.LengthName}");
        return ok;
    }

    private string EmitRuntimeIntStatusCall(string functionName, RuntimeValue? argument, string path)
    {
        if (argument is not RuntimeInt integer)
        {
            throw new SmallLangException($"{path} expects Int");
        }

        var ok = NextTemp("runtime_ok");
        EmitCall(ok, "i32", functionName, $"i64 {integer.ValueName}");
        return ok;
    }

    private RuntimeInt EmitRuntimeIntIntrinsic(BoundFunction function, RuntimeValue argument, string path)
    {
        return EmitRuntimeIntIntrinsic(function.Kind, argument, path);
    }

    private RuntimeInt EmitRuntimeIntIntrinsic(BoundFunctionKind kind, RuntimeValue argument, string path)
    {
        if (argument is not RuntimeInt integer)
        {
            throw new SmallLangException($"{path} expects Int");
        }

        var helperName = kind switch
        {
            BoundFunctionKind.RuntimeRandomBelow => "smalllang_random_below",
            BoundFunctionKind.RuntimeClosestInt => "smalllang_closest_i64_file",
            _ => throw new SmallLangException($"unsupported runtime int intrinsic '{kind}'")
        };

        var result = NextTemp("runtime_int");
        EmitCall(result, "%smalllang.file_int_result", helperName, $"i64 {integer.ValueName}");

        var value = NextTemp("runtime_value");
        EmitAssign(value, $"extractvalue %smalllang.file_int_result {result}, 0");

        var ok = NextTemp("runtime_ok");
        EmitAssign(ok, $"extractvalue %smalllang.file_int_result {result}, 1");

        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        return new RuntimeInt(value);
    }

}

