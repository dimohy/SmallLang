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

    private RuntimeTask EmitRuntimeSleepIntrinsic(
        BoundFunction function,
        RuntimeValue argument,
        string path)
    {
        EnsureRuntimeType(argument, function.InputType!.Value, path);
        if (argument is not RuntimeStruct duration)
        {
            throw new SmallLangException($"{path} expects Duration");
        }

        var millis = NextTemp("sleep_millis");
        EmitAssign(
            millis,
            $"extractvalue {LlvmStructType(duration.Type)} {duration.ValueName}, 0");
        var positive = NextTemp("sleep_positive");
        EmitCompare(positive, "sgt", "i64", millis, "0");
        var normalized = NextTemp("sleep_normalized");
        EmitAssign(normalized, $"select i1 {positive}, i64 {millis}, i64 0");
        var now = NextTemp("sleep_now");
        EmitCall(now, "i64", "smalllang_now_millis", "");
        var maximumRemaining = NextTemp("sleep_maximum_remaining");
        EmitAssign(maximumRemaining, $"sub i64 9223372036854775807, {now}");
        var tooLarge = NextTemp("sleep_too_large");
        EmitCompare(tooLarge, "sgt", "i64", normalized, maximumRemaining);
        var finiteDeadline = NextTemp("sleep_finite_deadline");
        EmitAssign(finiteDeadline, $"add i64 {now}, {normalized}");
        var deadline = NextTemp("sleep_deadline");
        EmitAssign(
            deadline,
            $"select i1 {tooLarge}, i64 9223372036854775807, i64 {finiteDeadline}");

        var context = NextTemp("sleep_context");
        EmitCall(context, "ptr", "smalllang_alloc", $"i64 {AsyncContextSize(null, BoundType.Unit)}");
        var allocated = NextTemp("sleep_context_allocated");
        EmitCompare(allocated, "ne", "ptr", context, "null");
        var initializeLabel = NextLabel("sleep_initialize");
        var allocationFailedLabel = NextLabel("sleep_allocation_failed");
        EmitConditionalBranch(allocated, initializeLabel, allocationFailedLabel);
        EmitLabel(allocationFailedLabel);
        EmitTrap();
        EmitLabel(initializeLabel);

        var resultAddress = AsyncContextField(
            context,
            null,
            BoundType.Unit,
            6,
            "sleep_result_address");
        EmitStore("i8", "0", resultAddress, 1);
        var handle = NextTemp("sleep_handle");
        EmitCall(
            handle,
            "ptr",
            "smalllang_task_start",
            $"ptr @smalllang_sleep_worker, ptr @smalllang_free, ptr @smalllang_sleep_cancel, ptr {context}");
        var started = NextTemp("sleep_started");
        EmitCompare(started, "ne", "ptr", handle, "null");
        var readyLabel = NextLabel("sleep_ready");
        var startFailedLabel = NextLabel("sleep_start_failed");
        EmitConditionalBranch(started, readyLabel, startFailedLabel);
        EmitLabel(startFailedLabel);
        EmitCall(target: null, "void", "smalllang_free", $"ptr {context}");
        EmitTrap();
        EmitLabel(readyLabel);
        var deadlineAddress = NextTemp("sleep_deadline_address");
        EmitAssign(
            deadlineAddress,
            $"getelementptr %smalllang.task_control, ptr {handle}, i32 0, i32 8");
        EmitStore("i64", deadline, deadlineAddress, 8);

        return new RuntimeTask(
            _program.Types.GetOrAddTask(BoundType.Unit),
            null,
            BoundType.Unit,
            handle,
            context);
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

    private RuntimeEnum EmitRuntimeReadScalar(BoundFunction function)
    {
        if (function.SpecializedType is not { } scalarType
            || (scalarType != BoundType.Bool && !IsNumericType(scalarType) && scalarType != BoundType.CodePoint))
        {
            throw new SmallLangException($"{function.Name} has an invalid scalar specialization");
        }
        if (!_program.Types.TryGetResultTypes(function.ReturnType, out var resultTypes)
            || !_program.Types.TryGetOptionValue(resultTypes.Ok, out var optionValue)
            || optionValue != scalarType)
        {
            throw new SmallLangException($"{function.Name} has an invalid scalar result type");
        }

        var resultDefinition = _program.Types.GetEnum(function.ReturnType);
        var okVariant = resultDefinition.Variants.First(variant => variant.Name == "Ok");
        var errVariant = resultDefinition.Variants.First(variant => variant.Name == "Err");
        var optionDefinition = _program.Types.GetEnum(resultTypes.Ok);
        var someVariant = optionDefinition.Variants.First(variant => variant.Name == "Some");
        var noneVariant = optionDefinition.Variants.First(variant => variant.Name == "None");
        if (resultTypes.Error != BoundType.Text)
        {
            throw new SmallLangException($"{function.Name} requires Text errors");
        }

        var byteSize = RuntimeScalarByteSize(scalarType);
        var storageType = scalarType == BoundType.Bool ? "i8" : LlvmType(scalarType);
        var slot = NextTemp("file_scalar_read");
        EmitAlloca(slot, storageType, RuntimeAlignment(scalarType));
        var readResult = NextTemp("file_scalar_read_result");
        EmitCall(readResult, "%smalllang.file_count_result", "smalllang_platform_read_file_bytes",
            $"ptr {slot}, i64 {byteSize}");
        var count = NextTemp("file_scalar_read_count");
        EmitAssign(count, $"extractvalue %smalllang.file_count_result {readResult}, 0");
        var readOk = NextTemp("file_scalar_read_ok");
        EmitAssign(readOk, $"extractvalue %smalllang.file_count_result {readResult}, 1");

        var ioLabel = NextLabel("file_scalar_io");
        var countLabel = NextLabel("file_scalar_count");
        var eofLabel = NextLabel("file_scalar_eof");
        var nonEofLabel = NextLabel("file_scalar_non_eof");
        var fullLabel = NextLabel("file_scalar_full");
        var truncatedLabel = NextLabel("file_scalar_truncated");
        var invalidLabel = NextLabel("file_scalar_invalid");
        var validLabel = NextLabel("file_scalar_valid");
        var endLabel = NextLabel("file_scalar_end");
        var incoming = new List<(RuntimeValue Value, string Label)>();
        var isReadOk = NextTemp("file_scalar_is_read_ok");
        EmitCompare(isReadOk, "ne", "i32", readOk, "0");
        EmitConditionalBranch(isReadOk, countLabel, ioLabel);

        EmitLabel(ioLabel);
        _currentBlockLabel = ioLabel;
        var ioError = EmitRuntimeErrorText("io");
        incoming.Add((EmitEnumValue(function.ReturnType, errVariant, ioError), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(countLabel);
        _currentBlockLabel = countLabel;
        var isEof = NextTemp("file_scalar_is_eof");
        EmitCompare(isEof, "eq", "i64", count, "0");
        EmitConditionalBranch(isEof, eofLabel, nonEofLabel);

        EmitLabel(eofLabel);
        _currentBlockLabel = eofLabel;
        var none = EmitEnumValue(resultTypes.Ok, noneVariant, null);
        incoming.Add((EmitEnumValue(function.ReturnType, okVariant, none), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(nonEofLabel);
        _currentBlockLabel = nonEofLabel;
        var isFull = NextTemp("file_scalar_is_full");
        EmitCompare(isFull, "eq", "i64", count, byteSize.ToString(CultureInfo.InvariantCulture));
        EmitConditionalBranch(isFull, fullLabel, truncatedLabel);

        EmitLabel(truncatedLabel);
        _currentBlockLabel = truncatedLabel;
        var truncatedError = EmitRuntimeErrorText("truncated");
        incoming.Add((EmitEnumValue(function.ReturnType, errVariant, truncatedError), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(fullLabel);
        _currentBlockLabel = fullLabel;
        var loaded = NextTemp("file_scalar_loaded");
        EmitLoad(loaded, storageType, slot, RuntimeAlignment(scalarType));
        string? encodingValid = null;
        if (scalarType == BoundType.Bool)
        {
            encodingValid = NextTemp("file_bool_valid");
            EmitCompare(encodingValid, "ule", "i8", loaded, "1");
        }
        else if (scalarType == BoundType.CodePoint)
        {
            var withinRange = NextTemp("file_codepoint_range");
            EmitCompare(withinRange, "ule", "i32", loaded, "1114111");
            var belowSurrogate = NextTemp("file_codepoint_below_surrogate");
            EmitCompare(belowSurrogate, "ult", "i32", loaded, "55296");
            var aboveSurrogate = NextTemp("file_codepoint_above_surrogate");
            EmitCompare(aboveSurrogate, "ugt", "i32", loaded, "57343");
            var outsideSurrogate = NextTemp("file_codepoint_outside_surrogate");
            EmitAssign(outsideSurrogate, $"or i1 {belowSurrogate}, {aboveSurrogate}");
            encodingValid = NextTemp("file_codepoint_valid");
            EmitAssign(encodingValid, $"and i1 {withinRange}, {outsideSurrogate}");
        }
        if (encodingValid is not null)
        {
            EmitConditionalBranch(encodingValid, validLabel, invalidLabel);
        }
        else
        {
            EmitBranch(validLabel);
        }

        EmitLabel(invalidLabel);
        _currentBlockLabel = invalidLabel;
        var invalidError = EmitRuntimeErrorText("invalid");
        incoming.Add((EmitEnumValue(function.ReturnType, errVariant, invalidError), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(validLabel);
        _currentBlockLabel = validLabel;
        RuntimeValue scalar = scalarType switch
        {
            BoundType.Bool => new RuntimeBool(EmitBoolFromByte(loaded)),
            BoundType.Float32 or BoundType.Float64 => new RuntimeFloat(scalarType, loaded),
            _ => new RuntimeInt(scalarType, loaded)
        };
        var some = EmitEnumValue(resultTypes.Ok, someVariant, scalar);
        incoming.Add((EmitEnumValue(function.ReturnType, okVariant, some), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        return EmitEnumPhi("file_scalar_result", function.ReturnType, incoming);
    }

    private string EmitBoolFromByte(string value)
    {
        var result = NextTemp("file_bool");
        EmitCompare(result, "ne", "i8", value, "0");
        return result;
    }

    private RuntimeText EmitRuntimeErrorText(string text)
    {
        var global = AddGlobalString(text);
        return new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture));
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

