using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitMain()
    {
        ClearLocalState();
        _currentFunctions = _program.Functions;
        _currentStackFramePlan = _program.MainStackFrame;
        _mainOk = "true";
        _currentBlockLabel = "entry";
        EmitFunctionLine($"define dso_local i32 @{_platform.EntryPointName}({_platform.EntryPointParameters}) local_unnamed_addr {{");
        EmitLabel("entry");
        EmitStackFrameAllocations();
        EmitAlloca("%written", "i32", 4);
        EmitAlloca("%read", "i32", 4);
        EmitAlloca("%ok_state", "i1", 1);
        EmitStore("i1", "true", "%ok_state", 1);
        EmitPlatformFunctionBlock(_platform.EmitEntryHandles);
        if (_usesProcessArguments)
        {
            EmitPlatformFunctionBlock(_platform.EmitProcessEntry);
        }

        EmitStatements(_program.MainStatements);

        DropOwnedLocals();
        if (_usesProcessArguments)
        {
            EmitPlatformFunctionBlock(_platform.EmitExitCleanup);
        }

        var finalOk = NextTemp("final_ok");
        EmitLoad(finalOk, "i1", "%ok_state", 1);
        var exit = NextTemp("exit");
        EmitSelect(exit, finalOk, "i32 0", "i32 1");
        EmitRet("i32", exit);
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BindingStatement binding:
                var movedSourceName = GetMoveConsumingContainerSourceName(binding.Value);
                var value = EmitExpression(binding.Value);
                if (RequiresHeapAllocation(value) && !_platform.SupportsHeapAllocation)
                {
                    throw new SmallLangException("dynamic arrays and dictionaries require heap allocation; wasm32-browser does not support them yet");
                }

                if (movedSourceName is not null)
                {
                    RemoveLocal(movedSourceName);
                }

                _mutableContainerSlots.Remove(binding.Name);
                _mutableStructSlots.Remove(binding.Name);
                _borrowedMutableLocals.Remove(binding.Name);
                _borrowedOwnedLocals.Remove(binding.Name);
                _locals.Add(binding.Name, value);
                if (binding.IsMutable)
                {
                    _mutableLocals.Add(binding.Name);
                    if (value is RuntimeStruct structure)
                    {
                        CreateMutableStructSlot(binding.Name, structure);
                    }
                    else
                    {
                        CreateMutableContainerSlot(binding, value);
                    }
                }
                break;
            case IndexAssignmentStatement assignment:
                EmitIndexAssignmentStatement(assignment);
                break;
            case FieldAssignmentStatement assignment:
                EmitFieldAssignmentStatement(assignment);
                break;
            case BlockFunctionCallStatement blockFunctionCall:
                EmitBlockFunctionCall(blockFunctionCall);
                break;
            case ExpressionStatement expressionStatement:
                _mainOk = EmitExpressionStatement(expressionStatement.Expression, _mainOk);
                break;
            default:
                throw new SmallLangException($"unsupported runtime statement {statement.GetType().Name}");
        }

        EmitStackLifetimeEndsAfter(statement);
    }

    private void CreateMutableStructSlot(string name, RuntimeStruct value)
    {
        var pointer = NextTemp("mutable_struct_slot");
        var llvmType = LlvmStructType(value.Type);
        EmitAlloca(pointer, llvmType, 8);
        EmitStore(llvmType, value.ValueName, pointer, 8);
        _mutableStructSlots.Add(name, pointer);
    }

    private void EmitFieldAssignmentStatement(FieldAssignmentStatement assignment)
    {
        if (!_mutableStructSlots.TryGetValue(assignment.Name, out var pointer))
        {
            throw new SmallLangException(
                $"field assignment requires a mutable struct owner; use '{assignment.Name.TrimEnd('!')}!'");
        }

        var type = _locals[assignment.Name].Type;
        var definition = _program.Types.GetStruct(type);
        var field = definition.Fields.First(candidate => candidate.Name == assignment.FieldName);
        var value = EmitExpression(assignment.Value);
        EnsureRuntimeType(value, field.Type, $"{definition.Name}.{field.Name}");
        var fieldAddress = NextTemp("field_addr");
        EmitAssign(
            fieldAddress,
            $"getelementptr inbounds {LlvmStructType(type)}, ptr {pointer}, i32 0, i32 {field.Index.ToString(CultureInfo.InvariantCulture)}");
        var materialized = MaterializeAggregateValue(value);
        EmitStore(materialized.TypeName, materialized.ValueName, fieldAddress, RuntimeAlignment(field.Type));
    }

    private void EmitIndexAssignmentStatement(IndexAssignmentStatement assignment)
    {
        if (!_mutableLocals.Contains(assignment.Name))
        {
            throw new SmallLangException($"indexed assignment requires a mutable owner binding; use '=> {assignment.Name.TrimEnd('!')}!'");
        }

        var target = ResolveLocal(assignment.Name);
        if (target is RuntimeMappedBytes mapped)
        {
            var mappedValue = EmitIntExpression(assignment.Value);
            EnsureRuntimeType(mappedValue, BoundType.UInt8, "mapped byte assignment");
            EmitMappedStore(mapped, assignment.Index, mappedValue);
            return;
        }
        var index = EmitIntExpression(assignment.Index);
        var value = EmitIntExpression(assignment.Value);
        switch (target)
        {
            case RuntimeStaticIntArray array:
                EmitStaticArrayAssign(array, EmitIntAsSize(index, "assignment_index"), value.ValueName);
                return;
            case RuntimeDynamicIntArray array:
                EmitDynamicArrayAssign(array, EmitIntAsSize(index, "assignment_index"), value.ValueName);
                return;
            case RuntimeIntDictionary dictionary:
                EmitDictionaryAssignExisting(dictionary, index.ValueName, value.ValueName);
                return;
            default:
                throw new SmallLangException("indexed assignment expects an array or dictionary owner");
        }
    }

    private void EmitBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var target = string.Join('.', statement.Target);
        switch (target)
        {
            case "each":
                if (statement.Source is RangeExpression range)
                {
                    EmitEachBlockFunctionCall(statement, range);
                    return;
                }

                EmitArrayEachBlockFunctionCall(statement);
                return;
            case "eachKey":
                EmitDictionaryEachBlockFunctionCall(statement, bindKey: true);
                return;
            case "eachValue":
                EmitDictionaryEachBlockFunctionCall(statement, bindKey: false);
                return;
            case "repeat":
                EmitRepeatBlockFunctionCall(statement);
                return;
            default:
                if (TryResolveFunction(statement.Target, out var function)
                    && function.Kind == BoundFunctionKind.UserBlock)
                {
                    EmitUserBlockFunctionCall(statement, function);
                    return;
                }

                throw new SmallLangException($"unknown block function '{target}'");
        }
    }

    private void EmitEachBlockFunctionCall(BlockFunctionCallStatement statement, RangeExpression range)
    {
        var start = EmitIntExpression(range.Start);
        var end = EmitIntExpression(range.End);
        var bodyLabel = NextLabel("each_body");
        var continueLabel = NextLabel("each_continue");
        var endLabel = NextLabel("each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("each_next");
        var initialDone = NextTemp("each_done");

        EmitCompare(initialDone, "sgt", "i32", start.ValueName, end.ValueName);
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(statement.ItemName);
        EmitPhi(item, "i32", (start.ValueName, entryLabel), (next, continueLabel));

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = new RuntimeInt(item);
            EmitStatements(statement.Body);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName: null);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i32", item, "1");
        var done = NextTemp("each_done");
        EmitCompare(done, "sgt", "i32", next, end.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitArrayEachBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var source = EmitExpression(statement.Source);
        if (source is RuntimeText text)
        {
            EmitTextEachBlockFunctionCall(statement, text);
            return;
        }
        var length = source switch
        {
            RuntimeIntSlice slice => slice.LengthName,
            RuntimeStaticIntArray array => array.LengthName,
            RuntimeStaticTextArray array => array.LengthName,
            RuntimeStaticInlineArray array => array.LengthName,
            RuntimeDynamicIntArray array => array.LengthName,
            RuntimeDynamicInlineArray array => array.LengthName,
            RuntimeMappedBytes mapped => mapped.LengthName,
            RuntimeArguments arguments => arguments.LengthName,
            _ => throw new SmallLangException("each expects a range, Text, or array input")
        };

        var bodyLabel = NextLabel("array_each_body");
        var continueLabel = NextLabel("array_each_continue");
        var endLabel = NextLabel("array_each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("array_each_next");
        var initialDone = NextTemp("array_each_done");

        EmitCompare(initialDone, "eq", "i64", length, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("array_each_i");
        EmitPhi(index, "i64", ("0", entryLabel), (next, continueLabel));

        RuntimeValue item = source switch
        {
            RuntimeIntSlice slice => EmitIntSliceLoad(slice, index),
            RuntimeStaticIntArray array => EmitStaticArrayLoad(array, index),
            RuntimeStaticTextArray array => EmitStaticTextArrayLoad(array, index),
            RuntimeStaticInlineArray array => EmitStaticInlineArrayLoad(array, index),
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, index),
            RuntimeDynamicInlineArray array => EmitDynamicInlineArrayLoad(array, index),
            RuntimeMappedBytes mapped => EmitMappedLoad(mapped, index),
            RuntimeArguments => EmitArgumentLoad(index),
            _ => throw new SmallLangException("each expects a range, Text, or array input")
        };

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            if (_program.Types.ContainsOwnedStorage(item.Type))
            {
                _borrowedOwnedLocals.Add(statement.ItemName);
            }
            EmitStatements(statement.Body);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName: null);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", index, "1");
        var done = NextTemp("array_each_done");
        EmitCompare(done, "eq", "i64", next, length);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitUserBlockFunctionCall(BlockFunctionCallStatement statement, BoundFunction function)
    {
        if (function.InputType is null || function.BlockInputType is null)
        {
            throw new SmallLangException($"block function '{function.Name}' is not callable");
        }

        var argument = EmitExpression(statement.Source);
        EnsureRuntimeType(argument, function.InputType.Value, function.Name);

        var callerLocals = CaptureLocals();
        var callerFunctions = _currentFunctions;
        var previousInvocation = _currentBlockInvocation;
        var previousFunctions = _currentFunctions;
        var blockLocals = new LocalScope(
            new Dictionary<string, RuntimeValue>(StringComparer.Ordinal)
            {
                [function.InputName ?? "it"] = argument
            },
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, MutableContainerSlot>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        _currentBlockInvocation = new RuntimeBlockInvocation(
            statement.ItemName,
            statement.Body,
            callerLocals,
            callerFunctions);
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        RestoreLocals(blockLocals);
        try
        {
            EmitStatements(function.BlockBody);
            DropOwnedLocalsCreatedSince(blockLocals, transferredOwnerName: null);
        }
        finally
        {
            _currentBlockInvocation = previousInvocation;
            _currentFunctions = previousFunctions;
            RestoreLocals(callerLocals);
        }
    }

    private void EmitDictionaryEachBlockFunctionCall(
        BlockFunctionCallStatement statement,
        bool bindKey)
    {
        var sourceValue = EmitExpression(statement.Source);
        RuntimeValue source = sourceValue is RuntimeIntDictionaryView view
            ? new RuntimeIntDictionary(view.PointerName, view.LengthName, view.CapacityName)
            : sourceValue;
        var capacity = source switch
        {
            RuntimeIntDictionary dictionary => dictionary.CapacityName,
            RuntimeInlineDictionary dictionary => dictionary.CapacityName,
            _ => throw new SmallLangException($"{(bindKey ? "eachKey" : "eachValue")} expects a dictionary input")
        };

        var loopLabel = NextLabel("dictionary_each");
        var bodyLabel = NextLabel("dictionary_each_body");
        var continueLabel = NextLabel("dictionary_each_continue");
        var endLabel = NextLabel("dictionary_each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("dictionary_each_next");
        var empty = NextTemp("dictionary_each_empty");
        EmitCompare(empty, "eq", "i64", capacity, "0");
        EmitConditionalBranch(empty, endLabel, loopLabel);
        EmitFunctionLine();

        EmitLabel(loopLabel);
        _currentBlockLabel = loopLabel;
        var slot = NextTemp("dictionary_each_slot");
        EmitPhi(slot, "i64", ("0", entryLabel), (next, continueLabel));
        var control = source switch
        {
            RuntimeIntDictionary dictionary => LoadDictionaryControl(dictionary, slot),
            RuntimeInlineDictionary dictionary => LoadInlineDictionaryControl(dictionary, slot),
            _ => throw new SmallLangException("dictionary iterator source was not lowered")
        };
        var occupied = NextTemp("dictionary_each_occupied");
        EmitCompare(occupied, "ne", "i8", control, "0");
        EmitConditionalBranch(occupied, bodyLabel, continueLabel);
        EmitFunctionLine();

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        RuntimeValue item;
        if (source is RuntimeIntDictionary intDictionary)
        {
            item = bindKey
                ? LoadDictionaryKey(intDictionary, slot)
                : LoadDictionaryValue(intDictionary, slot);
        }
        else if (source is RuntimeInlineDictionary inlineDictionary)
        {
            var definition = _program.Types.GetDictionary(inlineDictionary.DictionaryType);
            item = bindKey
                ? LoadInlineDictionaryField(inlineDictionary, slot, definition.KeyType, 0,
                    definition.KeyAlignment, "dictionary_each_key")
                : LoadInlineDictionaryField(inlineDictionary, slot, definition.ValueType,
                    definition.ValueOffset, definition.ValueAlignment, "dictionary_each_value");
        }
        else
        {
            throw new SmallLangException("dictionary iterator source was not lowered");
        }

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            if (_program.Types.ContainsOwnedStorage(item.Type))
            {
                _borrowedOwnedLocals.Add(statement.ItemName);
            }
            EmitStatements(statement.Body);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName: null);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
        EmitBranch(continueLabel);
        EmitFunctionLine();

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", slot, "1");
        var done = NextTemp("dictionary_each_done");
        EmitCompare(done, "eq", "i64", next, capacity);
        EmitConditionalBranch(done, endLabel, loopLabel);
        EmitFunctionLine();
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitTextEachBlockFunctionCall(BlockFunctionCallStatement statement, RuntimeText text)
    {
        var bodyLabel = NextLabel("text_each_body");
        var continueLabel = NextLabel("text_each_continue");
        var invalidLabel = NextLabel("text_each_invalid");
        var endLabel = NextLabel("text_each_end");
        var entryLabel = _currentBlockLabel;
        var nextIndex = NextTemp("text_each_next");
        var initialDone = NextTemp("text_each_done");

        EmitCompare(initialDone, "eq", "i64", text.LengthName, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("text_each_i");
        EmitPhi(index, "i64", ("0", entryLabel), (nextIndex, continueLabel));
        var packed = NextTemp("utf8_decoded");
        EmitCall(packed, "i64", "smalllang_utf8_decode",
            $"ptr {text.PointerName}, i64 {text.LengthName}, i64 {index}");
        var valid = NextTemp("utf8_valid");
        EmitCompare(valid, "ne", "i64", packed, "-1");
        var decodedLabel = NextLabel("text_each_decoded");
        EmitConditionalBranch(valid, decodedLabel, invalidLabel);

        EmitLabel(invalidLabel);
        EmitTrap();

        EmitLabel(decodedLabel);
        _currentBlockLabel = decodedLabel;
        var codePoint = NextTemp(statement.ItemName);
        EmitAssign(codePoint, $"trunc i64 {packed} to i32");
        var width = NextTemp("utf8_width");
        EmitAssign(width, $"lshr i64 {packed}, 32");

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = new RuntimeInt(BoundType.CodePoint, codePoint);
            EmitStatements(statement.Body);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName: null);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(nextIndex, "add", "i64", index, width);
        var done = NextTemp("text_each_done");
        EmitCompare(done, "uge", "i64", nextIndex, text.LengthName);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitRepeatBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var count = EmitIntExpression(statement.Source);
        var bodyLabel = NextLabel("repeat_body");
        var continueLabel = NextLabel("repeat_continue");
        var endLabel = NextLabel("repeat_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("repeat_next");
        var initialDone = NextTemp("repeat_done");

        EmitCompare(initialDone, "sle", "i32", count.ValueName, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(statement.ItemName);
        EmitPhi(item, "i32", ("1", entryLabel), (next, continueLabel));

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = new RuntimeInt(item);
            EmitStatements(statement.Body);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName: null);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i32", item, "1");
        var done = NextTemp("repeat_done");
        EmitCompare(done, "sgt", "i32", next, count.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

}

