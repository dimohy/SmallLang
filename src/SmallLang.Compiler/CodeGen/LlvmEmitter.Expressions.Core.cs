using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private string EmitExpressionStatement(Expression expression, string ok)
    {
        if (expression is CallExpression call)
        {
            var value = EmitFunctionCall(call);
            if (value.Type != BoundType.Unit)
            {
                throw new SmallLangException("only function calls with side effects are valid expression statements");
            }

            return _mainOk;
        }

        if (expression is FieldAccessExpression field)
        {
            var value = EmitFieldAccessExpression(field);
            if (value.Type != BoundType.Unit)
            {
                throw new SmallLangException("only zero-input properties with side effects are valid expression statements");
            }
            return _mainOk;
        }

        if (expression is FlowExpression flow)
        {
            var result = EmitFlowExpression(flow, ok, allowBindingTarget: false);
            if (result.Binding is { } binding)
            {
                _locals.Add(binding.Name, binding.Value);
                return result.Ok;
            }

            if (result.Value is null)
            {
                return result.Ok;
            }

            if (result.Value is RuntimeUnit)
            {
                return result.Ok;
            }

            throw new SmallLangException("value-flow expression statements must end in a unit-producing call or bind their result with '=>'");
        }

        if (expression is IfExpression or WhenExpression)
        {
            var value = EmitExpression(expression);
            if (value.Type != BoundType.Unit)
            {
                throw new SmallLangException("conditional expression statements must produce Unit");
            }

            return _mainOk;
        }

        throw new SmallLangException($"unsupported runtime expression statement {expression.GetType().Name}");
    }

    private string EmitPrintArgument(Expression expression, string ok)
    {
        if (expression is StringExpression str)
        {
            foreach (var segment in str.Segments)
            {
                ok = segment switch
                {
                    TextSegment text => EmitWriteText(text.Text, ok),
                    InterpolationSegment interpolation => EmitWriteInterpolation(interpolation, ok),
                    _ => throw new SmallLangException($"unsupported string segment {segment.GetType().Name}")
                };
            }

            return ok;
        }

        var value = EmitExpression(expression);
        return EmitWriteValue(value, ok);
    }

    private string EmitWriteInterpolation(InterpolationSegment interpolation, string ok)
    {
        return EmitWriteValue(EmitExpression(interpolation.Expression), ok);
    }

    private string EmitWriteValue(RuntimeValue value, string ok)
    {
        return value switch
        {
            RuntimeText text => EmitWriteTextValue(text, ok),
            RuntimeInt integer => EmitWriteIntegerValue(integer, ok),
            _ => throw new SmallLangException($"unsupported runtime value {value.GetType().Name}")
        };
    }

    private string EmitWriteText(string text, string ok)
    {
        if (text.Length == 0)
        {
            return ok;
        }

        var global = AddGlobalString(text);
        return EmitWriteTextValue(new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture)), ok);
    }

    private string EmitWriteTextValue(RuntimeText text, string ok)
    {
        var write = NextTemp("write");
        EmitCall(write, "i32", "smalllang_write", $"ptr %stdout, ptr {text.PointerName}, i64 {text.LengthName}, ptr %written");
        return CombineWriteOk(write, ok);
    }

    private string EmitWriteIntegerValue(RuntimeInt value, string ok)
    {
        var printable = value.ValueName;
        if (NumericBitWidth(value.Type) < 64)
        {
            printable = NextTemp("print_integer");
            var extension = IsSignedIntegerType(value.Type) ? "sext" : "zext";
            EmitAssign(printable, $"{extension} {LlvmType(value.Type)} {value.ValueName} to i64");
        }
        var write = NextTemp("write");
        var writer = IsSignedIntegerType(value.Type) ? "smalllang_write_i64" : "smalllang_write_u64";
        EmitCall(write, "i32", writer, $"ptr %stdout, i64 {printable}, ptr %written");
        return CombineWriteOk(write, ok);
    }

    private RuntimeInt EmitSizeAsInt(string size, string prefix)
    {
        var value = NextTemp(prefix);
        EmitAssign(value, $"trunc i64 {size} to i32");
        return new RuntimeInt(value);
    }

    private string EmitIntAsSize(RuntimeInt value, string prefix)
    {
        var size = NextTemp(prefix);
        EmitAssign(size, $"sext i32 {value.ValueName} to i64");
        return size;
    }

    private string CombineWriteOk(string writeResult, string ok)
    {
        var isOk = NextTemp("is_ok");
        EmitCompare(isOk, "ne", "i32", writeResult, "0");

        _ = ok;
        var previous = NextTemp("previous_ok");
        EmitLoad(previous, "i1", "%ok_state", 1);
        var combined = NextTemp("ok");
        EmitBinary(combined, "and", "i1", previous, isOk);
        EmitStore("i1", combined, "%ok_state", 1);
        return combined;
    }

    private RuntimeValue EmitExpression(Expression expression)
    {
        var value = expression switch
        {
            StringExpression str => EmitTextLiteral(str),
            NumberExpression number => EmitNumberLiteral(number),
            BoolExpression boolean => new RuntimeBool(boolean.Value ? "true" : "false"),
            NameExpression name => EmitNameExpression(name),
            TypeApplicationExpression application => EmitTypeApplicationExpression(application),
            ArrayLiteralExpression array => EmitArrayLiteral(array),
            ArrayRepeatExpression repeat => EmitArrayRepeat(repeat),
            TypedEmptyArrayExpression typedArray => EmitTypedEmptyArray(typedArray),
            DictionaryLiteralExpression dictionary => EmitDictionaryLiteral(dictionary),
            TypedEmptyDictionaryExpression typedDictionary => EmitTypedEmptyDictionary(typedDictionary),
            IndexExpression index => EmitIndexExpression(index),
            StructLiteralExpression literal => EmitStructLiteralExpression(literal),
            FieldAccessExpression field => EmitFieldAccessExpression(field),
            TryExpression attempt => EmitTryExpression(attempt),
            BoxExpression box => EmitBoxExpression(box),
            MapExpression mapping => EmitMapExpression(mapping),
            AddExpression add => EmitAddExpression(add),
            SubtractExpression subtract => EmitSubtractExpression(subtract),
            MultiplyExpression multiply => EmitMultiplyExpression(multiply),
            DivideExpression divide => EmitDivideExpression(divide),
            ModuloExpression modulo => EmitModuloExpression(modulo),
            NegateExpression negate => EmitNegateExpression(negate),
            CompareExpression compare => EmitCompareExpression(compare),
            AndExpression and => EmitAndExpression(and),
            OrExpression or => EmitOrExpression(or),
            NotExpression not => EmitNotExpression(not),
            IfExpression conditional => EmitIfExpression(conditional),
            WhenExpression whenExpression => EmitWhenExpression(whenExpression),
            EnumMatchExpression enumMatch => EmitEnumMatchExpression(enumMatch),
            EnumPatternExpression => throw new SmallLangException("enum patterns are only valid inside enum when"),
            SubjectCompareExpression => throw new SmallLangException("subject comparison is only valid inside value-flow when"),
            SubjectRangeExpression => throw new SmallLangException("subject range is only valid inside value-flow when"),
            FoldExpression fold => EmitFoldExpression(fold),
            RangeExpression => throw new SmallLangException("range values are only valid as block-function input"),
            CallExpression call => EmitFunctionCall(call),
            FlowExpression flow => EmitFlowExpressionValue(flow),
            _ => throw new SmallLangException($"unsupported runtime expression {expression.GetType().Name}")
        };

        EmitStackLifetimeEndsAfter(expression);
        return value;
    }

    private RuntimeValue EmitNameExpression(NameExpression expression)
    {
        if (_locals.ContainsKey(expression.Name))
        {
            return ResolveLocal(expression.Name);
        }
        if (_currentFunctions.TryGetValue(expression.Name, out var function)
            && function.InputType is null)
        {
            return EmitFunctionCall(function, argument: null);
        }
        throw new SmallLangException($"unknown runtime binding or zero-argument function '{expression.Name}'");
    }

    private RuntimeValue EmitTypeApplicationExpression(TypeApplicationExpression expression)
    {
        if (!_program.ResolvedGenericCalls.TryGetValue(expression, out var function))
        {
            throw new SmallLangException($"unresolved generic application '{string.Join('.', expression.Path)}'");
        }
        return EmitFunctionCall(function, argument: null);
    }

    private RuntimeText EmitTextLiteral(StringExpression expression)
    {
        var text = GetPlainText(expression, expression.Line, expression.Column);
        var global = AddGlobalString(text);
        return new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeValue EmitArrayLiteral(ArrayLiteralExpression expression)
    {
        return expression.IsDynamic
            ? EmitDynamicArrayLiteral(expression)
            : EmitStaticArrayLiteral(expression);
    }

    private RuntimeValue EmitNumberLiteral(NumberExpression expression)
    {
        if (!expression.Text.Contains('.', StringComparison.Ordinal)
            && !expression.Text.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeInt(ParseNumber(expression).ToString(CultureInfo.InvariantCulture));
        }
        if (!double.TryParse(expression.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new SmallLangException(
                $"codegen error at {expression.Line}:{expression.Column}: floating-point literal is out of range");
        }
        return new RuntimeFloat(
            BoundType.Float64,
            value.ToString("0.0################E+00", CultureInfo.InvariantCulture));
    }

    private RuntimeValue[] EmitArrayLiteralElements(ArrayLiteralExpression expression)
    {
        BoundType? elementType = expression.ElementType is not null
            && _program.Types.TryResolve(expression.ElementType, out var declaredElementType)
                ? declaredElementType
                : null;
        var elements = new RuntimeValue[expression.Elements.Count];
        for (var index = 0; index < expression.Elements.Count; index++)
        {
            var element = expression.Elements[index];
            elements[index] = element is DictionaryLiteralExpression contextual
                && elementType is { } contextualElementType
                && _program.Types.IsStruct(contextualElementType)
                    ? EmitContextualStructLiteral(contextual, contextualElementType)
                    : EmitExpression(element);
            elementType ??= elements[index].Type;
        }
        return elements;
    }

    private RuntimeValue EmitStaticArrayLiteral(ArrayLiteralExpression expression)
    {
        var elements = EmitArrayLiteralElements(expression);
        if (elements.Length == 0 || elements.All(static value => value.Type == BoundType.Int))
        {
            return EmitStaticIntArrayLiteral(expression, elements.Cast<RuntimeInt>().ToArray());
        }
        if (elements.All(static value => value is RuntimeText))
        {
            return EmitStaticTextArrayLiteral(expression, elements.Cast<RuntimeText>().ToArray());
        }
        if (elements.Length > 0
            && elements.All(value => value.Type == elements[0].Type)
            && _program.Types.TryGetStaticArrayForElement(elements[0].Type, out var arrayType))
        {
            return EmitStaticInlineArrayLiteral(arrayType, elements);
        }

        throw new SmallLangException("fixed array elements must have one supported runtime type");
    }

    private RuntimeStaticIntArray EmitStaticIntArrayLiteral(
        ArrayLiteralExpression expression,
        IReadOnlyList<RuntimeInt> elements)
    {
        var length = expression.Elements.Count;
        var allocatedLength = Math.Max(length, 1);
        string pointer;
        RuntimeContainerStorage storage;
        if (_currentStackFramePlan.TryGetAllocation(expression, out _))
        {
            pointer = EmitStackLifetimeStart(expression);
            storage = RuntimeContainerStorage.Stack;
        }
        else
        {
            pointer = EmitHeapAllocate(((long)allocatedLength * sizeof(int)).ToString(CultureInfo.InvariantCulture));
            storage = RuntimeContainerStorage.Heap;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            var value = elements[i];
            StoreStaticArrayElement(pointer, allocatedLength, i, value.ValueName);
        }

        return new RuntimeStaticIntArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            allocatedLength,
            storage);
    }

    private RuntimeStaticIntArray EmitArrayRepeat(ArrayRepeatExpression expression)
    {
        int? specializedCount = null;
        if (_currentFunction is { GenericParameterName: { } parameterName, SpecializedValue: { } specializedValue }
            && parameterName == expression.CountParameterName)
        {
            specializedCount = specializedValue;
        }

        var count = expression.Count
            ?? specializedCount
            ?? throw new SmallLangException(
                $"array repeat count '{expression.CountParameterName}' was not specialized");
        var allocatedLength = Math.Max(count, 1);
        string pointer;
        RuntimeContainerStorage storage;
        if (_currentStackFramePlan.TryGetAllocation(expression, out _))
        {
            pointer = EmitStackLifetimeStart(expression);
            storage = RuntimeContainerStorage.Stack;
        }
        else
        {
            pointer = EmitHeapAllocate(((long)allocatedLength * sizeof(int)).ToString(CultureInfo.InvariantCulture));
            storage = RuntimeContainerStorage.Heap;
        }

        var value = EmitIntExpression(expression.Value);
        for (var i = 0; i < count; i++)
        {
            StoreStaticArrayElement(pointer, allocatedLength, i, value.ValueName);
        }

        return new RuntimeStaticIntArray(
            pointer,
            count.ToString(CultureInfo.InvariantCulture),
            allocatedLength,
            storage);
    }

    private RuntimeValue EmitDynamicArrayLiteral(ArrayLiteralExpression expression)
    {
        var elements = EmitArrayLiteralElements(expression);
        if (elements.Length == 0 || elements.All(static value => value.Type == BoundType.Int))
        {
            return EmitDynamicIntArrayLiteral(expression, elements.Cast<RuntimeInt>().ToArray());
        }
        if (elements.Length > 0
            && elements.All(value => value.Type == elements[0].Type)
            && _program.Types.TryGetDynamicArrayForElement(elements[0].Type, out var arrayType))
        {
            return EmitDynamicInlineArrayLiteral(arrayType, elements);
        }

        throw new SmallLangException("growable array elements must have one supported runtime type");
    }

    private RuntimeDynamicIntArray EmitDynamicIntArrayLiteral(
        ArrayLiteralExpression expression,
        IReadOnlyList<RuntimeInt> elements)
    {
        var length = expression.Elements.Count;
        var capacity = length;
        var storage = _currentStackFramePlan.TryGetAllocation(expression, out _)
            ? RuntimeContainerStorage.Stack
            : RuntimeContainerStorage.Heap;
        string pointer;
        if (capacity == 0)
        {
            pointer = "null";
        }
        else if (storage == RuntimeContainerStorage.Stack)
        {
            pointer = EmitStackLifetimeStart(expression);
        }
        else
        {
            pointer = EmitHeapAllocate((capacity * 4).ToString(CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < elements.Count; i++)
        {
            var value = elements[i];
            StoreDynamicArrayElement(pointer, i.ToString(CultureInfo.InvariantCulture), value.ValueName);
        }

        return new RuntimeDynamicIntArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture),
            storage);
    }

    private RuntimeDynamicInlineArray EmitDynamicInlineArrayLiteral(
        BoundType arrayType,
        IReadOnlyList<RuntimeValue> elements)
    {
        var definition = _program.Types.GetDynamicArray(arrayType);
        var capacity = elements.Count;
        var pointer = capacity == 0
            ? "null"
            : EmitHeapAllocate(((long)capacity * definition.ElementSize).ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < elements.Count; i++)
        {
            StoreDynamicInlineArrayElement(pointer, definition, i.ToString(CultureInfo.InvariantCulture), elements[i]);
        }
        return new RuntimeDynamicInlineArray(
            arrayType,
            definition.ElementType,
            pointer,
            elements.Count.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeStaticTextArray EmitStaticTextArrayLiteral(
        ArrayLiteralExpression expression,
        IReadOnlyList<RuntimeText> elements)
    {
        var length = elements.Count;
        var allocatedLength = Math.Max(length, 1);
        var pointer = EmitHeapAllocate(((long)allocatedLength * 16).ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < elements.Count; i++)
        {
            StoreStaticTextArrayElement(pointer, i, elements[i]);
        }

        return new RuntimeStaticTextArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            allocatedLength,
            RuntimeContainerStorage.Heap);
    }

    private RuntimeStaticInlineArray EmitStaticInlineArrayLiteral(
        BoundType arrayType,
        IReadOnlyList<RuntimeValue> elements)
    {
        var definition = _program.Types.GetStaticArray(arrayType);
        var allocatedLength = Math.Max(elements.Count, 1);
        var pointer = EmitHeapAllocate(
            ((long)allocatedLength * definition.ElementSize).ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < elements.Count; i++)
        {
            StoreStaticInlineArrayElement(pointer, definition, i, elements[i]);
        }

        return new RuntimeStaticInlineArray(
            arrayType,
            definition.ElementType,
            pointer,
            elements.Count.ToString(CultureInfo.InvariantCulture),
            elements.Count,
            allocatedLength,
            RuntimeContainerStorage.Heap);
    }

    private RuntimeValue EmitTypedEmptyArray(TypedEmptyArrayExpression expression)
    {
        if (expression.ElementType == "Int")
        {
            var intCapacity = expression.CapacityHint ?? 0;
            var intPointer = intCapacity == 0
                ? "null"
                : EmitHeapAllocate(((long)intCapacity * 4).ToString(CultureInfo.InvariantCulture));
            return new RuntimeDynamicIntArray(intPointer, "0", intCapacity.ToString(CultureInfo.InvariantCulture));
        }
        if (!_program.Types.TryResolve(expression.ElementType, out var elementType)
            || !_program.Types.TryGetDynamicArrayForElement(elementType, out var arrayType))
        {
            throw new SmallLangException($"unknown growable array element type '{expression.ElementType}'");
        }
        var definition = _program.Types.GetDynamicArray(arrayType);
        var capacity = expression.CapacityHint ?? 0;
        var pointer = capacity == 0
            ? "null"
            : EmitHeapAllocate(((long)capacity * definition.ElementSize).ToString(CultureInfo.InvariantCulture));
        return new RuntimeDynamicInlineArray(
            arrayType,
            elementType,
            pointer,
            "0",
            capacity.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeValue EmitDictionaryLiteral(DictionaryLiteralExpression expression)
    {
        var entries = new List<(RuntimeValue Key, RuntimeValue Value)>(expression.Entries.Count);
        BoundType? inferredKeyType = expression.KeyType is not null
            && _program.Types.TryResolve(expression.KeyType, out var declaredKeyType)
                ? declaredKeyType
                : null;
        BoundType? inferredValueType = expression.ValueType is not null
            && _program.Types.TryResolve(expression.ValueType, out var declaredValueType)
                ? declaredValueType
                : null;
        foreach (var entry in expression.Entries)
        {
            var key = entry.Key is DictionaryLiteralExpression contextual
                && inferredKeyType is { } contextualKeyType
                && _program.Types.IsStruct(contextualKeyType)
                    ? EmitContextualStructLiteral(contextual, contextualKeyType)
                    : EmitExpression(entry.Key);
            inferredKeyType ??= key.Type;
            var value = entry.Value is DictionaryLiteralExpression contextualValue
                && inferredValueType is { } contextualValueType
                && _program.Types.IsStruct(contextualValueType)
                    ? EmitContextualStructLiteral(contextualValue, contextualValueType)
                    : EmitExpression(entry.Value);
            inferredValueType ??= value.Type;
            entries.Add((key, value));
        }
        if (entries[0].Key.Type != BoundType.Int || entries[0].Value.Type != BoundType.Int)
        {
            var dictionaryType = _program.Types.GetOrAddDictionary(entries[0].Key.Type, entries[0].Value.Type);
            return EmitInlineDictionaryLiteral(expression, dictionaryType, entries);
        }
        var length = expression.Entries.Count;
        var capacity = DictionaryCapacityForLength(length);
        var storage = _currentStackFramePlan.TryGetAllocation(expression, out _)
            ? RuntimeContainerStorage.Stack
            : RuntimeContainerStorage.Heap;
        var dictionary = new RuntimeIntDictionary(
            storage == RuntimeContainerStorage.Stack
                ? InitializeStackDictionary(EmitStackLifetimeStart(expression), capacity)
                : EmitDictionaryAllocate(capacity.ToString(CultureInfo.InvariantCulture)),
            length.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture),
            storage);

        foreach (var entry in entries)
        {
            EmitDictionaryInsertUnique(
                dictionary,
                ((RuntimeInt)entry.Key).ValueName,
                ((RuntimeInt)entry.Value).ValueName);
        }

        return dictionary;
    }

    private RuntimeValue EmitTypedEmptyDictionary(TypedEmptyDictionaryExpression expression)
    {
        if (expression.KeyType != "Int" || expression.ValueType != "Int")
        {
            if (!_program.Types.TryResolve(expression.KeyType, out var keyType)
                || !_program.Types.TryResolve(expression.ValueType, out var valueType)
                || !_program.Types.TryGetDictionaryForTypes(keyType, valueType, out var dictionaryType))
            {
                throw new SmallLangException($"unknown dictionary type '{{{expression.KeyType}: {expression.ValueType}}}'");
            }
            return EmitTypedEmptyInlineDictionary(expression, dictionaryType);
        }

        if (expression.CapacityHint is null)
        {
            return new RuntimeIntDictionary("null", "0", "0");
        }

        var capacity = DictionaryCapacityForLength(expression.CapacityHint.Value);
        return new RuntimeIntDictionary(
            EmitDictionaryAllocate(capacity.ToString(CultureInfo.InvariantCulture)),
            "0",
            capacity.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeValue EmitIndexExpression(IndexExpression expression)
    {
        var source = EmitExpression(expression.Source);
        if (source is RuntimeInlineDictionary inlineDictionary)
        {
            var key = expression.Index is DictionaryLiteralExpression contextual
                && _program.Types.IsStruct(inlineDictionary.KeyType)
                    ? EmitContextualStructLiteral(contextual, inlineDictionary.KeyType)
                    : EmitExpression(expression.Index);
            return EmitInlineDictionaryLookup(inlineDictionary, key);
        }
        if (source is RuntimeMappedBytes mapped)
        {
            return EmitMappedLoad(mapped, expression.Index);
        }
        if (source is RuntimeArguments arguments)
        {
            return EmitArgumentLoad(arguments, expression.Index);
        }
        var index = EmitIntExpression(expression.Index);
        var indexSize = EmitIntAsSize(index, "index_size");
        return source switch
        {
            RuntimeIntSlice slice => EmitIntSliceLoad(slice, indexSize),
            RuntimeStaticIntArray array => EmitStaticArrayLoad(array, indexSize),
            RuntimeStaticTextArray array => EmitStaticTextArrayLoad(array, indexSize),
            RuntimeStaticInlineArray array => EmitStaticInlineArrayLoad(array, indexSize),
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, indexSize),
            RuntimeDynamicInlineArray array => EmitDynamicInlineArrayLoad(array, indexSize),
            RuntimeIntDictionaryView dictionary => EmitDictionaryLookup(
                new RuntimeIntDictionary(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName),
                index.ValueName),
            RuntimeIntDictionary dictionary => EmitDictionaryLookup(dictionary, index.ValueName),
            _ => throw new SmallLangException("indexing expects an array or dictionary")
        };
    }

}

