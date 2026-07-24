using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private bool FinishTerminatedFunction()
    {
        if (!_currentBlockTerminated)
        {
            return false;
        }

        EmitFunctionLine("}");
        EmitFunctionLine();
        return true;
    }

    private IReadOnlyList<BoundFunction> GetEmittableUserFunctions()
    {
        var result = new List<BoundFunction>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var emittedFunctions = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in EnumerateEmittableFunctions(_program.Functions.Values))
        {
            if (function.Kind != BoundFunctionKind.User
                || (function.IsStandardLibrary && !_standaloneStandardLibraryFunctions.Contains(function))
                || (function.GenericParameterName is not null
                    && function.SpecializedType is null
                    && function.SpecializedValue is null)
                || !emittedFunctions.Add(function)
                || (!function.IsLocal && !emitted.Add(function.Name)))
            {
                continue;
            }

            result.Add(function);
        }
        return result;
    }

    private void EmitUserFunctions(IEnumerable<BoundFunction> functions)
    {
        foreach (var function in functions)
        {
            _currentFunction = function;
            _tempId = 0;
            _labelId = 0;
            if (function.IsAsync)
            {
                EmitAsyncFunction(function);
                continue;
            }
            if (IsNumericType(function.ReturnType))
            {
                EmitNumericFunction(function);
                continue;
            }
            if (_program.Types.IsReference(function.ReturnType))
            {
                EmitReferenceFunction(function);
                continue;
            }
            switch (function.ReturnType)
            {
                case BoundType.Unit:
                    EmitUnitFunction(function);
                    break;
                case BoundType.Text:
                    EmitTextFunction(function);
                    break;
                case BoundType.Int:
                    EmitIntFunction(function);
                    break;
                case BoundType.Bool:
                    EmitBoolFunction(function);
                    break;
                case BoundType.DynamicIntArray:
                    EmitDynamicIntArrayFunction(function);
                    break;
                case BoundType.IntDictionary:
                    EmitIntDictionaryFunction(function);
                    break;
                case BoundType.Arena:
                    EmitArenaFunction(function);
                    break;
                default:
                    if (_program.Types.IsDynamicArray(function.ReturnType))
                    {
                        EmitDynamicInlineArrayFunction(function);
                        break;
                    }
                    if (_program.Types.IsDictionary(function.ReturnType))
                    {
                        EmitInlineDictionaryFunction(function);
                        break;
                    }
                    if (_program.Types.IsStruct(function.ReturnType)
                        || _program.Types.IsEnum(function.ReturnType)
                        || _program.Types.IsBox(function.ReturnType)
                        || _program.Types.IsStream(function.ReturnType)
                        || _program.Types.IsEventStream(function.ReturnType))
                    {
                        EmitStructFunction(function);
                        break;
                    }

                    throw new SollangException($"unsupported function return type {function.ReturnType}");
            }
        }

        _currentFunction = null;
    }

    private static IEnumerable<BoundFunction> EnumerateEmittableFunctions(
        IEnumerable<BoundFunction> functions)
    {
        foreach (var function in functions)
        {
            yield return function;
            foreach (var local in EnumerateEmittableFunctions(function.LocalFunctions.Values))
            {
                yield return local;
            }
        }
    }

    private void EmitStructFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            var llvmType = LlvmType(function.ReturnType);
            EmitFunctionLine($"define internal {llvmType} {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = IsOwnedContainerRuntimeValue(value)
                ? GetFunctionResultTransferredOwnerName(function, function.Body)
                : null;
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var aggregate = MaterializeAggregateValue(value);
            EmitRet(aggregate.TypeName, aggregate.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitReferenceFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal ptr {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            BindAllFunctionParameters(function);
            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var reference = EmitReferencePlace(function.Body, function.ReturnType);
            EmitRet("ptr", reference.PointerName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitUnitFunction(BoundFunction function)
    {
        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal void {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            if (function.Body is not null)
            {
                var value = EmitExpression(function.Body);
                EnsureRuntimeType(value, BoundType.Unit, function.Name);
            }

            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            EmitInstruction("ret void");
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitTextFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %sollang.text {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.Text, function.Name);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            var text = (RuntimeText)value;
            var aggregate0 = NextTemp("text_ret");
            EmitAssign(aggregate0, $"insertvalue %sollang.text poison, ptr {text.PointerName}, 0");
            var aggregate1 = NextTemp("text_ret");
            EmitAssign(aggregate1, $"insertvalue %sollang.text {aggregate0}, i64 {text.LengthName}, 1");
            EmitRet("%sollang.text", aggregate1);
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
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal i64 {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitIntExpression(function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
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
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal i1 {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitBoolExpression(function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            EmitRet("i1", value.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitNumericFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            var llvmType = LlvmType(function.ReturnType);
            EmitFunctionLine($"define internal {llvmType} {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);
            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            var valueName = value switch
            {
                RuntimeInt integer => integer.ValueName,
                RuntimeFloat floating => floating.ValueName,
                _ => throw new SollangException($"function '{function.Name}' did not produce a numeric value")
            };
            EmitRet(llvmType, valueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitDynamicIntArrayFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %sollang.dynamic_int_array {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.DynamicIntArray, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var array = (RuntimeDynamicIntArray)value;
            var aggregate0 = NextTemp("array_ret");
            EmitAssign(aggregate0, $"insertvalue %sollang.dynamic_int_array poison, ptr {array.PointerName}, 0");
            var aggregate1 = NextTemp("array_ret");
            EmitAssign(aggregate1, $"insertvalue %sollang.dynamic_int_array {aggregate0}, i64 {array.LengthName}, 1");
            var aggregate2 = NextTemp("array_ret");
            EmitAssign(aggregate2, $"insertvalue %sollang.dynamic_int_array {aggregate1}, i64 {array.CapacityName}, 2");
            EmitRet("%sollang.dynamic_int_array", aggregate2);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitDynamicInlineArrayFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %sollang.dynamic_int_array {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);
            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var array = (RuntimeDynamicInlineArray)value;
            EmitRet("%sollang.dynamic_int_array", BuildDynamicArrayAggregate(
                array.PointerName, array.LengthName, array.CapacityName));
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitIntDictionaryFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %sollang.int_dictionary {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);

            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.IntDictionary, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var dictionary = (RuntimeIntDictionary)value;
            var aggregate0 = NextTemp("dict_ret");
            EmitAssign(aggregate0, $"insertvalue %sollang.int_dictionary poison, ptr {dictionary.PointerName}, 0");
            var aggregate1 = NextTemp("dict_ret");
            EmitAssign(aggregate1, $"insertvalue %sollang.int_dictionary {aggregate0}, i64 {dictionary.LengthName}, 1");
            var aggregate2 = NextTemp("dict_ret");
            EmitAssign(aggregate2, $"insertvalue %sollang.int_dictionary {aggregate1}, i64 {dictionary.CapacityName}, 2");
            EmitRet("%sollang.int_dictionary", aggregate2);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitInlineDictionaryFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %sollang.int_dictionary {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);
            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var dictionary = (RuntimeInlineDictionary)value;
            EmitRet("%sollang.int_dictionary", BuildDictionaryAggregate(
                dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName));
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private string ParameterListForFunction(BoundFunction function)
    {
        const string runtimeContext = "ptr %stdin, ptr %stdout, ptr %written, ptr %read, ptr %ok_state";
        var parameters = new[]
            {
                CaptureParameterListForFunction(function),
                ExplicitParameterListForFunction(function)
            }
            .Where(static part => part.Length > 0);
        var explicitParameters = string.Join(", ", parameters);
        return explicitParameters.Length == 0
            ? runtimeContext
            : $"{runtimeContext}, {explicitParameters}";
    }

    private string CaptureParameterListForFunction(BoundFunction function)
    {
        return string.Join(", ", CapturedBindingsForFunction(function)
            .Select((binding, index) =>
                $"{(CaptureUsesBorrowAbi(binding.Value) ? "ptr" : LlvmType(binding.Value))} %capture_{index.ToString(CultureInfo.InvariantCulture)}"));
    }

    private bool CaptureUsesBorrowAbi(BoundType type) =>
        _program.Types.ContainsOwnedStorage(type);

    private IReadOnlyList<KeyValuePair<string, BoundType>> CapturedBindingsForFunction(
        BoundFunction function)
    {
        return _program.FunctionCapturedBindings.TryGetValue(function, out var captures)
            ? captures.OrderBy(static binding => binding.Key, StringComparer.Ordinal).ToArray()
            : [];
    }

    private string ExplicitParameterListForFunction(BoundFunction function)
    {
        var parts = new List<string>();
        var primary = PrimaryParameterListForFunction(function);
        if (primary.Length > 0)
        {
            parts.Add(primary);
        }
        var additionalParameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < additionalParameters.Count; index++)
        {
            var parameter = additionalParameters[index];
            var name = $"%arg_{index.ToString(CultureInfo.InvariantCulture)}";
            if (parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow)
            {
                parts.Add(_program.Types.IsStruct(parameter.Type)
                    ? $"ptr {name}"
                    : $"%sollang.mutable_container {name}");
            }
            else if (_program.Types.IsReference(parameter.Type))
            {
                parts.Add($"ptr {name}");
            }
            else
            {
                parts.Add($"{LlvmType(parameter.Type)} {name}");
            }
        }
        return string.Join(", ", parts);
    }

    private string PrimaryParameterListForFunction(BoundFunction function)
    {
        if (function.InputType is { } referenceType && _program.Types.IsReference(referenceType))
        {
            return "ptr %it";
        }
        if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
        {
            if (function.InputType is { } mutableType && _program.Types.IsStruct(mutableType))
            {
                return "ptr %it";
            }

            return function.InputType switch
            {
                BoundType.DynamicIntArray => "%sollang.mutable_container %it",
                _ when function.InputType is { } type && _program.Types.IsDynamicArray(type) => "%sollang.mutable_container %it",
                BoundType.IntDictionary => "%sollang.mutable_container %it",
                BoundType.Arena => "%sollang.mutable_container %it",
                _ when function.InputType is { } type && _program.Types.IsDictionary(type) => "%sollang.mutable_container %it",
                _ => throw new SollangException("unsupported mutable borrow input type")
            };
        }

        if (function.HasValueGenericFixedArrayInput)
        {
            return "%sollang.int_slice %it";
        }

        if (function.InputType is { } inputType
            && (_program.Types.IsStruct(inputType)
                || _program.Types.IsEnum(inputType)
                || _program.Types.IsBox(inputType)
                || _program.Types.IsStream(inputType)
                || _program.Types.IsEventStream(inputType)))
        {
            return $"{LlvmType(inputType)} %it";
        }

        if (function.InputType is { } dynamicArrayType && _program.Types.IsDynamicArray(dynamicArrayType))
        {
            return "%sollang.dynamic_int_array %it";
        }
        if (function.InputType is { } dictionaryType && _program.Types.IsDictionary(dictionaryType))
        {
            return "%sollang.int_dictionary %it";
        }

        if (function.InputType is { } numericInput && IsNumericType(numericInput))
        {
            return $"{LlvmType(numericInput)} %it";
        }
        if (function.InputType == BoundType.Text)
        {
            return "%sollang.text %it";
        }

        return function.InputType switch
        {
            null => "",
            BoundType.Int => "i64 %it",
            BoundType.Bool => "i1 %it",
            BoundType.IntSlice => "%sollang.int_slice %it",
            BoundType.DynamicIntArray => "%sollang.dynamic_int_array %it",
            BoundType.IntDictionaryView => "%sollang.int_dictionary %it",
            BoundType.IntDictionary => "%sollang.int_dictionary %it",
            BoundType.Arena => "%sollang.dynamic_int_array %it",
            _ => throw new SollangException("unsupported function input type")
        };
    }

    private void BindFunctionCaptures(BoundFunction function)
    {
        var captures = CapturedBindingsForFunction(function);
        for (var index = 0; index < captures.Count; index++)
        {
            var capture = captures[index];
            var captureName = $"%capture_{index.ToString(CultureInfo.InvariantCulture)}";
            if (CaptureUsesBorrowAbi(capture.Value))
            {
                var loaded = NextTemp("capture_borrow");
                EmitLoad(
                    loaded,
                    LlvmType(capture.Value),
                    captureName,
                    RuntimeAlignment(capture.Value));
                _locals.Add(capture.Key, DematerializeAggregateValue(capture.Value, loaded));
                _borrowedOwnedLocals.Add(capture.Key);
                _readonlyCaptureBorrowPointers.Add(capture.Key, captureName);
                continue;
            }
            var value = DematerializeAggregateValue(
                capture.Value,
                captureName);
            _locals.Add(capture.Key, value);
            if (_program.Types.ContainsOwnedStorage(capture.Value))
            {
                _borrowedOwnedLocals.Add(capture.Key);
            }
        }
    }

    private void BindFunctionParameter(BoundFunction function)
    {
        if (function.InputType is { } referenceType && _program.Types.IsReference(referenceType))
        {
            var elementType = _program.Types.GetReference(referenceType).ElementType;
            _locals.Add(
                function.InputName ?? "it",
                new RuntimeReference(referenceType, elementType, "%it"));
            return;
        }
        if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
        {
            BindMutableBorrowFunctionParameter(function);
            return;
        }

        if (function.HasValueGenericFixedArrayInput)
        {
            if (function.InputType is not { } fixedArrayType
                || function.SpecializedValue is not { } fixedArrayLength)
            {
                throw new SollangException(
                    $"function '{function.Name}' has an unspecialized fixed-array input");
            }
            var pointer = NextTemp("param_fixed_array_ptr");
            EmitAssign(pointer, "extractvalue %sollang.int_slice %it, 0");
            var length = NextTemp("param_fixed_array_len");
            EmitAssign(length, "extractvalue %sollang.int_slice %it, 1");
            RuntimeValue value = fixedArrayType switch
            {
                BoundType.StaticIntArray => new RuntimeStaticIntArray(
                    pointer, length, fixedArrayLength),
                BoundType.StaticTextArray => new RuntimeStaticTextArray(
                    pointer, length, fixedArrayLength),
                _ when _program.Types.IsStaticArray(fixedArrayType) => CreateBorrowedStaticInlineArray(
                    fixedArrayType, pointer, length, fixedArrayLength),
                _ => throw new SollangException(
                    $"function '{function.Name}' has unsupported fixed-array input type {fixedArrayType}")
            };
            _locals.Add(function.InputName ?? "it", value);
            _borrowedOwnedLocals.Add(function.InputName ?? "it");
            return;
        }

        if (function.InputType is { } borrowedType
            && function.InputOwnership == BoundFunctionInputOwnership.Default
            && _program.Types.ContainsOwnedStorage(borrowedType))
        {
            _borrowedOwnedLocals.Add(function.InputName ?? "it");
        }

        if (function.InputType == BoundType.SourceText)
        {
            _locals.Add(function.InputName ?? "it", ExtractSourceTextAggregate("%it"));
            return;
        }
        if (function.InputType is BoundType.MappedBytes or BoundType.MutableMappedBytes)
        {
            _locals.Add(
                function.InputName ?? "it",
                ExtractMappedBytesAggregate(function.InputType.Value, "%it"));
            return;
        }
        if (function.InputType is { } producerType
            && (_program.Types.IsStream(producerType)
                || _program.Types.IsEventStream(producerType)))
        {
            _locals.Add(
                function.InputName ?? "it",
                DematerializeAggregateValue(producerType, "%it"));
            return;
        }
        if (function.InputType is { } inputType && _program.Types.IsStruct(inputType))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeStruct(inputType, "%it"));
            return;
        }
        if (function.InputType is { } enumType && _program.Types.IsEnum(enumType))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeEnum(enumType, "%it"));
            return;
        }
        if (function.InputType is { } boxType && _program.Types.IsBox(boxType))
        {
            var definition = _program.Types.GetBox(boxType);
            _locals.Add(
                function.InputName ?? "it",
                new RuntimeBox(boxType, definition.ElementType, "%it"));
            return;
        }
        if (function.InputType is { } dictionaryType && _program.Types.IsDictionary(dictionaryType))
        {
            var definition = _program.Types.GetDictionary(dictionaryType);
            var pointer = NextTemp("param_generic_dict_ptr");
            EmitAssign(pointer, "extractvalue %sollang.int_dictionary %it, 0");
            var length = NextTemp("param_generic_dict_len");
            EmitAssign(length, "extractvalue %sollang.int_dictionary %it, 1");
            var capacity = NextTemp("param_generic_dict_capacity");
            EmitAssign(capacity, "extractvalue %sollang.int_dictionary %it, 2");
            _locals.Add(function.InputName ?? "it", new RuntimeInlineDictionary(
                dictionaryType, definition.KeyType, definition.ValueType, pointer, length, capacity));
            return;
        }
        if (function.InputType is { } dynamicArrayType && _program.Types.IsDynamicArray(dynamicArrayType))
        {
            var definition = _program.Types.GetDynamicArray(dynamicArrayType);
            var pointer = NextTemp("param_generic_array_ptr");
            EmitAssign(pointer, "extractvalue %sollang.dynamic_int_array %it, 0");
            var length = NextTemp("param_generic_array_len");
            EmitAssign(length, "extractvalue %sollang.dynamic_int_array %it, 1");
            var capacity = NextTemp("param_generic_array_capacity");
            EmitAssign(capacity, "extractvalue %sollang.dynamic_int_array %it, 2");
            _locals.Add(function.InputName ?? "it", new RuntimeDynamicInlineArray(
                dynamicArrayType, definition.ElementType, pointer, length, capacity));
            return;
        }

        if (function.InputType is { } numericInput && IsIntegerType(numericInput))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeInt(numericInput, "%it"));
            return;
        }
        if (function.InputType is { } floatInput && IsFloatType(floatInput))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeFloat(floatInput, "%it"));
            return;
        }
        if (function.InputType == BoundType.Text)
        {
            var pointer = NextTemp("param_text_ptr");
            EmitAssign(pointer, "extractvalue %sollang.text %it, 0");
            var length = NextTemp("param_text_len");
            EmitAssign(length, "extractvalue %sollang.text %it, 1");
            _locals.Add(function.InputName ?? "it", new RuntimeText(pointer, length));
            return;
        }

        switch (function.InputType)
        {
            case null:
                return;
            case BoundType.Bool:
                _locals.Add(function.InputName ?? "it", new RuntimeBool("%it"));
                return;
            case BoundType.IntSlice:
                {
                    var pointer = NextTemp("param_slice_ptr");
                    EmitAssign(pointer, "extractvalue %sollang.int_slice %it, 0");
                    var length = NextTemp("param_slice_len");
                    EmitAssign(length, "extractvalue %sollang.int_slice %it, 1");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntSlice(pointer, length));
                    return;
                }
            case BoundType.DynamicIntArray:
                {
                    var pointer = NextTemp("param_array_ptr");
                    EmitAssign(pointer, "extractvalue %sollang.dynamic_int_array %it, 0");
                    var length = NextTemp("param_array_len");
                    EmitAssign(length, "extractvalue %sollang.dynamic_int_array %it, 1");
                    var capacity = NextTemp("param_array_capacity");
                    EmitAssign(capacity, "extractvalue %sollang.dynamic_int_array %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeDynamicIntArray(pointer, length, capacity));
                    return;
                }
            case BoundType.IntDictionary:
                {
                    var pointer = NextTemp("param_dict_ptr");
                    EmitAssign(pointer, "extractvalue %sollang.int_dictionary %it, 0");
                    var length = NextTemp("param_dict_len");
                    EmitAssign(length, "extractvalue %sollang.int_dictionary %it, 1");
                    var capacity = NextTemp("param_dict_capacity");
                    EmitAssign(capacity, "extractvalue %sollang.int_dictionary %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntDictionary(pointer, length, capacity));
                    return;
                }
            case BoundType.IntDictionaryView:
                {
                    var pointer = NextTemp("param_dict_view_ptr");
                    EmitAssign(pointer, "extractvalue %sollang.int_dictionary %it, 0");
                    var length = NextTemp("param_dict_view_len");
                    EmitAssign(length, "extractvalue %sollang.int_dictionary %it, 1");
                    var capacity = NextTemp("param_dict_view_capacity");
                    EmitAssign(capacity, "extractvalue %sollang.int_dictionary %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntDictionaryView(pointer, length, capacity));
                    return;
                }
            case BoundType.Arena:
                {
                    var pointer = NextTemp("param_arena_ptr");
                    EmitAssign(pointer, "extractvalue %sollang.dynamic_int_array %it, 0");
                    var used = NextTemp("param_arena_used");
                    EmitAssign(used, "extractvalue %sollang.dynamic_int_array %it, 1");
                    var capacity = NextTemp("param_arena_capacity");
                    EmitAssign(capacity, "extractvalue %sollang.dynamic_int_array %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeArena(pointer, used, capacity));
                    return;
                }
            default:
                throw new SollangException("unsupported function input type");
        }
    }

    private void BindAllFunctionParameters(BoundFunction function)
    {
        BindFunctionParameter(function);
        var parameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var argumentName = $"%arg_{index.ToString(CultureInfo.InvariantCulture)}";
            if (parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow)
            {
                BindMutableBorrowFunctionParameter(parameter.Name, parameter.Type, argumentName);
                continue;
            }
            if (_program.Types.IsReference(parameter.Type))
            {
                var elementType = _program.Types.GetReference(parameter.Type).ElementType;
                _locals.Add(
                    parameter.Name,
                    new RuntimeReference(parameter.Type, elementType, argumentName));
                continue;
            }
            _locals.Add(parameter.Name, DematerializeAggregateValue(parameter.Type, argumentName));
            if (parameter.Ownership == BoundFunctionInputOwnership.Default
                && _program.Types.ContainsOwnedStorage(parameter.Type))
            {
                _borrowedOwnedLocals.Add(parameter.Name);
            }
        }
    }

    private RuntimeStaticInlineArray CreateBorrowedStaticInlineArray(
        BoundType arrayType,
        string pointer,
        string length,
        int fixedArrayLength)
    {
        var definition = _program.Types.GetStaticArray(arrayType);
        return new RuntimeStaticInlineArray(
            arrayType,
            definition.ElementType,
            pointer,
            length,
            fixedArrayLength,
            fixedArrayLength);
    }

    private void BindMutableBorrowFunctionParameter(BoundFunction function)
    {
        BindMutableBorrowFunctionParameter(
            function.InputName ?? "it",
            function.InputType!.Value,
            "%it");
    }

    private void BindMutableBorrowFunctionParameter(string name, BoundType type, string argumentName)
    {
        if (_program.Types.IsStruct(type))
        {
            _locals.Add(name, new RuntimeStruct(type, ""));
            _mutableLocals.Add(name);
            _borrowedMutableLocals.Add(name);
            _mutableStructSlots.Add(name, argumentName);
            return;
        }

        if (type is not (BoundType.DynamicIntArray or BoundType.IntDictionary or BoundType.Arena)
            && !_program.Types.IsDynamicArray(type)
            && !_program.Types.IsDictionary(type))
        {
            throw new SollangException("unsupported mutable borrow input type");
        }

        var pointerAddress = NextTemp("param_mut_ptr_addr");
        EmitAssign(pointerAddress, $"extractvalue %sollang.mutable_container {argumentName}, 0");
        var lengthAddress = NextTemp("param_mut_len_addr");
        EmitAssign(lengthAddress, $"extractvalue %sollang.mutable_container {argumentName}, 1");
        var capacityAddress = NextTemp("param_mut_capacity_addr");
        EmitAssign(capacityAddress, $"extractvalue %sollang.mutable_container {argumentName}, 2");

        RuntimeValue mutableValue;
        if (_program.Types.IsDynamicArray(type))
        {
            var definition = _program.Types.GetDynamicArray(type);
            mutableValue = new RuntimeDynamicInlineArray(
                type, definition.ElementType, "", "", "");
        }
        else if (_program.Types.IsDictionary(type))
        {
            var definition = _program.Types.GetDictionary(type);
            mutableValue = new RuntimeInlineDictionary(
                type, definition.KeyType, definition.ValueType, "", "", "");
        }
        else
        {
            mutableValue = type switch
            {
                BoundType.DynamicIntArray => new RuntimeDynamicIntArray("", "", ""),
                BoundType.IntDictionary => new RuntimeIntDictionary("", "", ""),
                BoundType.Arena => new RuntimeArena("", "", ""),
                _ => throw new SollangException("unsupported mutable borrow input type")
            };
        }
        _locals.Add(name, mutableValue);
        _mutableLocals.Add(name);
        _borrowedMutableLocals.Add(name);
        _mutableContainerSlots.Add(
            name,
            new MutableContainerSlot(pointerAddress, lengthAddress, capacityAddress, StackAllocation: null));
    }

}

