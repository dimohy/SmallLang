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
        EmitLoad(value, "i64", pointer, 8);
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

    private RuntimeInt EmitAddExpression(AddExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("add");
        EmitBinary(result, "add nsw", "i64", left.ValueName, right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitMultiplyExpression(MultiplyExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("mul");
        EmitBinary(result, "mul nsw", "i64", left.ValueName, right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitSubtractExpression(SubtractExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("sub");
        EmitBinary(result, "sub nsw", "i64", left.ValueName, right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitDivideExpression(DivideExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("div");
        EmitBinary(result, "sdiv", "i64", left.ValueName, right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitModuloExpression(ModuloExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("mod");
        EmitBinary(result, "srem", "i64", left.ValueName, right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitNegateExpression(NegateExpression expression)
    {
        var value = EmitIntExpression(expression.Value);
        var result = NextTemp("neg");
        EmitBinary(result, "sub nsw", "i64", "0", value.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeBool EmitCompareExpression(CompareExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        return EmitIntegerComparison(left, expression.Operator, right);
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
        EmitCompare(result, instruction, "i64", left.ValueName, right.ValueName);
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
        EmitBranch(endLabel);

        BlockResult? elseResult = null;
        if (expression.Else is not null)
        {
            var activeElseLabel = elseLabel!;
            EmitLabel(activeElseLabel);
            _currentBlockLabel = activeElseLabel;
            elseResult = EmitScopedBlockBody(expression.Else);
            EmitBranch(endLabel);
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
            if (armResult.Value is not null)
            {
                valueResults.Add((armResult.Value, armResult.EndLabel));
            }

            EmitBranch(endLabel);
            EmitLabel(nextLabel);
            nextConditionLabel = nextLabel;
        }

        _currentBlockLabel = nextConditionLabel;
        var elseResult = EmitScopedBlockBody(expression.Else);
        if (elseResult.Value is not null)
        {
            valueResults.Add((elseResult.Value, elseResult.EndLabel));
        }

        EmitBranch(endLabel);
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

        EmitCompare(initialDone, "sgt", "i64", start.ValueName, end.ValueName);
        EmitConditionalBranch(initialDone, endLabel, bodyLabel);

        EmitLabel(bodyLabel);
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(expression.ItemName);
        EmitPhi(item, "i64", (start.ValueName, entryLabel), (nextItem, continueLabel));

        var nextAccumulator = NextTemp("fold_acc_next");
        var accumulator = NextTemp(expression.AccumulatorName);
        EmitPhi(accumulator, "i64", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));

        var outerLocals = CaptureLocals();
        _locals[expression.AccumulatorName] = new RuntimeInt(accumulator);
        _locals[expression.ItemName] = new RuntimeInt(item);
        var bodyResult = EmitScopedBlockBody(expression.Body);
        RestoreLocals(outerLocals);
        if (bodyResult.Value is not RuntimeInt bodyValue)
        {
            throw new SmallLangException("fold body must return an integer accumulator value");
        }

        EmitBinary(nextAccumulator, "add", "i64", bodyValue.ValueName, "0");
        EmitBranch(continueLabel);

        EmitLabel(continueLabel);
        _currentBlockLabel = continueLabel;
        EmitBinary(nextItem, "add", "i64", item, "1");
        var done = NextTemp("fold_done");
        EmitCompare(done, "sgt", "i64", nextItem, end.ValueName);
        EmitConditionalBranch(done, endLabel, bodyLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        var result = NextTemp("fold");
        EmitPhi(result, "i64", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitArrayFoldExpression(FoldExpression expression)
    {
        var source = EmitExpression(expression.Source);
        var (pointer, length, staticLength) = source switch
        {
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
        EmitPhi(accumulator, "i64", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));

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
        _locals[expression.AccumulatorName] = new RuntimeInt(accumulator);
        _locals[expression.ItemName] = item;
        var bodyResult = EmitScopedBlockBody(expression.Body);
        RestoreLocals(outerLocals);
        if (bodyResult.Value is not RuntimeInt bodyValue)
        {
            throw new SmallLangException("fold body must return an integer accumulator value");
        }

        EmitBinary(nextAccumulator, "add", "i64", bodyValue.ValueName, "0");
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
        EmitPhi(result, "i64", (initial.ValueName, entryLabel), (nextAccumulator, continueLabel));
        return new RuntimeInt(result);
    }

    private BlockResult EmitScopedBlockBody(BlockBody body)
    {
        var outerLocals = CaptureLocals();
        try
        {
            EmitStatements(body.Statements);
            var value = body.Value is null ? null : EmitExpression(body.Value);
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
            RuntimeInt => new RuntimeInt(EmitScalarPhi(prefix, "i64", incoming)),
            RuntimeBool => new RuntimeBool(EmitScalarPhi(prefix, "i1", incoming)),
            RuntimeText => EmitTextPhi(prefix, incoming),
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

    private static (string Value, string Label)[] FormatPhiIncoming(
        IReadOnlyList<(RuntimeValue Value, string Label)> incoming,
        Func<RuntimeValue, string> getValueName)
    {
        return incoming
            .Select(item => (getValueName(item.Value), item.Label))
            .ToArray();
    }

}

