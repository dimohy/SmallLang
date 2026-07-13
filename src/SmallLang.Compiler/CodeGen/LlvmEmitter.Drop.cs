using System.Globalization;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitOwnedDropHelpers()
    {
        var needsHelpers = _program.MainBindings.Values.Any(IsCustomOwnedType)
            || _program.MainStatements.Any(UsesBox)
            || _program.Functions.Values.Any(function => IsCustomOwnedType(function.ReturnType)
                || (function.InputType is { } input && IsCustomOwnedType(input))
                || (function.Body is not null && UsesBox(function.Body))
                || function.BlockBody.Any(UsesBox))
            || _program.Types.Structs.Any(definition => IsCustomOwnedType(definition.Id))
            || _program.Types.Enums.Any(definition => IsCustomOwnedType(definition.Id))
            || _program.Types.StaticArrays.Any(definition =>
                _program.Types.ContainsOwnedStorage(definition.ElementType))
            || _program.Types.DynamicArrays.Any(definition =>
                _program.Types.ContainsOwnedStorage(definition.ElementType));
        if (!needsHelpers)
        {
            return;
        }

        foreach (var box in _program.Types.Boxes.OrderBy(static box => box.Id))
        {
            EmitBoxDropHelper(box);
        }
        if (_program.Types.Structs.Any(definition =>
                definition.Fields.Any(field => field.Type == BoundType.DynamicIntArray)))
        {
            EmitDynamicArrayDropHelper(BoundType.DynamicIntArray, BoundType.Int);
        }
        foreach (var array in _program.Types.DynamicArrays
                     .Where(array => _program.Types.ContainsOwnedStorage(array.ElementType)
                         || _program.Types.Structs.Any(structure =>
                             structure.Fields.Any(field => field.Type == array.Id))
                         || _program.Types.Enums.Any(enumeration =>
                             enumeration.Variants.Any(variant => variant.PayloadType == array.Id)))
                     .OrderBy(static array => array.Id))
        {
            EmitDynamicArrayDropHelper(array.Id, array.ElementType);
        }
        foreach (var structure in _program.Types.Structs
                     .Where(definition => _program.Types.ContainsOwnedStorage(definition.Id))
                     .OrderBy(static definition => definition.Id))
        {
            EmitStructDropHelper(structure);
        }
        foreach (var enumeration in _program.Types.Enums
                     .Where(definition => _program.Types.ContainsOwnedStorage(definition.Id))
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
        EmitCall(target: null, "void", "smalllang_free", "ptr %value");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitDynamicArrayDropHelper(BoundType arrayType, BoundType elementType)
    {
        const string llvmType = "%smalllang.dynamic_int_array";
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
            var endLabel = NextLabel("drop_array_end");
            EmitBranch(loopLabel);
            EmitLabel(loopLabel);
            _currentBlockLabel = loopLabel;
            var index = NextTemp("drop_array_index");
            var next = NextTemp("drop_array_next");
            EmitInstruction($"{index} = phi i64 [ 0, %entry ], [ {next}, %{itemLabel} ]");
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
            EmitBinary(next, "add", "i64", index, "1");
            EmitBranch(loopLabel);
            EmitLabel(endLabel);
            _currentBlockLabel = endLabel;
        }

        EmitCall(target: null, "void", "smalllang_free", $"ptr {pointer}");
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
        EmitCall(target: null, "void", DropSymbol(type)[1..], $"{LlvmType(type)} {valueName}");
    }

    private static string DropSymbol(BoundType type)
    {
        return "@smalllang_drop_" + ((int)type).ToString(CultureInfo.InvariantCulture);
    }
}
