using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeInt LoadInt(string pointer, string prefix)
    {
        var value = NextTemp(prefix);
        EmitLoad(value, "i32", pointer, 4);
        return new RuntimeInt(value);
    }

    private void EmitTrapUnless(string condition, string prefix)
    {
        var okLabel = NextLabel(prefix + "_ok");
        var failLabel = NextLabel(prefix + "_fail");
        EmitConditionalBranch(condition, okLabel, failLabel);
        EmitFunctionLine();
        EmitLabel(failLabel);
        EmitTrap();
        EmitFunctionLine();
        EmitLabel(okLabel);
        _currentBlockLabel = okLabel;
    }

    private RuntimeInt EmitIntExpression(Expression expression)
    {
        var value = EmitExpression(expression);
        return value as RuntimeInt
            ?? throw new SmallLangException("expected runtime integer expression");
    }

    private RuntimeBool EmitBoolExpression(Expression expression)
    {
        var value = EmitExpression(expression);
        return value as RuntimeBool
            ?? throw new SmallLangException("expected runtime boolean expression");
    }

    private RuntimeValue EmitAddExpression(AddExpression expression)
    {
        return EmitNumericBinary(expression.Left, expression.Right, "add", "fadd", "add");
    }

    private RuntimeValue EmitMultiplyExpression(MultiplyExpression expression)
    {
        return EmitNumericBinary(expression.Left, expression.Right, "mul", "fmul", "mul");
    }

    private RuntimeValue EmitSubtractExpression(SubtractExpression expression)
    {
        return EmitNumericBinary(expression.Left, expression.Right, "sub", "fsub", "sub");
    }

    private RuntimeValue EmitDivideExpression(DivideExpression expression)
    {
        var left = EmitExpression(expression.Left);
        var integerOp = IsSignedIntegerType(left.Type) ? "sdiv" : "udiv";
        return EmitNumericBinary(left, EmitExpression(expression.Right), integerOp, "fdiv", "div");
    }

    private RuntimeValue EmitModuloExpression(ModuloExpression expression)
    {
        var left = EmitExpression(expression.Left);
        var integerOp = IsSignedIntegerType(left.Type) ? "srem" : "urem";
        return EmitNumericBinary(left, EmitExpression(expression.Right), integerOp, "frem", "mod");
    }

    private RuntimeValue EmitNegateExpression(NegateExpression expression)
    {
        var value = EmitExpression(expression.Value);
        var result = NextTemp("neg");
        if (value is RuntimeInt integer)
        {
            EmitBinary(result, "sub", LlvmType(value.Type), "0", integer.ValueName);
            return new RuntimeInt(value.Type, result);
        }
        if (value is RuntimeFloat floating)
        {
            EmitBinary(result, "fsub", LlvmType(value.Type), "-0.0", floating.ValueName);
            return new RuntimeFloat(value.Type, result);
        }
        throw new SmallLangException("unary '-' expects a numeric value");
    }

    private RuntimeValue EmitNumericBinary(
        Expression leftExpression,
        Expression rightExpression,
        string integerOperation,
        string floatOperation,
        string prefix) => EmitNumericBinary(
            EmitExpression(leftExpression), EmitExpression(rightExpression), integerOperation, floatOperation, prefix);

    private RuntimeValue EmitNumericBinary(
        RuntimeValue left,
        RuntimeValue right,
        string integerOperation,
        string floatOperation,
        string prefix)
    {
        var result = NextTemp(prefix);
        if (left is RuntimeInt leftInt && right is RuntimeInt rightInt && left.Type == right.Type)
        {
            EmitBinary(result, integerOperation, LlvmType(left.Type), leftInt.ValueName, rightInt.ValueName);
            return new RuntimeInt(left.Type, result);
        }
        if (left is RuntimeFloat leftFloat && right is RuntimeFloat rightFloat && left.Type == right.Type)
        {
            EmitBinary(result, floatOperation, LlvmType(left.Type), leftFloat.ValueName, rightFloat.ValueName);
            return new RuntimeFloat(left.Type, result);
        }
        throw new SmallLangException("numeric operands must have the same runtime type");
    }

    private RuntimeBool EmitCompareExpression(CompareExpression expression)
    {
        var left = EmitExpression(expression.Left);
        var right = EmitExpression(expression.Right);
        if (left is RuntimeInt leftInt && right is RuntimeInt rightInt)
        {
            return EmitIntegerComparison(leftInt, expression.Operator, rightInt);
        }
        if (left is RuntimeFloat leftFloat && right is RuntimeFloat rightFloat)
        {
            var result = NextTemp("fcmp");
            var instruction = expression.Operator switch
            {
                ComparisonOperator.Equal => "oeq",
                ComparisonOperator.NotEqual => "one",
                ComparisonOperator.Less => "olt",
                ComparisonOperator.LessOrEqual => "ole",
                ComparisonOperator.Greater => "ogt",
                ComparisonOperator.GreaterOrEqual => "oge",
                _ => throw new SmallLangException($"unsupported comparison operator '{expression.Operator}'")
            };
            EmitInstruction($"{result} = fcmp {instruction} {LlvmType(left.Type)} {leftFloat.ValueName}, {rightFloat.ValueName}");
            return new RuntimeBool(result);
        }
        throw new SmallLangException("comparison operands must have the same numeric runtime type");
    }

    private RuntimeBool EmitIntegerComparison(RuntimeInt left, ComparisonOperator comparisonOperator, RuntimeInt right)
    {
        var result = NextTemp("cmp");
        var instruction = comparisonOperator switch
        {
            ComparisonOperator.Equal => "eq",
            ComparisonOperator.NotEqual => "ne",
            ComparisonOperator.Less => "slt",
            ComparisonOperator.LessOrEqual => "sle",
            ComparisonOperator.Greater => "sgt",
            ComparisonOperator.GreaterOrEqual => "sge",
            _ => throw new SmallLangException($"unsupported comparison operator '{comparisonOperator}'")
        };
        if (!IsSignedIntegerType(left.Type))
        {
            instruction = instruction switch { "slt" => "ult", "sle" => "ule", "sgt" => "ugt", "sge" => "uge", _ => instruction };
        }
        EmitCompare(result, instruction, LlvmType(left.Type), left.ValueName, right.ValueName);
        return new RuntimeBool(result);
    }

    private RuntimeBool EmitAndExpression(AndExpression expression)
    {
        var left = EmitBoolExpression(expression.Left);
        var rhsLabel = NextLabel("and_rhs");
        var endLabel = NextLabel("and_end");
        var entryLabel = _currentBlockLabel;

        EmitConditionalBranch(left.ValueName, rhsLabel, endLabel);

        EmitLabel(rhsLabel);
        _currentBlockLabel = rhsLabel;
        var right = EmitBoolExpression(expression.Right);
        var rightLabel = _currentBlockLabel;
        EmitBranch(endLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        var result = NextTemp("and");
        EmitPhi(result, "i1", ("false", entryLabel), (right.ValueName, rightLabel));
        return new RuntimeBool(result);
    }

    private RuntimeBool EmitOrExpression(OrExpression expression)
    {
        var left = EmitBoolExpression(expression.Left);
        var rhsLabel = NextLabel("or_rhs");
        var endLabel = NextLabel("or_end");
        var entryLabel = _currentBlockLabel;

        EmitConditionalBranch(left.ValueName, endLabel, rhsLabel);

        EmitLabel(rhsLabel);
        _currentBlockLabel = rhsLabel;
        var right = EmitBoolExpression(expression.Right);
        var rightLabel = _currentBlockLabel;
        EmitBranch(endLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        var result = NextTemp("or");
        EmitPhi(result, "i1", ("true", entryLabel), (right.ValueName, rightLabel));
        return new RuntimeBool(result);
    }

    private RuntimeBool EmitNotExpression(NotExpression expression)
    {
        var value = EmitBoolExpression(expression.Value);
        var result = NextTemp("not");
        EmitBinary(result, "xor", "i1", value.ValueName, "true");
        return new RuntimeBool(result);
    }

    private RuntimeValue EmitIfExpression(IfExpression expression)
    {
        var condition = EmitBoolExpression(expression.Condition);
        var thenLabel = NextLabel("if_then");
        var elseLabel = expression.Else is null ? null : NextLabel("if_else");
        var endLabel = NextLabel("if_end");

        EmitConditionalBranch(condition.ValueName, thenLabel, elseLabel ?? endLabel);

        EmitLabel(thenLabel);
        _currentBlockLabel = thenLabel;
        var thenResult = EmitScopedBlockBody(expression.Then);
        var thenEndLabel = _currentBlockLabel;
        var thenTerminated = _currentBlockTerminated;
        if (!thenTerminated)
        {
            EmitBranch(endLabel);
        }

        BlockResult? elseResult = null;
        var elseTerminated = false;
        if (expression.Else is not null)
        {
            var activeElseLabel = elseLabel!;
            EmitLabel(activeElseLabel);
            _currentBlockLabel = activeElseLabel;
            elseResult = EmitScopedBlockBody(expression.Else);
            elseTerminated = _currentBlockTerminated;
            if (!elseTerminated)
            {
                EmitBranch(endLabel);
            }
        }

        if (expression.Else is not null && thenTerminated && elseTerminated)
        {
            return RuntimeUnit.Instance;
        }

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;

        if (expression.Else is null || thenResult.Value is null || elseResult?.Value is null)
        {
            return RuntimeUnit.Instance;
        }

        return EmitPhiValue("if", thenResult.Value, thenEndLabel, elseResult.Value, elseResult.EndLabel);
    }

    private RuntimeValue EmitWhenExpression(WhenExpression expression)
    {
        var endLabel = NextLabel("when_end");
        var valueResults = new List<(RuntimeValue Value, string Label)>();
        var hasEndPredecessor = false;
        var hasSubjectConditions = expression.Arms.Any(static arm => IsSubjectWhenCondition(arm.Condition));
        var subject = expression.Subject is not null
            ? EmitIntExpression(expression.Subject)
            : hasSubjectConditions
                ? ResolveLocal("it") as RuntimeInt
                    ?? throw new SmallLangException("subject-style when without an explicit subject requires runtime integer binding 'it'")
                : null;
        var nextConditionLabel = _currentBlockLabel;

        foreach (var arm in expression.Arms)
        {
            _currentBlockLabel = nextConditionLabel;
            var armLabel = NextLabel("when_arm");
            var nextLabel = NextLabel("when_next");
            var condition = subject is null
                ? EmitBoolExpression(arm.Condition)
                : EmitSubjectWhenCondition(subject, arm.Condition);
            EmitConditionalBranch(condition.ValueName, armLabel, nextLabel);

            EmitLabel(armLabel);
            _currentBlockLabel = armLabel;
            var armResult = EmitScopedBlockBody(arm.Body);
            if (!_currentBlockTerminated && armResult.Value is not null)
            {
                valueResults.Add((armResult.Value, armResult.EndLabel));
            }

            if (!_currentBlockTerminated)
            {
                EmitBranch(endLabel);
                hasEndPredecessor = true;
            }
            EmitLabel(nextLabel);
            nextConditionLabel = nextLabel;
        }

        _currentBlockLabel = nextConditionLabel;
        var elseResult = EmitScopedBlockBody(expression.Else);
        if (!_currentBlockTerminated && elseResult.Value is not null)
        {
            valueResults.Add((elseResult.Value, elseResult.EndLabel));
        }

        if (!_currentBlockTerminated)
        {
            EmitBranch(endLabel);
            hasEndPredecessor = true;
        }
        if (!hasEndPredecessor)
        {
            return RuntimeUnit.Instance;
        }
        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;

        if (valueResults.Count == 0)
        {
            return RuntimeUnit.Instance;
        }

        return EmitPhiValue("when", valueResults);
    }

    private static bool IsSubjectWhenCondition(Expression condition)
    {
        return condition is SubjectCompareExpression or SubjectRangeExpression;
    }

    private RuntimeBool EmitSubjectWhenCondition(RuntimeInt subject, Expression condition)
    {
        if (condition is SubjectCompareExpression compare)
        {
            return EmitIntegerComparison(subject, compare.Operator, EmitIntExpression(compare.Right));
        }

        if (condition is not SubjectRangeExpression range)
        {
            throw new SmallLangException("value-flow when arm must start with a comparison operator or range");
        }

        var lower = EmitIntegerComparison(subject, ComparisonOperator.GreaterOrEqual, EmitIntExpression(range.Start));
        var upper = EmitIntegerComparison(subject, ComparisonOperator.LessOrEqual, EmitIntExpression(range.End));
        var result = NextTemp("range");
        EmitBinary(result, "and", "i1", lower.ValueName, upper.ValueName);
        return new RuntimeBool(result);
    }

    private RuntimeInt EmitFoldExpression(FoldExpression expression)
    {
        return expression.Source is RangeExpression range
            ? EmitRangeFoldExpression(expression, range)
            : EmitArrayFoldExpression(expression);
    }

    private RuntimeInt EmitRangeFoldExpression(FoldExpression expression, RangeExpression range)
    {
        var start = EmitIntExpression(range.Start);
        var end = EmitIntExpression(range.End);
        var initial = EmitIntExpression(expression.Initial);
        var bodyLabel = NextLabel("fold_body");
        var continueLabel = NextLabel("fold_continue");
        var endLabel = NextLabel("fold_end");
        var entryLabel = _currentBlockLabel;
        var nextItem = NextTemp("fold_next");
        var initialDone = NextTemp("fold_done");

        EmitCompare(initialDone, "sgt", "i32", start.ValueName, end.ValueName);
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(expression.ItemName);
        EmitPhi(item, "i32", (start.ValueName, entryLabel), (nextItem, continueLabel));

        var nextAccumulator = NextTemp("fold_acc_next");
        var accumulator = NextTemp(expression.AccumulatorName);
        EmitPhi(accumulator, "i32", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));

        var outerLocals = CaptureLocals();
        _locals[expression.AccumulatorName] = new RuntimeInt(accumulator);
        _locals[expression.ItemName] = new RuntimeInt(item);
        var bodyResult = EmitScopedBlockBody(expression.Body);
        RestoreLocals(outerLocals);
        if (bodyResult.Value is not RuntimeInt bodyValue)
        {
            throw new SmallLangException("fold body must return an integer accumulator value");
        }

        EmitBinary(nextAccumulator, "add", "i32", bodyValue.ValueName, "0");
        EmitBranch(continueLabel);

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(nextItem, "add", "i32", item, "1");
        var done = NextTemp("fold_done");
        EmitCompare(done, "sgt", "i32", nextItem, end.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        var result = NextTemp("fold");
        EmitPhi(result, "i32", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitArrayFoldExpression(FoldExpression expression)
    {
        var source = EmitExpression(expression.Source);
        var (pointer, length, staticLength) = source switch
        {
            RuntimeIntSlice slice => (slice.PointerName, slice.LengthName, null),
            RuntimeStaticIntArray array => (array.PointerName, array.LengthName, (int?)array.AllocatedLength),
            RuntimeDynamicIntArray array => (array.PointerName, array.LengthName, null),
            _ => throw new SmallLangException("fold expects a range or Int array input")
        };

        var initial = EmitIntExpression(expression.Initial);
        var bodyLabel = NextLabel("array_fold_body");
        var continueLabel = NextLabel("array_fold_continue");
        var endLabel = NextLabel("array_fold_end");
        var entryLabel = _currentBlockLabel;
        var nextIndex = NextTemp("array_fold_next");
        var initialDone = NextTemp("array_fold_done");

        EmitCompare(initialDone, "eq", "i64", length, "0");
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var index = NextTemp("array_fold_i");
        EmitPhi(index, "i64", ("0", entryLabel), (nextIndex, continueLabel));

        var nextAccumulator = NextTemp("array_fold_acc_next");
        var accumulator = NextTemp(expression.AccumulatorName);
        EmitPhi(accumulator, "i32", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));

        RuntimeInt item;
        if (staticLength is { } allocatedLength)
        {
            item = EmitStaticArrayLoad(new RuntimeStaticIntArray(pointer, length, allocatedLength), index);
        }
        else if (source is RuntimeIntSlice)
        {
            item = EmitIntSliceLoad(new RuntimeIntSlice(pointer, length), index);
        }
        else
        {
            item = EmitDynamicArrayLoad(new RuntimeDynamicIntArray(pointer, length, length), index);
        }

        var outerLocals = CaptureLocals();
        _locals[expression.AccumulatorName] = new RuntimeInt(accumulator);
        _locals[expression.ItemName] = item;
        var bodyResult = EmitScopedBlockBody(expression.Body);
        RestoreLocals(outerLocals);
        if (bodyResult.Value is not RuntimeInt bodyValue)
        {
            throw new SmallLangException("fold body must return an integer accumulator value");
        }

        EmitBinary(nextAccumulator, "add", "i32", bodyValue.ValueName, "0");
        EmitBranch(continueLabel);

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(nextIndex, "add", "i64", index, "1");
        var done = NextTemp("array_fold_done");
        EmitCompare(done, "eq", "i64", nextIndex, length);
        EmitConditionalBranch(done, endLabel, bodyLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        var result = NextTemp("array_fold");
        EmitPhi(result, "i32", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));
        return new RuntimeInt(result);
    }

    private BlockResult EmitScopedBlockBody(BlockBody body)
    {
        var outerLocals = CaptureLocals();
        try
        {
            EmitStatements(body.Statements);
            if (_currentBlockTerminated)
            {
                return new BlockResult(null, _currentBlockLabel);
            }
            var transferredOwnerName = body.Value is null
                ? null
                : GetBlockResultTransferredOwnerName(body.Value);
            var value = body.Value is null ? null : EmitExpression(body.Value);
            DropOwnedLocalsCreatedSince(outerLocals, transferredOwnerName);
            return new BlockResult(value, _currentBlockLabel);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
    }

    private RuntimeValue EmitPhiValue(
        string prefix,
        RuntimeValue left,
        string leftLabel,
        RuntimeValue right,
        string rightLabel)
    {
        return EmitPhiValue(prefix, [(left, leftLabel), (right, rightLabel)]);
    }

    private RuntimeValue EmitPhiValue(string prefix, IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        return incoming[0].Value switch
        {
            RuntimeInt integer => new RuntimeInt(integer.Type, EmitScalarPhi(prefix, LlvmType(integer.Type), incoming)),
            RuntimeFloat floating => new RuntimeFloat(floating.Type, EmitScalarPhi(prefix, LlvmType(floating.Type), incoming)),
            RuntimeBool => new RuntimeBool(EmitScalarPhi(prefix, "i1", incoming)),
            RuntimeText => EmitTextPhi(prefix, incoming),
            RuntimeDynamicIntArray => EmitDynamicArrayPhi(prefix, incoming),
            RuntimeIntDictionary => EmitIntDictionaryPhi(prefix, incoming),
            RuntimeStruct structure => EmitStructPhi(prefix, structure.Type, incoming),
            RuntimeEnum enumeration => EmitEnumPhi(prefix, enumeration.Type, incoming),
            RuntimeUnit => RuntimeUnit.Instance,
            _ => throw new SmallLangException($"unsupported phi value {incoming[0].Value.GetType().Name}")
        };
    }

    private string EmitScalarPhi(string prefix, string typeName, IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var result = NextTemp(prefix);
        var incomingList = FormatPhiIncoming(incoming, static value => value switch
        {
            RuntimeInt integer => integer.ValueName,
            RuntimeFloat floating => floating.ValueName,
            RuntimeBool boolean => boolean.ValueName,
            _ => throw new SmallLangException($"unsupported scalar phi value {value.GetType().Name}")
        });
        EmitPhi(result, typeName, incomingList);
        return result;
    }

    private RuntimeText EmitTextPhi(string prefix, IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var pointer = NextTemp(prefix + "_ptr");
        EmitPhi(pointer, "ptr", FormatPhiIncoming(incoming, static value => ((RuntimeText)value).PointerName));

        var length = NextTemp(prefix + "_len");
        EmitPhi(length, "i64", FormatPhiIncoming(incoming, static value => ((RuntimeText)value).LengthName));

        return new RuntimeText(pointer, length);
    }

    private RuntimeDynamicIntArray EmitDynamicArrayPhi(string prefix, IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var pointer = NextTemp(prefix + "_ptr");
        EmitPhi(pointer, "ptr", FormatPhiIncoming(incoming, static value => ((RuntimeDynamicIntArray)value).PointerName));

        var length = NextTemp(prefix + "_len");
        EmitPhi(length, "i64", FormatPhiIncoming(incoming, static value => ((RuntimeDynamicIntArray)value).LengthName));

        var capacity = NextTemp(prefix + "_capacity");
        EmitPhi(capacity, "i64", FormatPhiIncoming(incoming, static value => ((RuntimeDynamicIntArray)value).CapacityName));

        return new RuntimeDynamicIntArray(pointer, length, capacity);
    }

    private RuntimeIntDictionary EmitIntDictionaryPhi(string prefix, IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var pointer = NextTemp(prefix + "_ptr");
        EmitPhi(pointer, "ptr", FormatPhiIncoming(incoming, static value => ((RuntimeIntDictionary)value).PointerName));

        var length = NextTemp(prefix + "_len");
        EmitPhi(length, "i64", FormatPhiIncoming(incoming, static value => ((RuntimeIntDictionary)value).LengthName));

        var capacity = NextTemp(prefix + "_capacity");
        EmitPhi(capacity, "i64", FormatPhiIncoming(incoming, static value => ((RuntimeIntDictionary)value).CapacityName));

        return new RuntimeIntDictionary(pointer, length, capacity);
    }

    private RuntimeStruct EmitStructPhi(
        string prefix,
        BoundType type,
        IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var value = NextTemp(prefix);
        EmitPhi(
            value,
            LlvmStructType(type),
            FormatPhiIncoming(incoming, static item => ((RuntimeStruct)item).ValueName));
        return new RuntimeStruct(type, value);
    }

    private RuntimeEnum EmitEnumPhi(
        string prefix,
        BoundType type,
        IReadOnlyList<(RuntimeValue Value, string Label)> incoming)
    {
        var value = NextTemp(prefix);
        EmitPhi(
            value,
            LlvmEnumType(type),
            FormatPhiIncoming(incoming, static item => ((RuntimeEnum)item).ValueName));
        return new RuntimeEnum(type, value);
    }

    private static (string Value, string Label)[] FormatPhiIncoming(
        IReadOnlyList<(RuntimeValue Value, string Label)> incoming,
        Func<RuntimeValue, string> getValueName)
    {
        return incoming
            .Select(item => (getValueName(item.Value), item.Label))
            .ToArray();
    }

}

