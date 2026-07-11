using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private string EmitHeapAllocate(string bytes)
    {
        if (!_platform.SupportsHeapAllocation)
        {
            throw new SmallLangException("heap allocation is not supported for this target yet");
        }

        var pointer = NextTemp("heap");
        EmitCall(pointer, "ptr", "smalllang_alloc", $"i64 {bytes}");
        var hasPointer = NextTemp("heap_ok");
        EmitCompare(hasPointer, "ne", "ptr", pointer, "null");
        EmitTrapUnless(hasPointer, "alloc");
        return pointer;
    }

    private void StoreStaticArrayElement(string pointer, int allocatedLength, int index, string value)
    {
        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr inbounds [{allocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {pointer}, i64 0, i64 {index.ToString(CultureInfo.InvariantCulture)}");
        EmitStore("i32", value, slot, 4);
    }

    private void StoreDynamicArrayElement(string pointer, string index, string value)
    {
        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr i32, ptr {pointer}, i64 {index}");
        EmitStore("i32", value, slot, 4);
    }

    private RuntimeInt EmitStaticArrayLoad(RuntimeStaticIntArray array, string index)
    {
        var inBounds = NextTemp("array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_bounds");

        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {array.PointerName}, i64 0, i64 {index}");
        return LoadInt(slot, "array_item");
    }

    private void StoreStaticTextArrayElement(string pointer, int index, RuntimeText value)
    {
        var slot = NextTemp("text_array_slot");
        EmitAssign(slot, $"getelementptr %smalllang.text, ptr {pointer}, i64 {index.ToString(CultureInfo.InvariantCulture)}");
        EmitStore("%smalllang.text", BuildTextAggregate(value), slot, 8);
    }

    private RuntimeText EmitStaticTextArrayLoad(RuntimeStaticTextArray array, string index)
    {
        var inBounds = NextTemp("text_array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "text_array_bounds");
        var slot = NextTemp("text_array_slot");
        EmitAssign(slot, $"getelementptr %smalllang.text, ptr {array.PointerName}, i64 {index}");
        var aggregate = NextTemp("text_array_item");
        EmitAssign(aggregate, $"load %smalllang.text, ptr {slot}, align 8");
        return ExtractTextAggregate(aggregate);
    }

    private void StoreStaticInlineArrayElement(
        string pointer,
        BoundStaticArrayDefinition definition,
        int index,
        RuntimeValue value)
    {
        var llvmType = LlvmType(definition.ElementType);
        var materialized = MaterializeAggregateValue(value);
        var slot = NextTemp("inline_array_slot");
        EmitAssign(
            slot,
            $"getelementptr {llvmType}, ptr {pointer}, i64 {index.ToString(CultureInfo.InvariantCulture)}");
        EmitStore(llvmType, materialized.ValueName, slot, definition.ElementAlignment);
    }

    private RuntimeValue EmitStaticInlineArrayLoad(RuntimeStaticInlineArray array, string index)
    {
        var inBounds = NextTemp("inline_array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "inline_array_bounds");
        var definition = _program.Types.GetStaticArray(array.ArrayType);
        var llvmType = LlvmType(definition.ElementType);
        var slot = NextTemp("inline_array_slot");
        EmitAssign(slot, $"getelementptr {llvmType}, ptr {array.PointerName}, i64 {index}");
        var value = NextTemp("inline_array_item");
        EmitAssign(
            value,
            $"load {llvmType}, ptr {slot}, align {definition.ElementAlignment.ToString(CultureInfo.InvariantCulture)}");
        return DematerializeAggregateValue(definition.ElementType, value);
    }

    private void EmitStaticArrayAssign(RuntimeStaticIntArray array, string index, string value)
    {
        var inBounds = NextTemp("array_assign_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_assign_bounds");

        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {array.PointerName}, i64 0, i64 {index}");
        EmitStore("i64", value, slot, 8);
    }

    private RuntimeInt EmitDynamicArrayLoad(RuntimeDynamicIntArray array, string index)
    {
        var inBounds = NextTemp("array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_bounds");

        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr i32, ptr {array.PointerName}, i64 {index}");
        return LoadInt(slot, "array_item");
    }

    private void StoreDynamicInlineArrayElement(
        string pointer,
        BoundDynamicArrayDefinition definition,
        string index,
        RuntimeValue value)
    {
        var llvmType = LlvmType(definition.ElementType);
        var materialized = MaterializeAggregateValue(value);
        var slot = NextTemp("dynamic_inline_slot");
        EmitAssign(slot, $"getelementptr {llvmType}, ptr {pointer}, i64 {index}");
        EmitStore(llvmType, materialized.ValueName, slot, definition.ElementAlignment);
    }

    private RuntimeValue EmitDynamicInlineArrayLoad(RuntimeDynamicInlineArray array, string index)
    {
        var inBounds = NextTemp("dynamic_inline_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "dynamic_inline_bounds");
        var definition = _program.Types.GetDynamicArray(array.ArrayType);
        var llvmType = LlvmType(definition.ElementType);
        var slot = NextTemp("dynamic_inline_slot");
        EmitAssign(slot, $"getelementptr {llvmType}, ptr {array.PointerName}, i64 {index}");
        var value = NextTemp("dynamic_inline_item");
        EmitLoad(value, llvmType, slot, definition.ElementAlignment);
        return DematerializeAggregateValue(definition.ElementType, value);
    }

    private void EmitDynamicInlineArrayAssign(RuntimeDynamicInlineArray array, string index, RuntimeValue value)
    {
        var inBounds = NextTemp("dynamic_inline_assign_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "dynamic_inline_assign_bounds");
        var definition = _program.Types.GetDynamicArray(array.ArrayType);
        StoreDynamicInlineArrayElement(array.PointerName, definition, index, value);
    }

    private RuntimeInt EmitIntSliceLoad(RuntimeIntSlice slice, string index)
    {
        var inBounds = NextTemp("slice_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, slice.LengthName);
        EmitTrapUnless(inBounds, "slice_bounds");

        var slot = NextTemp("slice_slot");
        EmitAssign(slot, $"getelementptr i32, ptr {slice.PointerName}, i64 {index}");
        return LoadInt(slot, "slice_item");
    }

    private void EmitDynamicArrayAssign(RuntimeDynamicIntArray array, string index, string value)
    {
        var inBounds = NextTemp("array_assign_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_assign_bounds");

        StoreDynamicArrayElement(array.PointerName, index, value);
    }

    private string EmitDictionaryAllocate(string capacity)
    {
        var entriesOffset = EmitDictionaryEntriesOffset(capacity, "dict_entries_offset");
        var entriesBytes = NextTemp("dict_entries_bytes");
        EmitBinary(entriesBytes, "mul", "i64", capacity, "16");
        var totalBytes = NextTemp("dict_bytes");
        EmitBinary(totalBytes, "add", "i64", entriesOffset, entriesBytes);
        var pointer = EmitHeapAllocate(totalBytes);
        EmitZeroByteBuffer(pointer, capacity, "dict_control_init");
        return pointer;
    }

    private string InitializeStackDictionary(string pointer, int capacity)
    {
        EmitZeroByteBuffer(
            pointer,
            capacity.ToString(CultureInfo.InvariantCulture),
            "dict_stack_control_init");
        return pointer;
    }

    private string EmitDictionaryEntriesOffset(string capacity, string prefix)
    {
        var offset = NextTemp(prefix);
        var isSmallCapacity = NextTemp(prefix + "_small");
        EmitCompare(isSmallCapacity, "eq", "i64", capacity, "4");
        EmitSelect(offset, isSmallCapacity, "i64 8", $"i64 {capacity}");
        return offset;
    }

    private void StoreDictionaryEntry(RuntimeIntDictionary dictionary, string index, string key, string value)
    {
        var entriesOffset = EmitDictionaryEntriesOffset(dictionary.CapacityName, "dict_entries_offset");
        var entriesPointer = NextTemp("dict_entries");
        EmitAssign(entriesPointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {entriesOffset}");
        var entryOffset = NextTemp("dict_offset");
        EmitBinary(entryOffset, "mul", "i64", index, "2");
        var keySlot = NextTemp("dict_key_slot");
        EmitAssign(keySlot, $"getelementptr i32, ptr {entriesPointer}, i64 {entryOffset}");
        var valueOffset = NextTemp("dict_value_offset");
        EmitBinary(valueOffset, "add", "i64", entryOffset, "1");
        var valueSlot = NextTemp("dict_value_slot");
        EmitAssign(valueSlot, $"getelementptr i32, ptr {entriesPointer}, i64 {valueOffset}");
        EmitStore("i32", key, keySlot, 4);
        EmitStore("i32", value, valueSlot, 4);
    }

    private RuntimeInt EmitDictionaryLookup(RuntimeIntDictionary dictionary, string key)
    {
        var hash = EmitHashInt(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var capacityMask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var startSlot = EmitDictionaryStartSlot(hash, capacityMask);
        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("dict_lookup");
        var bodyLabel = NextLabel("dict_lookup_body");
        var fullLabel = NextLabel("dict_lookup_full");
        var compareLabel = NextLabel("dict_lookup_compare");
        var matchLabel = NextLabel("dict_lookup_match");
        var nextLabel = NextLabel("dict_lookup_next");
        var missLabel = NextLabel("dict_lookup_miss");
        var doneLabel = NextLabel("dict_lookup_done");
        var nextProbe = NextTemp("dict_lookup_next_probe");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(loopLabel);
        var probe = NextTemp("dict_lookup_probe");
        EmitPhi(probe, "i64", ("0", entryLabel), (nextProbe, nextLabel));
        var active = NextTemp("dict_lookup_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, bodyLabel, missLabel);
        EmitFunctionLine();

        EmitLabel(bodyLabel);
        var unwrappedSlot = NextTemp("dict_lookup_unwrapped");
        EmitBinary(unwrappedSlot, "add", "i64", startSlot, probe);
        var slot = NextTemp("dict_lookup_slot");
        EmitBinary(slot, "and", "i64", unwrappedSlot, capacityMask);
        var control = LoadDictionaryControl(dictionary, slot);
        var isEmpty = NextTemp("dict_lookup_is_empty");
        EmitCompare(isEmpty, "eq", "i8", control, "0");
        EmitConditionalBranch(isEmpty, missLabel, fullLabel);
        EmitFunctionLine();

        EmitLabel(fullLabel);
        var h2Match = NextTemp("dict_lookup_h2_match");
        EmitCompare(h2Match, "eq", "i8", control, h2);
        EmitConditionalBranch(h2Match, compareLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(compareLabel);
        var entryKey = LoadDictionaryKey(dictionary, slot);
        var keyMatch = NextTemp("dict_lookup_key_match");
        EmitCompare(keyMatch, "eq", "i32", entryKey.ValueName, key);
        EmitConditionalBranch(keyMatch, matchLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(matchLabel);
        var value = LoadDictionaryValue(dictionary, slot);
        EmitBranch(doneLabel);
        EmitFunctionLine();

        EmitLabel(nextLabel);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(missLabel);
        EmitTrap();
        EmitFunctionLine();

        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
        return value;
    }

    private RuntimeInt LoadDictionaryKey(RuntimeIntDictionary dictionary, string index)
    {
        var entriesOffset = EmitDictionaryEntriesOffset(dictionary.CapacityName, "dict_entries_offset");
        var entriesPointer = NextTemp("dict_entries");
        EmitAssign(entriesPointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {entriesOffset}");
        var entryOffset = NextTemp("dict_key_offset");
        EmitBinary(entryOffset, "mul", "i64", index, "2");
        var slot = NextTemp("dict_key_slot");
        EmitAssign(slot, $"getelementptr i32, ptr {entriesPointer}, i64 {entryOffset}");
        return LoadInt(slot, "dict_key");
    }

    private RuntimeInt LoadDictionaryValue(RuntimeIntDictionary dictionary, string index)
    {
        var entriesOffset = EmitDictionaryEntriesOffset(dictionary.CapacityName, "dict_entries_offset");
        var entriesPointer = NextTemp("dict_entries");
        EmitAssign(entriesPointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {entriesOffset}");
        var entryOffset = NextTemp("dict_value_base");
        EmitBinary(entryOffset, "mul", "i64", index, "2");
        var valueOffset = NextTemp("dict_value_offset");
        EmitBinary(valueOffset, "add", "i64", entryOffset, "1");
        var slot = NextTemp("dict_value_slot");
        EmitAssign(slot, $"getelementptr i32, ptr {entriesPointer}, i64 {valueOffset}");
        return LoadInt(slot, "dict_value");
    }

}

