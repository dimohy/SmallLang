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
        EmitAssign(slot, $"getelementptr inbounds [{allocatedLength.ToString(CultureInfo.InvariantCulture)} x i64], ptr {pointer}, i64 0, i64 {index.ToString(CultureInfo.InvariantCulture)}");
        EmitStore("i64", value, slot, 8);
    }

    private void StoreDynamicArrayElement(string pointer, string index, string value)
    {
        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr i64, ptr {pointer}, i64 {index}");
        EmitStore("i64", value, slot, 8);
    }

    private RuntimeInt EmitStaticArrayLoad(RuntimeStaticIntArray array, string index)
    {
        var inBounds = NextTemp("array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_bounds");

        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i64], ptr {array.PointerName}, i64 0, i64 {index}");
        return LoadInt(slot, "array_item");
    }

    private RuntimeInt EmitDynamicArrayLoad(RuntimeDynamicIntArray array, string index)
    {
        var inBounds = NextTemp("array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_bounds");

        var slot = NextTemp("array_slot");
        EmitAssign(slot, $"getelementptr i64, ptr {array.PointerName}, i64 {index}");
        return LoadInt(slot, "array_item");
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
        EmitAssign(keySlot, $"getelementptr i64, ptr {entriesPointer}, i64 {entryOffset}");
        var valueOffset = NextTemp("dict_value_offset");
        EmitBinary(valueOffset, "add", "i64", entryOffset, "1");
        var valueSlot = NextTemp("dict_value_slot");
        EmitAssign(valueSlot, $"getelementptr i64, ptr {entriesPointer}, i64 {valueOffset}");
        EmitStore("i64", key, keySlot, 8);
        EmitStore("i64", value, valueSlot, 8);
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
        EmitCompare(keyMatch, "eq", "i64", entryKey.ValueName, key);
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
        EmitAssign(slot, $"getelementptr i64, ptr {entriesPointer}, i64 {entryOffset}");
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
        EmitAssign(slot, $"getelementptr i64, ptr {entriesPointer}, i64 {valueOffset}");
        return LoadInt(slot, "dict_value");
    }

}

