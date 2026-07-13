using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private string EmitStructTypeDefinitions()
    {
        if (_program.Types.Structs.Count == 0 && _program.Types.Enums.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var definition in _program.Types.Structs.OrderBy(static definition => definition.Id))
        {
            var fields = string.Join(", ", definition.Fields.Select(field => LlvmType(field.Type)));
            builder.Append(LlvmStructType(definition.Id))
                .Append(" = type { ")
                .Append(fields)
                .AppendLine(" }");
        }

        foreach (var definition in _program.Types.Enums.OrderBy(static definition => definition.Id))
        {
            builder.Append(LlvmEnumType(definition.Id))
                .Append(" = type { i32, [")
                .Append(definition.PayloadWords.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" x i64] }");
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private RuntimeStruct EmitStructLiteralExpression(StructLiteralExpression expression)
    {
        if (!_program.Types.TryResolve(expression.TypeName, out var type) || !_program.Types.IsStruct(type))
        {
            throw new SmallLangException($"unknown runtime struct type '{expression.TypeName}'");
        }

        var definition = _program.Types.GetStruct(type);
        var initializers = expression.Fields.ToDictionary(static field => field.Name, StringComparer.Ordinal);
        var aggregate = "poison";
        foreach (var field in definition.Fields)
        {
            var value = EmitExpression(initializers[field.Name].Value);
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
            var value = EmitExpression(initializers[field.Name].Value);
            EnsureRuntimeType(value, field.Type, $"{definition.Name}.{field.Name}");
            var materialized = MaterializeAggregateValue(value);
            var next = NextTemp("contextual_struct_init");
            EmitAssign(next,
                $"insertvalue {LlvmStructType(type)} {aggregate}, {materialized.TypeName} {materialized.ValueName}, {field.Index.ToString(CultureInfo.InvariantCulture)}");
            aggregate = next;
        }
        return new RuntimeStruct(type, aggregate);
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
        if (source is RuntimeBox box)
        {
            var loaded = NextTemp("box_value");
            EmitLoad(loaded, LlvmType(box.ElementType), box.PointerName, RuntimeAlignment(box.ElementType));
            source = DematerializeAggregateValue(box.ElementType, loaded);
        }
        if (source is not RuntimeStruct value)
        {
            throw new SmallLangException("field access expects a runtime struct value");
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

            throw new SmallLangException(
                $"struct '{definition.Name}' has no field or readonly computed member '{expression.FieldName}'");
        }
        var extracted = NextTemp("field");
        EmitAssign(
            extracted,
            $"extractvalue {LlvmStructType(value.Type)} {value.ValueName}, {field.Index.ToString(CultureInfo.InvariantCulture)}");
        return DematerializeAggregateValue(field.Type, extracted);
    }

    private (string TypeName, string ValueName) MaterializeAggregateValue(RuntimeValue value)
    {
        return value switch
        {
            RuntimeInt integer => (LlvmType(integer.Type), integer.ValueName),
            RuntimeFloat floating => (LlvmType(floating.Type), floating.ValueName),
            RuntimeBool boolean => ("i1", boolean.ValueName),
            RuntimeTask task => ("%smalllang.task", BuildTaskAggregate(task)),
            RuntimeText text => ("%smalllang.text", BuildTextAggregate(text)),
            RuntimeStruct structure => (LlvmStructType(structure.Type), structure.ValueName),
            RuntimeEnum enumeration => (LlvmEnumType(enumeration.Type), enumeration.ValueName),
            RuntimeBox box => ("ptr", box.PointerName),
            RuntimeDynamicIntArray array => (
                "%smalllang.dynamic_int_array",
                BuildDynamicArrayAggregate(array.PointerName, array.LengthName, array.CapacityName)),
            RuntimeDynamicInlineArray array => (
                "%smalllang.dynamic_int_array",
                BuildDynamicArrayAggregate(array.PointerName, array.LengthName, array.CapacityName)),
            RuntimeIntDictionary dictionary => (
                "%smalllang.int_dictionary",
                BuildDictionaryAggregate(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName)),
            RuntimeInlineDictionary dictionary => (
                "%smalllang.int_dictionary",
                BuildDictionaryAggregate(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName)),
            _ => throw new SmallLangException($"type {value.Type} is not supported in an inline struct field")
        };
    }

    private string BuildTaskAggregate(RuntimeTask task)
    {
        var withHandle = NextTemp("task_with_handle");
        EmitAssign(withHandle, $"insertvalue %smalllang.task poison, ptr {task.HandleName}, 0");
        var aggregate = NextTemp("task_with_context");
        EmitAssign(aggregate, $"insertvalue %smalllang.task {withHandle}, ptr {task.ContextName}, 1");
        return aggregate;
    }

    private RuntimeValue DematerializeAggregateValue(BoundType type, string valueName)
    {
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
            _ => throw new SmallLangException($"type {type} is not supported in an inline struct field")
        };
    }

    private string BuildTextAggregate(RuntimeText text)
    {
        var aggregate0 = NextTemp("text_value");
        EmitAssign(aggregate0, $"insertvalue %smalllang.text poison, ptr {text.PointerName}, 0");
        var aggregate1 = NextTemp("text_value");
        EmitAssign(aggregate1, $"insertvalue %smalllang.text {aggregate0}, i64 {text.LengthName}, 1");
        return aggregate1;
    }

    private RuntimeText ExtractTextAggregate(string aggregate)
    {
        var pointer = NextTemp("text_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.text {aggregate}, 0");
        var length = NextTemp("text_len");
        EmitAssign(length, $"extractvalue %smalllang.text {aggregate}, 1");
        return new RuntimeText(pointer, length);
    }

    private (string Pointer, string Length, string Capacity) ExtractDynamicArrayAggregate(string aggregate)
    {
        var pointer = NextTemp("array_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 0");
        var length = NextTemp("array_len");
        EmitAssign(length, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 1");
        var capacity = NextTemp("array_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 2");
        return (pointer, length, capacity);
    }

    private (string Pointer, string Length, string Capacity) ExtractDictionaryAggregate(string aggregate)
    {
        var pointer = NextTemp("dictionary_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.int_dictionary {aggregate}, 0");
        var length = NextTemp("dictionary_len");
        EmitAssign(length, $"extractvalue %smalllang.int_dictionary {aggregate}, 1");
        var capacity = NextTemp("dictionary_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.int_dictionary {aggregate}, 2");
        return (pointer, length, capacity);
    }

    private string LlvmType(BoundType type)
    {
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
        if (_program.Types.IsDictionary(type))
        {
            return "%smalllang.int_dictionary";
        }
        if (_program.Types.IsDynamicArray(type))
        {
            return "%smalllang.dynamic_int_array";
        }
        if (_program.Types.IsTask(type))
        {
            return "%smalllang.task";
        }

        return type switch
        {
            BoundType.Unit => "void",
            BoundType.Text => "%smalllang.text",
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
            BoundType.IntSlice => "%smalllang.int_slice",
            BoundType.DynamicIntArray => "%smalllang.dynamic_int_array",
            BoundType.IntDictionaryView or BoundType.IntDictionary => "%smalllang.int_dictionary",
            BoundType.Arena => "%smalllang.dynamic_int_array",
            _ => throw new SmallLangException($"type {type} has no first-class LLVM representation")
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
        _ => throw new SmallLangException($"type {type} is not numeric")
    };

    private static string LlvmStructType(BoundType type)
    {
        return "%smalllang.struct." + ((int)type).ToString(CultureInfo.InvariantCulture);
    }

    private static string LlvmEnumType(BoundType type)
    {
        return "%smalllang.enum." + ((int)type).ToString(CultureInfo.InvariantCulture);
    }
}
