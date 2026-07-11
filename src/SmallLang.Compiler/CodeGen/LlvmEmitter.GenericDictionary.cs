using System.Globalization;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeInlineDictionary EmitInlineDictionaryLiteral(
        DictionaryLiteralExpression expression,
        BoundType dictionaryType,
        IReadOnlyList<(RuntimeValue Key, RuntimeValue Value)> entries)
    {
        var definition = _program.Types.GetDictionary(dictionaryType);
        var capacity = DictionaryCapacityForLength(entries.Count);
        var dictionary = new RuntimeInlineDictionary(
            dictionaryType, definition.KeyType, definition.ValueType,
            EmitInlineDictionaryAllocate(capacity.ToString(CultureInfo.InvariantCulture), definition),
            entries.Count.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture));
        foreach (var entry in entries)
        {
            EmitInlineDictionaryInsertUnique(dictionary, entry.Key, entry.Value);
        }
        return dictionary;
    }

    private RuntimeInlineDictionary EmitTypedEmptyInlineDictionary(
        TypedEmptyDictionaryExpression expression,
        BoundType dictionaryType)
    {
        var definition = _program.Types.GetDictionary(dictionaryType);
        if (expression.CapacityHint is null)
        {
            return new RuntimeInlineDictionary(
                dictionaryType, definition.KeyType, definition.ValueType, "null", "0", "0");
        }
        var capacity = DictionaryCapacityForLength(expression.CapacityHint.Value);
        return new RuntimeInlineDictionary(
            dictionaryType, definition.KeyType, definition.ValueType,
            EmitInlineDictionaryAllocate(capacity.ToString(CultureInfo.InvariantCulture), definition),
            "0", capacity.ToString(CultureInfo.InvariantCulture));
    }

    private string EmitInlineDictionaryAllocate(string capacity, BoundDictionaryDefinition definition)
    {
        var entriesOffset = EmitDictionaryEntriesOffset(capacity, "generic_dict_entries_offset");
        var entriesBytes = NextTemp("generic_dict_entries_bytes");
        EmitBinary(entriesBytes, "mul", "i64", capacity, definition.EntryStride.ToString(CultureInfo.InvariantCulture));
        var totalBytes = NextTemp("generic_dict_bytes");
        EmitBinary(totalBytes, "add", "i64", entriesOffset, entriesBytes);
        var pointer = EmitHeapAllocate(totalBytes);
        EmitZeroByteBuffer(pointer, capacity, "generic_dict_control_init");
        return pointer;
    }

    private string EmitInlineDictionaryEntryPointer(
        RuntimeInlineDictionary dictionary, string index, int fieldOffset, string prefix)
    {
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var entriesOffset = EmitDictionaryEntriesOffset(dictionary.CapacityName, prefix + "_entries_offset");
        var entries = NextTemp(prefix + "_entries");
        EmitAssign(entries, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {entriesOffset}");
        var entryOffset = NextTemp(prefix + "_entry_offset");
        EmitBinary(entryOffset, "mul", "i64", index, definition.EntryStride.ToString(CultureInfo.InvariantCulture));
        if (fieldOffset == 0)
        {
            var slot = NextTemp(prefix + "_slot");
            EmitAssign(slot, $"getelementptr i8, ptr {entries}, i64 {entryOffset}");
            return slot;
        }
        var offset = NextTemp(prefix + "_offset");
        EmitBinary(offset, "add", "i64", entryOffset, fieldOffset.ToString(CultureInfo.InvariantCulture));
        var fieldSlot = NextTemp(prefix + "_slot");
        EmitAssign(fieldSlot, $"getelementptr i8, ptr {entries}, i64 {offset}");
        return fieldSlot;
    }

    private void StoreInlineDictionaryEntry(
        RuntimeInlineDictionary dictionary, string index, RuntimeValue key, RuntimeValue value)
    {
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var keyValue = MaterializeAggregateValue(key);
        var valueValue = MaterializeAggregateValue(value);
        EmitStore(keyValue.TypeName, keyValue.ValueName,
            EmitInlineDictionaryEntryPointer(dictionary, index, 0, "generic_dict_key"), definition.KeyAlignment);
        EmitStore(valueValue.TypeName, valueValue.ValueName,
            EmitInlineDictionaryEntryPointer(dictionary, index, definition.ValueOffset, "generic_dict_value"), definition.ValueAlignment);
    }

    private RuntimeValue LoadInlineDictionaryField(
        RuntimeInlineDictionary dictionary, string index, BoundType type, int offset, int alignment, string prefix)
    {
        var slot = EmitInlineDictionaryEntryPointer(dictionary, index, offset, prefix);
        var loaded = NextTemp(prefix);
        EmitLoad(loaded, LlvmType(type), slot, alignment);
        return DematerializeAggregateValue(type, loaded);
    }

    private string LoadInlineDictionaryControl(RuntimeInlineDictionary dictionary, string slot)
    {
        var pointer = NextTemp("generic_dict_control_slot");
        EmitAssign(pointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {slot}");
        var control = NextTemp("generic_dict_control");
        EmitLoad(control, "i8", pointer, 1);
        return control;
    }

    private void StoreInlineDictionaryControl(RuntimeInlineDictionary dictionary, string slot, string control)
    {
        var pointer = NextTemp("generic_dict_control_slot");
        EmitAssign(pointer, $"getelementptr i8, ptr {dictionary.PointerName}, i64 {slot}");
        EmitStore("i8", control, pointer, 1);
    }

    private string EmitInlineDictionaryHash(RuntimeValue key)
    {
        if (key is RuntimeInt integer)
        {
            return EmitHashInt(integer.ValueName);
        }
        if (key is not RuntimeText text)
        {
            throw new SmallLangException($"dictionary key type {key.Type} has no Hash implementation");
        }

        var entry = _currentBlockLabel;
        var loop = NextLabel("text_hash");
        var body = NextLabel("text_hash_body");
        var done = NextLabel("text_hash_done");
        var nextI = NextTemp("text_hash_next_i");
        var nextHash = NextTemp("text_hash_next");
        EmitBranch(loop);
        EmitFunctionLine();
        EmitLabel(loop);
        var i = NextTemp("text_hash_i");
        EmitPhi(i, "i64", ("0", entry), (nextI, body));
        var hash = NextTemp("text_hash_value");
        EmitPhi(hash, "i64", ("-3750763034362895579", entry), (nextHash, body));
        var active = NextTemp("text_hash_active");
        EmitCompare(active, "ult", "i64", i, text.LengthName);
        EmitConditionalBranch(active, body, done);
        EmitFunctionLine();
        EmitLabel(body);
        var bytePointer = NextTemp("text_hash_byte_ptr");
        EmitAssign(bytePointer, $"getelementptr i8, ptr {text.PointerName}, i64 {i}");
        var item = NextTemp("text_hash_byte");
        EmitLoad(item, "i8", bytePointer, 1);
        var wide = NextTemp("text_hash_wide");
        EmitAssign(wide, $"zext i8 {item} to i64");
        var mixed = NextTemp("text_hash_mixed");
        EmitBinary(mixed, "xor", "i64", hash, wide);
        EmitBinary(nextHash, "mul", "i64", mixed, "1099511628211");
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loop);
        EmitFunctionLine();
        EmitLabel(done);
        _currentBlockLabel = done;
        return hash;
    }

    private string EmitInlineDictionaryKeysEqual(RuntimeValue left, RuntimeValue right)
    {
        if (left is RuntimeInt leftInt && right is RuntimeInt rightInt)
        {
            var equal = NextTemp("generic_dict_key_equal");
            EmitCompare(equal, "eq", "i64", leftInt.ValueName, rightInt.ValueName);
            return equal;
        }
        if (left is not RuntimeText leftText || right is not RuntimeText rightText)
        {
            throw new SmallLangException($"dictionary key type {left.Type} has no Eq implementation");
        }

        var sameLength = NextTemp("text_eq_length");
        EmitCompare(sameLength, "eq", "i64", leftText.LengthName, rightText.LengthName);
        var entry = _currentBlockLabel;
        var loop = NextLabel("text_eq");
        var body = NextLabel("text_eq_body");
        var next = NextLabel("text_eq_next");
        var mismatch = NextLabel("text_eq_mismatch");
        var match = NextLabel("text_eq_match");
        var done = NextLabel("text_eq_done");
        var nextI = NextTemp("text_eq_next_i");
        EmitConditionalBranch(sameLength, loop, mismatch);
        EmitFunctionLine();
        EmitLabel(loop);
        var i = NextTemp("text_eq_i");
        EmitPhi(i, "i64", ("0", entry), (nextI, next));
        var active = NextTemp("text_eq_active");
        EmitCompare(active, "ult", "i64", i, leftText.LengthName);
        EmitConditionalBranch(active, body, match);
        EmitFunctionLine();
        EmitLabel(body);
        var lp = NextTemp("text_eq_left_ptr");
        var rp = NextTemp("text_eq_right_ptr");
        EmitAssign(lp, $"getelementptr i8, ptr {leftText.PointerName}, i64 {i}");
        EmitAssign(rp, $"getelementptr i8, ptr {rightText.PointerName}, i64 {i}");
        var lb = NextTemp("text_eq_left");
        var rb = NextTemp("text_eq_right");
        EmitLoad(lb, "i8", lp, 1);
        EmitLoad(rb, "i8", rp, 1);
        var byteEqual = NextTemp("text_eq_byte");
        EmitCompare(byteEqual, "eq", "i8", lb, rb);
        EmitConditionalBranch(byteEqual, next, mismatch);
        EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loop);
        EmitFunctionLine();
        EmitLabel(match);
        EmitBranch(done);
        EmitFunctionLine();
        EmitLabel(mismatch);
        EmitBranch(done);
        EmitFunctionLine();
        EmitLabel(done);
        var result = NextTemp("text_eq_result");
        EmitPhi(result, "i1", ("true", match), ("false", mismatch));
        _currentBlockLabel = done;
        return result;
    }

    private RuntimeValue EmitInlineDictionaryLookup(RuntimeInlineDictionary dictionary, RuntimeValue key)
    {
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var hash = EmitInlineDictionaryHash(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var mask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var start = EmitDictionaryStartSlot(hash, mask);
        var entry = _currentBlockLabel;
        var loop = NextLabel("generic_dict_lookup");
        var body = NextLabel("generic_dict_lookup_body");
        var full = NextLabel("generic_dict_lookup_full");
        var compare = NextLabel("generic_dict_lookup_compare");
        var match = NextLabel("generic_dict_lookup_match");
        var next = NextLabel("generic_dict_lookup_next");
        var miss = NextLabel("generic_dict_lookup_miss");
        var done = NextLabel("generic_dict_lookup_done");
        var nextProbe = NextTemp("generic_dict_lookup_next_probe");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(loop);
        var probe = NextTemp("generic_dict_lookup_probe");
        EmitPhi(probe, "i64", ("0", entry), (nextProbe, next));
        var active = NextTemp("generic_dict_lookup_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, body, miss); EmitFunctionLine();
        EmitLabel(body);
        var unwrapped = NextTemp("generic_dict_lookup_unwrapped");
        EmitBinary(unwrapped, "add", "i64", start, probe);
        var slot = NextTemp("generic_dict_lookup_slot");
        EmitBinary(slot, "and", "i64", unwrapped, mask);
        var control = LoadInlineDictionaryControl(dictionary, slot);
        var empty = NextTemp("generic_dict_lookup_empty");
        EmitCompare(empty, "eq", "i8", control, "0");
        EmitConditionalBranch(empty, miss, full); EmitFunctionLine();
        EmitLabel(full);
        var h2Match = NextTemp("generic_dict_lookup_h2");
        EmitCompare(h2Match, "eq", "i8", control, h2);
        EmitConditionalBranch(h2Match, compare, next); EmitFunctionLine();
        EmitLabel(compare);
        _currentBlockLabel = compare;
        var storedKey = LoadInlineDictionaryField(dictionary, slot, definition.KeyType, 0, definition.KeyAlignment, "generic_dict_key");
        var equal = EmitInlineDictionaryKeysEqual(storedKey, key);
        EmitConditionalBranch(equal, match, next); EmitFunctionLine();
        EmitLabel(match);
        var value = LoadInlineDictionaryField(dictionary, slot, definition.ValueType, definition.ValueOffset, definition.ValueAlignment, "generic_dict_value");
        EmitBranch(done); EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(miss);
        EmitTrap(); EmitFunctionLine();
        EmitLabel(done);
        _currentBlockLabel = done;
        return value;
    }

    private void EmitInlineDictionaryInsertUnique(RuntimeInlineDictionary dictionary, RuntimeValue key, RuntimeValue value)
    {
        var hash = EmitInlineDictionaryHash(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var mask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var start = EmitDictionaryStartSlot(hash, mask);
        var entry = _currentBlockLabel;
        var loop = NextLabel("generic_dict_insert");
        var body = NextLabel("generic_dict_insert_body");
        var place = NextLabel("generic_dict_insert_place");
        var next = NextLabel("generic_dict_insert_next");
        var full = NextLabel("generic_dict_insert_full");
        var done = NextLabel("generic_dict_insert_done");
        var nextProbe = NextTemp("generic_dict_insert_next_probe");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(loop);
        var probe = NextTemp("generic_dict_insert_probe");
        EmitPhi(probe, "i64", ("0", entry), (nextProbe, next));
        var active = NextTemp("generic_dict_insert_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, body, full); EmitFunctionLine();
        EmitLabel(body);
        var unwrapped = NextTemp("generic_dict_insert_unwrapped");
        EmitBinary(unwrapped, "add", "i64", start, probe);
        var slot = NextTemp("generic_dict_insert_slot");
        EmitBinary(slot, "and", "i64", unwrapped, mask);
        var control = LoadInlineDictionaryControl(dictionary, slot);
        var empty = NextTemp("generic_dict_insert_empty");
        EmitCompare(empty, "eq", "i8", control, "0");
        EmitConditionalBranch(empty, place, next); EmitFunctionLine();
        EmitLabel(place);
        _currentBlockLabel = place;
        StoreInlineDictionaryEntry(dictionary, slot, key, value);
        StoreInlineDictionaryControl(dictionary, slot, h2);
        EmitBranch(done); EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(full);
        EmitTrap(); EmitFunctionLine();
        EmitLabel(done);
        _currentBlockLabel = done;
    }

    private DictionaryFindResult EmitInlineDictionaryFindSlot(RuntimeInlineDictionary dictionary, RuntimeValue key)
    {
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var hash = EmitInlineDictionaryHash(key);
        var h2 = EmitDictionaryH2Byte(hash);
        var mask = EmitDictionaryCapacityMask(dictionary.CapacityName);
        var start = EmitDictionaryStartSlot(hash, mask);
        var entry = _currentBlockLabel;
        var loop = NextLabel("generic_dict_find");
        var body = NextLabel("generic_dict_find_body");
        var empty = NextLabel("generic_dict_find_empty");
        var full = NextLabel("generic_dict_find_full");
        var compare = NextLabel("generic_dict_find_compare");
        var match = NextLabel("generic_dict_find_match");
        var next = NextLabel("generic_dict_find_next");
        var miss = NextLabel("generic_dict_find_miss");
        var done = NextLabel("generic_dict_find_done");
        var nextProbe = NextTemp("generic_dict_find_next_probe");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(loop);
        var probe = NextTemp("generic_dict_find_probe");
        EmitPhi(probe, "i64", ("0", entry), (nextProbe, next));
        var active = NextTemp("generic_dict_find_active");
        EmitCompare(active, "ult", "i64", probe, dictionary.CapacityName);
        EmitConditionalBranch(active, body, miss); EmitFunctionLine();
        EmitLabel(body);
        var unwrapped = NextTemp("generic_dict_find_unwrapped");
        EmitBinary(unwrapped, "add", "i64", start, probe);
        var slot = NextTemp("generic_dict_find_slot");
        EmitBinary(slot, "and", "i64", unwrapped, mask);
        var control = LoadInlineDictionaryControl(dictionary, slot);
        var isEmpty = NextTemp("generic_dict_find_empty_control");
        EmitCompare(isEmpty, "eq", "i8", control, "0");
        EmitConditionalBranch(isEmpty, empty, full); EmitFunctionLine();
        EmitLabel(empty); EmitBranch(done); EmitFunctionLine();
        EmitLabel(full);
        var h2Match = NextTemp("generic_dict_find_h2");
        EmitCompare(h2Match, "eq", "i8", control, h2);
        EmitConditionalBranch(h2Match, compare, next); EmitFunctionLine();
        EmitLabel(compare);
        _currentBlockLabel = compare;
        var storedKey = LoadInlineDictionaryField(dictionary, slot, definition.KeyType, 0, definition.KeyAlignment, "generic_dict_key");
        var equal = EmitInlineDictionaryKeysEqual(storedKey, key);
        EmitConditionalBranch(equal, match, next); EmitFunctionLine();
        EmitLabel(match); EmitBranch(done); EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextProbe, "add", "i64", probe, "1");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(miss); EmitBranch(done); EmitFunctionLine();
        EmitLabel(done);
        var found = NextTemp("generic_dict_find_found");
        EmitPhi(found, "i1", ("false", empty), ("true", match), ("false", miss));
        var foundSlot = NextTemp("generic_dict_find_slot_result");
        EmitPhi(foundSlot, "i64", (slot, empty), (slot, match), ("0", miss));
        _currentBlockLabel = done;
        return new DictionaryFindResult(found, foundSlot, h2);
    }

    private RuntimeInlineDictionary EmitInlineDictionaryPut(
        RuntimeInlineDictionary dictionary, RuntimeValue key, RuntimeValue value)
    {
        var found = EmitInlineDictionaryFindSlot(dictionary, key);
        var update = NextLabel("generic_dict_put_update");
        var insert = NextLabel("generic_dict_put_insert");
        var insertCurrent = NextLabel("generic_dict_put_current");
        var insertGrown = NextLabel("generic_dict_put_grown");
        var done = NextLabel("generic_dict_put_done");
        EmitConditionalBranch(found.FoundName, update, insert); EmitFunctionLine();
        EmitLabel(update);
        _currentBlockLabel = update;
        StoreInlineDictionaryEntry(dictionary, found.SlotName, key, value);
        EmitBranch(done);
        var updateEnd = _currentBlockLabel; EmitFunctionLine();
        EmitLabel(insert);
        _currentBlockLabel = insert;
        var nextLength = NextTemp("generic_dict_next_len");
        EmitBinary(nextLength, "add", "i64", dictionary.LengthName, "1");
        var numerator = NextTemp("generic_dict_load_num");
        var denominator = NextTemp("generic_dict_load_den");
        EmitBinary(numerator, "mul", "i64", nextLength, "8");
        EmitBinary(denominator, "mul", "i64", dictionary.CapacityName, "7");
        var shouldGrow = NextTemp("generic_dict_should_grow");
        EmitCompare(shouldGrow, "ugt", "i64", numerator, denominator);
        EmitConditionalBranch(shouldGrow, insertGrown, insertCurrent); EmitFunctionLine();
        EmitLabel(insertCurrent);
        _currentBlockLabel = insertCurrent;
        StoreInlineDictionaryEntry(dictionary, found.SlotName, key, value);
        StoreInlineDictionaryControl(dictionary, found.SlotName, found.H2ByteName);
        EmitBranch(done);
        var currentEnd = _currentBlockLabel; EmitFunctionLine();
        EmitLabel(insertGrown);
        _currentBlockLabel = insertGrown;
        var grown = EmitInlineDictionaryGrow(dictionary);
        EmitInlineDictionaryInsertUnique(grown, key, value);
        EmitBranch(done);
        var grownEnd = _currentBlockLabel; EmitFunctionLine();
        EmitLabel(done);
        var pointer = NextTemp("generic_dict_ptr");
        EmitPhi(pointer, "ptr", (dictionary.PointerName, updateEnd), (dictionary.PointerName, currentEnd), (grown.PointerName, grownEnd));
        var length = NextTemp("generic_dict_len");
        EmitPhi(length, "i64", (dictionary.LengthName, updateEnd), (nextLength, currentEnd), (nextLength, grownEnd));
        var capacity = NextTemp("generic_dict_capacity");
        EmitPhi(capacity, "i64", (dictionary.CapacityName, updateEnd), (dictionary.CapacityName, currentEnd), (grown.CapacityName, grownEnd));
        _currentBlockLabel = done;
        return dictionary with { PointerName = pointer, LengthName = length, CapacityName = capacity };
    }

    private RuntimeInlineDictionary EmitInlineDictionaryGrow(RuntimeInlineDictionary dictionary)
    {
        var zero = NextTemp("generic_dict_zero_capacity");
        EmitCompare(zero, "eq", "i64", dictionary.CapacityName, "0");
        var doubled = NextTemp("generic_dict_doubled_capacity");
        EmitBinary(doubled, "mul", "i64", dictionary.CapacityName, "2");
        var capacity = NextTemp("generic_dict_new_capacity");
        EmitSelect(capacity, zero, "i64 4", $"i64 {doubled}");
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var target = dictionary with
        {
            PointerName = EmitInlineDictionaryAllocate(capacity, definition),
            CapacityName = capacity
        };
        EmitInlineDictionaryRehash(dictionary, target);
        EmitCall(target: null, "void", "smalllang_free", $"ptr {dictionary.PointerName}");
        return target;
    }

    private void EmitInlineDictionaryRehash(RuntimeInlineDictionary source, RuntimeInlineDictionary target)
    {
        var definition = _program.Types.GetDictionary(source.DictionaryType);
        var entry = _currentBlockLabel;
        var loop = NextLabel("generic_dict_rehash");
        var body = NextLabel("generic_dict_rehash_body");
        var move = NextLabel("generic_dict_rehash_move");
        var next = NextLabel("generic_dict_rehash_next");
        var done = NextLabel("generic_dict_rehash_done");
        var nextI = NextTemp("generic_dict_rehash_next_i");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(loop);
        var i = NextTemp("generic_dict_rehash_i");
        EmitPhi(i, "i64", ("0", entry), (nextI, next));
        var active = NextTemp("generic_dict_rehash_active");
        EmitCompare(active, "ult", "i64", i, source.CapacityName);
        EmitConditionalBranch(active, body, done); EmitFunctionLine();
        EmitLabel(body);
        var control = LoadInlineDictionaryControl(source, i);
        var occupied = NextTemp("generic_dict_rehash_occupied");
        EmitCompare(occupied, "ne", "i8", control, "0");
        EmitConditionalBranch(occupied, move, next); EmitFunctionLine();
        EmitLabel(move);
        _currentBlockLabel = move;
        var key = LoadInlineDictionaryField(source, i, definition.KeyType, 0, definition.KeyAlignment, "generic_dict_rehash_key");
        var value = LoadInlineDictionaryField(source, i, definition.ValueType, definition.ValueOffset, definition.ValueAlignment, "generic_dict_rehash_value");
        EmitInlineDictionaryInsertUnique(target, key, value);
        EmitBranch(next); EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(done);
        _currentBlockLabel = done;
    }

    private void DropInlineDictionaryElements(RuntimeInlineDictionary dictionary)
    {
        var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
        var dropKeys = _program.Types.ContainsOwnedStorage(definition.KeyType);
        var dropValues = _program.Types.ContainsOwnedStorage(definition.ValueType);
        if (!dropKeys && !dropValues)
        {
            return;
        }
        var entry = _currentBlockLabel;
        var loop = NextLabel("drop_generic_dict");
        var body = NextLabel("drop_generic_dict_body");
        var occupied = NextLabel("drop_generic_dict_occupied");
        var next = NextLabel("drop_generic_dict_next");
        var done = NextLabel("drop_generic_dict_done");
        var nextI = NextTemp("drop_generic_dict_next_i");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(loop);
        var i = NextTemp("drop_generic_dict_i");
        EmitPhi(i, "i64", ("0", entry), (nextI, next));
        var active = NextTemp("drop_generic_dict_active");
        EmitCompare(active, "ult", "i64", i, dictionary.CapacityName);
        EmitConditionalBranch(active, body, done); EmitFunctionLine();
        EmitLabel(body);
        var control = LoadInlineDictionaryControl(dictionary, i);
        var full = NextTemp("drop_generic_dict_full");
        EmitCompare(full, "ne", "i8", control, "0");
        EmitConditionalBranch(full, occupied, next); EmitFunctionLine();
        EmitLabel(occupied);
        _currentBlockLabel = occupied;
        if (dropKeys)
        {
            var key = LoadInlineDictionaryField(dictionary, i, definition.KeyType, 0, definition.KeyAlignment, "drop_generic_dict_key");
            var materialized = MaterializeAggregateValue(key);
            EmitOwnedDropCall(definition.KeyType, materialized.ValueName);
        }
        if (dropValues)
        {
            var value = LoadInlineDictionaryField(dictionary, i, definition.ValueType, definition.ValueOffset, definition.ValueAlignment, "drop_generic_dict_value");
            var materialized = MaterializeAggregateValue(value);
            EmitOwnedDropCall(definition.ValueType, materialized.ValueName);
        }
        EmitBranch(next); EmitFunctionLine();
        EmitLabel(next);
        EmitBinary(nextI, "add", "i64", i, "1");
        EmitBranch(loop); EmitFunctionLine();
        EmitLabel(done);
        _currentBlockLabel = done;
    }
}
