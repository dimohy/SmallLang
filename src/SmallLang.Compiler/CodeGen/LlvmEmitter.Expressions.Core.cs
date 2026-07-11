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
        var write = NextTemp("write");
        EmitCall(write, "i32", "smalllang_write_u64", $"ptr %stdout, i64 {value.ValueName}, ptr %written");
        return CombineWriteOk(write, ok);
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
            NumberExpression number => new RuntimeInt(ParseNumber(number).ToString(CultureInfo.InvariantCulture)),
            BoolExpression boolean => new RuntimeBool(boolean.Value ? "true" : "false"),
            NameExpression name => ResolveLocal(name.Name),
            ArrayLiteralExpression array => EmitArrayLiteral(array),
            ArrayRepeatExpression repeat => EmitArrayRepeat(repeat),
            TypedEmptyArrayExpression typedArray => EmitTypedEmptyArray(typedArray),
            DictionaryLiteralExpression dictionary => EmitDictionaryLiteral(dictionary),
            TypedEmptyDictionaryExpression typedDictionary => EmitTypedEmptyDictionary(typedDictionary),
            IndexExpression index => EmitIndexExpression(index),
            StructLiteralExpression literal => EmitStructLiteralExpression(literal),
            FieldAccessExpression field => EmitFieldAccessExpression(field),
            BoxExpression box => EmitBoxExpression(box),
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

    private RuntimeText EmitTextLiteral(StringExpression expression)
    {
        var text = GetPlainText(expression, expression.Line, expression.Column);
        var global = AddGlobalString(text);
        return new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeValue EmitArrayLiteral(ArrayLiteralExpression expression)
    {
        return expression.IsDynamic
            ? EmitDynamicIntArrayLiteral(expression)
            : EmitStaticArrayLiteral(expression);
    }

    private RuntimeValue EmitStaticArrayLiteral(ArrayLiteralExpression expression)
    {
        var elements = expression.Elements.Select(EmitExpression).ToArray();
        if (elements.Length == 0 || elements.All(static value => value is RuntimeInt))
        {
            return EmitStaticIntArrayLiteral(expression, elements.Cast<RuntimeInt>().ToArray());
        }
        if (elements.All(static value => value is RuntimeText))
        {
            return EmitStaticTextArrayLiteral(expression, elements.Cast<RuntimeText>().ToArray());
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
            pointer = EmitHeapAllocate(((long)allocatedLength * sizeof(long)).ToString(CultureInfo.InvariantCulture));
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
            pointer = EmitHeapAllocate(((long)allocatedLength * sizeof(long)).ToString(CultureInfo.InvariantCulture));
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

    private RuntimeDynamicIntArray EmitDynamicIntArrayLiteral(ArrayLiteralExpression expression)
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
            pointer = EmitHeapAllocate((capacity * 8).ToString(CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < expression.Elements.Count; i++)
        {
            var value = EmitIntExpression(expression.Elements[i]);
            StoreDynamicArrayElement(pointer, i.ToString(CultureInfo.InvariantCulture), value.ValueName);
        }

        return new RuntimeDynamicIntArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture),
            storage);
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

    private RuntimeValue EmitTypedEmptyArray(TypedEmptyArrayExpression expression)
    {
        if (expression.ElementType != "Int")
        {
            throw new SmallLangException("only [Int; ~] typed empty arrays are supported in the current runtime slice");
        }

        var capacity = expression.CapacityHint ?? 0;
        var pointer = capacity == 0
            ? "null"
            : EmitHeapAllocate(((long)capacity * 8).ToString(CultureInfo.InvariantCulture));
        return new RuntimeDynamicIntArray(pointer, "0", capacity.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeIntDictionary EmitDictionaryLiteral(DictionaryLiteralExpression expression)
    {
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

        foreach (var entry in expression.Entries)
        {
            var key = EmitIntExpression(entry.Key);
            var value = EmitIntExpression(entry.Value);
            EmitDictionaryInsertUnique(dictionary, key.ValueName, value.ValueName);
        }

        return dictionary;
    }

    private RuntimeIntDictionary EmitTypedEmptyDictionary(TypedEmptyDictionaryExpression expression)
    {
        if (expression.KeyType != "Int" || expression.ValueType != "Int")
        {
            throw new SmallLangException("only {Int: Int} typed empty dictionaries are supported in the current runtime slice");
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
        var index = EmitIntExpression(expression.Index);
        return source switch
        {
            RuntimeIntSlice slice => EmitIntSliceLoad(slice, index.ValueName),
            RuntimeStaticIntArray array => EmitStaticArrayLoad(array, index.ValueName),
            RuntimeStaticTextArray array => EmitStaticTextArrayLoad(array, index.ValueName),
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, index.ValueName),
            RuntimeIntDictionaryView dictionary => EmitDictionaryLookup(
                new RuntimeIntDictionary(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName),
                index.ValueName),
            RuntimeIntDictionary dictionary => EmitDictionaryLookup(dictionary, index.ValueName),
            _ => throw new SmallLangException("indexing expects an array or dictionary")
        };
    }

}

