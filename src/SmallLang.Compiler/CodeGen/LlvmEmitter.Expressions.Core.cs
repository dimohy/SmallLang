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
        return expression switch
        {
            StringExpression str => EmitTextLiteral(str),
            NumberExpression number => new RuntimeInt(ParseNumber(number).ToString(CultureInfo.InvariantCulture)),
            BoolExpression boolean => new RuntimeBool(boolean.Value ? "true" : "false"),
            NameExpression name => ResolveLocal(name.Name),
            ArrayLiteralExpression array => EmitArrayLiteral(array),
            ArrayRepeatExpression repeat => EmitArrayRepeat(repeat),
            DictionaryLiteralExpression dictionary => EmitDictionaryLiteral(dictionary),
            IndexExpression index => EmitIndexExpression(index),
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
            SubjectCompareExpression => throw new SmallLangException("subject comparison is only valid inside value-flow when"),
            SubjectRangeExpression => throw new SmallLangException("subject range is only valid inside value-flow when"),
            FoldExpression fold => EmitFoldExpression(fold),
            RangeExpression => throw new SmallLangException("range values are only valid as block-function input"),
            CallExpression call => EmitFunctionCall(call),
            FlowExpression flow => EmitFlowExpressionValue(flow),
            _ => throw new SmallLangException($"unsupported runtime expression {expression.GetType().Name}")
        };
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
            : EmitStaticIntArrayLiteral(expression);
    }

    private RuntimeStaticIntArray EmitStaticIntArrayLiteral(ArrayLiteralExpression expression)
    {
        var length = expression.Elements.Count;
        var allocatedLength = Math.Max(length, 1);
        var pointer = NextTemp("array");
        EmitAlloca(pointer, $"[{allocatedLength.ToString(CultureInfo.InvariantCulture)} x i64]", 8);

        for (var i = 0; i < expression.Elements.Count; i++)
        {
            var value = EmitIntExpression(expression.Elements[i]);
            StoreStaticArrayElement(pointer, allocatedLength, i, value.ValueName);
        }

        return new RuntimeStaticIntArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            allocatedLength);
    }

    private RuntimeStaticIntArray EmitArrayRepeat(ArrayRepeatExpression expression)
    {
        var allocatedLength = Math.Max(expression.Count, 1);
        var pointer = NextTemp("array");
        EmitAlloca(pointer, $"[{allocatedLength.ToString(CultureInfo.InvariantCulture)} x i64]", 8);

        var value = EmitIntExpression(expression.Value);
        for (var i = 0; i < expression.Count; i++)
        {
            StoreStaticArrayElement(pointer, allocatedLength, i, value.ValueName);
        }

        return new RuntimeStaticIntArray(
            pointer,
            expression.Count.ToString(CultureInfo.InvariantCulture),
            allocatedLength);
    }

    private RuntimeDynamicIntArray EmitDynamicIntArrayLiteral(ArrayLiteralExpression expression)
    {
        var length = expression.Elements.Count;
        var capacity = length;
        var pointer = capacity == 0
            ? "null"
            : EmitHeapAllocate((capacity * 8).ToString(CultureInfo.InvariantCulture));

        for (var i = 0; i < expression.Elements.Count; i++)
        {
            var value = EmitIntExpression(expression.Elements[i]);
            StoreDynamicArrayElement(pointer, i.ToString(CultureInfo.InvariantCulture), value.ValueName);
        }

        return new RuntimeDynamicIntArray(
            pointer,
            length.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeIntDictionary EmitDictionaryLiteral(DictionaryLiteralExpression expression)
    {
        var length = expression.Entries.Count;
        var capacity = DictionaryCapacityForLength(length);
        var dictionary = new RuntimeIntDictionary(
            EmitDictionaryAllocate(capacity.ToString(CultureInfo.InvariantCulture)),
            length.ToString(CultureInfo.InvariantCulture),
            capacity.ToString(CultureInfo.InvariantCulture));

        foreach (var entry in expression.Entries)
        {
            var key = EmitIntExpression(entry.Key);
            var value = EmitIntExpression(entry.Value);
            EmitDictionaryInsertUnique(dictionary, key.ValueName, value.ValueName);
        }

        return dictionary;
    }

    private RuntimeInt EmitIndexExpression(IndexExpression expression)
    {
        var source = EmitExpression(expression.Source);
        var index = EmitIntExpression(expression.Index);
        return source switch
        {
            RuntimeStaticIntArray array => EmitStaticArrayLoad(array, index.ValueName),
            RuntimeDynamicIntArray array => EmitDynamicArrayLoad(array, index.ValueName),
            RuntimeIntDictionary dictionary => EmitDictionaryLookup(dictionary, index.ValueName),
            _ => throw new SmallLangException("indexing expects an array or dictionary")
        };
    }

}

