using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitArenaFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.dynamic_int_array {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);
            EmitStatements(function.BlockBody);
            if (FinishTerminatedFunction()) return;
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.Arena, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var arena = (RuntimeArena)value;
            EmitRet("%smalllang.dynamic_int_array", BuildDynamicArrayAggregate(
                arena.PointerName, arena.UsedName, arena.CapacityName));
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private RuntimeArena EmitArenaFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("arena_call");
        EmitCall(aggregate, "%smalllang.dynamic_int_array", SymbolForFunction(function.Name)[1..],
            FunctionCallArgumentList(function, argument));
        var pointer = NextTemp("arena_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 0");
        var used = NextTemp("arena_used");
        EmitAssign(used, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 1");
        var capacity = NextTemp("arena_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 2");
        return new RuntimeArena(pointer, used, capacity);
    }

    private bool TryEmitArenaConstructor(CallExpression expression, out RuntimeArena arena)
    {
        arena = null!;
        if (expression.Path.Count != 1 || expression.Path[0] != "Arena")
        {
            return false;
        }
        if (!_platform.SupportsHeapAllocation)
        {
            throw new SmallLangException("Arena requires heap allocation on the current target");
        }
        var requested = EmitArenaSizeArgument(expression.Arguments[0], "arena_capacity");
        var positive = NextTemp("arena_capacity_positive");
        EmitCompare(positive, "ugt", "i64", requested, "0");
        var capacity = NextTemp("arena_capacity");
        EmitAssign(capacity, $"select i1 {positive}, i64 {requested}, i64 1");
        var pointer = EmitHeapAllocate(capacity);
        arena = new RuntimeArena(pointer, "0", capacity);
        return true;
    }

    private (RuntimeArena Arena, RuntimeInt Offset) EmitArenaAllocate(
        RuntimeArena arena,
        RuntimeInt bytes,
        RuntimeInt alignment)
    {
        var byteCount = EmitArenaSizeArgument(bytes, "arena_bytes");
        var align = EmitArenaSizeArgument(alignment, "arena_alignment");
        var nonzero = NextTemp("arena_alignment_nonzero");
        EmitCompare(nonzero, "ne", "i64", align, "0");
        EmitTrapUnless(nonzero, "arena_alignment_zero");
        var alignMinusOne = NextTemp("arena_alignment_minus_one");
        EmitBinary(alignMinusOne, "sub", "i64", align, "1");
        var alignBits = NextTemp("arena_alignment_bits");
        EmitBinary(alignBits, "and", "i64", align, alignMinusOne);
        var powerOfTwo = NextTemp("arena_alignment_power_of_two");
        EmitCompare(powerOfTwo, "eq", "i64", alignBits, "0");
        EmitTrapUnless(powerOfTwo, "arena_alignment_not_power_of_two");

        var padded = NextTemp("arena_padded");
        EmitBinary(padded, "add", "i64", arena.UsedName, alignMinusOne);
        var noPadOverflow = NextTemp("arena_padding_no_overflow");
        EmitCompare(noPadOverflow, "uge", "i64", padded, arena.UsedName);
        EmitTrapUnless(noPadOverflow, "arena_padding_overflow");
        var negativeAlign = NextTemp("arena_negative_alignment_mask");
        EmitBinary(negativeAlign, "sub", "i64", "0", align);
        var offset = NextTemp("arena_offset");
        EmitBinary(offset, "and", "i64", padded, negativeAlign);
        var end = NextTemp("arena_end");
        EmitBinary(end, "add", "i64", offset, byteCount);
        var noEndOverflow = NextTemp("arena_end_no_overflow");
        EmitCompare(noEndOverflow, "uge", "i64", end, offset);
        EmitTrapUnless(noEndOverflow, "arena_size_overflow");

        var fits = NextTemp("arena_fits");
        EmitCompare(fits, "ule", "i64", end, arena.CapacityName);
        var keepLabel = NextLabel("arena_keep");
        var growLabel = NextLabel("arena_grow");
        var mergeLabel = NextLabel("arena_allocated");
        EmitConditionalBranch(fits, keepLabel, growLabel);

        EmitLabel(keepLabel);
        _currentBlockLabel = keepLabel;
        EmitBranch(mergeLabel);

        EmitLabel(growLabel);
        _currentBlockLabel = growLabel;
        var doubled = NextTemp("arena_doubled");
        EmitBinary(doubled, "shl", "i64", arena.CapacityName, "1");
        var doubledEnough = NextTemp("arena_doubled_enough");
        EmitCompare(doubledEnough, "uge", "i64", doubled, end);
        var newCapacity = NextTemp("arena_new_capacity");
        EmitAssign(newCapacity, $"select i1 {doubledEnough}, i64 {doubled}, i64 {end}");
        var newPointer = EmitHeapAllocate(newCapacity);
        EmitInstruction($"call void @llvm.memcpy.p0.p0.i64(ptr {newPointer}, ptr {arena.PointerName}, i64 {arena.UsedName}, i1 false)");
        EmitCall(target: null, "void", "smalllang_free", $"ptr {arena.PointerName}");
        var growEndLabel = _currentBlockLabel;
        EmitBranch(mergeLabel);

        EmitLabel(mergeLabel);
        _currentBlockLabel = mergeLabel;
        var pointer = NextTemp("arena_pointer");
        EmitPhi(pointer, "ptr", (arena.PointerName, keepLabel), (newPointer, growEndLabel));
        var capacity = NextTemp("arena_capacity");
        EmitPhi(capacity, "i64", (arena.CapacityName, keepLabel), (newCapacity, growEndLabel));
        return (
            new RuntimeArena(pointer, end, capacity),
            new RuntimeInt(BoundType.UIntSize, EmitArenaResultSize(offset)));
    }

    private void EmitArenaStore(RuntimeArena arena, RuntimeInt offset, RuntimeInt value)
    {
        var index = EmitArenaSizeArgument(offset, "arena_store_offset");
        EmitArenaBoundsCheck(index, arena.UsedName, "arena_store");
        var slot = NextTemp("arena_store_slot");
        EmitAssign(slot, $"getelementptr i8, ptr {arena.PointerName}, i64 {index}");
        EmitStore("i8", value.ValueName, slot, 1);
    }

    private RuntimeInt EmitArenaLoad(RuntimeArena arena, RuntimeInt offset)
    {
        var index = EmitArenaSizeArgument(offset, "arena_load_offset");
        EmitArenaBoundsCheck(index, arena.UsedName, "arena_load");
        var slot = NextTemp("arena_load_slot");
        EmitAssign(slot, $"getelementptr i8, ptr {arena.PointerName}, i64 {index}");
        var value = NextTemp("arena_load_value");
        EmitLoad(value, "i8", slot, 1);
        return new RuntimeInt(BoundType.UInt8, value);
    }

    private void EmitArenaBoundsCheck(string offset, string used, string prefix)
    {
        var inBounds = NextTemp(prefix + "_in_bounds");
        EmitCompare(inBounds, "ult", "i64", offset, used);
        EmitTrapUnless(inBounds, prefix + "_bounds");
    }

    private string EmitArenaSizeArgument(Expression expression, string prefix) =>
        EmitArenaSizeArgument(EmitIntExpression(expression), prefix);

    private string EmitArenaSizeArgument(RuntimeInt value, string prefix)
    {
        var llvmType = LlvmType(value.Type);
        if (IsSignedIntegerType(value.Type))
        {
            var nonnegative = NextTemp(prefix + "_nonnegative");
            EmitCompare(nonnegative, "sge", llvmType, value.ValueName, "0");
            EmitTrapUnless(nonnegative, prefix + "_negative");
        }
        if (NumericBitWidth(value.Type) == 64)
        {
            return value.ValueName;
        }
        var widened = NextTemp(prefix);
        var extension = IsSignedIntegerType(value.Type) ? "sext" : "zext";
        EmitAssign(widened, $"{extension} {llvmType} {value.ValueName} to i64");
        return widened;
    }

    private string EmitArenaResultSize(string value)
    {
        if (_platform.PointerBitWidth == 64)
        {
            return value;
        }
        var narrowed = NextTemp("arena_size_result");
        EmitAssign(narrowed, $"trunc i64 {value} to i32");
        return narrowed;
    }
}
