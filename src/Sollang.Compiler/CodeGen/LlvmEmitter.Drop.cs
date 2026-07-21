using System.Globalization;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitOwnedDropHelpers()
    {
        var needsHelpers = _program.MainBindings.Values.Any(IsCustomOwnedType)
            || _program.MainStatements.Any(UsesBox)
            || _program.Functions.Values.Where(function =>
                !function.IsStandardLibrary || _standaloneStandardLibraryFunctions.Contains(function)).Any(function =>
                IsCustomOwnedType(function.ReturnType)
                || (function.InputType is { } input && IsCustomOwnedType(input))
                || (function.Body is not null && UsesBox(function.Body))
                || function.BlockBody.Any(UsesBox))
            || _program.FunctionBindings.Any(item =>
                (!item.Key.IsStandardLibrary || _standaloneStandardLibraryFunctions.Contains(item.Key))
                && item.Value.Values.Any(IsCustomOwnedType))
            || _program.Types.StaticArrays.Any(definition =>
                _program.Types.ContainsOwnedStorage(definition.ElementType))
            || _program.Types.DynamicArrays.Any(definition =>
                ShouldEmitTypeDefinition(definition.Id)
                && _program.Types.ContainsOwnedStorage(definition.ElementType))
            || _program.Types.Dictionaries.Any(definition =>
                ShouldEmitTypeDefinition(definition.Id)
                && (_program.Types.ContainsOwnedStorage(definition.KeyType)
                    || _program.Types.ContainsOwnedStorage(definition.ValueType)));
        if (!needsHelpers)
        {
            return;
        }

        foreach (var box in _program.Types.Boxes.OrderBy(static box => box.Id))
        {
            EmitBoxDropHelper(box);
        }
        foreach (var dyn in _program.Types.DynTraits.OrderBy(static dyn => dyn.Id))
        {
            EmitDynTraitDropHelper(dyn);
        }
        EmitDynamicArrayDropHelper(BoundType.DynamicIntArray, BoundType.Int);
        foreach (var array in _program.Types.DynamicArrays
                     .Where(array => ShouldEmitTypeDefinition(array.Id))
                     .OrderBy(static array => array.Id))
        {
            EmitDynamicArrayDropHelper(array.Id, array.ElementType);
        }
        EmitDictionaryDropHelper(BoundType.IntDictionary);
        foreach (var dictionary in _program.Types.Dictionaries.OrderBy(static dictionary => dictionary.Id))
        {
            EmitDictionaryDropHelper(dictionary.Id);
        }
        foreach (var structure in _program.Types.Structs
                     .Where(definition => ShouldEmitTypeDefinition(definition.Id)
                         && _program.Types.ContainsOwnedStorage(definition.Id))
                     .OrderBy(static definition => definition.Id))
        {
            EmitStructDropHelper(structure);
        }
        foreach (var enumeration in _program.Types.Enums
                     .Where(definition => ShouldEmitTypeDefinition(definition.Id)
                         && _program.Types.ContainsOwnedStorage(definition.Id))
                     .OrderBy(static definition => definition.Id))
        {
            EmitEnumDropHelper(enumeration);
        }
    }

    private static bool UsesBox(Statement statement) => statement switch
    {
        BindingStatement value => UsesBox(value.Value),
        IndexAssignmentStatement value => UsesBox(value.Index) || UsesBox(value.Value),
        FieldAssignmentStatement value => UsesBox(value.Value),
        BlockFunctionCallStatement value => UsesBox(value.Source) || value.Body.Any(UsesBox),
        ExpressionStatement value => UsesBox(value.Expression),
        GuardLoopControlStatement value => UsesBox(value.Condition),
        ReturnStatement { Value: { } value } => UsesBox(value),
        _ => false
    };

    private static bool UsesBox(Expression expression) => expression switch
    {
        BoxExpression => true,
        StringExpression value => value.Segments.OfType<InterpolationSegment>().Any(x => UsesBox(x.Expression)),
        AddExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        SubtractExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        MultiplyExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        DivideExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        ModuloExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        NegateExpression value => UsesBox(value.Value),
        CompareExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        AndExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        OrExpression value => UsesBox(value.Left) || UsesBox(value.Right),
        NotExpression value => UsesBox(value.Value),
        RangeExpression value => UsesBox(value.Start) || UsesBox(value.End),
        FlowExpression value => UsesBox(value.Source) || value.Targets.SelectMany(x => x.Arguments).Any(UsesBox),
        CallExpression value => value.Arguments.Any(UsesBox),
        ArrayLiteralExpression value => value.Elements.Any(UsesBox),
        ArrayRepeatExpression value => UsesBox(value.Value),
        DictionaryLiteralExpression value => value.Entries.Any(x => UsesBox(x.Key) || UsesBox(x.Value)),
        IndexExpression value => UsesBox(value.Source) || UsesBox(value.Index),
        StructLiteralExpression value => value.Fields.Any(x => UsesBox(x.Value)),
        FieldAccessExpression value => UsesBox(value.Source),
        TryExpression value => UsesBox(value.Value),
        MapExpression value => UsesBox(value.Path)
            || (value.Offset is not null && UsesBox(value.Offset))
            || (value.Length is not null && UsesBox(value.Length))
            || (value.FileSize is not null && UsesBox(value.FileSize)),
        IfExpression value => UsesBox(value.Condition) || UsesBox(value.Then)
            || (value.Else is not null && UsesBox(value.Else)),
        WhenExpression value => (value.Subject is not null && UsesBox(value.Subject))
            || value.Arms.Any(x => UsesBox(x.Condition) || UsesBox(x.Body))
            || UsesBox(value.Else),
        EnumMatchExpression value => UsesBox(value.Subject)
            || value.Arms.Any(x => UsesBox(x.Body))
            || (value.Else is not null && UsesBox(value.Else)),
        FoldExpression value => UsesBox(value.Source) || UsesBox(value.Initial) || UsesBox(value.Body),
        _ => false
    };

    private static bool UsesBox(BlockBody body) => body.Statements.Any(UsesBox)
        || (body.Value is not null && UsesBox(body.Value));

    private bool IsCustomOwnedType(BoundType type)
    {
        return _program.Types.IsBox(type)
            || _program.Types.IsDynTrait(type)
            || ((_program.Types.IsStruct(type) || _program.Types.IsEnum(type))
                && _program.Types.ContainsOwnedStorage(type));
    }

    private void EmitBoxDropHelper(BoundBoxDefinition box)
    {
        EmitFunctionLine($"define internal void {DropSymbol(box.Id)}(ptr %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        if (_program.Types.ContainsOwnedStorage(box.ElementType))
        {
            var loaded = NextTemp("drop_box_value");
            EmitLoad(loaded, LlvmType(box.ElementType), "%value", RuntimeAlignment(box.ElementType));
            EmitOwnedDropCall(box.ElementType, loaded);
        }
        EmitCall(target: null, "void", "sollang_free", "ptr %value");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitDynTraitDropHelper(BoundDynTraitDefinition dyn)
    {
        EmitFunctionLine($"define internal void {DropSymbol(dyn.Id)}(%sollang.dyn %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        var data = NextTemp("drop_dyn_data");
        EmitAssign(data, "extractvalue %sollang.dyn %value, 0");
        var vtable = NextTemp("drop_dyn_vtable");
        EmitAssign(vtable, "extractvalue %sollang.dyn %value, 1");
        var slot = NextTemp("drop_dyn_slot");
        EmitAssign(slot, $"getelementptr ptr, ptr {vtable}, i64 0");
        var drop = NextTemp("drop_dyn_function");
        EmitLoad(drop, "ptr", slot, _platform.PointerBitWidth / 8);
        EmitInstruction($"call void {drop}(ptr {data})");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitDynamicArrayDropHelper(BoundType arrayType, BoundType elementType)
    {
        const string llvmType = "%sollang.dynamic_int_array";
        EmitFunctionLine($"define internal void {DropSymbol(arrayType)}({llvmType} %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        var pointer = NextTemp("drop_array_ptr");
        EmitAssign(pointer, $"extractvalue {llvmType} %value, 0");

        if (_program.Types.ContainsOwnedStorage(elementType))
        {
            var length = NextTemp("drop_array_len");
            EmitAssign(length, $"extractvalue {llvmType} %value, 1");
            var loopLabel = NextLabel("drop_array_loop");
            var itemLabel = NextLabel("drop_array_item");
            var continueLabel = NextLabel("drop_array_continue");
            var endLabel = NextLabel("drop_array_end");
            EmitBranch(loopLabel);
            EmitLabel(loopLabel);
            _currentBlockLabel = loopLabel;
            var index = NextTemp("drop_array_index");
            var next = NextTemp("drop_array_next");
            EmitInstruction($"{index} = phi i64 [ 0, %entry ], [ {next}, %{continueLabel} ]");
            var inRange = NextTemp("drop_array_in_range");
            EmitCompare(inRange, "ult", "i64", index, length);
            EmitConditionalBranch(inRange, itemLabel, endLabel);
            EmitLabel(itemLabel);
            _currentBlockLabel = itemLabel;
            var slot = NextTemp("drop_array_slot");
            EmitAssign(slot, $"getelementptr {LlvmType(elementType)}, ptr {pointer}, i64 {index}");
            var element = NextTemp("drop_array_element");
            EmitLoad(element, LlvmType(elementType), slot, RuntimeAlignment(elementType));
            EmitOwnedDropCall(elementType, element);
            EmitBranch(continueLabel);
            EmitLabel(continueLabel);
            _currentBlockLabel = continueLabel;
            EmitBinary(next, "add", "i64", index, "1");
            EmitBranch(loopLabel);
            EmitLabel(endLabel);
            _currentBlockLabel = endLabel;
        }

        EmitCall(target: null, "void", "sollang_free", $"ptr {pointer}");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitDictionaryDropHelper(BoundType dictionaryType)
    {
        const string llvmType = "%sollang.int_dictionary";
        EmitFunctionLine($"define internal void {DropSymbol(dictionaryType)}({llvmType} %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        var pointer = NextTemp("drop_dictionary_ptr");
        EmitAssign(pointer, $"extractvalue {llvmType} %value, 0");
        var length = NextTemp("drop_dictionary_len");
        EmitAssign(length, $"extractvalue {llvmType} %value, 1");
        var capacity = NextTemp("drop_dictionary_capacity");
        EmitAssign(capacity, $"extractvalue {llvmType} %value, 2");
        if (_program.Types.IsDictionary(dictionaryType))
        {
            var definition = _program.Types.GetDictionary(dictionaryType);
            DropInlineDictionaryElements(new RuntimeInlineDictionary(
                dictionaryType,
                definition.KeyType,
                definition.ValueType,
                pointer,
                length,
                capacity));
        }
        EmitCall(target: null, "void", "sollang_free", $"ptr {pointer}");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitStructDropHelper(BoundStructDefinition structure)
    {
        var llvmType = LlvmStructType(structure.Id);
        EmitFunctionLine($"define internal void {DropSymbol(structure.Id)}({llvmType} %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        if (structure.Name is "sys.file.File" or "sys.file.FileWriter")
        {
            var handle = NextTemp("drop_file_handle");
            EmitAssign(handle, $"extractvalue {llvmType} %value, 0");
            EmitCall(
                target: null,
                "void",
                "sollang_platform_close_owned_file",
                $"i64 {handle}");
            EmitInstruction("ret void");
            EmitFunctionLine("}");
            EmitFunctionLine();
            return;
        }
        foreach (var field in structure.Fields.Where(field => _program.Types.ContainsOwnedStorage(field.Type)))
        {
            var extracted = NextTemp("drop_field");
            EmitAssign(
                extracted,
                $"extractvalue {llvmType} %value, {field.Index.ToString(CultureInfo.InvariantCulture)}");
            EmitOwnedDropCall(field.Type, extracted);
        }
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitEnumDropHelper(BoundEnumDefinition enumeration)
    {
        var llvmType = LlvmEnumType(enumeration.Id);
        var ownedVariants = enumeration.Variants
            .Where(variant => variant.PayloadType is { } payload
                && _program.Types.ContainsOwnedStorage(payload))
            .ToArray();
        var endLabel = NextLabel("drop_enum_end");

        EmitFunctionLine($"define internal void {DropSymbol(enumeration.Id)}({llvmType} %value) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        var tag = NextTemp("drop_enum_tag");
        EmitAssign(tag, $"extractvalue {llvmType} %value, 0");
        var labels = ownedVariants.Select(variant =>
            (Variant: variant, Label: NextLabel("drop_enum_variant"))).ToArray();
        var cases = string.Join(
            " ",
            labels.Select(item =>
                $"i32 {item.Variant.Tag.ToString(CultureInfo.InvariantCulture)}, label %{item.Label}"));
        EmitInstruction($"switch i32 {tag}, label %{endLabel} [ {cases} ]");

        foreach (var (variant, label) in labels)
        {
            EmitLabel(label);
            _currentBlockLabel = label;
            var slot = NextTemp("drop_enum_slot");
            EmitAlloca(slot, llvmType, 8);
            EmitStore(llvmType, "%value", slot, 8);
            var payloadAddress = NextTemp("drop_enum_payload_addr");
            EmitAssign(payloadAddress, $"getelementptr inbounds {llvmType}, ptr {slot}, i32 0, i32 1");
            var payloadType = variant.PayloadType!.Value;
            var payload = NextTemp("drop_enum_payload");
            EmitLoad(payload, LlvmType(payloadType), payloadAddress, RuntimeAlignment(payloadType));
            EmitOwnedDropCall(payloadType, payload);
            EmitBranch(endLabel);
        }

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitOwnedDropCall(BoundType type, string valueName)
    {
        if (type == BoundType.SourceText)
        {
            var basePointer = NextTemp("drop_source_text_base");
            EmitAssign(basePointer, $"extractvalue %sollang.source_text {valueName}, 2");
            var mappedLength = NextTemp("drop_source_text_length");
            EmitAssign(mappedLength, $"extractvalue %sollang.source_text {valueName}, 3");
            EmitSourceTextUnmap(basePointer, mappedLength);
            return;
        }
        if (type is BoundType.MappedBytes or BoundType.MutableMappedBytes)
        {
            var basePointer = NextTemp("drop_mapped_base");
            EmitAssign(basePointer, $"extractvalue %sollang.mapped_bytes {valueName}, 2");
            var mappedLength = NextTemp("drop_mapped_length");
            EmitAssign(mappedLength, $"extractvalue %sollang.mapped_bytes {valueName}, 3");
            EmitCall(target: null, "void", "sollang_mapped_unmap",
                $"ptr {basePointer}, i64 {mappedLength}");
            return;
        }
        EmitCall(target: null, "void", DropSymbol(type)[1..], $"{LlvmType(type)} {valueName}");
    }

    private void EmitSourceTextUnmap(string basePointer, string mappedLength)
    {
        var owned = NextTemp("source_text_owned");
        EmitCompare(owned, "ne", "ptr", basePointer, "null");
        var unmapLabel = NextLabel("source_text_unmap");
        var doneLabel = NextLabel("source_text_drop_done");
        EmitConditionalBranch(owned, unmapLabel, doneLabel);
        EmitFunctionLine();
        EmitLabel(unmapLabel);
        EmitCall(target: null, "void", "sollang_mapped_unmap",
            $"ptr {basePointer}, i64 {mappedLength}");
        EmitBranch(doneLabel);
        EmitFunctionLine();
        EmitLabel(doneLabel);
        _currentBlockLabel = doneLabel;
    }

    private static string DropSymbol(BoundType type)
    {
        return "@sollang_drop_" + ((int)type).ToString(CultureInfo.InvariantCulture);
    }
}
