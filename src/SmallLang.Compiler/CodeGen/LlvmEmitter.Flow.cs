using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

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
                    throw new SmallLangException("yield does not accept arguments");
                }

                if (_currentBlockInvocation is null)
                {
                    throw new SmallLangException("yield is only valid inside a block function");
                }

                if (!isLast)
                {
                    throw new SmallLangException("yield must be the final value-flow target");
                }

                EmitYield(current, _currentBlockInvocation);
                return new RuntimeFlowResult(
                    Value: null,
                    Binding: null,
                    Ok: ok);
            }

            if (_program.ResolvedGenericCalls.TryGetValue(target, out var function)
                || TryResolveFunction(target.Path, out function)
                || TryResolveInstanceMethod(current.Type, path, out function))
            {
                if (target.Arguments.Count != 0)
                {
                    throw new SmallLangException($"function value-flow target '{path}' does not accept additional arguments in this slice");
                }

                switch (function.Kind)
                {
                    case BoundFunctionKind.RuntimePrint:
                    case BoundFunctionKind.RuntimePrintLine:
                        if (!isLast)
                        {
                            throw new SmallLangException($"{path} must be the final value-flow target");
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
                            throw new SmallLangException($"{path} must be the final value-flow target");
                        }

                        EmitRuntimeUnitIntrinsic(function, current, path);
                        return new RuntimeFlowResult(
                            Value: null,
                            Binding: null,
                            Ok: _mainOk);
                    case BoundFunctionKind.RuntimeRandomBelow:
                    case BoundFunctionKind.RuntimeClosestInt:
                        current = EmitRuntimeIntIntrinsic(function, current, path);
                        ok = _mainOk;
                        continue;
                    case BoundFunctionKind.RuntimeCloseIntWriter:
                    case BoundFunctionKind.RuntimeCloseIntReader:
                        throw new SmallLangException($"{path} does not accept a flowed input");
                    case BoundFunctionKind.User:
                        current = EmitFlowFunctionCall(function, current, expression.Source);
                        continue;
                    default:
                        throw new SmallLangException($"unsupported runtime function kind '{function.Kind}'");
                }
            }

            throw new SmallLangException($"unknown runtime value-flow target '{path}'");
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
        switch (path)
        {
            case "used" when current is RuntimeArena usedArena:
                if (target.Arguments.Count != 0)
                {
                    throw new SmallLangException("used does not accept arguments");
                }
                result = new RuntimeFlowResult(
                    new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(usedArena.UsedName)),
                    null,
                    _mainOk);
                return true;
            case "alloc" when current is RuntimeArena allocationArena:
                if (!isLast || target.Arguments.Count != 2)
                {
                    throw new SmallLangException("arena alloc must be final and expects byte-count and alignment");
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
                    throw new SmallLangException("arena store must be final and expects offset and UInt8 value");
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
                    throw new SmallLangException("arena load expects one offset");
                }
                result = new RuntimeFlowResult(
                    EmitArenaLoad(loadArena, EmitIntExpression(target.Arguments[0])),
                    null,
                    _mainOk);
                return true;
            case "reset" when current is RuntimeArena resetArena:
                if (!isLast || target.Arguments.Count != 0)
                {
                    throw new SmallLangException("arena reset must be final and takes no arguments");
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
                    throw new SmallLangException("flush must be final and takes no arguments");
                }
                RequireMutableContainerSource(source, "flush");
                var flushOk = NextTemp("mapped_flush_ok");
                EmitCall(flushOk, "i32", "smalllang_mapped_flush", $"ptr {mapped.BasePointerName}, i64 {mapped.MappedLengthName}");
                var flushSucceeded = NextTemp("mapped_flush_succeeded");
                EmitCompare(flushSucceeded, "ne", "i32", flushOk, "0");
                EmitTrapUnless(flushSucceeded, "mapped_flush");
                result = new RuntimeFlowResult(RuntimeUnit.Instance, null, _mainOk);
                return true;
            case "len":
                if (target.Arguments.Count != 0)
                {
                    throw new SmallLangException("len does not accept arguments");
                }

                result = current switch
                {
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
                    RuntimeArguments arguments => new RuntimeFlowResult(
                        new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(arguments.LengthName)), null, _mainOk),
                    _ => result
                };
                return result.Value is not null;
            case "capacity":
                if (target.Arguments.Count != 0)
                {
                    throw new SmallLangException("capacity does not accept arguments");
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
                    throw new SmallLangException("push must be the final value-flow target");
                }

                if (target.Arguments.Count != 1)
                {
                    throw new SmallLangException("push expects exactly one Int argument");
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
                    _ => throw new SmallLangException("push argument does not match array element type")
                };
                StoreMutableContainer(arrayName, pushedArray);
                _locals[arrayName] = pushedArray;
                result = new RuntimeFlowResult(null, null, _mainOk);
                return true;
            case "append":
                if (current is not RuntimeDynamicIntArray appendArray)
                {
                    return false;
                }

                if (!isLast)
                {
                    throw new SmallLangException("append must be bound directly with '=>'");
                }

                RequireMoveContainerSource(source, "append");

                if (target.Arguments.Count != 1)
                {
                    throw new SmallLangException("append expects exactly one Int argument");
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
                        throw new SmallLangException("put must be final and expects key and value arguments");
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
                    throw new SmallLangException("put must be the final value-flow target");
                }

                if (target.Arguments.Count != 2)
                {
                    throw new SmallLangException("put expects key and value Int arguments");
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
                    throw new SmallLangException("updated must be bound directly with '=>'");
                }

                RequireMoveContainerSource(source, "updated");

                if (target.Arguments.Count != 2)
                {
                    throw new SmallLangException("updated expects two Int arguments");
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
            throw new SmallLangException($"{operation} requires a named mutable owner in the current slice");
        }

        if (!_mutableLocals.Contains(name.Name))
        {
            throw new SmallLangException($"{operation} requires a mutable owner binding; use '=> {name.Name.TrimEnd('!')}!'");
        }

        return name.Name;
    }

    private string RequireMoveContainerSource(Expression source, string operation)
    {
        if (source is not NameExpression name)
        {
            throw new SmallLangException($"{operation} requires a named container owner so ownership can move");
        }

        if (!_locals.ContainsKey(name.Name))
        {
            throw new SmallLangException($"{operation} source owner '{name.Name}' is not live");
        }

        return name.Name;
    }

}

