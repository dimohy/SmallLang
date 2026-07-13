using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitGlobalLine(string line = "")
    {
        _globals.Add(line + Environment.NewLine);
    }

    private void EmitGlobalBlock(string block)
    {
        _globals.Add(EnsureTrailingNewLine(block));
    }

    private void EmitFunctionLine(string line = "")
    {
        _functions.Add(line + Environment.NewLine);
        if (line.EndsWith(':'))
        {
            _currentBlockTerminated = false;
        }
    }

    private void EmitFunctionBlock(string block)
    {
        _functions.Add(EnsureTrailingNewLine(block));
    }

    private void EmitPlatformGlobalBlock(Action<StringBuilder> emit)
    {
        var block = new StringBuilder();
        emit(block);
        EmitGlobalBlock(block.ToString());
    }

    private void EmitPlatformFunctionBlock(Action<StringBuilder> emit)
    {
        var block = new StringBuilder();
        emit(block);
        EmitFunctionBlock(block.ToString());
    }

    private static string EnsureTrailingNewLine(string block)
    {
        return block.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? block
            : block + Environment.NewLine;
    }

    private bool TryResolveFunction(IReadOnlyList<string> path, out BoundFunction function)
    {
        var name = string.Join('.', path);
        if (_currentFunctions.TryGetValue(name, out function!))
        {
            return true;
        }

        return !name.Contains('.', StringComparison.Ordinal)
            && _currentFunction is { ModuleName.Length: > 0 } currentFunction
            && _currentFunctions.TryGetValue(currentFunction.ModuleName + "." + name, out function!);
    }

    private static IReadOnlyDictionary<string, BoundFunction> CreateFunctionScope(
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyDictionary<string, BoundFunction> localFunctions)
    {
        if (localFunctions.Count == 0)
        {
            return parentFunctions;
        }

        var functions = new Dictionary<string, BoundFunction>(parentFunctions, StringComparer.Ordinal);
        foreach (var (name, function) in localFunctions)
        {
            functions[name] = function;
        }

        return functions;
    }

    private RuntimeValue ResolveLocal(string name)
    {
        var value = _locals.TryGetValue(name, out var local)
            ? local
            : throw new SmallLangException($"unknown runtime binding '{name}'");

        if (_mutableStructSlots.TryGetValue(name, out var pointer))
        {
            var loaded = NextTemp("mutable_struct");
            EmitLoad(loaded, LlvmStructType(value.Type), pointer, 8);
            return new RuntimeStruct(value.Type, loaded);
        }
        if (_mutableScalarSlots.TryGetValue(name, out var scalarPointer))
        {
            var loaded = NextTemp("mutable_scalar");
            EmitLoad(loaded, LlvmType(value.Type), scalarPointer, RuntimeAlignment(value.Type));
            return DematerializeAggregateValue(value.Type, loaded);
        }

        return LoadMutableContainer(name, value);
    }

    private void EmitYield(RuntimeValue value, RuntimeBlockInvocation invocation)
    {
        var blockFunctionLocals = CaptureLocals();
        var blockFunctionFunctions = _currentFunctions;
        RestoreLocals(invocation.CallerLocals);
        _locals[invocation.ItemName] = value;
        var yieldedBlockLocals = CaptureLocals();
        _currentFunctions = invocation.CallerFunctions;
        try
        {
            EmitStatements(invocation.Body);
            DropOwnedLocalsCreatedSince(yieldedBlockLocals, transferredOwnerName: null);
        }
        finally
        {
            _currentFunctions = blockFunctionFunctions;
            RestoreLocals(blockFunctionLocals);
        }
    }

    private LocalScope CaptureLocals()
    {
        return new LocalScope(
            new Dictionary<string, RuntimeValue>(_locals, StringComparer.Ordinal),
            new HashSet<string>(_mutableLocals, StringComparer.Ordinal),
            new HashSet<string>(_borrowedMutableLocals, StringComparer.Ordinal),
            new HashSet<string>(_borrowedOwnedLocals, StringComparer.Ordinal),
            new Dictionary<string, MutableContainerSlot>(_mutableContainerSlots, StringComparer.Ordinal),
            new Dictionary<string, string>(_mutableStructSlots, StringComparer.Ordinal),
            new Dictionary<string, string>(_mutableScalarSlots, StringComparer.Ordinal));
    }

    private void ClearLocalState()
    {
        _locals.Clear();
        _mutableLocals.Clear();
        _borrowedMutableLocals.Clear();
        _borrowedOwnedLocals.Clear();
        _mutableContainerSlots.Clear();
        _mutableStructSlots.Clear();
        _mutableScalarSlots.Clear();
    }

    private void SelectStackFrame(BoundFunction function)
    {
        _currentStackFramePlan = _program.FunctionStackFrames.TryGetValue(function, out var frame)
            ? frame
            : StackFramePlan.Empty;
    }

    private void EmitStackFrameAllocations()
    {
        foreach (var slot in _currentStackFramePlan.Slots)
        {
            EmitAlloca(
                StackSlotPointer(slot.Index),
                $"[{slot.Size.ToString(CultureInfo.InvariantCulture)} x i8]",
                slot.Alignment);
        }
    }

    private string EmitStackLifetimeStart(object unit)
    {
        if (!_currentStackFramePlan.TryGetAllocation(unit, out var allocation))
        {
            throw new SmallLangException("stack-promoted container has no frame allocation");
        }

        var pointer = StackSlotPointer(allocation.SlotIndex);
        EmitInstruction(
            $"call void @llvm.lifetime.start.p0(i64 {allocation.Size.ToString(CultureInfo.InvariantCulture)}, ptr {pointer})");
        return pointer;
    }

    private void EmitStackLifetimeEndsAfter(object unit)
    {
        foreach (var allocation in _currentStackFramePlan.GetLifetimesEndingAfter(unit))
        {
            EmitStackLifetimeEnd(allocation);
        }
    }

    private void EmitStackLifetimeEnd(StackAllocationPlan allocation)
    {
        EmitInstruction(
            $"call void @llvm.lifetime.end.p0(i64 {allocation.Size.ToString(CultureInfo.InvariantCulture)}, ptr {StackSlotPointer(allocation.SlotIndex)})");
    }

    private static string StackSlotPointer(int slotIndex)
    {
        return $"%stack_slot{slotIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void RestoreLocals(LocalScope scope)
    {
        _locals.Clear();
        foreach (var (name, value) in scope.Locals)
        {
            _locals.Add(name, value);
        }

        _mutableLocals.Clear();
        foreach (var name in scope.MutableLocals)
        {
            _mutableLocals.Add(name);
        }

        _borrowedMutableLocals.Clear();
        foreach (var name in scope.BorrowedMutableLocals)
        {
            _borrowedMutableLocals.Add(name);
        }

        _borrowedOwnedLocals.Clear();
        foreach (var name in scope.BorrowedOwnedLocals)
        {
            _borrowedOwnedLocals.Add(name);
        }

        _mutableContainerSlots.Clear();
        foreach (var (name, slot) in scope.MutableContainerSlots)
        {
            _mutableContainerSlots.Add(name, slot);
        }

        _mutableStructSlots.Clear();
        foreach (var (name, pointer) in scope.MutableStructSlots)
        {
            _mutableStructSlots.Add(name, pointer);
        }

        _mutableScalarSlots.Clear();
        foreach (var (name, pointer) in scope.MutableScalarSlots)
        {
            _mutableScalarSlots.Add(name, pointer);
        }
    }

    private void DropOwnedLocals()
    {
        foreach (var (name, storedValue) in _locals.Reverse())
        {
            DropOwnedLocal(name, storedValue);
        }
    }

    private void DropOwnedLocalsCreatedSince(LocalScope outerScope, string? transferredOwnerName)
    {
        foreach (var (name, storedValue) in _locals.Reverse())
        {
            if (outerScope.Locals.ContainsKey(name))
            {
                continue;
            }

            if (string.Equals(name, transferredOwnerName, StringComparison.Ordinal))
            {
                EndMutableContainerSlotLifetime(name);
                continue;
            }

            DropOwnedLocal(name, storedValue);
        }
    }

    private void DropOwnedLocal(string name, RuntimeValue storedValue)
    {
        if (_borrowedMutableLocals.Contains(name) || _borrowedOwnedLocals.Contains(name))
        {
            return;
        }

        var value = LoadMutableContainer(name, storedValue);
        if (IsCustomOwnedType(value.Type))
        {
            var materialized = MaterializeAggregateValue(value);
            EmitOwnedDropCall(value.Type, materialized.ValueName);
            EndMutableContainerSlotLifetime(name);
            return;
        }

        switch (value)
        {
            case RuntimeStaticIntArray { Storage: RuntimeContainerStorage.Heap } array:
                EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                break;
            case RuntimeStaticTextArray { Storage: RuntimeContainerStorage.Heap } array:
                EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                break;
            case RuntimeStaticInlineArray { Storage: RuntimeContainerStorage.Heap } array:
                DropStaticInlineArrayElements(array);
                EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                break;
            case RuntimeDynamicIntArray { Storage: RuntimeContainerStorage.Heap } array:
                EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                break;
            case RuntimeDynamicInlineArray { Storage: RuntimeContainerStorage.Heap } array:
                DropDynamicInlineArrayElements(array);
                EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                break;
            case RuntimeIntDictionary { Storage: RuntimeContainerStorage.Heap } dictionary:
                EmitCall(target: null, "void", "smalllang_free", $"ptr {dictionary.PointerName}");
                break;
            case RuntimeInlineDictionary { Storage: RuntimeContainerStorage.Heap } dictionary:
                DropInlineDictionaryElements(dictionary);
                EmitCall(target: null, "void", "smalllang_free", $"ptr {dictionary.PointerName}");
                break;
            case RuntimeArena arena:
                EmitCall(target: null, "void", "smalllang_free", $"ptr {arena.PointerName}");
                break;
            case RuntimeMappedBytes mapped:
                EmitCall(target: null, "void", "smalllang_mapped_unmap",
                    $"ptr {mapped.BasePointerName}, i64 {mapped.MappedLengthName}");
                break;
        }

        EndMutableContainerSlotLifetime(name);
    }

    private void DropStaticInlineArrayElements(RuntimeStaticInlineArray array)
    {
        var definition = _program.Types.GetStaticArray(array.ArrayType);
        if (!_program.Types.ContainsOwnedStorage(definition.ElementType))
        {
            return;
        }

        var llvmType = LlvmType(definition.ElementType);
        for (var index = 0; index < array.Length; index++)
        {
            var slot = NextTemp("drop_array_slot");
            EmitAssign(
                slot,
                $"getelementptr {llvmType}, ptr {array.PointerName}, i64 {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var value = NextTemp("drop_array_value");
            EmitLoad(value, llvmType, slot, definition.ElementAlignment);
            EmitOwnedDropCall(definition.ElementType, value);
        }
    }

    private void DropDynamicInlineArrayElements(RuntimeDynamicInlineArray array)
    {
        var definition = _program.Types.GetDynamicArray(array.ArrayType);
        if (!_program.Types.ContainsOwnedStorage(definition.ElementType))
        {
            return;
        }

        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("drop_dynamic_array");
        var bodyLabel = NextLabel("drop_dynamic_array_body");
        var doneLabel = NextLabel("drop_dynamic_array_done");
        var nextIndex = NextTemp("drop_dynamic_array_next");
        var llvmType = LlvmType(definition.ElementType);
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(loopLabel);
        var index = NextTemp("drop_dynamic_array_index");
        EmitPhi(index, "i64", ("0", entryLabel), (nextIndex, bodyLabel));
        var active = NextTemp("drop_dynamic_array_active");
        EmitCompare(active, "ult", "i64", index, array.LengthName);
        EmitConditionalBranch(active, bodyLabel, doneLabel);
        EmitFunctionLine();
        EmitLabel(bodyLabel);
        var slot = NextTemp("drop_dynamic_array_slot");
        EmitAssign(slot, $"getelementptr {llvmType}, ptr {array.PointerName}, i64 {index}");
        var value = NextTemp("drop_dynamic_array_value");
        EmitLoad(value, llvmType, slot, definition.ElementAlignment);
        EmitOwnedDropCall(definition.ElementType, value);
        EmitBinary(nextIndex, "add", "i64", index, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

    private static bool RequiresHeapAllocation(RuntimeValue value)
    {
        return value is RuntimeStaticIntArray { Storage: RuntimeContainerStorage.Heap }
            or RuntimeStaticTextArray { Storage: RuntimeContainerStorage.Heap }
            or RuntimeStaticInlineArray { Storage: RuntimeContainerStorage.Heap }
            or RuntimeDynamicIntArray { Storage: RuntimeContainerStorage.Heap }
            or RuntimeDynamicInlineArray { Storage: RuntimeContainerStorage.Heap }
            or RuntimeIntDictionary { Storage: RuntimeContainerStorage.Heap }
            or RuntimeInlineDictionary { Storage: RuntimeContainerStorage.Heap }
            or RuntimeArena
            or RuntimeBox;
    }

    private bool IsOwnedContainerRuntimeValue(RuntimeValue value)
    {
        return value is RuntimeDynamicIntArray or RuntimeDynamicInlineArray or RuntimeIntDictionary or RuntimeInlineDictionary
            || _program.Types.ContainsOwnedStorage(value.Type);
    }

    private void RemoveLocal(string name)
    {
        EndMutableContainerSlotLifetime(name);
        _locals.Remove(name);
        _mutableLocals.Remove(name);
        _borrowedMutableLocals.Remove(name);
        _borrowedOwnedLocals.Remove(name);
        _mutableContainerSlots.Remove(name);
        _mutableStructSlots.Remove(name);
        _mutableScalarSlots.Remove(name);
    }

    private void CreateMutableContainerSlot(BindingStatement binding, RuntimeValue value)
    {
        if (!RequiresHeapAllocation(value))
        {
            if (value is not (RuntimeInt or RuntimeFloat or RuntimeBool or RuntimeText))
            {
                return;
            }
            var materialized = MaterializeAggregateValue(value);
            var scalarPointer = NextTemp("mutable_scalar_slot");
            EmitAlloca(scalarPointer, materialized.TypeName, RuntimeAlignment(value.Type));
            EmitStore(materialized.TypeName, materialized.ValueName, scalarPointer, RuntimeAlignment(value.Type));
            _mutableScalarSlots.Add(binding.Name, scalarPointer);
            return;
        }

        MutableContainerSlot slot;
        if (_currentStackFramePlan.TryGetAllocation(binding, out var allocation))
        {
            var pointerAddress = EmitStackLifetimeStart(binding);
            var lengthAddress = NextTemp("mutable_len_addr");
            EmitAssign(lengthAddress, $"getelementptr i8, ptr {pointerAddress}, i64 8");
            var capacityAddress = NextTemp("mutable_capacity_addr");
            EmitAssign(capacityAddress, $"getelementptr i8, ptr {pointerAddress}, i64 16");
            slot = new MutableContainerSlot(
                pointerAddress,
                lengthAddress,
                capacityAddress,
                allocation);
        }
        else
        {
            slot = new MutableContainerSlot(
                NextTemp("mutable_ptr_addr"),
                NextTemp("mutable_len_addr"),
                NextTemp("mutable_capacity_addr"),
                StackAllocation: null);
            EmitAlloca(slot.PointerAddress, "ptr", 8);
            EmitAlloca(slot.LengthAddress, "i64", 8);
            EmitAlloca(slot.CapacityAddress, "i64", 8);
        }

        _mutableContainerSlots[binding.Name] = slot;
        StoreMutableContainer(binding.Name, value);
    }

    private void EndMutableContainerSlotLifetime(string name)
    {
        if (_mutableContainerSlots.TryGetValue(name, out var slot)
            && slot.StackAllocation is not null)
        {
            EmitStackLifetimeEnd(slot.StackAllocation);
        }
    }

    private RuntimeValue LoadMutableContainer(string name, RuntimeValue value)
    {
        if (!_mutableContainerSlots.TryGetValue(name, out var slot))
        {
            return value;
        }

        var pointer = NextTemp("mutable_ptr");
        var length = NextTemp("mutable_len");
        var capacity = NextTemp("mutable_capacity");
        EmitLoad(pointer, "ptr", slot.PointerAddress, 8);
        EmitLoad(length, "i64", slot.LengthAddress, 8);
        EmitLoad(capacity, "i64", slot.CapacityAddress, 8);

        return value switch
        {
            RuntimeDynamicIntArray => new RuntimeDynamicIntArray(pointer, length, capacity),
            RuntimeDynamicInlineArray array => array with
            {
                PointerName = pointer,
                LengthName = length,
                CapacityName = capacity
            },
            RuntimeIntDictionary => new RuntimeIntDictionary(pointer, length, capacity),
            RuntimeArena => new RuntimeArena(pointer, length, capacity),
            RuntimeInlineDictionary dictionary => dictionary with
            {
                PointerName = pointer,
                LengthName = length,
                CapacityName = capacity
            },
            _ => value
        };
    }

    private void StoreMutableContainer(string name, RuntimeValue value)
    {
        if (!_mutableContainerSlots.TryGetValue(name, out var slot))
        {
            return;
        }

        switch (value)
        {
            case RuntimeDynamicIntArray array:
                EmitStore("ptr", array.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", array.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", array.CapacityName, slot.CapacityAddress, 8);
                break;
            case RuntimeDynamicInlineArray array:
                EmitStore("ptr", array.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", array.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", array.CapacityName, slot.CapacityAddress, 8);
                break;
            case RuntimeArena arena:
                EmitStore("ptr", arena.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", arena.UsedName, slot.LengthAddress, 8);
                EmitStore("i64", arena.CapacityName, slot.CapacityAddress, 8);
                break;
            case RuntimeIntDictionary dictionary:
                EmitStore("ptr", dictionary.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", dictionary.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", dictionary.CapacityName, slot.CapacityAddress, 8);
                break;
            case RuntimeInlineDictionary dictionary:
                EmitStore("ptr", dictionary.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", dictionary.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", dictionary.CapacityName, slot.CapacityAddress, 8);
                break;
        }
    }

    private static string? GetMoveConsumingContainerSourceName(Expression expression)
    {
        if (expression is not FlowExpression flow || flow.Targets.Count == 0)
        {
            return null;
        }

        var lastTarget = flow.Targets[^1];
        if (lastTarget.Path.Count != 1
            || lastTarget.Path[0] is not ("append" or "updated")
            || flow.Source is not NameExpression name)
        {
            return null;
        }

        return name.Name;
    }

    private string? GetBlockResultTransferredOwnerName(Expression expression)
    {
        if (expression is NameExpression name)
        {
            return name.Name;
        }

        var movedSourceName = GetMoveConsumingContainerSourceName(expression);
        if (movedSourceName is not null)
        {
            return movedSourceName;
        }

        if (expression is CallExpression call
            && TryResolveFunction(call.Path, out var callFunction)
            && FunctionConsumesOwnedHeapInput(callFunction)
            && call.Arguments.Count == 1
            && call.Arguments[0] is NameExpression argumentName)
        {
            return argumentName.Name;
        }

        if (expression is FlowExpression flow
            && flow.Source is NameExpression sourceName
            && flow.Targets.Any(target =>
                TryResolveFunction(target.Path, out var targetFunction)
                && FunctionConsumesOwnedHeapInput(targetFunction)))
        {
            return sourceName.Name;
        }

        if (expression is IfExpression conditional && conditional.Else is not null)
        {
            return CommonTransferredOwnerName(
                GetBlockTransferredOwnerName(conditional.Then),
                GetBlockTransferredOwnerName(conditional.Else));
        }

        if (expression is WhenExpression whenExpression)
        {
            var transferredName = GetBlockTransferredOwnerName(whenExpression.Else);
            foreach (var arm in whenExpression.Arms)
            {
                transferredName = CommonTransferredOwnerName(
                    transferredName,
                    GetBlockTransferredOwnerName(arm.Body));
                if (transferredName is null)
                {
                    return null;
                }
            }

            return transferredName;
        }

        return null;
    }

    private string? GetFunctionResultTransferredOwnerName(
        BoundFunction function,
        Expression expression)
    {
        if (FunctionConsumesOwnedHeapInput(function)
            && function.InputType == function.ReturnType)
        {
            var inputName = function.InputName ?? "it";
            if (TransfersOwnerName(expression, inputName, isResult: true))
            {
                return inputName;
            }
        }

        return GetBlockResultTransferredOwnerName(expression);
    }

    private bool TransfersOwnerName(Expression expression, string ownerName, bool isResult)
    {
        if (isResult && expression is NameExpression name && name.Name == ownerName)
        {
            return true;
        }

        if (string.Equals(
            GetMoveConsumingContainerSourceName(expression),
            ownerName,
            StringComparison.Ordinal))
        {
            return true;
        }

        if (expression is CallExpression call
            && TryResolveFunction(call.Path, out var callFunction)
            && FunctionConsumesOwnedHeapInput(callFunction)
            && call.Arguments.Count == 1
            && call.Arguments[0] is NameExpression argumentName
            && argumentName.Name == ownerName)
        {
            return true;
        }

        if (expression is FlowExpression flow
            && flow.Source is NameExpression sourceName
            && sourceName.Name == ownerName
            && flow.Targets.Any(target =>
                TryResolveFunction(target.Path, out var targetFunction)
                && FunctionConsumesOwnedHeapInput(targetFunction)))
        {
            return true;
        }

        if (expression is IfExpression conditional && conditional.Else is not null)
        {
            return TransfersOwnerName(conditional.Then, ownerName)
                && TransfersOwnerName(conditional.Else, ownerName);
        }

        return expression is WhenExpression whenExpression
            && TransfersOwnerName(whenExpression.Else, ownerName)
            && whenExpression.Arms.All(arm => TransfersOwnerName(arm.Body, ownerName));
    }

    private bool TransfersOwnerName(BlockBody body, string ownerName)
    {
        foreach (var statement in body.Statements)
        {
            var expression = statement switch
            {
                BindingStatement binding => binding.Value,
                ExpressionStatement expressionStatement => expressionStatement.Expression,
                _ => null
            };
            if (expression is not null && TransfersOwnerName(expression, ownerName, isResult: false))
            {
                return true;
            }
        }

        return body.Value is not null && TransfersOwnerName(body.Value, ownerName, isResult: true);
    }

    private string? GetBlockTransferredOwnerName(BlockBody body)
    {
        foreach (var statement in body.Statements)
        {
            var expression = statement switch
            {
                BindingStatement binding => binding.Value,
                ExpressionStatement expressionStatement => expressionStatement.Expression,
                _ => null
            };
            if (expression is null)
            {
                continue;
            }

            var transferredName = GetBlockResultTransferredOwnerName(expression);
            if (transferredName is not null)
            {
                return transferredName;
            }
        }

        return body.Value is null ? null : GetBlockResultTransferredOwnerName(body.Value);
    }

    private static string? CommonTransferredOwnerName(string? left, string? right)
    {
        return left is not null && string.Equals(left, right, StringComparison.Ordinal)
            ? left
            : null;
    }

    private GlobalString AddGlobalString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var name = "@.smalllang.str." + _stringId.ToString(CultureInfo.InvariantCulture);
        _stringId++;
        EmitGlobalLine($"""{name} = private unnamed_addr constant [{bytes.Length.ToString(CultureInfo.InvariantCulture)} x i8] c"{EscapeLlvmBytes(bytes)}", align 1""");
        return new GlobalString(name, bytes.Length);
    }

    private static string GetPlainText(Expression expression, int line, int column)
    {
        if (expression is not StringExpression str)
        {
            throw new SmallLangException($"codegen error at {line}:{column}: expected a string literal");
        }

        var segments = new List<string>();
        foreach (var segment in str.Segments)
        {
            if (segment is TextSegment text)
            {
                segments.Add(text.Text);
                continue;
            }

            throw new SmallLangException($"codegen error at {line}:{column}: expected a plain string literal");
        }

        return string.Concat(segments);
    }

    private static long ParseNumber(NumberExpression expression)
    {
        return long.TryParse(
            expression.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new SmallLangException($"codegen error at {expression.Line}:{expression.Column}: integer literal is out of range");
    }

    private static int DictionaryCapacityForLength(int length)
    {
        return IntDictionaryLayout.CapacityForLength(length);
    }

    private static string SymbolForFunction(string name)
    {
        return "@smalllang_fn_" + string.Concat(name.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }

    private string NextTemp(string prefix)
    {
        var name = "%" + prefix + _tempId.ToString(CultureInfo.InvariantCulture);
        _tempId++;
        return name;
    }

    private string NextLabel(string prefix)
    {
        var name = prefix + _labelId.ToString(CultureInfo.InvariantCulture);
        _labelId++;
        return name;
    }

    private string CurrentTemp(string prefix)
    {
        return "%" + prefix + (_tempId - 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string EscapeLlvmBytes(byte[] bytes)
    {
        return string.Concat(bytes.Select(static b =>
            b is >= 0x20 and <= 0x7E && b != (byte)'\\' && b != (byte)'"'
                ? ((char)b).ToString()
                : "\\" + b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private sealed record GlobalString(string Name, int Length);

    private abstract record RuntimeValue(BoundType Type);

    private sealed record RuntimeText(string PointerName, string LengthName) : RuntimeValue(BoundType.Text);

    private sealed record RuntimeInt(BoundType IntegerType, string ValueName) : RuntimeValue(IntegerType)
    {
        public RuntimeInt(string valueName) : this(BoundType.Int, valueName) { }
    }

    private sealed record RuntimeFloat(BoundType FloatType, string ValueName) : RuntimeValue(FloatType);

    private sealed record RuntimeBool(string ValueName) : RuntimeValue(BoundType.Bool);

    private sealed record RuntimeStruct(BoundType StructType, string ValueName) : RuntimeValue(StructType);

    private sealed record RuntimeEnum(BoundType EnumType, string ValueName) : RuntimeValue(EnumType);

    private sealed record RuntimeBox(BoundType BoxType, BoundType ElementType, string PointerName)
        : RuntimeValue(BoxType);

    private sealed record RuntimeIntSlice(string PointerName, string LengthName) : RuntimeValue(BoundType.IntSlice);

    private sealed record RuntimeStaticIntArray(
        string PointerName,
        string LengthName,
        int AllocatedLength,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Stack)
        : RuntimeValue(BoundType.StaticIntArray);

    private sealed record RuntimeStaticTextArray(
        string PointerName,
        string LengthName,
        int AllocatedLength,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(BoundType.StaticTextArray);

    private sealed record RuntimeStaticInlineArray(
        BoundType ArrayType,
        BoundType ElementType,
        string PointerName,
        string LengthName,
        int Length,
        int AllocatedLength,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(ArrayType);

    private sealed record RuntimeDynamicIntArray(
        string PointerName,
        string LengthName,
        string CapacityName,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(BoundType.DynamicIntArray);

    private sealed record RuntimeArena(string PointerName, string UsedName, string CapacityName)
        : RuntimeValue(BoundType.Arena);

    private sealed record RuntimeArguments(string LengthName)
        : RuntimeValue(BoundType.Arguments);

    private sealed record RuntimeMappedBytes(
        BoundType MappedType,
        string DataPointerName,
        string LengthName,
        string BasePointerName,
        string MappedLengthName)
        : RuntimeValue(MappedType);

    private sealed record RuntimeDynamicInlineArray(
        BoundType ArrayType,
        BoundType ElementType,
        string PointerName,
        string LengthName,
        string CapacityName,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(ArrayType);

    private enum RuntimeContainerStorage
    {
        Heap,
        Stack
    }

    private sealed record RuntimeIntDictionary(
        string PointerName,
        string LengthName,
        string CapacityName,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(BoundType.IntDictionary);

    private sealed record RuntimeIntDictionaryView(string PointerName, string LengthName, string CapacityName)
        : RuntimeValue(BoundType.IntDictionaryView);

    private sealed record RuntimeInlineDictionary(
        BoundType DictionaryType,
        BoundType KeyType,
        BoundType ValueType,
        string PointerName,
        string LengthName,
        string CapacityName,
        RuntimeContainerStorage Storage = RuntimeContainerStorage.Heap)
        : RuntimeValue(DictionaryType);

    private sealed record RuntimeMutableContainerReference(
        BoundType TargetType,
        string PointerAddress,
        string LengthAddress,
        string CapacityAddress)
        : RuntimeValue(TargetType);

    private sealed record RuntimeMutableStructReference(BoundType TargetType, string PointerAddress)
        : RuntimeValue(TargetType);

    private sealed record DictionaryFindResult(string FoundName, string SlotName, string H2ByteName);

    private sealed record RuntimeUnit() : RuntimeValue(BoundType.Unit)
    {
        public static RuntimeUnit Instance { get; } = new();
    }

    private sealed record BlockResult(RuntimeValue? Value, string EndLabel);

    private sealed record RuntimeFlowBinding(string Name, RuntimeValue Value);

    private sealed record RuntimeFlowResult(RuntimeValue? Value, RuntimeFlowBinding? Binding, string Ok);

    private sealed record MutableContainerSlot(
        string PointerAddress,
        string LengthAddress,
        string CapacityAddress,
        StackAllocationPlan? StackAllocation);

    private sealed record LocalScope(
        Dictionary<string, RuntimeValue> Locals,
        HashSet<string> MutableLocals,
        HashSet<string> BorrowedMutableLocals,
        HashSet<string> BorrowedOwnedLocals,
        Dictionary<string, MutableContainerSlot> MutableContainerSlots,
        Dictionary<string, string> MutableStructSlots,
        Dictionary<string, string> MutableScalarSlots);

    private sealed record LoopContext(string ContinueLabel, string BreakLabel, LocalScope OuterScope);

    private sealed record RuntimeBlockInvocation(
        string ItemName,
        IReadOnlyList<Statement> Body,
        LocalScope CallerLocals,
        IReadOnlyDictionary<string, BoundFunction> CallerFunctions);
}

