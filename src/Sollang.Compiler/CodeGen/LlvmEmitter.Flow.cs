using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeValue EmitFlowExpressionValue(FlowExpression expression)
    {
        var result = EmitFlowExpression(expression, ok: "true", allowBindingTarget: false);
        return result.Value
            ?? RuntimeUnit.Instance;
    }

    private RuntimeFlowResult EmitFlowExpression(FlowExpression expression, string ok, bool allowBindingTarget)
    {
        if (!_platform.SupportsChildProcesses
            && expression.Targets.Any(target => TryResolveFunction(target.Path, out var function)
                && function.Kind is BoundFunctionKind.RuntimeRunProcess
                    or BoundFunctionKind.RuntimeRunProcessToFile))
        {
            throw new SollangException("child processes are unavailable on the current target");
        }
        if (expression.Targets.Count == 1
            && expression.Targets[0].Arguments.Count == 0
            && TryResolveFunction(expression.Targets[0].Path, out var directFunction)
            && TryGetRuntimePrinterKind(directFunction, out var directPrinterKind))
        {
            ok = EmitPrintFlowSource(expression.Source, ok);
            if (directPrinterKind == BoundFunctionKind.RuntimePrintLine)
            {
                ok = EmitWriteText("\n", ok);
            }

            return new RuntimeFlowResult(
                Value: null,
                Binding: null,
                Ok: ok);
        }

        var current = EmitFlowSource(expression.Source);
        for (var i = 0; i < expression.Targets.Count; i++)
        {
            var target = expression.Targets[i];
            var isLast = i == expression.Targets.Count - 1;
            var path = string.Join('.', target.Path);

            if (TryEmitContainerFlowTarget(expression.Source, target, path, current, isLast, out var containerResult))
            {
                if (containerResult.Value is null)
                {
                    return new RuntimeFlowResult(
                        Value: null,
                        Binding: null,
                        Ok: ok);
                }

                current = containerResult.Value;
                continue;
            }

            if (path == "yield")
            {
                if (target.Arguments.Count != 0)
                {
                    throw new SollangException("yield does not accept arguments");
                }

                if (_currentBlockInvocation is null)
                {
                    throw new SollangException("yield is only valid inside a block function");
                }

                if (!isLast)
                {
                    throw new SollangException("yield must be the final value-flow target");
                }

                var yielded = EmitYield(current, _currentBlockInvocation);
                return new RuntimeFlowResult(
                    Value: yielded is RuntimeUnit ? null : yielded,
                    Binding: null,
                    Ok: ok);
            }

            if (_program.ResolvedGenericCalls.TryGetValue(target, out var function)
                || TryResolveFunction(target.Path, out function)
                || TryResolveInstanceMethod(current.Type, path, out function))
            {
                if (function.Kind != BoundFunctionKind.User && target.Arguments.Count != 0)
                {
                    throw new SollangException($"function value-flow target '{path}' does not accept additional arguments in this slice");
                }
                if (i == 0
                    && function.InputType is { } contextualInput
                    && IsIntegerType(contextualInput)
                    && TryGetIntegerLiteralText(expression.Source, out var integerLiteral))
                {
                    current = new RuntimeInt(contextualInput, integerLiteral);
                }

                switch (function.Kind)
                {
                    case BoundFunctionKind.RuntimePrint:
                    case BoundFunctionKind.RuntimePrintLine:
                        if (!isLast)
                        {
                            throw new SollangException($"{path} must be the final value-flow target");
                        }

                        ok = EmitWriteValue(current, ok);
                        if (function.Kind == BoundFunctionKind.RuntimePrintLine)
                        {
                            ok = EmitWriteText("\n", ok);
                        }

                        return new RuntimeFlowResult(
                            Value: null,
                            Binding: null,
                            Ok: ok);
                    case BoundFunctionKind.RuntimeReadInt:
                        EnsureRuntimeType(current, BoundType.Text, path);
                        current = EmitReadIntPrompt(current);
                        ok = _mainOk;
                        continue;
                    case BoundFunctionKind.RuntimeSeedRandom:
                    case BoundFunctionKind.RuntimeOpenIntWriter:
                    case BoundFunctionKind.RuntimeWriteInt:
                    case BoundFunctionKind.RuntimeOpenIntReader:
                        if (!isLast)
                        {
                            throw new SollangException($"{path} must be the final value-flow target");
                        }

                        EmitRuntimeUnitIntrinsic(function, current, path);
                        return new RuntimeFlowResult(
                            Value: null,
                            Binding: null,
                            Ok: _mainOk);
                    case BoundFunctionKind.RuntimeWriteScalar:
                        if (!isLast)
                        {
                            throw new SollangException($"{path} must be the final value-flow target");
                        }
                        EmitRuntimeWriteScalar(function, current);
                        return new RuntimeFlowResult(
                            Value: null,
                            Binding: null,
                            Ok: _mainOk);
                    case BoundFunctionKind.RuntimeRandomBelow:
                    case BoundFunctionKind.RuntimeClosestInt:
                        current = EmitRuntimeIntIntrinsic(function, current, path);
                        ok = _mainOk;
                        continue;
                    case BoundFunctionKind.RuntimeLimitParallelWorkers:
                        current = EmitRuntimeLimitParallelWorkersIntrinsic(current, path);
                        continue;
                    case BoundFunctionKind.RuntimeCloseIntWriter:
                    case BoundFunctionKind.RuntimeCloseIntReader:
                        throw new SollangException($"{path} does not accept a flowed input");
                    case BoundFunctionKind.RuntimeRunProcess:
                        current = current is RuntimeDynamicInlineArray argv
                            ? EmitRuntimeRunProcessIntrinsic(function, argv)
                            : throw new SollangException($"{path} expects a dynamic Text argv array");
                        continue;
                    case BoundFunctionKind.RuntimeRunProcessToFile:
                        current = current is RuntimeStruct request
                            ? EmitRuntimeRunProcessToFileIntrinsic(function, request)
                            : throw new SollangException($"{path} expects a RunToFileRequest");
                        continue;
                    case BoundFunctionKind.RuntimeExitProcess:
                        if (!isLast)
                        {
                            throw new SollangException($"{path} must be the final value-flow target");
                        }
                        EmitRuntimeExitProcessIntrinsic(current, path);
                        return new RuntimeFlowResult(
                            Value: null,
                            Binding: null,
                            Ok: _mainOk);
                    case BoundFunctionKind.RuntimeSyncFile:
                        current = current is RuntimeStruct syncWriter
                            ? EmitRuntimeSyncFile(syncWriter)
                            : throw new SollangException($"{path} expects a FileWriter");
                        continue;
                    case BoundFunctionKind.RuntimeAtomicReplaceFile:
                        current = current is RuntimeStruct replaceRequest
                            ? EmitRuntimeAtomicReplaceFile(function, replaceRequest)
                            : throw new SollangException($"{path} expects an AtomicReplaceRequest");
                        continue;
                    case BoundFunctionKind.RuntimeBorrowSourceText:
                        current = EmitBorrowSourceText(current);
                        continue;
                    case BoundFunctionKind.RuntimeMapSourceText:
                        current = EmitMapSourceText(current);
                        continue;
                    case BoundFunctionKind.RuntimeMapSourcePath:
                        current = EmitMapSourcePath(current);
                        continue;
                    case BoundFunctionKind.RuntimeOpenFile:
                    case BoundFunctionKind.RuntimeOpenWriteFile:
                        current = EmitRuntimeOpenFile(function, current);
                        continue;
                    case BoundFunctionKind.RuntimeReadDirectory:
                        current = EmitRuntimeReadDirectory(function, current);
                        continue;
                    case BoundFunctionKind.RuntimePathQuery:
                        current = EmitRuntimePathQuery(function, current);
                        continue;
                    case BoundFunctionKind.RuntimeOpenFileAsync:
                    case BoundFunctionKind.RuntimeOpenWriteFileAsync:
                        current = EmitRuntimeOpenFileAsync(function, current);
                        continue;
                    case BoundFunctionKind.RuntimeSleep:
                        current = EmitRuntimeSleepIntrinsic(function, current, path);
                        continue;
                    case BoundFunctionKind.User:
                        current = EmitFlowFunctionCall(function, current, expression.Source, target.Arguments);
                        continue;
                    default:
                        throw new SollangException($"unsupported runtime function kind '{function.Kind}'");
                }
            }

            throw new SollangException($"unknown runtime value-flow target '{path}'");
        }

        return new RuntimeFlowResult(
            Value: current,
            Binding: null,
            Ok: ok);
    }

    private bool TryEmitContainerFlowTarget(
        Expression source,
        FlowTarget target,
        string path,
        RuntimeValue current,
        bool isLast,
        out RuntimeFlowResult result)
    {
        result = new RuntimeFlowResult(null, null, _mainOk);

        if (current is RuntimeStruct writer
            && _program.Types.IsStruct(writer.Type)
            && string.Equals(
                _program.Types.GetStruct(writer.Type).Name,
                "sys.file.FileWriter",
                StringComparison.Ordinal)
            && path is "writeAt" or "writeAtAsync" or "syncAsync")
        {
            if (!_program.ResolvedGenericCalls.TryGetValue(target, out var writeFunction))
            {
                throw new SollangException($"unresolved file operation '{path}'");
            }
            RuntimeValue value = path switch
            {
                "writeAtAsync" => EmitRuntimeWriteScalarAtAsync(
                    writeFunction,
                    writer,
                    target.Arguments[0],
                    target.Arguments[1]),
                "syncAsync" => EmitRuntimeSyncFileAsync(writeFunction, writer),
                _ => EmitRuntimeWriteScalarAt(
                    writeFunction,
                    writer,
                    target.Arguments[0],
                    target.Arguments[1])
            };
            result = new RuntimeFlowResult(value, null, _mainOk);
            return true;
        }

        if (current is RuntimeStruct file
            && _program.Types.IsStruct(file.Type)
            && string.Equals(
                _program.Types.GetStruct(file.Type).Name,
                "sys.file.File",
                StringComparison.Ordinal)
            && path is "readAt" or "readAtAsync")
        {
            if (!_program.ResolvedGenericCalls.TryGetValue(target, out var fileFunction))
            {
                throw new SollangException($"unresolved generic file operation '{path}'");
            }
            var value = path == "readAtAsync"
                ? (RuntimeValue)EmitRuntimeReadScalarAsync(
                    fileFunction,
                    file,
                    target.Arguments[0])
                : EmitRuntimeReadScalar(
                    fileFunction,
                    file: file,
                    offsetExpression: target.Arguments[0]);
            result = new RuntimeFlowResult(value, null, _mainOk);
            return true;
        }

        switch (path)
        {
            case "await" when current is RuntimeTask task:
                if (target.Arguments.Count != 0)
                {
                    throw new SollangException("await does not accept arguments");
                }
                result = new RuntimeFlowResult(EmitAwaitTask(task), null, _mainOk);
                return true;
            case "cancel" when current is RuntimeTask task:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw new SollangException("cancel must be final and takes no arguments");
                }
                EmitCancelTask(task);
                result = new RuntimeFlowResult(RuntimeUnit.Instance, null, _mainOk);
                return true;
            case "used" when current is RuntimeArena usedArena:
                if (target.Arguments.Count != 0)
                {
                    throw new SollangException("used does not accept arguments");
                }
                result = new RuntimeFlowResult(
                    new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(usedArena.UsedName)),
                    null,
                    _mainOk);
                return true;
            case "alloc" when current is RuntimeArena allocationArena:
                if (!isLast || target.Arguments.Count != 2)
                {
                    throw new SollangException("arena alloc must be final and expects byte-count and alignment");
                }
                var arenaName = RequireMutableContainerSource(source, "alloc");
                var bytes = EmitIntExpression(target.Arguments[0]);
                var alignment = EmitIntExpression(target.Arguments[1]);
                var allocation = EmitArenaAllocate(allocationArena, bytes, alignment);
                StoreMutableContainer(arenaName, allocation.Arena);
                _locals[arenaName] = allocation.Arena;
                result = new RuntimeFlowResult(allocation.Offset, null, _mainOk);
                return true;
            case "store" when current is RuntimeArena storeArena:
                if (!isLast || target.Arguments.Count != 2)
                {
                    throw new SollangException("arena store must be final and expects offset and UInt8 value");
                }
                RequireMutableContainerSource(source, "store");
                EmitArenaStore(
                    storeArena,
                    EmitIntExpression(target.Arguments[0]),
                    EmitIntExpression(target.Arguments[1]));
                result = new RuntimeFlowResult(RuntimeUnit.Instance, null, _mainOk);
                return true;
            case "load" when current is RuntimeArena loadArena:
                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("arena load expects one offset");
                }
                result = new RuntimeFlowResult(
                    EmitArenaLoad(loadArena, EmitIntExpression(target.Arguments[0])),
                    null,
                    _mainOk);
                return true;
            case "reset" when current is RuntimeArena resetArena:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw new SollangException("arena reset must be final and takes no arguments");
                }
                var resetName = RequireMutableContainerSource(source, "reset");
                var reset = resetArena with { UsedName = "0" };
                StoreMutableContainer(resetName, reset);
                _locals[resetName] = reset;
                result = new RuntimeFlowResult(RuntimeUnit.Instance, null, _mainOk);
                return true;
            case "flush" when current is RuntimeMappedBytes mapped:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw new SollangException("flush must be final and takes no arguments");
                }
                RequireMutableContainerSource(source, "flush");
                var flushOk = NextTemp("mapped_flush_ok");
                EmitCall(flushOk, "i32", "sollang_mapped_flush", $"ptr {mapped.BasePointerName}, i64 {mapped.MappedLengthName}");
                var flushSucceeded = NextTemp("mapped_flush_succeeded");
                EmitCompare(flushSucceeded, "ne", "i32", flushOk, "0");
                EmitTrapUnless(flushSucceeded, "mapped_flush");
                result = new RuntimeFlowResult(RuntimeUnit.Instance, null, _mainOk);
                return true;
            case "len":
                if (target.Arguments.Count != 0)
                {
                    throw new SollangException("len does not accept arguments");
                }

                result = current switch
                {
                    RuntimeText text => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(text.LengthName)), null, _mainOk),
                    RuntimeIntSlice slice => new RuntimeFlowResult(EmitSizeAsInt(slice.LengthName, "slice_len_value"), null, _mainOk),
                    RuntimeStaticIntArray staticArray => new RuntimeFlowResult(EmitSizeAsInt(staticArray.LengthName, "array_len_value"), null, _mainOk),
                    RuntimeStaticTextArray staticArray => new RuntimeFlowResult(EmitSizeAsInt(staticArray.LengthName, "array_len_value"), null, _mainOk),
                    RuntimeStaticInlineArray staticArray => new RuntimeFlowResult(EmitSizeAsInt(staticArray.LengthName, "array_len_value"), null, _mainOk),
                    RuntimeDynamicIntArray dynamicArray => new RuntimeFlowResult(EmitSizeAsInt(dynamicArray.LengthName, "array_len_value"), null, _mainOk),
                    RuntimeDynamicInlineArray dynamicArray => new RuntimeFlowResult(EmitSizeAsInt(dynamicArray.LengthName, "array_len_value"), null, _mainOk),
                    RuntimeIntDictionaryView dictionaryView => new RuntimeFlowResult(EmitSizeAsInt(dictionaryView.LengthName, "dict_len_value"), null, _mainOk),
                    RuntimeIntDictionary intDictionary => new RuntimeFlowResult(EmitSizeAsInt(intDictionary.LengthName, "dict_len_value"), null, _mainOk),
                    RuntimeInlineDictionary inlineMap => new RuntimeFlowResult(EmitSizeAsInt(inlineMap.LengthName, "dict_len_value"), null, _mainOk),
                    RuntimeMappedBytes mapped => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(mapped.LengthName)), null, _mainOk),
                    RuntimeSourceText sourceText => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(sourceText.LengthName)), null, _mainOk),
                    RuntimeArguments arguments => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(arguments.LengthName)), null, _mainOk),
                    _ => result
                };
                return result.Value is not null;
            case "byte" when current is RuntimeSourceText sourceText:
                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("SourceText byte expects one index");
                }
                var sourceByteIndex = EmitMapInteger(target.Arguments[0], BoundType.UIntSize, "source_text_byte_index");
                var sourceByteInBounds = NextTemp("source_text_byte_in_bounds");
                EmitCompare(sourceByteInBounds, "ult", "i64", sourceByteIndex, sourceText.LengthName);
                EmitTrapUnless(sourceByteInBounds, "source_text_byte_bounds");
                var sourceBytePointer = NextTemp("source_text_byte_ptr");
                EmitAssign(sourceBytePointer, $"getelementptr i8, ptr {sourceText.DataPointerName}, i64 {sourceByteIndex}");
                var sourceByteValue = NextTemp("source_text_byte");
                EmitLoad(sourceByteValue, "i8", sourceBytePointer, 1);
                result = new RuntimeFlowResult(new RuntimeInt(BoundType.UInt8, sourceByteValue), null, _mainOk);
                return true;
            case "slice" when current is RuntimeSourceText sourceText:
                if (target.Arguments.Count != 2)
                {
                    throw new SollangException("SourceText slice expects start and byte length");
                }
                var sourceSliceStart = EmitMapInteger(target.Arguments[0], BoundType.UIntSize, "source_text_slice_start");
                var sourceSliceLength = EmitMapInteger(target.Arguments[1], BoundType.UIntSize, "source_text_slice_length");
                result = new RuntimeFlowResult(
                    EmitTextSlice(new RuntimeText(sourceText.DataPointerName, sourceText.LengthName), sourceSliceStart, sourceSliceLength),
                    null,
                    _mainOk);
                return true;
            case "byte" when current is RuntimeText text:
                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("Text byte expects one index");
                }
                var byteIndex = EmitMapInteger(target.Arguments[0], BoundType.UIntSize, "text_byte_index");
                var byteInBounds = NextTemp("text_byte_in_bounds");
                EmitCompare(byteInBounds, "ult", "i64", byteIndex, text.LengthName);
                EmitTrapUnless(byteInBounds, "text_byte_bounds");
                var bytePointer = NextTemp("text_byte_ptr");
                EmitAssign(bytePointer, $"getelementptr i8, ptr {text.PointerName}, i64 {byteIndex}");
                var byteValue = NextTemp("text_byte");
                EmitLoad(byteValue, "i8", bytePointer, 1);
                result = new RuntimeFlowResult(new RuntimeInt(BoundType.UInt8, byteValue), null, _mainOk);
                return true;
            case "slice" when current is RuntimeText text:
                if (target.Arguments.Count != 2)
                {
                    throw new SollangException("Text slice expects start and byte length");
                }
                var sliceStart = EmitMapInteger(target.Arguments[0], BoundType.UIntSize, "text_slice_start");
                var sliceLength = EmitMapInteger(target.Arguments[1], BoundType.UIntSize, "text_slice_length");
                result = new RuntimeFlowResult(EmitTextSlice(text, sliceStart, sliceLength), null, _mainOk);
                return true;
            case "capacity":
                if (target.Arguments.Count != 0)
                {
                    throw new SollangException("capacity does not accept arguments");
                }

                result = current switch
                {
                    RuntimeDynamicIntArray dynamicArray => new RuntimeFlowResult(EmitSizeAsInt(dynamicArray.CapacityName, "array_capacity_value"), null, _mainOk),
                    RuntimeDynamicInlineArray dynamicArray => new RuntimeFlowResult(EmitSizeAsInt(dynamicArray.CapacityName, "array_capacity_value"), null, _mainOk),
                    RuntimeIntDictionaryView dictionaryView => new RuntimeFlowResult(EmitSizeAsInt(dictionaryView.CapacityName, "dict_capacity_value"), null, _mainOk),
                    RuntimeIntDictionary intDictionary => new RuntimeFlowResult(EmitSizeAsInt(intDictionary.CapacityName, "dict_capacity_value"), null, _mainOk),
                    RuntimeInlineDictionary inlineMap => new RuntimeFlowResult(EmitSizeAsInt(inlineMap.CapacityName, "dict_capacity_value"), null, _mainOk),
                    RuntimeArena arena => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(arena.CapacityName)), null, _mainOk),
                    _ => result
                };
                return result.Value is not null;
            case "push":
                if (current is not (RuntimeDynamicIntArray or RuntimeDynamicInlineArray))
                {
                    return false;
                }

                if (!isLast)
                {
                    throw new SollangException("push must be the final value-flow target");
                }

                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("push expects exactly one Int argument");
                }

                var arrayName = RequireMutableContainerSource(source, "push");
                var pushed = current is RuntimeDynamicInlineArray contextualArray
                    && target.Arguments[0] is DictionaryLiteralExpression contextualElement
                    && _program.Types.IsStruct(contextualArray.ElementType)
                        ? EmitContextualStructLiteral(contextualElement, contextualArray.ElementType)
                        : EmitExpression(target.Arguments[0]);
                var pushedArray = current switch
                {
                    RuntimeDynamicIntArray array when pushed is RuntimeInt integer =>
                        (RuntimeValue)EmitDynamicArrayPush(array, integer.ValueName),
                    RuntimeDynamicInlineArray array => EmitDynamicInlineArrayPush(array, pushed),
                    _ => throw new SollangException("push argument does not match array element type")
                };
                StoreMutableContainer(arrayName, pushedArray);
                _locals[arrayName] = pushedArray;
                if (current is RuntimeDynamicInlineArray ownedElementArray
                    && _program.Types.ContainsOwnedStorage(ownedElementArray.ElementType))
                {
                    RemoveOwnedLiteralSources(target.Arguments[0], ownedElementArray.ElementType);
                }
                result = new RuntimeFlowResult(null, null, _mainOk);
                return true;
            case "take":
            {
                if (current is not (RuntimeDynamicIntArray
                    or RuntimeDynamicInlineArray
                    or RuntimeIntDictionary
                    or RuntimeInlineDictionary))
                {
                    return false;
                }

                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("take expects exactly one index or key argument");
                }

                var takeName = RequireMutableContainerSource(source, "take");
                RuntimeValue takenValue;
                RuntimeValue remainingContainer;
                switch (current)
                {
                    case RuntimeDynamicIntArray intArray:
                    {
                        var index = EmitIntAsSize(EmitIntExpression(target.Arguments[0]), "take_index");
                        var taken = EmitDynamicArrayTake(intArray, index);
                        takenValue = taken.Value;
                        remainingContainer = taken.Array;
                        break;
                    }
                    case RuntimeDynamicInlineArray inlineArray:
                    {
                        var index = EmitIntAsSize(EmitIntExpression(target.Arguments[0]), "take_index");
                        var taken = EmitDynamicInlineArrayTake(inlineArray, index);
                        takenValue = taken.Value;
                        remainingContainer = taken.Array;
                        break;
                    }
                    case RuntimeIntDictionary intDictionary:
                    {
                        var takeIntKey = EmitIntExpression(target.Arguments[0]);
                        var taken = EmitDictionaryTake(intDictionary, takeIntKey.ValueName);
                        takenValue = taken.Value;
                        remainingContainer = taken.Dictionary;
                        break;
                    }
                    case RuntimeInlineDictionary takeInlineDictionary:
                    {
                        var takeInlineKey = target.Arguments[0] is DictionaryLiteralExpression contextualTakeKey
                            && _program.Types.IsStruct(takeInlineDictionary.KeyType)
                                ? EmitContextualStructLiteral(contextualTakeKey, takeInlineDictionary.KeyType)
                                : EmitExpression(target.Arguments[0]);
                        var taken = EmitInlineDictionaryTake(takeInlineDictionary, takeInlineKey);
                        takenValue = taken.Value;
                        remainingContainer = taken.Dictionary;
                        break;
                    }
                    default:
                        throw new SollangException("take expects a dynamic array or dictionary owner");
                }

                StoreMutableContainer(takeName, remainingContainer);
                _locals[takeName] = remainingContainer;
                result = new RuntimeFlowResult(takenValue, null, _mainOk);
                return true;
            }
            case "append":
                if (current is not RuntimeDynamicIntArray appendArray)
                {
                    return false;
                }

                if (!isLast)
                {
                    throw new SollangException("append must be bound directly with '=>'");
                }

                RequireMoveContainerSource(source, "append");

                if (target.Arguments.Count != 1)
                {
                    throw new SollangException("append expects exactly one Int argument");
                }

                var appended = EmitIntExpression(target.Arguments[0]);
                result = new RuntimeFlowResult(
                    EmitDynamicArrayAppendMove(appendArray, appended.ValueName),
                    null,
                    _mainOk);
                return true;
            case "put":
                if (current is RuntimeInlineDictionary inlineDictionary)
                {
                    if (!isLast || target.Arguments.Count != 2)
                    {
                        throw new SollangException("put must be final and expects key and value arguments");
                    }
                    var inlineName = RequireMutableContainerSource(source, "put");
                    var inlineKey = target.Arguments[0] is DictionaryLiteralExpression contextualKey
                        && _program.Types.IsStruct(inlineDictionary.KeyType)
                            ? EmitContextualStructLiteral(contextualKey, inlineDictionary.KeyType)
                            : EmitExpression(target.Arguments[0]);
                    var inlineValue = target.Arguments[1] is DictionaryLiteralExpression contextualValue
                        && _program.Types.IsStruct(inlineDictionary.ValueType)
                            ? EmitContextualStructLiteral(contextualValue, inlineDictionary.ValueType)
                            : EmitExpression(target.Arguments[1]);
                    var inlineUpdated = EmitInlineDictionaryPut(inlineDictionary, inlineKey, inlineValue);
                    RemoveOwnedLiteralSources(target.Arguments[0], inlineDictionary.KeyType);
                    RemoveOwnedLiteralSources(target.Arguments[1], inlineDictionary.ValueType);
                    StoreMutableContainer(inlineName, inlineUpdated);
                    _locals[inlineName] = inlineUpdated;
                    result = new RuntimeFlowResult(null, null, _mainOk);
                    return true;
                }
                if (current is not RuntimeIntDictionary dictionary)
                {
                    return false;
                }

                if (!isLast)
                {
                    throw new SollangException("put must be the final value-flow target");
                }

                if (target.Arguments.Count != 2)
                {
                    throw new SollangException("put expects key and value Int arguments");
                }

                var dictionaryName = RequireMutableContainerSource(source, "put");
                var key = EmitIntExpression(target.Arguments[0]);
                var value = EmitIntExpression(target.Arguments[1]);
                var updatedDictionary = EmitDictionaryPut(dictionary, key.ValueName, value.ValueName);
                StoreMutableContainer(dictionaryName, updatedDictionary);
                _locals[dictionaryName] = updatedDictionary;
                result = new RuntimeFlowResult(null, null, _mainOk);
                return true;
            case "updated":
                if (current is not (RuntimeDynamicIntArray or RuntimeIntDictionary))
                {
                    return false;
                }

                if (!isLast)
                {
                    throw new SollangException("updated must be bound directly with '=>'");
                }

                RequireMoveContainerSource(source, "updated");

                if (target.Arguments.Count != 2)
                {
                    throw new SollangException("updated expects two Int arguments");
                }

                var updateKeyOrIndex = EmitIntExpression(target.Arguments[0]);
                var updateValue = EmitIntExpression(target.Arguments[1]);
                result = current switch
                {
                    RuntimeDynamicIntArray updateArray => new RuntimeFlowResult(
                        EmitDynamicArrayUpdatedMove(updateArray, EmitIntAsSize(updateKeyOrIndex, "updated_index"), updateValue.ValueName),
                        null,
                        _mainOk),
                    RuntimeIntDictionary updateDictionary => new RuntimeFlowResult(
                        EmitDictionaryUpdatedMove(updateDictionary, updateKeyOrIndex.ValueName, updateValue.ValueName),
                        null,
                        _mainOk),
                    _ => result
                };
                return result.Value is not null;
            default:
                return false;
        }
    }

    private string RequireMutableContainerSource(Expression source, string operation)
    {
        if (source is not NameExpression name)
        {
            throw new SollangException($"{operation} requires a named mutable owner in the current slice");
        }

        if (!_mutableLocals.Contains(name.Name))
        {
            throw new SollangException($"{operation} requires a mutable owner binding; use '=> {name.Name.TrimEnd('!')}!'");
        }

        return name.Name;
    }

    private string RequireMoveContainerSource(Expression source, string operation)
    {
        if (source is not NameExpression name)
        {
            throw new SollangException($"{operation} requires a named container owner so ownership can move");
        }

        if (!_locals.ContainsKey(name.Name))
        {
            throw new SollangException($"{operation} source owner '{name.Name}' is not live");
        }

        return name.Name;
    }

}

