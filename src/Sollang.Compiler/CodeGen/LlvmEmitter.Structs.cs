using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private string EmitStructTypeDefinitions()
    {
        if (_program.Types.Structs.Count == 0 && _program.Types.Enums.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var definition in _program.Types.Structs
                     .Where(definition => ShouldEmitTypeDefinition(definition.Id))
                     .OrderBy(static definition => definition.Id))
        {
            var fields = string.Join(", ", definition.Fields.Select(field => LlvmType(field.Type)));
            builder.Append(LlvmStructType(definition.Id))
                .Append(" = type { ")
                .Append(fields)
                .AppendLine(" }");
        }

        foreach (var definition in _program.Types.Enums
                     .Where(definition => ShouldEmitTypeDefinition(definition.Id))
                     .OrderBy(static definition => definition.Id))
        {
            builder.Append(LlvmEnumType(definition.Id))
                .Append(" = type { i32, [")
                .Append(definition.PayloadWords.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" x i64] }");
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private bool ShouldEmitTypeDefinition(TypeId type) =>
        _usesDirectoryTraversal
        || type is not (TypeId.DirectoryRaw
            or TypeId.DirectoryEntry
            or TypeId.DynamicDirectoryEntryArray
            or TypeId.DirectoryRawResult
            or TypeId.DirectoryReadResult);

    private RuntimeStruct EmitStructLiteralExpression(StructLiteralExpression expression)
    {
        if (!_program.Types.TryResolve(expression.TypeName, out var type) || !_program.Types.IsStruct(type))
        {
            throw new SollangException($"unknown runtime struct type '{expression.TypeName}'");
        }

        var definition = _program.Types.GetStruct(type);
        var initializers = expression.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        var aggregate = "poison";
        foreach (var field in definition.Fields)
        {
            var value = EmitFunctionArgumentExpression(initializers[field.Name].Value, field.Type);
            EnsureRuntimeType(value, field.Type, $"{definition.Name}.{field.Name}");
            var materialized = MaterializeAggregateValue(value);
            var next = NextTemp("struct_init");
            EmitAssign(
                next,
                $"insertvalue {LlvmStructType(type)} {aggregate}, {materialized.TypeName} {materialized.ValueName}, {field.Index.ToString(CultureInfo.InvariantCulture)}");
            aggregate = next;
        }

        return new RuntimeStruct(type, aggregate);
    }

    private RuntimeStruct EmitContextualStructLiteral(
        DictionaryLiteralExpression expression,
        BoundType type)
    {
        var definition = _program.Types.GetStruct(type);
        var initializers = expression.Entries.ToDictionary(
            entry => ((NameExpression)entry.Key).Name,
            StringComparer.Ordinal);
        var aggregate = "poison";
        foreach (var field in definition.Fields)
        {
            var value = EmitFunctionArgumentExpression(initializers[field.Name].Value, field.Type);
            EnsureRuntimeType(value, field.Type, $"{definition.Name}.{field.Name}");
            var materialized = MaterializeAggregateValue(value);
            var next = NextTemp("contextual_struct_init");
            EmitAssign(next,
                $"insertvalue {LlvmStructType(type)} {aggregate}, {materialized.TypeName} {materialized.ValueName}, {field.Index.ToString(CultureInfo.InvariantCulture)}");
            aggregate = next;
        }
        return new RuntimeStruct(type, aggregate);
    }

    private void RemoveOwnedLiteralSources(Expression expression, BoundType expectedType)
    {
        if (!_program.Types.ContainsOwnedStorage(expectedType))
        {
            return;
        }

        if (expression is NameExpression name)
        {
            RemoveLocal(name.Name);
            return;
        }

        if (_program.Types.IsDynamicArray(expectedType)
            && expression is ArrayLiteralExpression array)
        {
            var elementType = _program.Types.GetDynamicArray(expectedType).ElementType;
            foreach (var element in array.Elements)
            {
                RemoveOwnedLiteralSources(element, elementType);
            }
            return;
        }

        if (_program.Types.IsDictionary(expectedType)
            && expression is DictionaryLiteralExpression dictionary)
        {
            var dictionaryDefinition = _program.Types.GetDictionary(expectedType);
            foreach (var entry in dictionary.Entries)
            {
                RemoveOwnedLiteralSources(entry.Key, dictionaryDefinition.KeyType);
                RemoveOwnedLiteralSources(entry.Value, dictionaryDefinition.ValueType);
            }
            return;
        }

        if (!_program.Types.IsStruct(expectedType))
        {
            return;
        }

        var definition = _program.Types.GetStruct(expectedType);
        IReadOnlyDictionary<string, Expression>? initializers = expression switch
        {
            StructLiteralExpression structure => structure.Fields.ToDictionary(
                static field => field.Name,
                static field => field.Value,
                StringComparer.Ordinal),
            DictionaryLiteralExpression contextual => contextual.Entries.ToDictionary(
                entry => ((NameExpression)entry.Key).Name,
                static entry => entry.Value,
                StringComparer.Ordinal),
            _ => null
        };
        if (initializers is null)
        {
            return;
        }

        foreach (var field in definition.Fields)
        {
            if (initializers.TryGetValue(field.Name, out var value))
            {
                RemoveOwnedLiteralSources(value, field.Type);
            }
        }
    }

    private RuntimeValue EmitFieldAccessExpression(FieldAccessExpression expression)
    {
        if (TryEmitPayloadlessEnumVariant(expression, out var enumValue))
        {
            return enumValue;
        }
        if (expression.Source is NameExpression functionOwner
            && !_locals.ContainsKey(functionOwner.Name)
            && _currentFunctions.TryGetValue(functionOwner.Name + "." + expression.FieldName, out var zeroArgumentFunction)
            && zeroArgumentFunction.InputType is null)
        {
            return EmitFunctionCall(zeroArgumentFunction, argument: null);
        }
        if (expression.Source is NameExpression typeName
            && !_locals.ContainsKey(typeName.Name)
            && _program.Types.TryResolve(typeName.Name, out var type)
            && _program.Types.IsStruct(type)
            && _currentFunctions.TryGetValue(typeName.Name + "." + expression.FieldName, out var associated)
            && associated.InputType is null)
        {
            return EmitFunctionCall(associated, argument: null);
        }

        var source = EmitExpression(expression.Source);
        if (source is RuntimeReference reference)
        {
            source = LoadReference(reference);
        }
        if (source is RuntimeBox box)
        {
            var loaded = NextTemp("box_value");
            EmitLoad(loaded, LlvmType(box.ElementType), box.PointerName, RuntimeAlignment(box.ElementType));
            source = DematerializeAggregateValue(box.ElementType, loaded);
        }
        if (source is not RuntimeStruct value)
        {
            throw new SollangException("field access expects a runtime struct value");
        }

        var definition = _program.Types.GetStruct(value.Type);
        var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == expression.FieldName);
        if (field is null)
        {
            if (TryResolveInstanceMethod(value.Type, expression.FieldName, out var method)
                && method.InputOwnership == BoundFunctionInputOwnership.Default)
            {
                return EmitFunctionCall(method, value);
            }

            throw new SollangException(
                $"struct '{definition.Name}' has no field or readonly computed member '{expression.FieldName}'");
        }
        var extracted = NextTemp("field");
        EmitAssign(
            extracted,
            $"extractvalue {LlvmStructType(value.Type)} {value.ValueName}, {field.Index.ToString(CultureInfo.InvariantCulture)}");
        return DematerializeAggregateValue(field.Type, extracted);
    }

    private RuntimeValue LoadReference(RuntimeReference reference)
    {
        var loaded = NextTemp("ref_value");
        EmitLoad(
            loaded,
            LlvmType(reference.ElementType),
            reference.PointerName,
            RuntimeAlignment(reference.ElementType));
        return DematerializeAggregateValue(reference.ElementType, loaded);
    }

    private RuntimeReference EmitReferencePlace(Expression expression, BoundType expectedReferenceType)
    {
        var expectedElementType = _program.Types.GetReference(expectedReferenceType).ElementType;
        var place = EmitReferencePlace(expression);
        if (place.ElementType != expectedElementType)
        {
            throw new SollangException(
                $"reference place has type {place.ElementType} but {expectedElementType} was expected");
        }
        return new RuntimeReference(expectedReferenceType, expectedElementType, place.PointerName);
    }

    private RuntimeReference EmitReferencePlace(Expression expression)
    {
        if (expression is NameExpression name
            && _locals.TryGetValue(name.Name, out var local))
        {
            if (local is RuntimeReference reference)
            {
                return reference;
            }
            if (_mutableStructSlots.TryGetValue(name.Name, out var mutablePointer))
            {
                var mutableReferenceType = _program.Types.GetOrAddReference(local.Type);
                return new RuntimeReference(mutableReferenceType, local.Type, mutablePointer);
            }
            if (_readonlyCaptureBorrowPointers.TryGetValue(name.Name, out var capturePointer))
            {
                var captureReferenceType = _program.Types.GetOrAddReference(local.Type);
                return new RuntimeReference(captureReferenceType, local.Type, capturePointer);
            }

            var materialized = MaterializeAggregateValue(local);
            var pointer = NextTemp("ref_place");
            EmitAlloca(pointer, materialized.TypeName, RuntimeAlignment(local.Type));
            EmitStore(materialized.TypeName, materialized.ValueName, pointer, RuntimeAlignment(local.Type));
            var referenceType = _program.Types.GetOrAddReference(local.Type);
            return new RuntimeReference(referenceType, local.Type, pointer);
        }

        if (expression is FieldAccessExpression field)
        {
            var source = EmitReferencePlace(field.Source);
            if (!_program.Types.IsStruct(source.ElementType))
            {
                throw new SollangException("reference field access expects a struct place");
            }
            var definition = _program.Types.GetStruct(source.ElementType);
            var member = definition.Fields.FirstOrDefault(candidate => candidate.Name == field.FieldName)
                ?? throw new SollangException(
                    $"struct '{definition.Name}' has no field '{field.FieldName}'");
            var pointer = NextTemp("ref_field");
            EmitAssign(
                pointer,
                $"getelementptr inbounds {LlvmStructType(source.ElementType)}, ptr {source.PointerName}, i32 0, i32 {member.Index.ToString(CultureInfo.InvariantCulture)}");
            if (_program.Types.IsReference(member.Type))
            {
                var storedPointer = NextTemp("stored_ref");
                EmitLoad(storedPointer, "ptr", pointer, RuntimeAlignment(member.Type));
                return new RuntimeReference(
                    member.Type,
                    _program.Types.GetReference(member.Type).ElementType,
                    storedPointer);
            }
            var memberReferenceType = _program.Types.GetOrAddReference(member.Type);
            return new RuntimeReference(memberReferenceType, member.Type, pointer);
        }

        if (expression is IndexExpression index)
        {
            return EmitReferenceIndexPlace(index);
        }

        throw new SollangException(
            "reference returns currently require a named reference input, field, or array element");
    }

    private RuntimeReference EmitReferenceIndexPlace(IndexExpression expression)
    {
        var source = EmitExpression(expression.Source);
        var indexSize = string.Empty;
        if (source is not RuntimeInlineDictionary)
        {
            var index = EmitIntExpression(expression.Index);
            indexSize = EmitIntAsSize(index, "ref_index_size");
        }

        BoundType elementType;
        string pointer;
        switch (source)
        {
            case RuntimeIntSlice slice:
                elementType = BoundType.Int;
                pointer = EmitReferenceElementPointer(
                    slice.PointerName,
                    slice.LengthName,
                    "i32",
                    indexSize,
                    "ref_slice");
                break;
            case RuntimeStaticIntArray array:
                elementType = BoundType.Int;
                pointer = EmitReferenceStaticIntElementPointer(array, indexSize);
                break;
            case RuntimeStaticTextArray array:
                elementType = BoundType.Text;
                pointer = EmitReferenceElementPointer(
                    array.PointerName,
                    array.LengthName,
                    "%sollang.text",
                    indexSize,
                    "ref_text_array");
                break;
            case RuntimeStaticInlineArray array:
                elementType = array.ElementType;
                pointer = EmitReferenceElementPointer(
                    array.PointerName,
                    array.LengthName,
                    LlvmType(array.ElementType),
                    indexSize,
                    "ref_inline_array");
                break;
            case RuntimeDynamicIntArray array:
                elementType = BoundType.Int;
                pointer = EmitReferenceElementPointer(
                    array.PointerName,
                    array.LengthName,
                    "i32",
                    indexSize,
                    "ref_dynamic_array");
                break;
            case RuntimeDynamicInlineArray array:
                elementType = array.ElementType;
                pointer = EmitReferenceElementPointer(
                    array.PointerName,
                    array.LengthName,
                    LlvmType(array.ElementType),
                    indexSize,
                    "ref_dynamic_inline_array");
                break;
            case RuntimeInlineDictionary dictionary:
                elementType = dictionary.ValueType;
                var key = EmitExpression(expression.Index);
                var found = EmitInlineDictionaryFindSlot(dictionary, key);
                EmitTrapUnless(found.FoundName, "ref_dictionary_missing");
                var definition = _program.Types.GetDictionary(dictionary.DictionaryType);
                pointer = EmitInlineDictionaryEntryPointer(
                    dictionary,
                    found.SlotName,
                    definition.ValueOffset,
                    "ref_dictionary_value");
                if (_program.Types.IsReference(elementType))
                {
                    var storedPointer = NextTemp("ref_dictionary_stored");
                    EmitLoad(storedPointer, "ptr", pointer, RuntimeAlignment(elementType));
                    pointer = storedPointer;
                    elementType = _program.Types.GetReference(elementType).ElementType;
                }
                break;
            default:
                throw new SollangException("reference indexing currently requires an array, dictionary, or IntSlice place");
        }

        return new RuntimeReference(_program.Types.GetOrAddReference(elementType), elementType, pointer);
    }

    private string EmitReferenceStaticIntElementPointer(RuntimeStaticIntArray array, string index)
    {
        var inBounds = NextTemp("ref_array_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, array.LengthName);
        EmitTrapUnless(inBounds, "ref_array_bounds");
        var pointer = NextTemp("ref_array_place");
        EmitAssign(
            pointer,
            $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {array.PointerName}, i64 0, i64 {index}");
        return pointer;
    }

    private string EmitReferenceElementPointer(
        string sourcePointer,
        string length,
        string llvmElementType,
        string index,
        string prefix)
    {
        var inBounds = NextTemp(prefix + "_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, length);
        EmitTrapUnless(inBounds, prefix + "_bounds");
        var pointer = NextTemp(prefix + "_place");
        EmitAssign(pointer, $"getelementptr {llvmElementType}, ptr {sourcePointer}, i64 {index}");
        return pointer;
    }

    private (string TypeName, string ValueName) MaterializeAggregateValue(RuntimeValue value)
    {
        return value switch
        {
            RuntimeInt integer => (LlvmType(integer.Type), integer.ValueName),
            RuntimeFloat floating => (LlvmType(floating.Type), floating.ValueName),
            RuntimeBool boolean => ("i1", boolean.ValueName),
            RuntimeTask task => ("%sollang.task", BuildTaskAggregate(task)),
            RuntimeProducerStream stream => (
                stream.IsEvent ? "%sollang.event_stream" : "%sollang.stream",
                BuildProducerStreamAggregate(stream)),
            RuntimeText text => ("%sollang.text", BuildTextAggregate(text)),
            RuntimeSourceText source => ("%sollang.source_text", BuildSourceTextAggregate(source)),
            RuntimeMappedBytes mapped => ("%sollang.mapped_bytes", BuildMappedBytesAggregate(mapped)),
            RuntimeStruct structure => (LlvmStructType(structure.Type), structure.ValueName),
            RuntimeEnum enumeration => (LlvmEnumType(enumeration.Type), enumeration.ValueName),
            RuntimeBox box => ("ptr", box.PointerName),
            RuntimeReference reference => ("ptr", reference.PointerName),
            RuntimeDynTrait dyn => ("%sollang.dyn", BuildDynTraitAggregate(dyn)),
            RuntimeDynamicIntArray array => (
                "%sollang.dynamic_int_array",
                BuildDynamicArrayAggregate(array.PointerName, array.LengthName, array.CapacityName)),
            RuntimeDynamicInlineArray array => (
                "%sollang.dynamic_int_array",
                BuildDynamicArrayAggregate(array.PointerName, array.LengthName, array.CapacityName)),
            RuntimeIntSlice slice => (
                "%sollang.int_slice",
                BuildIntSliceAggregate(slice.PointerName, slice.LengthName)),
            RuntimeIntDictionaryView dictionary => (
                "%sollang.int_dictionary",
                BuildDictionaryAggregate(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName)),
            RuntimeIntDictionary dictionary => (
                "%sollang.int_dictionary",
                BuildDictionaryAggregate(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName)),
            RuntimeInlineDictionary dictionary => (
                "%sollang.int_dictionary",
                BuildDictionaryAggregate(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName)),
            _ => throw new SollangException($"type {value.Type} is not supported in an inline struct field")
        };
    }

    private string BuildTaskAggregate(RuntimeTask task)
    {
        var withHandle = NextTemp("task_with_handle");
        EmitAssign(withHandle, $"insertvalue %sollang.task poison, ptr {task.HandleName}, 0");
        var aggregate = NextTemp("task_with_context");
        EmitAssign(aggregate, $"insertvalue %sollang.task {withHandle}, ptr {task.ContextName}, 1");
        return aggregate;
    }

    private string BuildProducerStreamAggregate(RuntimeProducerStream stream)
    {
        var llvmType = stream.IsEvent ? "%sollang.event_stream" : "%sollang.stream";
        var withContext = NextTemp("stream_with_context");
        EmitAssign(withContext, $"insertvalue {llvmType} poison, ptr {stream.ContextName}, 0");
        var withNext = NextTemp("stream_with_next");
        EmitAssign(withNext, $"insertvalue {llvmType} {withContext}, ptr {stream.NextName}, 1");
        var aggregate = NextTemp("stream_with_drop");
        EmitAssign(aggregate, $"insertvalue {llvmType} {withNext}, ptr {stream.DropName}, 2");
        return aggregate;
    }

    private RuntimeValue DematerializeAggregateValue(BoundType type, string valueName)
    {
        if (type == BoundType.Unit)
        {
            return RuntimeUnit.Instance;
        }
        if (type is BoundType.MappedBytes or BoundType.MutableMappedBytes)
        {
            return ExtractMappedBytesAggregate(type, valueName);
        }
        if (type == BoundType.SourceText)
        {
            return ExtractSourceTextAggregate(valueName);
        }
        if (_program.Types.IsStruct(type))
        {
            return new RuntimeStruct(type, valueName);
        }
        if (_program.Types.IsEnum(type))
        {
            return new RuntimeEnum(type, valueName);
        }
        if (_program.Types.IsBox(type))
        {
            var box = _program.Types.GetBox(type);
            return new RuntimeBox(type, box.ElementType, valueName);
        }
        if (_program.Types.IsReference(type))
        {
            return new RuntimeReference(type, _program.Types.GetReference(type).ElementType, valueName);
        }
        if (_program.Types.IsDynTrait(type))
        {
            var data = NextTemp("dyn_data");
            EmitAssign(data, $"extractvalue %sollang.dyn {valueName}, 0");
            var vtable = NextTemp("dyn_vtable");
            EmitAssign(vtable, $"extractvalue %sollang.dyn {valueName}, 1");
            return new RuntimeDynTrait(type, data, vtable);
        }
        if (_program.Types.TryGetStreamValue(type, out var streamElementType)
            || _program.Types.TryGetEventStreamValue(type, out streamElementType))
        {
            var isEvent = _program.Types.IsEventStream(type);
            var llvmType = isEvent ? "%sollang.event_stream" : "%sollang.stream";
            var context = NextTemp("stream_context");
            EmitAssign(context, $"extractvalue {llvmType} {valueName}, 0");
            var next = NextTemp("stream_next");
            EmitAssign(next, $"extractvalue {llvmType} {valueName}, 1");
            var drop = NextTemp("stream_drop");
            EmitAssign(drop, $"extractvalue {llvmType} {valueName}, 2");
            return new RuntimeProducerStream(type, streamElementType, context, next, drop, isEvent);
        }
        if (type == BoundType.DynamicIntArray)
        {
            var (pointer, length, capacity) = ExtractDynamicArrayAggregate(valueName);
            return new RuntimeDynamicIntArray(pointer, length, capacity);
        }
        if (_program.Types.IsDynamicArray(type))
        {
            var definition = _program.Types.GetDynamicArray(type);
            var (pointer, length, capacity) = ExtractDynamicArrayAggregate(valueName);
            return new RuntimeDynamicInlineArray(type, definition.ElementType, pointer, length, capacity);
        }
        if (type == BoundType.IntDictionary)
        {
            var (pointer, length, capacity) = ExtractDictionaryAggregate(valueName);
            return new RuntimeIntDictionary(pointer, length, capacity);
        }
        if (type == BoundType.IntDictionaryView)
        {
            var (pointer, length, capacity) = ExtractDictionaryAggregate(valueName);
            return new RuntimeIntDictionaryView(pointer, length, capacity);
        }
        if (type == BoundType.IntSlice)
        {
            var pointer = NextTemp("slice_ptr");
            EmitAssign(pointer, $"extractvalue %sollang.int_slice {valueName}, 0");
            var length = NextTemp("slice_len");
            EmitAssign(length, $"extractvalue %sollang.int_slice {valueName}, 1");
            return new RuntimeIntSlice(pointer, length);
        }
        if (_program.Types.IsDictionary(type))
        {
            var definition = _program.Types.GetDictionary(type);
            var (pointer, length, capacity) = ExtractDictionaryAggregate(valueName);
            return new RuntimeInlineDictionary(
                type, definition.KeyType, definition.ValueType, pointer, length, capacity);
        }
        if (IsIntegerType(type))
        {
            return new RuntimeInt(type, valueName);
        }
        if (IsFloatType(type))
        {
            return new RuntimeFloat(type, valueName);
        }

        return type switch
        {
            BoundType.Bool => new RuntimeBool(valueName),
            BoundType.Text => ExtractTextAggregate(valueName),
            _ => throw new SollangException($"type {type} is not supported in an inline struct field")
        };
    }

    private string BuildTextAggregate(RuntimeText text)
    {
        var aggregate0 = NextTemp("text_value");
        EmitAssign(aggregate0, $"insertvalue %sollang.text poison, ptr {text.PointerName}, 0");
        var aggregate1 = NextTemp("text_value");
        EmitAssign(aggregate1, $"insertvalue %sollang.text {aggregate0}, i64 {text.LengthName}, 1");
        return aggregate1;
    }

    private string BuildDynTraitAggregate(RuntimeDynTrait dyn)
    {
        var withData = NextTemp("dyn_value");
        EmitAssign(withData, $"insertvalue %sollang.dyn poison, ptr {dyn.DataPointerName}, 0");
        var aggregate = NextTemp("dyn_value");
        EmitAssign(aggregate, $"insertvalue %sollang.dyn {withData}, ptr {dyn.VtablePointerName}, 1");
        return aggregate;
    }

    private string BuildIntSliceAggregate(string pointer, string length)
    {
        var aggregate0 = NextTemp("slice_value");
        EmitAssign(aggregate0, $"insertvalue %sollang.int_slice poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("slice_value");
        EmitAssign(aggregate1, $"insertvalue %sollang.int_slice {aggregate0}, i64 {length}, 1");
        return aggregate1;
    }

    private RuntimeText ExtractTextAggregate(string aggregate)
    {
        var pointer = NextTemp("text_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.text {aggregate}, 0");
        var length = NextTemp("text_len");
        EmitAssign(length, $"extractvalue %sollang.text {aggregate}, 1");
        return new RuntimeText(pointer, length);
    }

    private string BuildSourceTextAggregate(RuntimeSourceText source)
    {
        var value0 = NextTemp("source_text_value");
        EmitAssign(value0, $"insertvalue %sollang.source_text poison, ptr {source.DataPointerName}, 0");
        var value1 = NextTemp("source_text_value");
        EmitAssign(value1, $"insertvalue %sollang.source_text {value0}, i64 {source.LengthName}, 1");
        var value2 = NextTemp("source_text_value");
        EmitAssign(value2, $"insertvalue %sollang.source_text {value1}, ptr {source.BasePointerName}, 2");
        var value3 = NextTemp("source_text_value");
        EmitAssign(value3, $"insertvalue %sollang.source_text {value2}, i64 {source.MappedLengthName}, 3");
        return value3;
    }

    private RuntimeSourceText ExtractSourceTextAggregate(string aggregate)
    {
        var data = NextTemp("source_text_data");
        EmitAssign(data, $"extractvalue %sollang.source_text {aggregate}, 0");
        var length = NextTemp("source_text_length");
        EmitAssign(length, $"extractvalue %sollang.source_text {aggregate}, 1");
        var basePointer = NextTemp("source_text_base");
        EmitAssign(basePointer, $"extractvalue %sollang.source_text {aggregate}, 2");
        var mappedLength = NextTemp("source_text_mapped_length");
        EmitAssign(mappedLength, $"extractvalue %sollang.source_text {aggregate}, 3");
        return new RuntimeSourceText(data, length, basePointer, mappedLength);
    }

    private string BuildMappedBytesAggregate(RuntimeMappedBytes mapped)
    {
        var value0 = NextTemp("mapped_value");
        EmitAssign(value0, $"insertvalue %sollang.mapped_bytes poison, ptr {mapped.DataPointerName}, 0");
        var value1 = NextTemp("mapped_value");
        EmitAssign(value1, $"insertvalue %sollang.mapped_bytes {value0}, i64 {mapped.LengthName}, 1");
        var value2 = NextTemp("mapped_value");
        EmitAssign(value2, $"insertvalue %sollang.mapped_bytes {value1}, ptr {mapped.BasePointerName}, 2");
        var value3 = NextTemp("mapped_value");
        EmitAssign(value3, $"insertvalue %sollang.mapped_bytes {value2}, i64 {mapped.MappedLengthName}, 3");
        var value4 = NextTemp("mapped_value");
        EmitAssign(value4,
            $"insertvalue %sollang.mapped_bytes {value3}, i1 {(mapped.Type == BoundType.MutableMappedBytes ? "true" : "false")}, 4");
        return value4;
    }

    private RuntimeMappedBytes ExtractMappedBytesAggregate(BoundType type, string aggregate)
    {
        var data = NextTemp("mapped_data");
        EmitAssign(data, $"extractvalue %sollang.mapped_bytes {aggregate}, 0");
        var length = NextTemp("mapped_length");
        EmitAssign(length, $"extractvalue %sollang.mapped_bytes {aggregate}, 1");
        var basePointer = NextTemp("mapped_base");
        EmitAssign(basePointer, $"extractvalue %sollang.mapped_bytes {aggregate}, 2");
        var mappedLength = NextTemp("mapped_base_length");
        EmitAssign(mappedLength, $"extractvalue %sollang.mapped_bytes {aggregate}, 3");
        return new RuntimeMappedBytes(type, data, length, basePointer, mappedLength);
    }

    private (string Pointer, string Length, string Capacity) ExtractDynamicArrayAggregate(string aggregate)
    {
        var pointer = NextTemp("array_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.dynamic_int_array {aggregate}, 0");
        var length = NextTemp("array_len");
        EmitAssign(length, $"extractvalue %sollang.dynamic_int_array {aggregate}, 1");
        var capacity = NextTemp("array_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.dynamic_int_array {aggregate}, 2");
        return (pointer, length, capacity);
    }

    private (string Pointer, string Length, string Capacity) ExtractDictionaryAggregate(string aggregate)
    {
        var pointer = NextTemp("dictionary_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.int_dictionary {aggregate}, 0");
        var length = NextTemp("dictionary_len");
        EmitAssign(length, $"extractvalue %sollang.int_dictionary {aggregate}, 1");
        var capacity = NextTemp("dictionary_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.int_dictionary {aggregate}, 2");
        return (pointer, length, capacity);
    }

    private string LlvmType(BoundType type)
    {
        if (type == BoundType.SourceText)
        {
            return "%sollang.source_text";
        }
        if (type is BoundType.MappedBytes or BoundType.MutableMappedBytes)
        {
            return "%sollang.mapped_bytes";
        }
        if (_program.Types.IsStruct(type))
        {
            return LlvmStructType(type);
        }
        if (_program.Types.IsEnum(type))
        {
            return LlvmEnumType(type);
        }
        if (_program.Types.IsBox(type))
        {
            return "ptr";
        }
        if (_program.Types.IsReference(type))
        {
            return "ptr";
        }
        if (_program.Types.IsDynTrait(type))
        {
            return "%sollang.dyn";
        }
        if (_program.Types.IsDictionary(type))
        {
            return "%sollang.int_dictionary";
        }
        if (_program.Types.IsDynamicArray(type))
        {
            return "%sollang.dynamic_int_array";
        }
        if (_program.Types.IsTask(type))
        {
            return "%sollang.task";
        }
        if (_program.Types.IsStream(type))
        {
            return "%sollang.stream";
        }
        if (_program.Types.IsEventStream(type))
        {
            return "%sollang.event_stream";
        }

        return type switch
        {
            BoundType.Unit => "void",
            BoundType.Text => "%sollang.text",
            BoundType.Int => "i32",
            BoundType.Int8 or BoundType.UInt8 => "i8",
            BoundType.Int16 or BoundType.UInt16 => "i16",
            BoundType.UInt32 => "i32",
            BoundType.Int64 => "i64",
            BoundType.UInt64 => "i64",
            BoundType.Size or BoundType.UIntSize => $"i{_platform.PointerBitWidth}",
            BoundType.CodePoint => "i32",
            BoundType.Float32 => "float",
            BoundType.Float64 => "double",
            BoundType.Bool => "i1",
            BoundType.IntSlice => "%sollang.int_slice",
            BoundType.DynamicIntArray => "%sollang.dynamic_int_array",
            BoundType.IntDictionaryView or BoundType.IntDictionary => "%sollang.int_dictionary",
            BoundType.Arena => "%sollang.dynamic_int_array",
            BoundType.SourceText => "%sollang.source_text",
            BoundType.MappedBytes or BoundType.MutableMappedBytes => "%sollang.mapped_bytes",
            _ => throw new SollangException($"type {type} has no first-class LLVM representation")
        };
    }

    private static bool IsIntegerType(BoundType type) => type is
        BoundType.Int or BoundType.Int8 or BoundType.Int16 or BoundType.Int64
        or BoundType.UInt8 or BoundType.UInt16 or BoundType.UInt32 or BoundType.UInt64
        or BoundType.Size or BoundType.UIntSize or BoundType.CodePoint;

    private static bool IsSignedIntegerType(BoundType type) => type is
        BoundType.Int or BoundType.Int8 or BoundType.Int16 or BoundType.Int64 or BoundType.Size;

    private static bool IsFloatType(BoundType type) => type is BoundType.Float32 or BoundType.Float64;

    private static bool IsNumericType(BoundType type) => IsIntegerType(type) || IsFloatType(type);

    private int NumericBitWidth(BoundType type) => type switch
    {
        BoundType.Int8 or BoundType.UInt8 => 8,
        BoundType.Int16 or BoundType.UInt16 => 16,
        BoundType.Int or BoundType.UInt32 or BoundType.Float32 => 32,
        BoundType.CodePoint => 32,
        BoundType.Int64 or BoundType.UInt64 or BoundType.Float64 => 64,
        BoundType.Size or BoundType.UIntSize => _platform.PointerBitWidth,
        _ => throw new SollangException($"type {type} is not numeric")
    };

    private static string LlvmStructType(BoundType type)
    {
        return "%sollang.struct." + ((int)type).ToString(CultureInfo.InvariantCulture);
    }

    private static string LlvmEnumType(BoundType type)
    {
        return "%sollang.enum." + ((int)type).ToString(CultureInfo.InvariantCulture);
    }
}
