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
        var narrowed = NextTemp("read_int32");
        EmitAssign(narrowed, $"trunc i64 {value} to i32");
        return new RuntimeInt(narrowed);
    }

    private RuntimeInt EmitRuntimeNowMillisIntrinsic(string path)
    {
        _ = path;
        var value = NextTemp("now_ms");
        EmitCall(value, "i64", "smalllang_now_millis", "");
        return new RuntimeInt(BoundType.Int64, value);
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
        var wide = EmitRuntimeIntegerAsI64(integer, "runtime_argument");
        EmitCall(ok, "i32", functionName, $"i64 {wide}");
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
        var wide = EmitRuntimeIntegerAsI64(integer, "runtime_argument");
        EmitCall(result, "%smalllang.file_int_result", helperName, $"i64 {wide}");

        var value = NextTemp("runtime_value");
        EmitAssign(value, $"extractvalue %smalllang.file_int_result {result}, 0");

        var ok = NextTemp("runtime_ok");
        EmitAssign(ok, $"extractvalue %smalllang.file_int_result {result}, 1");

        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        var narrowed = NextTemp("runtime_int32");
        EmitAssign(narrowed, $"trunc i64 {value} to i32");
        return new RuntimeInt(narrowed);
    }

    private string EmitRuntimeIntegerAsI64(RuntimeInt integer, string prefix)
    {
        if (NumericBitWidth(integer.Type) == 64)
        {
            return integer.ValueName;
        }
        var wide = NextTemp(prefix);
        var extension = IsSignedIntegerType(integer.Type) ? "sext" : "zext";
        EmitAssign(wide, $"{extension} {LlvmType(integer.Type)} {integer.ValueName} to i64");
        return wide;
    }

    private RuntimeUnit EmitRuntimeWriteScalar(BoundFunction function, RuntimeValue value)
    {
        if (function.SpecializedType is not { } scalarType || value.Type != scalarType)
        {
            throw new SmallLangException($"{function.Name} has an invalid scalar specialization");
        }
        if (scalarType != BoundType.Bool && !IsNumericType(scalarType) && scalarType != BoundType.CodePoint)
        {
            throw new SmallLangException($"{function.Name} does not support {scalarType}");
        }

        var materialized = MaterializeAggregateValue(value);
        var slot = NextTemp("file_scalar");
        EmitAlloca(slot, materialized.TypeName, RuntimeAlignment(scalarType));
        EmitStore(materialized.TypeName, materialized.ValueName, slot, RuntimeAlignment(scalarType));

        var flushOk = NextTemp("file_scalar_flush_ok");
        EmitCall(flushOk, "i32", "smalllang_flush_i64_file", "");
        var writeOk = NextTemp("file_scalar_write_ok");
        EmitCall(writeOk, "i32", "smalllang_platform_write_file_bytes",
            $"ptr {slot}, i64 {RuntimeScalarByteSize(scalarType)}");
        var ok = NextTemp("file_scalar_ok");
        EmitAssign(ok, $"and i32 {flushOk}, {writeOk}");
        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        return RuntimeUnit.Instance;
    }

    private int RuntimeScalarByteSize(BoundType type) => type switch
    {
        BoundType.Bool or BoundType.Int8 or BoundType.UInt8 => 1,
        BoundType.Int16 or BoundType.UInt16 => 2,
        BoundType.Int or BoundType.UInt32 or BoundType.Float32 or BoundType.CodePoint => 4,
        BoundType.Int64 or BoundType.UInt64 or BoundType.Float64 => 8,
        BoundType.Size or BoundType.UIntSize => _platform.PointerBitWidth / 8,
        _ => throw new SmallLangException($"{type} is not a binary scalar")
    };

}

