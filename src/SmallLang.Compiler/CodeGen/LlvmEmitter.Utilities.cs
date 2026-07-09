using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitGlobalLine(string line = "")
    {
        _globals.Add(line + Environment.NewLine);
    }

    private void EmitGlobalBlock(string block)
    {
        _globals.Add(EnsureTrailingNewLine(block));
    }

    private void EmitFunctionLine(string line = "")
    {
        _functions.Add(line + Environment.NewLine);
    }

    private void EmitFunctionBlock(string block)
    {
        _functions.Add(EnsureTrailingNewLine(block));
    }

    private void EmitPlatformGlobalBlock(Action<StringBuilder> emit)
    {
        var block = new StringBuilder();
        emit(block);
        EmitGlobalBlock(block.ToString());
    }

    private void EmitPlatformFunctionBlock(Action<StringBuilder> emit)
    {
        var block = new StringBuilder();
        emit(block);
        EmitFunctionBlock(block.ToString());
    }

    private static string EnsureTrailingNewLine(string block)
    {
        return block.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? block
            : block + Environment.NewLine;
    }

    private bool TryResolveFunction(IReadOnlyList<string> path, out BoundFunction function)
    {
        return _currentFunctions.TryGetValue(string.Join('.', path), out function!);
    }

    private static IReadOnlyDictionary<string, BoundFunction> CreateFunctionScope(
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyDictionary<string, BoundFunction> localFunctions)
    {
        if (localFunctions.Count == 0)
        {
            return parentFunctions;
        }

        var functions = new Dictionary<string, BoundFunction>(parentFunctions, StringComparer.Ordinal);
        foreach (var (name, function) in localFunctions)
        {
            functions[name] = function;
        }

        return functions;
    }

    private RuntimeValue ResolveLocal(string name)
    {
        var value = _locals.TryGetValue(name, out var local)
            ? local
            : throw new SmallLangException($"unknown runtime binding '{name}'");

        return LoadMutableContainer(name, value);
    }

    private void EmitYield(RuntimeValue value, RuntimeBlockInvocation invocation)
    {
        var blockFunctionLocals = CaptureLocals();
        var blockFunctionFunctions = _currentFunctions;
        RestoreLocals(invocation.CallerLocals);
        _locals[invocation.ItemName] = value;
        _currentFunctions = invocation.CallerFunctions;
        try
        {
            EmitStatements(invocation.Body);
        }
        finally
        {
            _currentFunctions = blockFunctionFunctions;
            RestoreLocals(blockFunctionLocals);
        }
    }

    private LocalScope CaptureLocals()
    {
        return new LocalScope(
            new Dictionary<string, RuntimeValue>(_locals, StringComparer.Ordinal),
            new HashSet<string>(_mutableLocals, StringComparer.Ordinal),
            new Dictionary<string, MutableContainerSlot>(_mutableContainerSlots, StringComparer.Ordinal));
    }

    private void RestoreLocals(LocalScope scope)
    {
        _locals.Clear();
        foreach (var (name, value) in scope.Locals)
        {
            _locals.Add(name, value);
        }

        _mutableLocals.Clear();
        foreach (var name in scope.MutableLocals)
        {
            _mutableLocals.Add(name);
        }

        _mutableContainerSlots.Clear();
        foreach (var (name, slot) in scope.MutableContainerSlots)
        {
            _mutableContainerSlots.Add(name, slot);
        }
    }

    private void DropOwnedLocals()
    {
        foreach (var (name, storedValue) in _locals.Reverse())
        {
            var value = LoadMutableContainer(name, storedValue);
            switch (value)
            {
                case RuntimeDynamicIntArray array:
                    EmitCall(target: null, "void", "smalllang_free", $"ptr {array.PointerName}");
                    break;
                case RuntimeIntDictionary dictionary:
                    EmitCall(target: null, "void", "smalllang_free", $"ptr {dictionary.PointerName}");
                    break;
            }
        }
    }

    private static bool RequiresHeapAllocation(RuntimeValue value)
    {
        return value is RuntimeDynamicIntArray or RuntimeIntDictionary;
    }

    private void RemoveLocal(string name)
    {
        _locals.Remove(name);
        _mutableLocals.Remove(name);
        _mutableContainerSlots.Remove(name);
    }

    private void CreateMutableContainerSlot(string name, RuntimeValue value)
    {
        if (!RequiresHeapAllocation(value))
        {
            return;
        }

        var slot = new MutableContainerSlot(
            NextTemp("mutable_ptr_addr"),
            NextTemp("mutable_len_addr"),
            NextTemp("mutable_capacity_addr"));
        EmitAlloca(slot.PointerAddress, "ptr", 8);
        EmitAlloca(slot.LengthAddress, "i64", 8);
        EmitAlloca(slot.CapacityAddress, "i64", 8);
        _mutableContainerSlots[name] = slot;
        StoreMutableContainer(name, value);
    }

    private RuntimeValue LoadMutableContainer(string name, RuntimeValue value)
    {
        if (!_mutableContainerSlots.TryGetValue(name, out var slot))
        {
            return value;
        }

        var pointer = NextTemp("mutable_ptr");
        var length = NextTemp("mutable_len");
        var capacity = NextTemp("mutable_capacity");
        EmitLoad(pointer, "ptr", slot.PointerAddress, 8);
        EmitLoad(length, "i64", slot.LengthAddress, 8);
        EmitLoad(capacity, "i64", slot.CapacityAddress, 8);

        return value switch
        {
            RuntimeDynamicIntArray => new RuntimeDynamicIntArray(pointer, length, capacity),
            RuntimeIntDictionary => new RuntimeIntDictionary(pointer, length, capacity),
            _ => value
        };
    }

    private void StoreMutableContainer(string name, RuntimeValue value)
    {
        if (!_mutableContainerSlots.TryGetValue(name, out var slot))
        {
            return;
        }

        switch (value)
        {
            case RuntimeDynamicIntArray array:
                EmitStore("ptr", array.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", array.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", array.CapacityName, slot.CapacityAddress, 8);
                break;
            case RuntimeIntDictionary dictionary:
                EmitStore("ptr", dictionary.PointerName, slot.PointerAddress, 8);
                EmitStore("i64", dictionary.LengthName, slot.LengthAddress, 8);
                EmitStore("i64", dictionary.CapacityName, slot.CapacityAddress, 8);
                break;
        }
    }

    private static string? GetMoveConsumingContainerSourceName(Expression expression)
    {
        if (expression is not FlowExpression flow || flow.Targets.Count == 0)
        {
            return null;
        }

        var lastTarget = flow.Targets[^1];
        if (lastTarget.Path.Count != 1
            || lastTarget.Path[0] is not ("append" or "updated")
            || flow.Source is not NameExpression name)
        {
            return null;
        }

        return name.Name;
    }

    private GlobalString AddGlobalString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var name = "@.smalllang.str." + _stringId.ToString(CultureInfo.InvariantCulture);
        _stringId++;
        EmitGlobalLine($"""{name} = private unnamed_addr constant [{bytes.Length.ToString(CultureInfo.InvariantCulture)} x i8] c"{EscapeLlvmBytes(bytes)}", align 1""");
        return new GlobalString(name, bytes.Length);
    }

    private static string GetPlainText(Expression expression, int line, int column)
    {
        if (expression is not StringExpression str)
        {
            throw new SmallLangException($"codegen error at {line}:{column}: expected a string literal");
        }

        var segments = new List<string>();
        foreach (var segment in str.Segments)
        {
            if (segment is TextSegment text)
            {
                segments.Add(text.Text);
                continue;
            }

            throw new SmallLangException($"codegen error at {line}:{column}: expected a plain string literal");
        }

        return string.Concat(segments);
    }

    private static long ParseNumber(NumberExpression expression)
    {
        return long.TryParse(
            expression.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new SmallLangException($"codegen error at {expression.Line}:{expression.Column}: integer literal is out of range");
    }

    private static int DictionaryCapacityForLength(int length)
    {
        var minimum = Math.Max(length * 2, 4);
        var capacity = 1;
        while (capacity < minimum)
        {
            capacity *= 2;
        }

        return capacity;
    }

    private static string SymbolForFunction(string name)
    {
        return "@smalllang_fn_" + string.Concat(name.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }

    private string NextTemp(string prefix)
    {
        var name = "%" + prefix + _tempId.ToString(CultureInfo.InvariantCulture);
        _tempId++;
        return name;
    }

    private string NextLabel(string prefix)
    {
        var name = prefix + _labelId.ToString(CultureInfo.InvariantCulture);
        _labelId++;
        return name;
    }

    private string CurrentTemp(string prefix)
    {
        return "%" + prefix + (_tempId - 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string EscapeLlvmBytes(byte[] bytes)
    {
        return string.Concat(bytes.Select(static b =>
            b is >= 0x20 and <= 0x7E && b != (byte)'\\' && b != (byte)'"'
                ? ((char)b).ToString()
                : "\\" + b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private sealed record GlobalString(string Name, int Length);

    private abstract record RuntimeValue(BoundType Type);

    private sealed record RuntimeText(string PointerName, string LengthName) : RuntimeValue(BoundType.Text);

    private sealed record RuntimeInt(string ValueName) : RuntimeValue(BoundType.Int);

    private sealed record RuntimeBool(string ValueName) : RuntimeValue(BoundType.Bool);

    private sealed record RuntimeStaticIntArray(string PointerName, string LengthName, int AllocatedLength)
        : RuntimeValue(BoundType.StaticIntArray);

    private sealed record RuntimeDynamicIntArray(string PointerName, string LengthName, string CapacityName)
        : RuntimeValue(BoundType.DynamicIntArray);

    private sealed record RuntimeIntDictionary(string PointerName, string LengthName, string CapacityName)
        : RuntimeValue(BoundType.IntDictionary);

    private sealed record DictionaryFindResult(string FoundName, string SlotName, string H2ByteName);

    private sealed record RuntimeUnit() : RuntimeValue(BoundType.Unit)
    {
        public static RuntimeUnit Instance { get; } = new();
    }

    private sealed record BlockResult(RuntimeValue? Value, string EndLabel);

    private sealed record RuntimeFlowBinding(string Name, RuntimeValue Value);

    private sealed record RuntimeFlowResult(RuntimeValue? Value, RuntimeFlowBinding? Binding, string Ok);

    private sealed record MutableContainerSlot(string PointerAddress, string LengthAddress, string CapacityAddress);

    private sealed record LocalScope(
        Dictionary<string, RuntimeValue> Locals,
        HashSet<string> MutableLocals,
        Dictionary<string, MutableContainerSlot> MutableContainerSlots);

    private sealed record RuntimeBlockInvocation(
        string ItemName,
        IReadOnlyList<Statement> Body,
        LocalScope CallerLocals,
        IReadOnlyDictionary<string, BoundFunction> CallerFunctions);
}

