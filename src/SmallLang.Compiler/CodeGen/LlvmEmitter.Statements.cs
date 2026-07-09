using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitMain()
    {
        _locals.Clear();
        _mutableLocals.Clear();
        _currentFunctions = _program.Functions;
        _mainOk = "true";
        _currentBlockLabel = "entry";
        EmitFunctionLine($"define dso_local i32 @{_platform.EntryPointName}() local_unnamed_addr {{");
        EmitLabel("entry");
        EmitAlloca("%written", "i32", 4);
        EmitAlloca("%read", "i32", 4);
        EmitAlloca("%ok_state", "i1", 1);
        EmitStore("i1", "true", "%ok_state", 1);
        EmitPlatformFunctionBlock(_platform.EmitEntryHandles);

        EmitStatements(_program.MainStatements);

        DropOwnedLocals();

        var finalOk = NextTemp("final_ok");
        EmitLoad(finalOk, "i1", "%ok_state", 1);
        var exit = NextTemp("exit");
        EmitSelect(exit, finalOk, "i32 0", "i32 1");
        EmitRet("i32", exit);
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BindingStatement binding:
                var movedSourceName = GetMoveConsumingContainerSourceName(binding.Value);
                var value = EmitExpression(binding.Value);
                if (RequiresHeapAllocation(value) && !_platform.SupportsHeapAllocation)
                {
                    throw new SmallLangException("dynamic arrays and dictionaries require heap allocation; wasm32-browser does not support them yet");
                }

                if (movedSourceName is not null)
                {
                    RemoveLocal(movedSourceName);
                }

                _mutableContainerSlots.Remove(binding.Name);
                _locals.Add(binding.Name, value);
                if (binding.IsMutable)
                {
                    _mutableLocals.Add(binding.Name);
                    CreateMutableContainerSlot(binding.Name, value);
                }
                break;
            case BlockFunctionCallStatement blockFunctionCall:
                EmitBlockFunctionCall(blockFunctionCall);
                break;
            case ExpressionStatement expressionStatement:
                _mainOk = EmitExpressionStatement(expressionStatement.Expression, _mainOk);
                break;
            default:
                throw new SmallLangException($"unsupported runtime statement {statement.GetType().Name}");
        }
    }

    private void EmitBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var target = string.Join('.', statement.Target);
        switch (target)
        {
            case "each":
                if (statement.Source is RangeExpression range)
                {
                    EmitEachBlockFunctionCall(statement, range);
                    return;
                }

                EmitArrayEachBlockFunctionCall(statement);
                return;
            case "repeat":
                EmitRepeatBlockFunctionCall(statement);
                return;
            default:
                if (TryResolveFunction(statement.Target, out var function)
                    && function.Kind == BoundFunctionKind.UserBlock)
                {
                    EmitUserBlockFunctionCall(statement, function);
                    return;
                }

                throw new SmallLangException($"unknown block function '{target}'");
        }
    }

    private void EmitEachBlockFunctionCall(BlockFunctionCallStatement statement, RangeExpression range)
    {
        var start = EmitIntExpression(range.Start);
        var end = EmitIntExpression(range.End);
        var bodyLabel = NextLabel("each_body");
        var continueLabel = NextLabel("each_continue");
        var endLabel = NextLabel("each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("each_next");
        var initialDone = NextTemp("each_done");

        EmitCompare(initialDone, "sgt", "i64", start.ValueName, end.ValueName);
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(statement.ItemName);
        EmitPhi(item, "i64", (start.ValueName, entryLabel), (next, continueLabel));

        var outerLocals = CaptureLocals();
        _locals[statement.ItemName] = new RuntimeInt(item);
        EmitStatements(statement.Body);
        RestoreLocals(outerLocals);

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", item, "1");
        var done = NextTemp("each_done");
        EmitCompare(done, "sgt", "i64", next, end.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitArrayEachBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var source = EmitExpression(statement.Source);
        var (pointer, length, staticLength) = source switch
        {
            RuntimeStaticIntArray array => (array.PointerName, array.LengthName, (int?)array.AllocatedLength),
            RuntimeDynamicIntArray array => (array.PointerName, array.LengthName, null),
            _ => throw new SmallLangException("each expects a range or Int array input")
        };

        var bodyLabel = NextLabel("array_each_body");
        var continueLabel = NextLabel("array_each_continue");
        var endLabel = NextLabel("array_each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("array_each_next");
        var initialDone = NextTemp("array_each_done");

        EmitCompare(initialDone, "eq", "i64", length, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("array_each_i");
        EmitPhi(index, "i64", ("0", entryLabel), (next, continueLabel));

        RuntimeInt item;
        if (staticLength is { } allocatedLength)
        {
            item = EmitStaticArrayLoad(new RuntimeStaticIntArray(pointer, length, allocatedLength), index);
        }
        else
        {
            item = EmitDynamicArrayLoad(new RuntimeDynamicIntArray(pointer, length, length), index);
        }

        var outerLocals = CaptureLocals();
        _locals[statement.ItemName] = item;
        EmitStatements(statement.Body);
        RestoreLocals(outerLocals);

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", index, "1");
        var done = NextTemp("array_each_done");
        EmitCompare(done, "eq", "i64", next, length);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

    private void EmitUserBlockFunctionCall(BlockFunctionCallStatement statement, BoundFunction function)
    {
        if (function.InputType is null || function.BlockInputType is null)
        {
            throw new SmallLangException($"block function '{function.Name}' is not callable");
        }

        var argument = EmitExpression(statement.Source);
        EnsureRuntimeType(argument, function.InputType.Value, function.Name);

        var callerLocals = CaptureLocals();
        var callerFunctions = _currentFunctions;
        var previousInvocation = _currentBlockInvocation;
        var previousFunctions = _currentFunctions;
        var blockLocals = new LocalScope(
            new Dictionary<string, RuntimeValue>(StringComparer.Ordinal)
            {
                [function.InputName ?? "it"] = argument
            },
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, MutableContainerSlot>(StringComparer.Ordinal));

        _currentBlockInvocation = new RuntimeBlockInvocation(
            statement.ItemName,
            statement.Body,
            callerLocals,
            callerFunctions);
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        RestoreLocals(blockLocals);
        try
        {
            EmitStatements(function.BlockBody);
        }
        finally
        {
            _currentBlockInvocation = previousInvocation;
            _currentFunctions = previousFunctions;
            RestoreLocals(callerLocals);
        }
    }

    private void EmitRepeatBlockFunctionCall(BlockFunctionCallStatement statement)
    {
        var count = EmitIntExpression(statement.Source);
        var bodyLabel = NextLabel("repeat_body");
        var continueLabel = NextLabel("repeat_continue");
        var endLabel = NextLabel("repeat_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("repeat_next");
        var initialDone = NextTemp("repeat_done");

        EmitCompare(initialDone, "sle", "i64", count.ValueName, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(statement.ItemName);
        EmitPhi(item, "i64", ("1", entryLabel), (next, continueLabel));

        var outerLocals = CaptureLocals();
        _locals[statement.ItemName] = new RuntimeInt(item);
        EmitStatements(statement.Body);
        RestoreLocals(outerLocals);

        EmitBranch(continueLabel);
        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(next, "add", "i64", item, "1");
        var done = NextTemp("repeat_done");
        EmitCompare(done, "sgt", "i64", next, count.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
    }

}

