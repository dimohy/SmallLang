using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

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
        if (_usesAsync)
        {
            EmitCall(target: null, "void", "sollang_async_shutdown", "");
        }
        if (_usesParallel)
        {
            EmitCall(target: null, "void", "sollang_compute_shutdown", "");
        }
        if (_usesProcessEnvironment)
        {
            EmitPlatformFunctionBlock(_platform.EmitEnvironmentCleanup);
        }
        if (_usesProcessArguments)
        {
            EmitPlatformFunctionBlock(_platform.EmitExitCleanup);
        }
        EmitPlatformFunctionBlock(_platform.EmitExitHandles);

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
            if (_currentBlockTerminated)
            {
                break;
            }
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        if (statement is ExpressionStatement cfgYield && TryEmitCfgYieldStatement(cfgYield))
        {
            EmitStackLifetimeEndsAfter(statement);
            return;
        }
        if (statement is BindingStatement cfgAwait && TryEmitCfgAwaitBinding(cfgAwait))
        {
            EmitStackLifetimeEndsAfter(statement);
            return;
        }

        switch (statement)
        {
            case BindingStatement binding:
                var movedSourceName = GetMoveConsumingContainerSourceName(binding.Value);
                var value = EmitExpression(binding.Value);
                var movedFieldOwnerName = GetMoveConsumingOwnedFieldOwnerName(binding.Value, value);
                if (binding.IsMutable
                    && _mutableScalarSlots.TryGetValue(binding.Name, out var reboundPointer))
                {
                    var previous = _locals[binding.Name];
                    EnsureRuntimeType(value, previous.Type, binding.Name);
                    var reboundValue = MaterializeAggregateValue(value);
                    EmitStore(reboundValue.TypeName, reboundValue.ValueName, reboundPointer,
                        RuntimeAlignment(value.Type));
                    break;
                }
                if (RequiresHeapAllocation(value) && !_platform.SupportsHeapAllocation)
                {
                    throw new SollangException("dynamic arrays and dictionaries require heap allocation; wasm32-browser does not support them yet");
                }

                if (movedSourceName is not null)
                {
                    RemoveLocal(movedSourceName);
                }
                if (movedFieldOwnerName is not null)
                {
                    RemoveLocal(movedFieldOwnerName);
                }
                RemoveOwnedLiteralSources(binding.Value, value.Type);

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
            case LoopControlStatement loopControl:
                EmitLoopControlStatement(loopControl);
                return;
            case GuardLoopControlStatement guardLoopControl:
                EmitGuardLoopControlStatement(guardLoopControl);
                break;
            case ReturnStatement returnStatement:
                EmitReturnStatement(returnStatement);
                return;
            case ExpressionStatement expressionStatement:
                _mainOk = EmitExpressionStatement(expressionStatement.Expression, _mainOk);
                break;
            default:
                throw new SollangException($"unsupported runtime statement {statement.GetType().Name}");
        }

        EmitStackLifetimeEndsAfter(statement);
    }

    private void EmitLoopControlStatement(LoopControlStatement statement)
    {
        if (!_loopContexts.TryPeek(out var loop))
        {
            throw new SollangException($"'{statement.Kind.ToString().ToLowerInvariant()}' is only valid inside a loop");
        }

        DropOwnedLocalsCreatedSince(loop.OuterScope, transferredOwnerName: null);
        var edges = statement.Kind == LoopControlKind.Break
            ? loop.BreakEdges
            : loop.ContinueEdges;
        edges?.Add((CaptureLocals(), _currentBlockLabel));
        EmitBranch(statement.Kind == LoopControlKind.Break ? loop.BreakLabel : loop.ContinueLabel);
    }

    private void EmitReturnStatement(ReturnStatement statement)
    {
        var function = _currentFunction
            ?? throw new SollangException("'return' is only valid inside a function");
        if (statement.Value is null)
        {
            if (function.ReturnType != BoundType.Unit)
            {
                throw new SollangException($"return requires {function.ReturnType} but received Unit");
            }

            DropOwnedLocals();
            EmitInstruction("ret void");
            return;
        }

        if (_program.Types.IsReference(function.ReturnType))
        {
            var reference = EmitReferencePlace(statement.Value, function.ReturnType);
            EmitRet("ptr", reference.PointerName);
            return;
        }

        var value = EmitExpression(statement.Value);
        EnsureRuntimeType(value, function.ReturnType, function.Name);
        var transferredOwnerName = IsOwnedContainerRuntimeValue(value)
            ? GetFunctionResultTransferredOwnerName(function, statement.Value)
            : null;
        DropOwnedLocals(transferredOwnerName);
        var materialized = MaterializeAggregateValue(value);
        EmitRet(materialized.TypeName, materialized.ValueName);
    }

    private void EmitGuardLoopControlStatement(GuardLoopControlStatement statement)
    {
        if (!_loopContexts.TryPeek(out var loop))
        {
            throw new SollangException(
                $"'{statement.Kind.ToString().ToLowerInvariant()}' guard is only valid inside a loop");
        }

        var condition = EmitBoolExpression(statement.Condition);
        var exitLabel = NextLabel("guard_exit");
        var nextLabel = NextLabel("guard_next");
        EmitConditionalBranch(condition.ValueName, exitLabel, nextLabel);

        EmitLabel(exitLabel);
        _currentBlockLabel = exitLabel;
        DropOwnedLocalsCreatedSince(loop.OuterScope, transferredOwnerName: null);
        var edges = statement.Kind == LoopControlKind.Break
            ? loop.BreakEdges
            : loop.ContinueEdges;
        edges?.Add((CaptureLocals(), exitLabel));
        EmitBranch(statement.Kind == LoopControlKind.Break ? loop.BreakLabel : loop.ContinueLabel);

        EmitLabel(nextLabel);
        _currentBlockLabel = nextLabel;
    }

    private void EmitLoopBody(
        IReadOnlyList<Statement> statements,
        LocalScope outerScope,
        string continueLabel,
        string breakLabel,
        List<(LocalScope Scope, string Label)>? continueEdges = null,
        List<(LocalScope Scope, string Label)>? breakEdges = null)
    {
        _loopContexts.Push(new LoopContext(
            continueLabel,
            breakLabel,
            outerScope,
            continueEdges,
            breakEdges));
        try
        {
            EmitStatements(statements);
            if (!_currentBlockTerminated)
            {
                DropOwnedLocalsCreatedSince(outerScope, transferredOwnerName: null);
            }
        }
        finally
        {
            _loopContexts.Pop();
        }
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
            throw new SollangException(
                $"field assignment requires a mutable struct owner; use '{assignment.Name.TrimEnd('!')}!'");
        }

        var type = _locals[assignment.Name].Type;
        var definition = _program.Types.GetStruct(type);
        var field = definition.Fields.First(candidate => candidate.Name == assignment.FieldName);
        var movedSourceName = GetMoveConsumingContainerSourceName(assignment.Value);
        var structFieldSourceNames = GetOwnedStructFieldSourceNames(assignment.Value);
        var directOwnedSourceName = _program.Types.ContainsOwnedStorage(field.Type)
            && assignment.Value is NameExpression sourceName
            && _locals.TryGetValue(sourceName.Name, out var sourceValue)
            && sourceValue.Type == field.Type
                ? sourceName.Name
                : null;
        var value = EmitExpression(assignment.Value);
        EnsureRuntimeType(value, field.Type, $"{definition.Name}.{field.Name}");
        var fieldAddress = NextTemp("field_addr");
        EmitAssign(
            fieldAddress,
            $"getelementptr inbounds {LlvmStructType(type)}, ptr {pointer}, i32 0, i32 {field.Index.ToString(CultureInfo.InvariantCulture)}");
        if (_program.Types.ContainsOwnedStorage(field.Type))
        {
            var previous = NextTemp("field_previous");
            EmitLoad(previous, LlvmType(field.Type), fieldAddress, RuntimeAlignment(field.Type));
            DropOwnedRuntimeValue(DematerializeAggregateValue(field.Type, previous));
        }
        var materialized = MaterializeAggregateValue(value);
        EmitStore(materialized.TypeName, materialized.ValueName, fieldAddress, RuntimeAlignment(field.Type));
        if (movedSourceName is not null)
        {
            RemoveLocal(movedSourceName);
        }
        if (directOwnedSourceName is not null)
        {
            RemoveLocal(directOwnedSourceName);
        }
        foreach (var transferredName in structFieldSourceNames)
        {
            RemoveLocal(transferredName);
        }
    }

    private void EmitIndexAssignmentStatement(IndexAssignmentStatement assignment)
    {
        if (!_mutableLocals.Contains(assignment.Name))
        {
            throw new SollangException($"indexed assignment requires a mutable owner binding; use '=> {assignment.Name.TrimEnd('!')}!'");
        }

        var target = ResolveLocal(assignment.Name);
        if (target is RuntimeMappedBytes mapped)
        {
            var mappedValue = EmitIntExpression(assignment.Value);
            EnsureRuntimeType(mappedValue, BoundType.UInt8, "mapped byte assignment");
            EmitMappedStore(mapped, assignment.Index, mappedValue);
            return;
        }
        var movedSourceName = GetMoveConsumingContainerSourceName(assignment.Value);
        var structFieldSourceNames = GetOwnedStructFieldSourceNames(assignment.Value);
        var expectedValueType = target switch
        {
            RuntimeDynamicInlineArray array => array.ElementType,
            RuntimeInlineDictionary dictionary => dictionary.ValueType,
            _ => (BoundType?)null
        };
        var directOwnedSourceName = assignment.Value is NameExpression sourceName
            && _locals.TryGetValue(sourceName.Name, out var sourceValue)
            && expectedValueType == sourceValue.Type
            && _program.Types.ContainsOwnedStorage(sourceValue.Type)
                ? sourceName.Name
                : null;
        var value = assignment.Value is DictionaryLiteralExpression contextualValue
            && expectedValueType is { } contextualValueType
            && _program.Types.IsStruct(contextualValueType)
                ? EmitContextualStructLiteral(contextualValue, contextualValueType)
                : EmitExpression(assignment.Value);
        switch (target)
        {
            case RuntimeStaticIntArray array:
                var staticIndex = EmitIntExpression(assignment.Index);
                EmitStaticArrayAssign(array, EmitIntAsSize(staticIndex, "assignment_index"), ((RuntimeInt)value).ValueName);
                return;
            case RuntimeDynamicIntArray array:
                var dynamicIndex = EmitIntExpression(assignment.Index);
                EmitDynamicArrayAssign(array, EmitIntAsSize(dynamicIndex, "assignment_index"), ((RuntimeInt)value).ValueName);
                return;
            case RuntimeDynamicInlineArray array:
                var inlineIndex = EmitIntExpression(assignment.Index);
                EnsureRuntimeType(value, array.ElementType, "array indexed assignment");
                EmitDynamicInlineArrayAssign(array, EmitIntAsSize(inlineIndex, "assignment_index"), value);
                if (movedSourceName is not null)
                {
                    RemoveLocal(movedSourceName);
                }
                if (directOwnedSourceName is not null)
                {
                    RemoveLocal(directOwnedSourceName);
                }
                foreach (var transferredName in structFieldSourceNames)
                {
                    RemoveLocal(transferredName);
                }
                return;
            case RuntimeIntDictionary dictionary:
                var dictionaryIndex = EmitIntExpression(assignment.Index);
                EmitDictionaryAssignExisting(dictionary, dictionaryIndex.ValueName, ((RuntimeInt)value).ValueName);
                return;
            case RuntimeInlineDictionary dictionary:
                var inlineDictionaryKey = assignment.Index is DictionaryLiteralExpression contextualKey
                    && _program.Types.IsStruct(dictionary.KeyType)
                        ? EmitContextualStructLiteral(contextualKey, dictionary.KeyType)
                        : EmitExpression(assignment.Index);
                EnsureRuntimeType(inlineDictionaryKey, dictionary.KeyType, "dictionary indexed assignment key");
                EnsureRuntimeType(value, dictionary.ValueType, "dictionary indexed assignment value");
                EmitInlineDictionaryAssignExisting(dictionary, inlineDictionaryKey, value);
                if (movedSourceName is not null)
                {
                    RemoveLocal(movedSourceName);
                }
                if (directOwnedSourceName is not null)
                {
                    RemoveLocal(directOwnedSourceName);
                }
                foreach (var transferredName in structFieldSourceNames)
                {
                    RemoveLocal(transferredName);
                }
                return;
            default:
                throw new SollangException("indexed assignment expects an array or dictionary owner");
        }
    }

    private string? GetMoveConsumingOwnedFieldOwnerName(Expression expression, RuntimeValue value)
    {
        if (expression is not FieldAccessExpression { Source: NameExpression owner }
            || _currentFunction is null
            || _currentFunction.InputOwnership != BoundFunctionInputOwnership.Move
            || !string.Equals(owner.Name, _currentFunction.InputName ?? "it", StringComparison.Ordinal)
            || !_program.Types.ContainsOwnedStorage(value.Type))
        {
            return null;
        }

        return owner.Name;
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
            case "while":
                EmitWhileBlockFunctionCall(statement);
                return;
            default:
                var resolvedGeneric = _program.ResolvedGenericCalls.TryGetValue(statement, out var function);
                if ((resolvedGeneric || TryResolveFunction(statement.Target, out function))
                    && function is { Kind: BoundFunctionKind.RuntimeParallel })
                {
                    EmitParallelBlockFunctionCall(statement, function);
                    return;
                }
                if ((resolvedGeneric || TryResolveFunction(statement.Target, out function))
                    && function is { Kind: BoundFunctionKind.RuntimeTryParallel })
                {
                    EmitTryParallelBlockFunctionCall(statement, function);
                    return;
                }
                if ((resolvedGeneric || TryResolveFunction(statement.Target, out function))
                    && function is { Kind: BoundFunctionKind.UserBlock })
                {
                    EmitUserBlockFunctionCall(statement, function);
                    return;
                }

                throw new SollangException($"unknown block function '{target}'");
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
            EmitLoopBody(statement.Body, outerLocals, continueLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
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
            _ => throw new SollangException("each expects a range, Text, or array input")
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
            _ => throw new SollangException("each expects a range, Text, or array input")
        };

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            if (_program.Types.ContainsOwnedStorage(item.Type))
            {
                _borrowedOwnedLocals.Add(statement.ItemName);
            }
            EmitLoopBody(statement.Body, outerLocals, continueLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
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
            throw new SollangException($"block function '{function.Name}' is not callable");
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
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        _currentBlockInvocation = new RuntimeBlockInvocation(
            statement.ItemName,
            statement.Body,
            callerLocals,
            callerFunctions,
            function.BlockResultType ?? BoundType.Unit);
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        RestoreLocals(blockLocals);
        RuntimeValue result = RuntimeUnit.Instance;
        try
        {
            EmitStatements(function.BlockBody);
            result = function.Body is null
                ? RuntimeUnit.Instance
                : EmitExpression(function.Body);
            EnsureRuntimeType(result, function.ReturnType, function.Name);
            var transferredOwnerName = statement.ResultName is not null
                && function.Body is not null
                && IsOwnedContainerRuntimeValue(result)
                    ? GetFunctionResultTransferredOwnerName(function, function.Body)
                    : null;
            DropOwnedLocalsCreatedSince(blockLocals, transferredOwnerName);
        }
        finally
        {
            _currentBlockInvocation = previousInvocation;
            _currentFunctions = previousFunctions;
            RestoreLocals(callerLocals);
        }

        if (statement.ResultName is null)
        {
            return;
        }

        if (RequiresHeapAllocation(result) && !_platform.SupportsHeapAllocation)
        {
            throw new SollangException(
                "dynamic arrays and dictionaries require heap allocation; wasm32-browser does not support them yet");
        }

        _locals.Add(statement.ResultName, result);
        if (statement.ResultIsMutable)
        {
            _mutableLocals.Add(statement.ResultName);
            if (result is RuntimeStruct structure)
            {
                CreateMutableStructSlot(statement.ResultName, structure);
            }
            else
            {
                var binding = new BindingStatement(
                    statement.ResultName,
                    statement.Source,
                    statement.Line,
                    statement.Column,
                    IsMutable: true);
                CreateMutableContainerSlot(binding, result);
            }
        }
    }

    private void EmitParallelBlockFunctionCall(BlockFunctionCallStatement statement, BoundFunction function)
    {
        var source = EmitExpression(statement.Source);
        var length = source switch
        {
            RuntimeDynamicIntArray array => array.LengthName,
            RuntimeDynamicInlineArray array => array.LengthName,
            _ => throw new SollangException("parallel expects a growable array")
        };
        if (statement.Body.Count == 0 || statement.Body[^1] is not ExpressionStatement callbackResult)
        {
            throw new SollangException("parallel callback requires a final expression");
        }

        var resultType = function.BlockResultType
            ?? throw new SollangException("parallel callback result type was not specialized");
        RuntimeValue resultArray;
        string outputPointer;
        if (resultType == BoundType.Int)
        {
            outputPointer = EmitHeapAllocateProduct(length, "4", "parallel_result_bytes");
            resultArray = new RuntimeDynamicIntArray(outputPointer, length, length);
        }
        else
        {
            var outputDefinition = _program.Types.GetDynamicArray(function.ReturnType);
            outputPointer = EmitHeapAllocateProduct(
                length,
                outputDefinition.ElementSize.ToString(CultureInfo.InvariantCulture),
                "parallel_result_bytes");
            resultArray = new RuntimeDynamicInlineArray(
                function.ReturnType,
                resultType,
                outputPointer,
                length,
                length);
        }

        if (_parallelCallbacks.TryGetValue(statement, out var callback))
        {
            var inputPointer = source switch
            {
                RuntimeDynamicIntArray array => array.PointerName,
                RuntimeDynamicInlineArray array => array.PointerName,
                _ => throw new SollangException("parallel expects a growable array")
            };
            var group = NextTemp("parallel_group");
            EmitAlloca(group, "%sollang.compute_group", 8);
            var callbackSlot = NextTemp("parallel_callback_slot");
            EmitInstruction($"{callbackSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 0");
            EmitStore("ptr", $"@{callback.Name}", callbackSlot, 8);
            var inputSlot = NextTemp("parallel_input_slot");
            EmitInstruction($"{inputSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 1");
            EmitStore("ptr", inputPointer, inputSlot, 8);
            var outputSlot = NextTemp("parallel_output_slot");
            EmitInstruction($"{outputSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 2");
            EmitStore("ptr", outputPointer, outputSlot, 8);
            var countSlot = NextTemp("parallel_count_slot");
            EmitInstruction($"{countSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 3");
            EmitStore("i64", length, countSlot, 8);
            var captureEnvironment = "null";
            if (callback.Captures.Count > 0)
            {
                var captureType = ParallelCaptureType(callback.Captures);
                captureEnvironment = NextTemp("parallel_capture_environment");
                EmitAlloca(captureEnvironment, captureType, 8);
                for (var captureIndex = 0; captureIndex < callback.Captures.Count; captureIndex++)
                {
                    var capture = callback.Captures[captureIndex];
                    var captureValue = ResolveLocal(capture.Key);
                    EnsureRuntimeType(captureValue, capture.Value, callback.Target.Name);
                    var materialized = MaterializeAggregateValue(captureValue);
                    var captureAddress = NextTemp("parallel_capture_address");
                    EmitInstruction($"{captureAddress} = getelementptr {captureType}, ptr {captureEnvironment}, i32 0, i32 {captureIndex.ToString(CultureInfo.InvariantCulture)}");
                    EmitStore(
                        materialized.TypeName,
                        materialized.ValueName,
                        captureAddress,
                        RuntimeAlignment(capture.Value));
                }
            }
            var captureSlot = NextTemp("parallel_capture_slot");
            EmitInstruction($"{captureSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 4");
            EmitStore("ptr", captureEnvironment, captureSlot, 8);
            var sinkBytes = NextTemp("parallel_sink_bytes");
            EmitInstruction($"{sinkBytes} = mul i64 {length}, 24");
            var sinks = NextTemp("parallel_sinks");
            EmitCall(sinks, "ptr", "sollang_alloc", $"i64 {sinkBytes}");
            EmitCall(target: null, "void", "llvm.memset.p0.i64", $"ptr {sinks}, i8 0, i64 {sinkBytes}, i1 false");
            var sinksSlot = NextTemp("parallel_sinks_slot");
            EmitInstruction($"{sinksSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 5");
            EmitStore("ptr", sinks, sinksSlot, 8);
            var runtimeValues = new[] { "%stdin", "%stdout", "%written", "%read", "%ok_state" };
            for (var runtimeIndex = 0; runtimeIndex < runtimeValues.Length; runtimeIndex++)
            {
                var runtimeSlot = NextTemp("parallel_runtime_slot");
                EmitInstruction($"{runtimeSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 {runtimeIndex + 6}");
                EmitStore("ptr", runtimeValues[runtimeIndex], runtimeSlot, 8);
            }
            var failureLimitSlot = NextTemp("parallel_failure_limit_slot");
            EmitInstruction($"{failureLimitSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 11");
            EmitStore("i64", length, failureLimitSlot, 8);
            var initializedSlot = NextTemp("parallel_initialized_slot");
            EmitInstruction($"{initializedSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 12");
            EmitStore("ptr", "null", initializedSlot, 8);
            EmitCall(target: null, "void", "sollang_compute_execute", $"ptr {group}");
            BindParallelResult(statement, resultArray);
            return;
        }

        var bodyLabel = NextLabel("parallel_body");
        var continueLabel = NextLabel("parallel_continue");
        var endLabel = NextLabel("parallel_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("parallel_next");
        var initialDone = NextTemp("parallel_done");
        EmitCompare(initialDone, "eq", "i64", length, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("parallel_i");
        EmitPhi(index, "i64", ("0", entryLabel), (next, continueLabel));
        RuntimeValue item = source switch
        {
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, index),
            RuntimeDynamicInlineArray array => EmitDynamicInlineArrayLoad(array, index),
            _ => throw new SollangException("parallel expects a growable array")
        };

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            var callbackLocals = CaptureLocals();
            EmitStatements(statement.Body.Take(statement.Body.Count - 1).ToArray());
            var mapped = EmitExpression(callbackResult.Expression);
            EnsureRuntimeType(mapped, resultType, "parallel callback");
            switch (resultArray)
            {
                case RuntimeDynamicIntArray integers:
                    EmitDynamicArrayAssign(integers, index, ((RuntimeInt)mapped).ValueName);
                    break;
                case RuntimeDynamicInlineArray inline:
                    StoreDynamicInlineArrayElement(
                        inline.PointerName,
                        _program.Types.GetDynamicArray(inline.Type),
                        index,
                        mapped);
                    break;
            }
            var transferred = IsOwnedContainerRuntimeValue(mapped)
                ? GetMoveConsumingContainerSourceName(callbackResult.Expression)
                : null;
            DropOwnedLocalsCreatedSince(callbackLocals, transferred);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", index, "1");
        var done = NextTemp("parallel_done");
        EmitCompare(done, "eq", "i64", next, length);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;

        BindParallelResult(statement, resultArray);
    }

    private void BindParallelResult(BlockFunctionCallStatement statement, RuntimeValue resultArray)
    {
        if (statement.ResultName is not null)
        {
            _locals.Add(statement.ResultName, resultArray);
            if (statement.ResultIsMutable)
            {
                _mutableLocals.Add(statement.ResultName);
                CreateMutableContainerSlot(new BindingStatement(
                    statement.ResultName,
                    statement.Source,
                    statement.Line,
                    statement.Column,
                    IsMutable: true), resultArray);
            }
        }
    }

    private void EmitTryParallelBlockFunctionCall(
        BlockFunctionCallStatement statement,
        BoundFunction function)
    {
        var source = EmitExpression(statement.Source);
        var length = source switch
        {
            RuntimeDynamicIntArray array => array.LengthName,
            RuntimeDynamicInlineArray array => array.LengthName,
            _ => throw new SollangException("tryParallel expects a growable array")
        };
        if (statement.Body.Count == 0 || statement.Body[^1] is not ExpressionStatement callbackResult)
        {
            throw new SollangException("tryParallel callback requires a final Result expression");
        }
        if (function.BlockResultType is not { } callbackResultType
            || !_program.Types.TryGetResultTypes(callbackResultType, out var callbackTypes)
            || !_program.Types.TryGetResultTypes(function.ReturnType, out var outerTypes))
        {
            throw new SollangException("tryParallel Result types were not specialized");
        }

        RuntimeValue resultArray;
        string outputPointer;
        if (callbackTypes.Ok == BoundType.Int)
        {
            outputPointer = EmitHeapAllocateProduct(length, "4", "try_parallel_result_bytes");
            resultArray = new RuntimeDynamicIntArray(outputPointer, length, length);
        }
        else
        {
            var outputDefinition = _program.Types.GetDynamicArray(outerTypes.Ok);
            outputPointer = EmitHeapAllocateProduct(
                length,
                outputDefinition.ElementSize.ToString(CultureInfo.InvariantCulture),
                "try_parallel_result_bytes");
            resultArray = new RuntimeDynamicInlineArray(
                outerTypes.Ok,
                callbackTypes.Ok,
                outputPointer,
                length,
                length);
        }

        if (_parallelCallbacks.TryGetValue(statement, out var callback))
        {
            var callbackStride = _program.Types.InlineSizeOf(callbackResultType);
            var callbackBytes = NextTemp("try_parallel_callback_bytes");
            EmitInstruction($"{callbackBytes} = mul i64 {length}, {callbackStride.ToString(CultureInfo.InvariantCulture)}");
            var callbackResults = NextTemp("try_parallel_callback_results");
            EmitCall(callbackResults, "ptr", "sollang_alloc", $"i64 {callbackBytes}");
            var initialized = NextTemp("try_parallel_initialized");
            EmitCall(initialized, "ptr", "sollang_alloc", $"i64 {length}");
            EmitCall(target: null, "void", "llvm.memset.p0.i64", $"ptr {initialized}, i8 0, i64 {length}, i1 false");
            var sinksBytes = NextTemp("try_parallel_sink_bytes");
            EmitInstruction($"{sinksBytes} = mul i64 {length}, 24");
            var sinks = NextTemp("try_parallel_sinks");
            EmitCall(sinks, "ptr", "sollang_alloc", $"i64 {sinksBytes}");
            EmitCall(target: null, "void", "llvm.memset.p0.i64", $"ptr {sinks}, i8 0, i64 {sinksBytes}, i1 false");

            var inputPointer = source switch
            {
                RuntimeDynamicIntArray array => array.PointerName,
                RuntimeDynamicInlineArray array => array.PointerName,
                _ => throw new SollangException("tryParallel expects a growable array")
            };
            var group = NextTemp("try_parallel_group");
            EmitAlloca(group, "%sollang.compute_group", 8);
            EmitComputeGroupField(group, 0, "ptr", $"@{callback.Name}");
            EmitComputeGroupField(group, 1, "ptr", inputPointer);
            EmitComputeGroupField(group, 2, "ptr", callbackResults);
            EmitComputeGroupField(group, 3, "i64", length);

            var captureEnvironment = "null";
            if (callback.Captures.Count > 0)
            {
                var captureType = ParallelCaptureType(callback.Captures);
                captureEnvironment = NextTemp("try_parallel_capture_environment");
                EmitAlloca(captureEnvironment, captureType, 8);
                for (var captureIndex = 0; captureIndex < callback.Captures.Count; captureIndex++)
                {
                    var capture = callback.Captures[captureIndex];
                    var captureValue = ResolveLocal(capture.Key);
                    EnsureRuntimeType(captureValue, capture.Value, callback.Target.Name);
                    var materialized = MaterializeAggregateValue(captureValue);
                    var captureAddress = NextTemp("try_parallel_capture_address");
                    EmitInstruction($"{captureAddress} = getelementptr {captureType}, ptr {captureEnvironment}, i32 0, i32 {captureIndex.ToString(CultureInfo.InvariantCulture)}");
                    EmitStore(materialized.TypeName, materialized.ValueName, captureAddress, RuntimeAlignment(capture.Value));
                }
            }
            EmitComputeGroupField(group, 4, "ptr", captureEnvironment);
            EmitComputeGroupField(group, 5, "ptr", sinks);
            var runtimeValues = new[] { "%stdin", "%stdout", "%written", "%read", "%ok_state" };
            for (var runtimeIndex = 0; runtimeIndex < runtimeValues.Length; runtimeIndex++)
            {
                EmitComputeGroupField(group, runtimeIndex + 6, "ptr", runtimeValues[runtimeIndex]);
            }
            EmitComputeGroupField(group, 11, "i64", length);
            EmitComputeGroupField(group, 12, "ptr", initialized);
            EmitCall(target: null, "void", "sollang_compute_execute", $"ptr {group}");
            var collectedResult = EmitCollectTryParallelResults(
                group,
                function,
                resultArray,
                outputPointer,
                callbackResults,
                initialized,
                length,
                callbackResultType,
                callbackTypes,
                callbackStride);
            BindParallelResult(statement, collectedResult);
            return;
        }

        var bodyLabel = NextLabel("try_parallel_body");
        var continueLabel = NextLabel("try_parallel_continue");
        var successLabel = NextLabel("try_parallel_success");
        var mergeLabel = NextLabel("try_parallel_merge");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("try_parallel_next");
        var initialDone = NextTemp("try_parallel_done");
        EmitCompare(initialDone, "eq", "i64", length, "0");
        EmitConditionalBranch(initialDone, successLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("try_parallel_i");
        EmitPhi(index, "i64", ("0", entryLabel), (next, continueLabel));
        RuntimeValue item = source switch
        {
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, index),
            RuntimeDynamicInlineArray array => EmitDynamicInlineArrayLoad(array, index),
            _ => throw new SollangException("tryParallel expects a growable array")
        };

        RuntimeEnum errorResult;
        string errorResultLabel;
        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            var callbackLocals = CaptureLocals();
            EmitStatements(statement.Body.Take(statement.Body.Count - 1).ToArray());
            var mapped = EmitExpression(callbackResult.Expression) as RuntimeEnum
                ?? throw new SollangException("tryParallel callback must return Result<R, E>");
            EnsureRuntimeType(mapped, callbackResultType, "tryParallel callback");
            var tag = NextTemp("try_parallel_tag");
            EmitAssign(tag, $"extractvalue {LlvmEnumType(mapped.Type)} {mapped.ValueName}, 0");
            var isError = NextTemp("try_parallel_is_error");
            EmitCompare(isError, "eq", "i32", tag, "1");
            var errorLabel = NextLabel("try_parallel_error");
            var okLabel = NextLabel("try_parallel_ok");
            EmitConditionalBranch(isError, errorLabel, okLabel);

            EmitLabel(okLabel);
            _currentBlockLabel = okLabel;
            var successful = ExtractEnumPayload(mapped, callbackTypes.Ok);
            switch (resultArray)
            {
                case RuntimeDynamicIntArray integers:
                    StoreDynamicArrayElement(integers.PointerName, index, ((RuntimeInt)successful).ValueName);
                    break;
                case RuntimeDynamicInlineArray inline:
                    StoreDynamicInlineArrayElement(
                        inline.PointerName,
                        _program.Types.GetDynamicArray(inline.Type),
                        index,
                        successful);
                    break;
            }
            DropOwnedLocalsCreatedSince(callbackLocals, transferredOwnerName: null);
            EmitBranch(continueLabel);

            EmitLabel(errorLabel);
            _currentBlockLabel = errorLabel;
            var error = ExtractEnumPayload(mapped, callbackTypes.Error);
            DropOwnedLocalsCreatedSince(callbackLocals, transferredOwnerName: null);
            EmitDropTryParallelPrefix(outputPointer, callbackTypes.Ok, index);
            var outerDefinition = _program.Types.GetEnum(function.ReturnType);
            var errorVariant = outerDefinition.Variants.First(variant => variant.Name == "Err");
            errorResult = EmitEnumValue(function.ReturnType, errorVariant, error);
            errorResultLabel = _currentBlockLabel;
            EmitBranch(mergeLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", index, "1");
        var done = NextTemp("try_parallel_done");
        EmitCompare(done, "eq", "i64", next, length);
        EmitConditionalBranch(done, successLabel, bodyLabel);

        EmitLabel(successLabel);
        _currentBlockLabel = successLabel;
        var resultDefinition = _program.Types.GetEnum(function.ReturnType);
        var okVariant = resultDefinition.Variants.First(variant => variant.Name == "Ok");
        var successResult = EmitEnumValue(function.ReturnType, okVariant, resultArray);
        var successResultLabel = _currentBlockLabel;
        EmitBranch(mergeLabel);

        EmitLabel(mergeLabel);
        _currentBlockLabel = mergeLabel;
        var result = EmitEnumPhi(
            "try_parallel_result",
            function.ReturnType,
            [(errorResult, errorResultLabel), (successResult, successResultLabel)]);
        BindParallelResult(statement, result);
    }

    private void EmitComputeGroupField(string group, int index, string type, string value)
    {
        var slot = NextTemp("compute_group_slot");
        EmitInstruction($"{slot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 {index.ToString(CultureInfo.InvariantCulture)}");
        EmitStore(type, value, slot, 8);
    }

    private RuntimeEnum EmitCollectTryParallelResults(
        string group,
        BoundFunction function,
        RuntimeValue resultArray,
        string outputPointer,
        string callbackResults,
        string initialized,
        string length,
        BoundType callbackResultType,
        (BoundType Ok, BoundType Error) callbackTypes,
        int callbackStride)
    {
        var groupFailureSlot = NextTemp("try_parallel_failure_slot");
        EmitInstruction($"{groupFailureSlot} = getelementptr %sollang.compute_group, ptr {group}, i32 0, i32 11");
        var failureIndex = NextTemp("try_parallel_failure_index");
        EmitInstruction($"{failureIndex} = load atomic i64, ptr {groupFailureSlot} acquire, align 8");
        var hasFailure = NextTemp("try_parallel_has_failure");
        EmitCompare(hasFailure, "ult", "i64", failureIndex, length);
        var errorLabel = NextLabel("try_parallel_collect_error");
        var successLabel = NextLabel("try_parallel_collect_success");
        var mergeLabel = NextLabel("try_parallel_collect_merge");
        EmitConditionalBranch(hasFailure, errorLabel, successLabel);

        EmitLabel(errorLabel);
        _currentBlockLabel = errorLabel;
        var selected = EmitLoadTryParallelCallbackResult(
            callbackResults, failureIndex, callbackResultType, callbackStride, "try_parallel_selected");
        var errorPayload = ExtractEnumPayload(selected, callbackTypes.Error);
        EmitDropInitializedTryParallelResults(
            callbackResults,
            initialized,
            length,
            failureIndex,
            callbackResultType,
            callbackTypes,
            callbackStride);
        EmitCall(target: null, "void", "sollang_free", $"ptr {callbackResults}");
        EmitCall(target: null, "void", "sollang_free", $"ptr {initialized}");
        EmitCall(target: null, "void", "sollang_free", $"ptr {outputPointer}");
        var resultDefinition = _program.Types.GetEnum(function.ReturnType);
        var errorVariant = resultDefinition.Variants.First(variant => variant.Name == "Err");
        var errorResult = EmitEnumValue(function.ReturnType, errorVariant, errorPayload);
        var errorResultLabel = _currentBlockLabel;
        EmitBranch(mergeLabel);

        EmitLabel(successLabel);
        _currentBlockLabel = successLabel;
        var copyLoopLabel = NextLabel("try_parallel_copy_loop");
        var copyBodyLabel = NextLabel("try_parallel_copy_body");
        var copyDoneLabel = NextLabel("try_parallel_copy_done");
        var copyEntryLabel = _currentBlockLabel;
        var copyNext = NextTemp("try_parallel_copy_next");
        EmitBranch(copyLoopLabel);
        EmitLabel(copyLoopLabel);
        _currentBlockLabel = copyLoopLabel;
        var copyIndex = NextTemp("try_parallel_copy_i");
        EmitPhi(copyIndex, "i64", ("0", copyEntryLabel), (copyNext, copyBodyLabel));
        var copyInRange = NextTemp("try_parallel_copy_in_range");
        EmitCompare(copyInRange, "ult", "i64", copyIndex, length);
        EmitConditionalBranch(copyInRange, copyBodyLabel, copyDoneLabel);
        EmitLabel(copyBodyLabel);
        _currentBlockLabel = copyBodyLabel;
        var callbackResult = EmitLoadTryParallelCallbackResult(
            callbackResults, copyIndex, callbackResultType, callbackStride, "try_parallel_copy_result");
        var successful = ExtractEnumPayload(callbackResult, callbackTypes.Ok);
        switch (resultArray)
        {
            case RuntimeDynamicIntArray integers:
                StoreDynamicArrayElement(integers.PointerName, copyIndex, ((RuntimeInt)successful).ValueName);
                break;
            case RuntimeDynamicInlineArray inline:
                StoreDynamicInlineArrayElement(
                    inline.PointerName,
                    _program.Types.GetDynamicArray(inline.Type),
                    copyIndex,
                    successful);
                break;
        }
        EmitBinary(copyNext, "add", "i64", copyIndex, "1");
        EmitBranch(copyLoopLabel);
        EmitLabel(copyDoneLabel);
        _currentBlockLabel = copyDoneLabel;
        EmitCall(target: null, "void", "sollang_free", $"ptr {callbackResults}");
        EmitCall(target: null, "void", "sollang_free", $"ptr {initialized}");
        var okVariant = resultDefinition.Variants.First(variant => variant.Name == "Ok");
        var successResult = EmitEnumValue(function.ReturnType, okVariant, resultArray);
        var successResultLabel = _currentBlockLabel;
        EmitBranch(mergeLabel);

        EmitLabel(mergeLabel);
        _currentBlockLabel = mergeLabel;
        return EmitEnumPhi(
            "try_parallel_collected",
            function.ReturnType,
            [(errorResult, errorResultLabel), (successResult, successResultLabel)]);
    }

    private RuntimeEnum EmitLoadTryParallelCallbackResult(
        string pointer,
        string index,
        BoundType resultType,
        int stride,
        string prefix)
    {
        var offset = NextTemp(prefix + "_offset");
        EmitInstruction($"{offset} = mul i64 {index}, {stride.ToString(CultureInfo.InvariantCulture)}");
        var address = NextTemp(prefix + "_address");
        EmitInstruction($"{address} = getelementptr i8, ptr {pointer}, i64 {offset}");
        var loaded = NextTemp(prefix + "_value");
        EmitLoad(loaded, LlvmEnumType(resultType), address, RuntimeAlignment(resultType));
        return new RuntimeEnum(resultType, loaded);
    }

    private void EmitDropInitializedTryParallelResults(
        string callbackResults,
        string initialized,
        string length,
        string selectedIndex,
        BoundType callbackResultType,
        (BoundType Ok, BoundType Error) callbackTypes,
        int callbackStride)
    {
        if (!_program.Types.ContainsOwnedStorage(callbackTypes.Ok)
            && !_program.Types.ContainsOwnedStorage(callbackTypes.Error))
        {
            return;
        }

        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("try_parallel_cleanup_loop");
        var inspectLabel = NextLabel("try_parallel_cleanup_inspect");
        var okLabel = NextLabel("try_parallel_cleanup_ok");
        var errorLabel = NextLabel("try_parallel_cleanup_error");
        var nextLabel = NextLabel("try_parallel_cleanup_next");
        var doneLabel = NextLabel("try_parallel_cleanup_done");
        var next = NextTemp("try_parallel_cleanup_next_i");
        EmitBranch(loopLabel);
        EmitLabel(loopLabel);
        _currentBlockLabel = loopLabel;
        var index = NextTemp("try_parallel_cleanup_i");
        EmitPhi(index, "i64", ("0", entryLabel), (next, nextLabel));
        var inRange = NextTemp("try_parallel_cleanup_in_range");
        EmitCompare(inRange, "ult", "i64", index, length);
        var checkLabel = NextLabel("try_parallel_cleanup_check");
        EmitConditionalBranch(inRange, checkLabel, doneLabel);
        EmitLabel(checkLabel);
        _currentBlockLabel = checkLabel;
        var flagAddress = NextTemp("try_parallel_cleanup_flag_address");
        EmitInstruction($"{flagAddress} = getelementptr i8, ptr {initialized}, i64 {index}");
        var flag = NextTemp("try_parallel_cleanup_flag");
        EmitInstruction($"{flag} = load atomic i8, ptr {flagAddress} acquire, align 1");
        var wasInitialized = NextTemp("try_parallel_cleanup_initialized");
        EmitCompare(wasInitialized, "ne", "i8", flag, "0");
        var isSelected = NextTemp("try_parallel_cleanup_selected");
        EmitCompare(isSelected, "eq", "i64", index, selectedIndex);
        var notSelected = NextTemp("try_parallel_cleanup_not_selected");
        EmitInstruction($"{notSelected} = xor i1 {isSelected}, true");
        var shouldInspect = NextTemp("try_parallel_cleanup_should_inspect");
        EmitInstruction($"{shouldInspect} = and i1 {wasInitialized}, {notSelected}");
        EmitConditionalBranch(shouldInspect, inspectLabel, nextLabel);

        EmitLabel(inspectLabel);
        _currentBlockLabel = inspectLabel;
        var value = EmitLoadTryParallelCallbackResult(
            callbackResults, index, callbackResultType, callbackStride, "try_parallel_cleanup_result");
        var tag = NextTemp("try_parallel_cleanup_tag");
        EmitAssign(tag, $"extractvalue {LlvmEnumType(callbackResultType)} {value.ValueName}, 0");
        var isError = NextTemp("try_parallel_cleanup_is_error");
        EmitCompare(isError, "eq", "i32", tag, "1");
        EmitConditionalBranch(isError, errorLabel, okLabel);

        EmitLabel(okLabel);
        _currentBlockLabel = okLabel;
        if (_program.Types.ContainsOwnedStorage(callbackTypes.Ok))
        {
            DropOwnedRuntimeValue(ExtractEnumPayload(value, callbackTypes.Ok));
        }
        EmitBranch(nextLabel);

        EmitLabel(errorLabel);
        _currentBlockLabel = errorLabel;
        if (_program.Types.ContainsOwnedStorage(callbackTypes.Error))
        {
            DropOwnedRuntimeValue(ExtractEnumPayload(value, callbackTypes.Error));
        }
        EmitBranch(nextLabel);

        EmitLabel(nextLabel);
        _currentBlockLabel = nextLabel;
        EmitBinary(next, "add", "i64", index, "1");
        EmitBranch(loopLabel);
        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

    private void EmitDropTryParallelPrefix(string pointer, BoundType elementType, string length)
    {
        if (_program.Types.ContainsOwnedStorage(elementType))
        {
            var entryLabel = _currentBlockLabel;
            var loopLabel = NextLabel("try_parallel_drop_loop");
            var bodyLabel = NextLabel("try_parallel_drop_body");
            var endLabel = NextLabel("try_parallel_drop_end");
            var next = NextTemp("try_parallel_drop_next");
            EmitBranch(loopLabel);
            EmitLabel(loopLabel);
            _currentBlockLabel = loopLabel;
            var index = NextTemp("try_parallel_drop_i");
            EmitPhi(index, "i64", ("0", entryLabel), (next, bodyLabel));
            var inRange = NextTemp("try_parallel_drop_in_range");
            EmitCompare(inRange, "ult", "i64", index, length);
            EmitConditionalBranch(inRange, bodyLabel, endLabel);
            EmitLabel(bodyLabel);
            _currentBlockLabel = bodyLabel;
            var address = NextTemp("try_parallel_drop_address");
            EmitAssign(address, $"getelementptr {LlvmType(elementType)}, ptr {pointer}, i64 {index}");
            var loaded = NextTemp("try_parallel_drop_value");
            EmitLoad(loaded, LlvmType(elementType), address, RuntimeAlignment(elementType));
            DropOwnedRuntimeValue(DematerializeAggregateValue(elementType, loaded));
            EmitBinary(next, "add", "i64", index, "1");
            EmitBranch(loopLabel);
            EmitLabel(endLabel);
            _currentBlockLabel = endLabel;
        }
        EmitCall(target: null, "void", "sollang_free", $"ptr {pointer}");
    }

    private string EmitHeapAllocateProduct(string count, string stride, string name)
    {
        var bytes = NextTemp(name);
        EmitBinary(bytes, "mul", "i64", count, stride);
        return EmitHeapAllocate(bytes);
    }

    private void EmitWhileBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        if (HasPlannedCfgSuspension(statement.Body))
        {
            EmitAsyncWhileBlockFunctionCall(statement);
            return;
        }

        var conditionLabel = NextLabel("while_condition");
        var bodyLabel = NextLabel("while_body");
        var endLabel = NextLabel("while_end");
        EmitBranch(conditionLabel);

        EmitLabel(conditionLabel);
        _currentBlockLabel = conditionLabel;
        var condition = EmitExpression(statement.Source) as RuntimeBool
            ?? throw new SollangException("while condition must be Bool");
        EmitConditionalBranch(condition.ValueName, bodyLabel, endLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var outerLocals = CaptureLocals();
        try
        {
            EmitLoopBody(statement.Body, outerLocals, conditionLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
        if (!_currentBlockTerminated)
        {
            EmitBranch(conditionLabel);
        }

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private bool HasPlannedCfgSuspension(IReadOnlyList<Statement> statements)
    {
        if (_activeAsyncCfg is not { } lowering)
        {
            return false;
        }

        var candidates = new List<AsyncCfgCandidate>();
        CollectCfgSuspensions(statements, nested: true, candidates);
        return candidates.Any(candidate => lowering.Plan.BySite.ContainsKey(candidate.Site));
    }

    private void EmitAsyncWhileBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        if (_activeAsyncCfg is not { } lowering)
        {
            throw new SollangException("async while lowering requires an active CFG plan");
        }
        var entryLabel = _currentBlockLabel;
        var initialScope = CaptureLocals();
        var continueEdges = new List<(LocalScope Scope, string Label)>
        {
            (initialScope, entryLabel)
        };
        var breakEdges = new List<(LocalScope Scope, string Label)>();
        var carriedNames = initialScope.Locals.Keys
            .Where(name => !lowering.ResumeBaseLocals.Locals.ContainsKey(name))
            .ToArray();
        var immutableCarries = new List<(
            string Name,
            BoundType Type,
            string LlvmType,
            string EntryValue,
            string NextValue)>();
        var scalarCarries = new List<(string Name, string EntryPointer, string NextPointer)>();
        var structCarries = new List<(string Name, string EntryPointer, string NextPointer)>();
        var containerCarries = new List<(
            string Name,
            MutableContainerSlot Entry,
            string NextPointer,
            string NextLength,
            string NextCapacity)>();

        foreach (var name in carriedNames)
        {
            if (!initialScope.MutableLocals.Contains(name))
            {
                var value = initialScope.Locals[name];
                var materialized = MaterializeAggregateValue(value);
                immutableCarries.Add((
                    name,
                    value.Type,
                    materialized.TypeName,
                    materialized.ValueName,
                    NextTemp($"async_loop_{name}_next")));
                continue;
            }
            var prefix = name.TrimEnd('!');
            if (initialScope.MutableScalarSlots.TryGetValue(name, out var scalar))
            {
                scalarCarries.Add((name, scalar, NextTemp($"async_loop_{prefix}_slot_next")));
                continue;
            }
            if (initialScope.MutableStructSlots.TryGetValue(name, out var structure))
            {
                structCarries.Add((name, structure, NextTemp($"async_loop_{prefix}_struct_next")));
                continue;
            }
            if (initialScope.MutableContainerSlots.TryGetValue(name, out var container))
            {
                containerCarries.Add((
                    name,
                    container,
                    NextTemp($"async_loop_{prefix}_ptr_next"),
                    NextTemp($"async_loop_{prefix}_len_next"),
                    NextTemp($"async_loop_{prefix}_capacity_next")));
                continue;
            }
            throw new SollangException(
                $"mutable loop-carried binding '{name}' has no storage slot");
        }

        var conditionLabel = NextLabel("async_while_condition");
        var bodyLabel = NextLabel("async_while_body");
        var continueLabel = NextLabel("async_while_continue");
        var endLabel = NextLabel("async_while_end");
        // The statically-false edge makes the continue block a valid CFG
        // predecessor even when every source path breaks on its first
        // iteration. LLVM removes it, while the phi shape stays uniform.
        EmitConditionalBranch("false", continueLabel, conditionLabel);

        EmitLabel(conditionLabel);
        _currentBlockLabel = conditionLabel;
        foreach (var carry in immutableCarries)
        {
            var value = NextTemp($"async_loop_{carry.Name}");
            EmitPhi(
                value,
                carry.LlvmType,
                (carry.EntryValue, entryLabel),
                (carry.NextValue, continueLabel));
            _locals[carry.Name] = DematerializeAggregateValue(carry.Type, value);
        }
        foreach (var carry in scalarCarries)
        {
            var pointer = NextTemp($"async_loop_{carry.Name.TrimEnd('!')}_slot");
            EmitPhi(
                pointer,
                "ptr",
                (carry.EntryPointer, entryLabel),
                (carry.NextPointer, continueLabel));
            _mutableScalarSlots[carry.Name] = pointer;
        }
        foreach (var carry in structCarries)
        {
            var pointer = NextTemp($"async_loop_{carry.Name.TrimEnd('!')}_struct_slot");
            EmitPhi(
                pointer,
                "ptr",
                (carry.EntryPointer, entryLabel),
                (carry.NextPointer, continueLabel));
            _mutableStructSlots[carry.Name] = pointer;
        }
        foreach (var carry in containerCarries)
        {
            var prefix = carry.Name.TrimEnd('!');
            var pointer = NextTemp($"async_loop_{prefix}_ptr_slot");
            EmitPhi(
                pointer,
                "ptr",
                (carry.Entry.PointerAddress, entryLabel),
                (carry.NextPointer, continueLabel));
            var length = NextTemp($"async_loop_{prefix}_len_slot");
            EmitPhi(
                length,
                "ptr",
                (carry.Entry.LengthAddress, entryLabel),
                (carry.NextLength, continueLabel));
            var capacity = NextTemp($"async_loop_{prefix}_capacity_slot");
            EmitPhi(
                capacity,
                "ptr",
                (carry.Entry.CapacityAddress, entryLabel),
                (carry.NextCapacity, continueLabel));
            _mutableContainerSlots[carry.Name] = new MutableContainerSlot(
                pointer,
                length,
                capacity,
                StackAllocation: null);
        }

        var headerScope = CaptureLocals();
        var condition = EmitExpression(statement.Source) as RuntimeBool
            ?? throw new SollangException("while condition must be Bool");
        EmitConditionalBranch(condition.ValueName, bodyLabel, endLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var loopBodyScope = CaptureLocals();
        _asyncScopeSnapshots.Push(loopBodyScope);
        LocalScope bodyExitScope;
        try
        {
            EmitLoopBody(
                statement.Body,
                loopBodyScope,
                continueLabel,
                endLabel,
                continueEdges,
                breakEdges);
            bodyExitScope = CaptureLocals();
        }
        finally
        {
            _asyncScopeSnapshots.Pop();
        }
        if (!_currentBlockTerminated)
        {
            var bodyExitLabel = _currentBlockLabel;
            RestoreLocals(headerScope);
            foreach (var name in carriedNames)
            {
                if (!bodyExitScope.Locals.TryGetValue(name, out var value))
                {
                    throw new SollangException(
                        $"loop-carried binding '{name}' is consumed on the back-edge");
                }
                _locals[name] = value;
                if (headerScope.MutableScalarSlots.ContainsKey(name))
                {
                    _mutableScalarSlots[name] = bodyExitScope.MutableScalarSlots[name];
                }
                if (headerScope.MutableStructSlots.ContainsKey(name))
                {
                    _mutableStructSlots[name] = bodyExitScope.MutableStructSlots[name];
                }
                if (headerScope.MutableContainerSlots.ContainsKey(name))
                {
                    _mutableContainerSlots[name] = bodyExitScope.MutableContainerSlots[name];
                }
            }
            continueEdges.Add((CaptureLocals(), bodyExitLabel));
            EmitBranch(continueLabel);
        }

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        MergeAsyncOuterScope(headerScope, continueEdges);
        foreach (var carry in immutableCarries)
        {
            var current = MaterializeAggregateValue(ResolveLocal(carry.Name));
            EmitAssign(
                carry.NextValue,
                $"select i1 true, {carry.LlvmType} {current.ValueName}, {carry.LlvmType} {current.ValueName}");
        }
        foreach (var carry in scalarCarries)
        {
            EmitAssign(
                carry.NextPointer,
                $"getelementptr i8, ptr {_mutableScalarSlots[carry.Name]}, i64 0");
        }
        foreach (var carry in structCarries)
        {
            EmitAssign(
                carry.NextPointer,
                $"getelementptr i8, ptr {_mutableStructSlots[carry.Name]}, i64 0");
        }
        foreach (var carry in containerCarries)
        {
            var slot = _mutableContainerSlots[carry.Name];
            EmitAssign(carry.NextPointer, $"getelementptr i8, ptr {slot.PointerAddress}, i64 0");
            EmitAssign(carry.NextLength, $"getelementptr i8, ptr {slot.LengthAddress}, i64 0");
            EmitAssign(carry.NextCapacity, $"getelementptr i8, ptr {slot.CapacityAddress}, i64 0");
        }
        EmitBranch(conditionLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        if (breakEdges.Count == 0)
        {
            RestoreLocals(headerScope);
            return;
        }

        var exitEdges = new List<(LocalScope Scope, string Label)>
        {
            (headerScope, conditionLabel)
        };
        exitEdges.AddRange(breakEdges);
        MergeAsyncOuterScope(headerScope, exitEdges);
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
            _ => throw new SollangException($"{(bindKey ? "eachKey" : "eachValue")} expects a dictionary input")
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
            _ => throw new SollangException("dictionary iterator source was not lowered")
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
            throw new SollangException("dictionary iterator source was not lowered");
        }

        var outerLocals = CaptureLocals();
        try
        {
            _locals[statement.ItemName] = item;
            if (_program.Types.ContainsOwnedStorage(item.Type))
            {
                _borrowedOwnedLocals.Add(statement.ItemName);
            }
            EmitLoopBody(statement.Body, outerLocals, continueLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
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
        EmitCall(packed, "i64", "sollang_utf8_decode",
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
            EmitLoopBody(statement.Body, outerLocals, continueLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
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
            EmitLoopBody(statement.Body, outerLocals, continueLabel, endLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }

        if (!_currentBlockTerminated)
        {
            EmitBranch(continueLabel);
        }
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

