using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeDynamicIntArray EmitDynamicArrayPush(RuntimeDynamicIntArray array, string value)
    {
        var hasCapacity = NextTemp("array_has_capacity");
        EmitCompare(hasCapacity, "ult", "i64", array.LengthName, array.CapacityName);

        var appendLabel = NextLabel("array_push_append");
        var growLabel = NextLabel("array_push_grow");
        var doneLabel = NextLabel("array_push_done");
        EmitConditionalBranch(hasCapacity, appendLabel, growLabel);
        EmitFunctionLine();

        EmitLabel(appendLabel);
        _currentBlockLabel = appendLabel;
        StoreDynamicArrayElement(array.PointerName, array.LengthName, value);
        var appendNextLen = NextTemp("array_next_len");
        EmitBinary(appendNextLen, "add", "i64", array.LengthName, "1");
        EmitBranch(doneLabel);
        var appendEnd = appendLabel;
        EmitFunctionLine();

        EmitLabel(growLabel);
        _currentBlockLabel = growLabel;
        var hasAnyCapacity = NextTemp("array_has_any_capacity");
        EmitCompare(hasAnyCapacity, "eq", "i64", array.CapacityName, "0");
        var doubledCapacity = NextTemp("array_doubled_capacity");
        EmitBinary(doubledCapacity, "mul", "i64", array.CapacityName, "2");
        var newCapacity = NextTemp("array_new_capacity");
        EmitSelect(newCapacity, hasAnyCapacity, "i64 4", $"i64 {doubledCapacity}");
        var newBytes = NextTemp("array_new_bytes");
        EmitBinary(newBytes, "mul", "i64", newCapacity, "8");
        var newPointer = EmitHeapAllocate(newBytes);
        EmitCopyIntBuffer(array.PointerName, newPointer, array.LengthName, "array_copy");
        EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
        StoreDynamicArrayElement(newPointer, array.LengthName, value);
        var growNextLen = NextTemp("array_next_len");
        EmitBinary(growNextLen, "add", "i64", array.LengthName, "1");
        EmitBranch(doneLabel);
        var growEnd = _currentBlockLabel;
        EmitFunctionLine();

        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
        var resultPointer = NextTemp("array_ptr");
        EmitPhi(resultPointer, "ptr", (array.PointerName, appendEnd), (newPointer, growEnd));
        var resultLength = NextTemp("array_len");
        EmitPhi(resultLength, "i64", (appendNextLen, appendEnd), (growNextLen, growEnd));
        var resultCapacity = NextTemp("array_capacity");
        EmitPhi(resultCapacity, "i64", (array.CapacityName, appendEnd), (newCapacity, growEnd));
        return new RuntimeDynamicIntArray(resultPointer, resultLength, resultCapacity);
    }

    private RuntimeDynamicIntArray EmitDynamicArrayAppendMove(RuntimeDynamicIntArray array, string value)
    {
        return EmitDynamicArrayPush(array, value);
    }

    private RuntimeDynamicIntArray EmitDynamicArrayUpdatedMove(RuntimeDynamicIntArray array, string index, string value)
    {
        var inBounds = NextTemp("array_update_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "array_update_bounds");

        StoreDynamicArrayElement(array.PointerName, index, value);
        return array;
    }

    private RuntimeIntDictionary EmitDictionaryPut(RuntimeIntDictionary dictionary, string key, string value)
    {
        var found = EmitDictionaryFindSlot(dictionary, key);

        var updateLabel = NextLabel("dict_put_update");
        var insertLabel = NextLabel("dict_put_insert");
        var currentInsertLabel = NextLabel("dict_put_insert_current");
        var growInsertLabel = NextLabel("dict_put_insert_grow");
        var doneLabel = NextLabel("dict_put_done");
        EmitConditionalBranch(found.FoundName, updateLabel, insertLabel);
        EmitFunctionLine();

        EmitLabel(updateLabel);
        _currentBlockLabel = updateLabel;
        StoreDictionaryEntry(dictionary, found.SlotName, key, value);
        EmitBranch(doneLabel);
        var updateEnd = updateLabel;
        EmitFunctionLine();

        EmitLabel(insertLabel);
        _currentBlockLabel = insertLabel;
        var nextLength = NextTemp("dict_next_len");
        EmitBinary(nextLength, "add", "i64", dictionary.LengthName, "1");
        var loadNumerator = NextTemp("dict_load_numerator");
        EmitBinary(loadNumerator, "mul", "i64", nextLength, "8");
        var loadDenominator = NextTemp("dict_load_denominator");
        EmitBinary(loadDenominator, "mul", "i64", dictionary.CapacityName, "7");
        var shouldGrow = NextTemp("dict_should_grow");
        EmitCompare(shouldGrow, "ugt", "i64", loadNumerator, loadDenominator);
        EmitConditionalBranch(shouldGrow, growInsertLabel, currentInsertLabel);
        EmitFunctionLine();

        EmitLabel(currentInsertLabel);
        _currentBlockLabel = currentInsertLabel;
        StoreDictionaryEntry(dictionary, found.SlotName, key, value);
        StoreDictionaryControl(dictionary, found.SlotName, found.H2ByteName);
        EmitBranch(doneLabel);
        var currentInsertEnd = _currentBlockLabel;
        EmitFunctionLine();

        EmitLabel(growInsertLabel);
        _currentBlockLabel = growInsertLabel;
        var grown = EmitDictionaryGrow(dictionary);
        EmitDictionaryInsertUnique(grown, key, value);
        EmitBranch(doneLabel);
        var growInsertEnd = _currentBlockLabel;
        EmitFunctionLine();

        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
        var resultPointer = NextTemp("dict_ptr");
        EmitPhi(resultPointer, "ptr", (dictionary.PointerName, updateEnd), (dictionary.PointerName, currentInsertEnd), (grown.PointerName, growInsertEnd));
        var resultLength = NextTemp("dict_len");
        EmitPhi(resultLength, "i64", (dictionary.LengthName, updateEnd), (nextLength, currentInsertEnd), (nextLength, growInsertEnd));
        var resultCapacity = NextTemp("dict_capacity");
        EmitPhi(resultCapacity, "i64", (dictionary.CapacityName, updateEnd), (dictionary.CapacityName, currentInsertEnd), (grown.CapacityName, growInsertEnd));
        return new RuntimeIntDictionary(resultPointer, resultLength, resultCapacity);
    }

    private RuntimeIntDictionary EmitDictionaryUpdatedMove(RuntimeIntDictionary dictionary, string key, string value)
    {
        return EmitDictionaryPut(dictionary, key, value);
    }

    private RuntimeIntDictionary EmitDictionaryGrow(RuntimeIntDictionary dictionary)
    {
        var newCapacity = NextTemp("dict_new_capacity");
        EmitBinary(newCapacity, "mul", "i64", dictionary.CapacityName, "2");
        var newDictionary = new RuntimeIntDictionary(
            EmitDictionaryAllocate(newCapacity),
            dictionary.LengthName,
            newCapacity);
        EmitDictionaryRehash(dictionary, newDictionary);
        EmitCall(target: null, "void", "smalllang_free", $"ptr {dictionary.PointerName}");
        return newDictionary;
    }

    private void EmitDictionaryRehash(RuntimeIntDictionary source, RuntimeIntDictionary target)
    {
        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("dict_rehash");
        var bodyLabel = NextLabel("dict_rehash_body");
        var moveLabel = NextLabel("dict_rehash_move");
        var nextLabel = NextLabel("dict_rehash_next");
        var doneLabel = NextLabel("dict_rehash_done");
        var nextI = NextTemp("dict_rehash_next_i");
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(loopLabel);
        var i = NextTemp("dict_rehash_i");
        EmitPhi(i, "i64", ("0", entryLabel), (nextI, nextLabel));
        var active = NextTemp("dict_rehash_active");
        EmitCompare(active, "ult", "i64", i, source.CapacityName);
        EmitConditionalBranch(active, bodyLabel, doneLabel);
        EmitFunctionLine();
        EmitLabel(bodyLabel);
        var control = LoadDictionaryControl(source, i);
        var isFull = NextTemp("dict_rehash_full");
        EmitCompare(isFull, "ne", "i8", control, "0");
        EmitConditionalBranch(isFull, moveLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(moveLabel);
        _currentBlockLabel = moveLabel;
        var key = LoadDictionaryKey(source, i);
        var value = LoadDictionaryValue(source, i);
        EmitDictionaryInsertUnique(target, key.ValueName, value.ValueName);
        EmitBranch(nextLabel);
        EmitFunctionLine();

        EmitLabel(nextLabel);
        _currentBlockLabel = nextLabel;
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

    private DictionaryFindResult EmitDictionaryFindSlot(
        RuntimeIntDictionary dictionary,
        string key)
    {
        var hash = EmitHashInt(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var capacityMask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var startSlot = EmitDictionaryStartSlot(hash, capacityMask);
        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("dict_find");
        var bodyLabel = NextLabel("dict_find_body");
        var emptyLabel = NextLabel("dict_find_empty");
        var fullLabel = NextLabel("dict_find_full");
        var compareLabel = NextLabel("dict_find_compare");
        var matchLabel = NextLabel("dict_find_match");
        var nextLabel = NextLabel("dict_find_next");
        var missLabel = NextLabel("dict_find_miss");
        var doneLabel = NextLabel("dict_find_done");
        var nextProbe = NextTemp("dict_find_next_probe");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(loopLabel);
        var probe = NextTemp("dict_find_probe");
        EmitPhi(probe, "i64", ("0", entryLabel), (nextProbe, nextLabel));
        var active = NextTemp("dict_find_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, bodyLabel, missLabel);
        EmitFunctionLine();

        EmitLabel(bodyLabel);
        var unwrappedSlot = NextTemp("dict_find_unwrapped");
        EmitBinary(unwrappedSlot, "add", "i64", startSlot, probe);
        var slot = NextTemp("dict_find_slot");
        EmitBinary(slot, "and", "i64", unwrappedSlot, capacityMask);
        var control = LoadDictionaryControl(dictionary, slot);
        var isEmpty = NextTemp("dict_find_is_empty");
        EmitCompare(isEmpty, "eq", "i8", control, "0");
        EmitConditionalBranch(isEmpty, emptyLabel, fullLabel);
        EmitFunctionLine();

        EmitLabel(emptyLabel);
        EmitBranch(doneLabel);
        EmitFunctionLine();

        EmitLabel(fullLabel);
        var h2Match = NextTemp("dict_find_h2_match");
        EmitCompare(h2Match, "eq", "i8", control, h2);
        EmitConditionalBranch(h2Match, compareLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(compareLabel);
        var entryKey = LoadDictionaryKey(dictionary, slot);
        var keyMatch = NextTemp("dict_find_key_match");
        EmitCompare(keyMatch, "eq", "i64", entryKey.ValueName, key);
        EmitConditionalBranch(keyMatch, matchLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(matchLabel);
        EmitBranch(doneLabel);
        EmitFunctionLine();

        EmitLabel(nextLabel);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(missLabel);
        EmitBranch(doneLabel);
        EmitFunctionLine();

        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
        var foundName = NextTemp("dict_find_found");
        EmitPhi(foundName, "i1", ("false", emptyLabel), ("true", matchLabel), ("false", missLabel));
        var foundSlot = NextTemp("dict_find_found_slot");
        EmitPhi(foundSlot, "i64", (slot, emptyLabel), (slot, matchLabel), ("0", missLabel));
        return new DictionaryFindResult(foundName, foundSlot, h2);
    }

    private void EmitDictionaryInsertUnique(RuntimeIntDictionary dictionary, string key, string value)
    {
        var hash = EmitHashInt(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var capacityMask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var startSlot = EmitDictionaryStartSlot(hash, capacityMask);
        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel("dict_insert");
        var bodyLabel = NextLabel("dict_insert_body");
        var placeLabel = NextLabel("dict_insert_place");
        var nextLabel = NextLabel("dict_insert_next");
        var fullLabel = NextLabel("dict_insert_full");
        var doneLabel = NextLabel("dict_insert_done");
        var nextProbe = NextTemp("dict_insert_next_probe");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(loopLabel);
        var probe = NextTemp("dict_insert_probe");
        EmitPhi(probe, "i64", ("0", entryLabel), (nextProbe, nextLabel));
        var active = NextTemp("dict_insert_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, bodyLabel, fullLabel);
        EmitFunctionLine();

        EmitLabel(bodyLabel);
        var unwrappedSlot = NextTemp("dict_insert_unwrapped");
        EmitBinary(unwrappedSlot, "add", "i64", startSlot, probe);
        var slot = NextTemp("dict_insert_slot");
        EmitBinary(slot, "and", "i64", unwrappedSlot, capacityMask);
        var control = LoadDictionaryControl(dictionary, slot);
        var isEmpty = NextTemp("dict_insert_is_empty");
        EmitCompare(isEmpty, "eq", "i8", control, "0");
        EmitConditionalBranch(isEmpty, placeLabel, nextLabel);
        EmitFunctionLine();

        EmitLabel(placeLabel);
        _currentBlockLabel = placeLabel;
        StoreDictionaryEntry(dictionary, slot, key, value);
        StoreDictionaryControl(dictionary, slot, h2);
        EmitBranch(doneLabel);
        EmitFunctionLine();

        EmitLabel(nextLabel);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();

        EmitLabel(fullLabel);
        EmitTrap();
        EmitFunctionLine();

        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

    private string LoadDictionaryControl(RuntimeIntDictionary dictionary, string slot)
    {
        var pointer = NextTemp("dict_control_slot");
        EmitAssign(pointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {slot}");
        var control = NextTemp("dict_control");
        EmitLoad(control, "i8", pointer, 1);
        return control;
    }

    private void StoreDictionaryControl(RuntimeIntDictionary dictionary, string slot, string control)
    {
        var pointer = NextTemp("dict_control_slot");
        EmitAssign(pointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {slot}");
        EmitStore("i8", control, pointer, 1);
    }

    private string EmitHashInt(string key)
    {
        var folded = NextTemp("hash_fold");
        var high = NextTemp("hash_high");
        EmitBinary(high, "lshr", "i64", key, "32");
        EmitBinary(folded, "xor", "i64", key, high);
        var hash = NextTemp("hash");
        EmitBinary(hash, "mul", "i64", folded, "-7046029254386353131");
        return hash;
    }

    private string EmitDictionaryH2Byte(string hash)
    {
        var h2Shifted = NextTemp("dict_h2_shifted");
        EmitBinary(h2Shifted, "lshr", "i64", hash, "57");
        var h2Raw = NextTemp("dict_h2_raw");
        EmitBinary(h2Raw, "and", "i64", h2Shifted, "127");
        var isZero = NextTemp("dict_h2_zero");
        EmitCompare(isZero, "eq", "i64", h2Raw, "0");
        var h2 = NextTemp("dict_h2");
        EmitSelect(h2, isZero, "i64 1", $"i64 {h2Raw}");
        var h2Byte = NextTemp("dict_h2_byte");
        EmitAssign(h2Byte, $"trunc i64 {h2} to i8");
        return h2Byte;
    }

    private string EmitDictionaryCapacityMask(string capacity)
    {
        var mask = NextTemp("dict_capacity_mask");
        EmitBinary(mask, "sub", "i64", capacity, "1");
        return mask;
    }

    private string EmitDictionaryStartSlot(string hash, string capacityMask)
    {
        var slot = NextTemp("dict_start_slot");
        EmitBinary(slot, "and", "i64", hash, capacityMask);
        return slot;
    }

    private void EmitZeroByteBuffer(string pointer, string count, string prefix)
    {
        EmitInstruction($"call void @llvm.memset.p0.i64(ptr {pointer}, i8 0, i64 {count}, i1 false)");
    }

    private void EmitCopyIntBuffer(string sourcePointer, string targetPointer, string count, string prefix)
    {
        var entryLabel = _currentBlockLabel;
        var loopLabel = NextLabel(prefix);
        var bodyLabel = NextLabel(prefix + "_body");
        var doneLabel = NextLabel(prefix + "_done");
        var nextI = NextTemp(prefix + "_next_i");
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(loopLabel);
        var i = NextTemp(prefix + "_i");
        EmitPhi(i, "i64", ("0", entryLabel), (nextI, bodyLabel));
        var active = NextTemp(prefix + "_active");
        EmitCompare(active, "ult", "i64", i, count);
        EmitConditionalBranch(active, bodyLabel, doneLabel);
        EmitFunctionLine();
        EmitLabel(bodyLabel);
        var sourceSlot = NextTemp(prefix + "_src");
        EmitAssign(sourceSlot, $"getelementptr i64, ptr {sourcePointer}, i64 {i}");
        var targetSlot = NextTemp(prefix + "_dst");
        EmitAssign(targetSlot, $"getelementptr i64, ptr {targetPointer}, i64 {i}");
        var value = LoadInt(sourceSlot, prefix + "_value");
        EmitStore("i64", value.ValueName, targetSlot, 8);
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loopLabel);
        EmitFunctionLine();
        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

}

