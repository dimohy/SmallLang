using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed record StackSlotPlan(int Index, int Size, int Alignment);

internal sealed record StackAllocationPlan(int SlotIndex, int Size, int Alignment);

internal sealed class StackFramePlan
{
    public StackFramePlan(
        IReadOnlyList<StackSlotPlan> slots,
        IReadOnlyDictionary<object, StackAllocationPlan> allocations,
        IReadOnlyDictionary<object, IReadOnlyList<StackAllocationPlan>> lifetimeEndsAfter)
    {
        Slots = slots;
        Allocations = allocations;
        LifetimeEndsAfter = lifetimeEndsAfter;
    }

    public static StackFramePlan Empty { get; } = new(
        [],
        new Dictionary<object, StackAllocationPlan>(ReferenceEqualityComparer.Instance),
        new Dictionary<object, IReadOnlyList<StackAllocationPlan>>(ReferenceEqualityComparer.Instance));

    public IReadOnlyList<StackSlotPlan> Slots { get; }

    internal IReadOnlyDictionary<object, StackAllocationPlan> Allocations { get; }

    internal IReadOnlyDictionary<object, IReadOnlyList<StackAllocationPlan>> LifetimeEndsAfter { get; }

    public bool TryGetAllocation(object unit, out StackAllocationPlan allocation)
    {
        return Allocations.TryGetValue(unit, out allocation!);
    }

    public IReadOnlyList<StackAllocationPlan> GetLifetimesEndingAfter(object unit)
    {
        return LifetimeEndsAfter.TryGetValue(unit, out var ending) ? ending : [];
    }
}

internal sealed record StoragePlacementAnalysis(
    StackFramePlan MainFrame,
    IReadOnlyDictionary<BoundFunction, StackFramePlan> FunctionFrames);

internal static class StoragePlacementAnalyzer
{
    internal const int StackPromotionBudgetBytes = 4096;

    public static StoragePlacementAnalysis Analyze(
        SmallLangProgram program,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var standardInlineFunctions = DistinctFunctions(functions.Values
            .Where(static function => function.Kind == BoundFunctionKind.User && function.IsStandardLibrary));
        var mainFrame = MergeFramePlans(
            AnalyzeFrame(program.Statements, result: null, functions),
            AnalyzeInlineFunctions(standardInlineFunctions, functions));
        var functionFrames = new Dictionary<BoundFunction, StackFramePlan>(ReferenceEqualityComparer.Instance);

        var visited = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        foreach (var function in functions.Values)
        {
            AnalyzeFunction(function, functions, standardInlineFunctions, functionFrames, visited);
        }

        return new StoragePlacementAnalysis(mainFrame, functionFrames);
    }

    private static void AnalyzeFunction(
        BoundFunction function,
        IReadOnlyDictionary<string, BoundFunction> parentFunctions,
        IReadOnlyList<BoundFunction> standardInlineFunctions,
        Dictionary<BoundFunction, StackFramePlan> functionFrames,
        HashSet<BoundFunction> visited)
    {
        if (!visited.Add(function))
        {
            return;
        }

        var functions = CreateFunctionScope(parentFunctions, function.LocalFunctions);
        if (function.Kind == BoundFunctionKind.User
            && !function.IsLocal
            && !function.IsStandardLibrary)
        {
            var inlineFunctions = DistinctFunctions(
                EnumerateLocalFunctions(function.LocalFunctions.Values).Concat(standardInlineFunctions));
            functionFrames.Add(
                function,
                MergeFramePlans(
                    AnalyzeFrame(function.BlockBody, function.Body, functions),
                    AnalyzeInlineFunctions(inlineFunctions, functions)));
        }

        foreach (var localFunction in function.LocalFunctions.Values)
        {
            AnalyzeFunction(localFunction, functions, standardInlineFunctions, functionFrames, visited);
        }
    }

    private static IEnumerable<BoundFunction> EnumerateLocalFunctions(IEnumerable<BoundFunction> functions)
    {
        foreach (var function in functions)
        {
            yield return function;
            foreach (var nested in EnumerateLocalFunctions(function.LocalFunctions.Values))
            {
                yield return nested;
            }
        }
    }

    private static BoundFunction[] DistinctFunctions(IEnumerable<BoundFunction> functions)
    {
        var seen = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        return functions.Where(seen.Add).ToArray();
    }

    private static IReadOnlyList<StackFramePlan> AnalyzeInlineFunctions(
        IEnumerable<BoundFunction> functions,
        IReadOnlyDictionary<string, BoundFunction> parentFunctions)
    {
        var plans = new List<StackFramePlan>();
        foreach (var function in functions)
        {
            var scopedFunctions = CreateFunctionScope(parentFunctions, function.LocalFunctions);
            plans.Add(AnalyzeFrame(function.BlockBody, function.Body, scopedFunctions));
        }

        return plans;
    }

    private static StackFramePlan MergeFramePlans(
        StackFramePlan primary,
        IReadOnlyList<StackFramePlan> inlineFrames)
    {
        var slots = primary.Slots.ToList();
        var allocations = new Dictionary<object, StackAllocationPlan>(
            primary.Allocations,
            ReferenceEqualityComparer.Instance);
        var endings = new Dictionary<object, IReadOnlyList<StackAllocationPlan>>(
            primary.LifetimeEndsAfter,
            ReferenceEqualityComparer.Instance);
        var frameBytes = slots.Sum(static slot => slot.Size);

        foreach (var inlineFrame in inlineFrames)
        {
            var inlineBytes = inlineFrame.Slots.Sum(static slot => slot.Size);
            if (inlineBytes > StackPromotionBudgetBytes - frameBytes)
            {
                continue;
            }

            var slotOffset = slots.Count;
            slots.AddRange(inlineFrame.Slots.Select(slot => new StackSlotPlan(
                slot.Index + slotOffset,
                slot.Size,
                slot.Alignment)));
            frameBytes += inlineBytes;

            foreach (var (expression, allocation) in inlineFrame.Allocations)
            {
                allocations.TryAdd(
                    expression,
                    allocation with { SlotIndex = allocation.SlotIndex + slotOffset });
            }

            foreach (var (unit, unitEndings) in inlineFrame.LifetimeEndsAfter)
            {
                endings.TryAdd(
                    unit,
                    unitEndings.Select(allocation =>
                        allocation with { SlotIndex = allocation.SlotIndex + slotOffset }).ToArray());
            }
        }

        return new StackFramePlan(slots, allocations, endings);
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

    private static StackFramePlan AnalyzeFrame(
        IReadOnlyList<Statement> statements,
        Expression? result,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var positions = BuildUnitPositions(statements, result);
        var candidates = new List<StackCandidate>();
        CollectScopeCandidates(statements, result, functions, positions, candidates);
        return AllocateFrame(candidates);
    }

    private static void CollectScopeCandidates(
        IReadOnlyList<Statement> statements,
        Expression? result,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<object, int> positions,
        List<StackCandidate> candidates)
    {
        var scopeEndUnit = (object?)result ?? statements.LastOrDefault();
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is BindingStatement binding)
            {
                if (TryGetCandidate(binding.Value, out var kind, out var payloadBytes)
                    && (kind == PromotedOwnerKind.StaticArray
                        || (!binding.IsMutable
                            && UsesOwnerReadOnly(
                                statements,
                                i + 1,
                                result,
                                binding.Name,
                                kind,
                                functions))))
                {
                    var endUnit = FindLastUseUnit(statements, i + 1, result, binding.Name) ?? binding;
                    candidates.Add(new StackCandidate(
                        binding.Value,
                        payloadBytes,
                        positions[binding],
                        positions[endUnit],
                        endUnit));
                }

                if (binding.IsMutable
                    && scopeEndUnit is not null
                    && ProducesOwnedHeapContainer(binding.Value, functions))
                {
                    candidates.Add(new StackCandidate(
                        binding,
                        3 * sizeof(long),
                        positions[binding],
                        positions[scopeEndUnit],
                        scopeEndUnit,
                        EmitAutomaticEnd: false));
                }
            }

            CollectNestedScopeCandidates(statements[i], functions, positions, candidates);
        }

        if (result is not null)
        {
            CollectNestedScopeCandidates(result, functions, positions, candidates);
        }
    }

    private static IReadOnlyDictionary<object, int> BuildUnitPositions(
        IReadOnlyList<Statement> statements,
        Expression? result)
    {
        var positions = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var next = 0;
        IndexStatements(statements, positions, ref next);
        if (result is not null)
        {
            IndexNestedScopes(result, positions, ref next);
            positions.Add(result, next++);
        }

        return positions;
    }

    private static void IndexStatements(
        IReadOnlyList<Statement> statements,
        Dictionary<object, int> positions,
        ref int next)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    IndexNestedScopes(binding.Value, positions, ref next);
                    break;
            case IndexAssignmentStatement assignment:
                IndexNestedScopes(assignment.Index, positions, ref next);
                IndexNestedScopes(assignment.Value, positions, ref next);
                break;
            case FieldAssignmentStatement assignment:
                IndexNestedScopes(assignment.Value, positions, ref next);
                break;
                case BlockFunctionCallStatement blockCall:
                    IndexNestedScopes(blockCall.Source, positions, ref next);
                    IndexStatements(blockCall.Body, positions, ref next);
                    break;
                case ExpressionStatement expression:
                    IndexNestedScopes(expression.Expression, positions, ref next);
                    break;
            }

            positions.Add(statement, next++);
        }
    }

    private static void IndexBlockBody(
        BlockBody body,
        Dictionary<object, int> positions,
        ref int next)
    {
        IndexStatements(body.Statements, positions, ref next);
        if (body.Value is not null)
        {
            IndexNestedScopes(body.Value, positions, ref next);
            positions.Add(body.Value, next++);
        }
    }

    private static void IndexNestedScopes(
        Expression expression,
        Dictionary<object, int> positions,
        ref int next)
    {
        switch (expression)
        {
            case StringExpression text:
                foreach (var interpolation in text.Segments.OfType<InterpolationSegment>())
                {
                    IndexNestedScopes(interpolation.Expression, positions, ref next);
                }
                break;
            case AddExpression add:
                IndexNestedScopes(add.Left, positions, ref next);
                IndexNestedScopes(add.Right, positions, ref next);
                break;
            case SubtractExpression subtract:
                IndexNestedScopes(subtract.Left, positions, ref next);
                IndexNestedScopes(subtract.Right, positions, ref next);
                break;
            case MultiplyExpression multiply:
                IndexNestedScopes(multiply.Left, positions, ref next);
                IndexNestedScopes(multiply.Right, positions, ref next);
                break;
            case DivideExpression divide:
                IndexNestedScopes(divide.Left, positions, ref next);
                IndexNestedScopes(divide.Right, positions, ref next);
                break;
            case ModuloExpression modulo:
                IndexNestedScopes(modulo.Left, positions, ref next);
                IndexNestedScopes(modulo.Right, positions, ref next);
                break;
            case NegateExpression negate:
                IndexNestedScopes(negate.Value, positions, ref next);
                break;
            case CompareExpression compare:
                IndexNestedScopes(compare.Left, positions, ref next);
                IndexNestedScopes(compare.Right, positions, ref next);
                break;
            case AndExpression and:
                IndexNestedScopes(and.Left, positions, ref next);
                IndexNestedScopes(and.Right, positions, ref next);
                break;
            case OrExpression or:
                IndexNestedScopes(or.Left, positions, ref next);
                IndexNestedScopes(or.Right, positions, ref next);
                break;
            case NotExpression not:
                IndexNestedScopes(not.Value, positions, ref next);
                break;
            case RangeExpression range:
                IndexNestedScopes(range.Start, positions, ref next);
                IndexNestedScopes(range.End, positions, ref next);
                break;
            case FoldExpression fold:
                IndexNestedScopes(fold.Source, positions, ref next);
                IndexNestedScopes(fold.Initial, positions, ref next);
                IndexBlockBody(fold.Body, positions, ref next);
                break;
            case IfExpression conditional:
                IndexNestedScopes(conditional.Condition, positions, ref next);
                IndexBlockBody(conditional.Then, positions, ref next);
                if (conditional.Else is not null)
                {
                    IndexBlockBody(conditional.Else, positions, ref next);
                }
                break;
            case WhenExpression selection:
                if (selection.Subject is not null)
                {
                    IndexNestedScopes(selection.Subject, positions, ref next);
                }
                foreach (var arm in selection.Arms)
                {
                    IndexNestedScopes(arm.Condition, positions, ref next);
                    IndexBlockBody(arm.Body, positions, ref next);
                }
                IndexBlockBody(selection.Else, positions, ref next);
                break;
            case EnumMatchExpression match:
                IndexNestedScopes(match.Subject, positions, ref next);
                foreach (var arm in match.Arms)
                {
                    IndexBlockBody(arm.Body, positions, ref next);
                }
                if (match.Else is not null)
                {
                    IndexBlockBody(match.Else, positions, ref next);
                }
                break;
            case SubjectCompareExpression subjectCompare:
                IndexNestedScopes(subjectCompare.Right, positions, ref next);
                break;
            case SubjectRangeExpression subjectRange:
                IndexNestedScopes(subjectRange.Start, positions, ref next);
                IndexNestedScopes(subjectRange.End, positions, ref next);
                break;
            case FlowExpression flow:
                IndexNestedScopes(flow.Source, positions, ref next);
                foreach (var argument in flow.Targets.SelectMany(static target => target.Arguments))
                {
                    IndexNestedScopes(argument, positions, ref next);
                }
                break;
            case CallExpression call:
                foreach (var argument in call.Arguments)
                {
                    IndexNestedScopes(argument, positions, ref next);
                }
                break;
            case ArrayLiteralExpression array:
                foreach (var element in array.Elements)
                {
                    IndexNestedScopes(element, positions, ref next);
                }
                break;
            case ArrayRepeatExpression repeat:
                IndexNestedScopes(repeat.Value, positions, ref next);
                break;
            case DictionaryLiteralExpression dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    IndexNestedScopes(entry.Key, positions, ref next);
                    IndexNestedScopes(entry.Value, positions, ref next);
                }
                break;
            case IndexExpression index:
                IndexNestedScopes(index.Source, positions, ref next);
                IndexNestedScopes(index.Index, positions, ref next);
                break;
            case StructLiteralExpression structure:
                foreach (var field in structure.Fields)
                {
                    IndexNestedScopes(field.Value, positions, ref next);
                }
                break;
            case BoxExpression box:
                IndexNestedScopes(box.Value, positions, ref next);
                break;
            case FieldAccessExpression field:
                IndexNestedScopes(field.Source, positions, ref next);
                break;
        }
    }

    private static void CollectNestedScopeCandidates(
        Statement statement,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<object, int> positions,
        List<StackCandidate> candidates)
    {
        switch (statement)
        {
            case BindingStatement binding:
                CollectNestedScopeCandidates(binding.Value, functions, positions, candidates);
                break;
            case IndexAssignmentStatement assignment:
                CollectNestedScopeCandidates(assignment.Index, functions, positions, candidates);
                CollectNestedScopeCandidates(assignment.Value, functions, positions, candidates);
                break;
            case FieldAssignmentStatement assignment:
                CollectNestedScopeCandidates(assignment.Value, functions, positions, candidates);
                break;
            case BlockFunctionCallStatement blockCall:
                CollectNestedScopeCandidates(blockCall.Source, functions, positions, candidates);
                CollectScopeCandidates(blockCall.Body, result: null, functions, positions, candidates);
                break;
            case ExpressionStatement expression:
                CollectNestedScopeCandidates(expression.Expression, functions, positions, candidates);
                break;
        }
    }

    private static void CollectNestedScopeCandidates(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions,
        IReadOnlyDictionary<object, int> positions,
        List<StackCandidate> candidates)
    {
        switch (expression)
        {
            case StringExpression text:
                foreach (var interpolation in text.Segments.OfType<InterpolationSegment>())
                {
                    CollectNestedScopeCandidates(interpolation.Expression, functions, positions, candidates);
                }
                break;
            case AddExpression add:
                CollectNestedScopeCandidates(add.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(add.Right, functions, positions, candidates);
                break;
            case SubtractExpression subtract:
                CollectNestedScopeCandidates(subtract.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(subtract.Right, functions, positions, candidates);
                break;
            case MultiplyExpression multiply:
                CollectNestedScopeCandidates(multiply.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(multiply.Right, functions, positions, candidates);
                break;
            case DivideExpression divide:
                CollectNestedScopeCandidates(divide.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(divide.Right, functions, positions, candidates);
                break;
            case ModuloExpression modulo:
                CollectNestedScopeCandidates(modulo.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(modulo.Right, functions, positions, candidates);
                break;
            case NegateExpression negate:
                CollectNestedScopeCandidates(negate.Value, functions, positions, candidates);
                break;
            case CompareExpression compare:
                CollectNestedScopeCandidates(compare.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(compare.Right, functions, positions, candidates);
                break;
            case AndExpression and:
                CollectNestedScopeCandidates(and.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(and.Right, functions, positions, candidates);
                break;
            case OrExpression or:
                CollectNestedScopeCandidates(or.Left, functions, positions, candidates);
                CollectNestedScopeCandidates(or.Right, functions, positions, candidates);
                break;
            case NotExpression not:
                CollectNestedScopeCandidates(not.Value, functions, positions, candidates);
                break;
            case RangeExpression range:
                CollectNestedScopeCandidates(range.Start, functions, positions, candidates);
                CollectNestedScopeCandidates(range.End, functions, positions, candidates);
                break;
            case FoldExpression fold:
                CollectNestedScopeCandidates(fold.Source, functions, positions, candidates);
                CollectNestedScopeCandidates(fold.Initial, functions, positions, candidates);
                CollectScopeCandidates(fold.Body.Statements, fold.Body.Value, functions, positions, candidates);
                break;
            case IfExpression conditional:
                CollectNestedScopeCandidates(conditional.Condition, functions, positions, candidates);
                CollectScopeCandidates(conditional.Then.Statements, conditional.Then.Value, functions, positions, candidates);
                if (conditional.Else is not null)
                {
                    CollectScopeCandidates(conditional.Else.Statements, conditional.Else.Value, functions, positions, candidates);
                }
                break;
            case WhenExpression selection:
                if (selection.Subject is not null)
                {
                    CollectNestedScopeCandidates(selection.Subject, functions, positions, candidates);
                }
                foreach (var arm in selection.Arms)
                {
                    CollectNestedScopeCandidates(arm.Condition, functions, positions, candidates);
                    CollectScopeCandidates(arm.Body.Statements, arm.Body.Value, functions, positions, candidates);
                }
                CollectScopeCandidates(selection.Else.Statements, selection.Else.Value, functions, positions, candidates);
                break;
            case EnumMatchExpression match:
                CollectNestedScopeCandidates(match.Subject, functions, positions, candidates);
                foreach (var arm in match.Arms)
                {
                    CollectScopeCandidates(arm.Body.Statements, arm.Body.Value, functions, positions, candidates);
                }
                if (match.Else is not null)
                {
                    CollectScopeCandidates(match.Else.Statements, match.Else.Value, functions, positions, candidates);
                }
                break;
            case SubjectCompareExpression subjectCompare:
                CollectNestedScopeCandidates(subjectCompare.Right, functions, positions, candidates);
                break;
            case SubjectRangeExpression subjectRange:
                CollectNestedScopeCandidates(subjectRange.Start, functions, positions, candidates);
                CollectNestedScopeCandidates(subjectRange.End, functions, positions, candidates);
                break;
            case FlowExpression flow:
                CollectNestedScopeCandidates(flow.Source, functions, positions, candidates);
                foreach (var argument in flow.Targets.SelectMany(static target => target.Arguments))
                {
                    CollectNestedScopeCandidates(argument, functions, positions, candidates);
                }
                break;
            case CallExpression call:
                foreach (var argument in call.Arguments)
                {
                    CollectNestedScopeCandidates(argument, functions, positions, candidates);
                }
                break;
            case ArrayLiteralExpression array:
                foreach (var element in array.Elements)
                {
                    CollectNestedScopeCandidates(element, functions, positions, candidates);
                }
                break;
            case ArrayRepeatExpression repeat:
                CollectNestedScopeCandidates(repeat.Value, functions, positions, candidates);
                break;
            case DictionaryLiteralExpression dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    CollectNestedScopeCandidates(entry.Key, functions, positions, candidates);
                    CollectNestedScopeCandidates(entry.Value, functions, positions, candidates);
                }
                break;
            case IndexExpression index:
                CollectNestedScopeCandidates(index.Source, functions, positions, candidates);
                CollectNestedScopeCandidates(index.Index, functions, positions, candidates);
                break;
            case StructLiteralExpression structure:
                foreach (var field in structure.Fields)
                {
                    CollectNestedScopeCandidates(field.Value, functions, positions, candidates);
                }
                break;
            case BoxExpression box:
                CollectNestedScopeCandidates(box.Value, functions, positions, candidates);
                break;
            case FieldAccessExpression field:
                CollectNestedScopeCandidates(field.Source, functions, positions, candidates);
                break;
        }
    }

    private static object? FindLastUseUnit(
        IReadOnlyList<Statement> statements,
        int start,
        Expression? result,
        string ownerName)
    {
        if (result is not null && ContainsOwner(result, ownerName))
        {
            return result;
        }

        for (var i = statements.Count - 1; i >= start; i--)
        {
            if (ContainsOwner(statements[i], ownerName))
            {
                return statements[i];
            }
        }

        return null;
    }

    private static StackFramePlan AllocateFrame(IReadOnlyList<StackCandidate> candidates)
    {
        var slots = new List<MutableStackSlot>();
        var accepted = new List<AcceptedStackCandidate>();
        var allocations = new Dictionary<object, StackAllocationPlan>(ReferenceEqualityComparer.Instance);
        var ending = new Dictionary<object, List<StackAllocationPlan>>(ReferenceEqualityComparer.Instance);
        var frameBytes = 0;

        foreach (var candidate in candidates.OrderBy(static item => item.StartPosition))
        {
            var usedSlots = accepted
                .Where(item => item.EndPosition >= candidate.StartPosition)
                .Select(static item => item.SlotIndex)
                .ToHashSet();
            var reusable = slots
                .Where(slot => !usedSlots.Contains(slot.Index))
                .OrderBy(slot => Math.Max(slot.Size, candidate.Size) - slot.Size)
                .ThenBy(static slot => slot.Size)
                .FirstOrDefault();

            MutableStackSlot slot;
            if (reusable is not null)
            {
                var grownSize = Math.Max(reusable.Size, candidate.Size);
                var growth = grownSize - reusable.Size;
                if (growth > StackPromotionBudgetBytes - frameBytes)
                {
                    continue;
                }

                reusable.Size = grownSize;
                frameBytes += growth;
                slot = reusable;
            }
            else
            {
                if (candidate.Size > StackPromotionBudgetBytes - frameBytes)
                {
                    continue;
                }

                slot = new MutableStackSlot(slots.Count, candidate.Size, 8);
                slots.Add(slot);
                frameBytes += candidate.Size;
            }

            var allocation = new StackAllocationPlan(slot.Index, candidate.Size, slot.Alignment);
            allocations.Add(candidate.AllocationUnit, allocation);
            if (!candidate.EmitAutomaticEnd)
            {
                accepted.Add(new AcceptedStackCandidate(slot.Index, candidate.EndPosition));
                continue;
            }

            if (!ending.TryGetValue(candidate.EndUnit, out var endList))
            {
                endList = [];
                ending.Add(candidate.EndUnit, endList);
            }
            endList.Add(allocation);
            accepted.Add(new AcceptedStackCandidate(slot.Index, candidate.EndPosition));
        }

        return new StackFramePlan(
            slots.Select(static slot => new StackSlotPlan(slot.Index, slot.Size, slot.Alignment)).ToArray(),
            allocations,
            ending.ToDictionary(
                static item => item.Key,
                static item => (IReadOnlyList<StackAllocationPlan>)item.Value,
                ReferenceEqualityComparer.Instance));
    }

    private static bool TryGetCandidate(
        Expression expression,
        out PromotedOwnerKind kind,
        out int payloadBytes)
    {
        switch (expression)
        {
            case ArrayLiteralExpression { Elements.Count: > 0 } array
                when array.Elements.All(static element => element is not StringExpression):
                kind = array.IsDynamic
                    ? PromotedOwnerKind.DynamicArray
                    : PromotedOwnerKind.StaticArray;
                payloadBytes = checked(array.Elements.Count * sizeof(long));
                return true;
            case ArrayRepeatExpression repeat:
                if (repeat.Count is null)
                {
                    kind = default;
                    payloadBytes = 0;
                    return false;
                }
                kind = PromotedOwnerKind.StaticArray;
                payloadBytes = checked(Math.Max(repeat.Count.Value, 1) * sizeof(long));
                return true;
            case DictionaryLiteralExpression { Entries.Count: > 0 } dictionary:
                kind = PromotedOwnerKind.Dictionary;
                var capacity = IntDictionaryLayout.CapacityForLength(dictionary.Entries.Count);
                payloadBytes = IntDictionaryLayout.AllocationBytesForCapacity(capacity);
                return true;
            default:
                kind = default;
                payloadBytes = 0;
                return false;
        }
    }

    private static bool ProducesOwnedHeapContainer(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        switch (expression)
        {
            case ArrayLiteralExpression { IsDynamic: true }:
            case TypedEmptyArrayExpression:
            case DictionaryLiteralExpression:
            case TypedEmptyDictionaryExpression:
            case BoxExpression:
                return true;
            case CallExpression call:
                return functions.TryGetValue(string.Join('.', call.Path), out var called)
                    && called.ReturnType is BoundType.DynamicIntArray or BoundType.IntDictionary;
            case FlowExpression flow when flow.Targets.Count > 0:
                var path = string.Join('.', flow.Targets[^1].Path);
                if (path is "append" or "updated")
                {
                    return true;
                }
                return functions.TryGetValue(path, out var target)
                    && target.ReturnType is BoundType.DynamicIntArray or BoundType.IntDictionary;
            case IfExpression conditional when conditional.Else is not null:
                return ProducesOwnedHeapContainer(conditional.Then, functions)
                    && ProducesOwnedHeapContainer(conditional.Else, functions);
            case WhenExpression selection:
                return selection.Arms.All(arm => ProducesOwnedHeapContainer(arm.Body, functions))
                    && ProducesOwnedHeapContainer(selection.Else, functions);
            case EnumMatchExpression match:
                return match.Arms.All(arm => ProducesOwnedHeapContainer(arm.Body, functions))
                    && (match.Else is null || ProducesOwnedHeapContainer(match.Else, functions));
            default:
                return false;
        }
    }

    private static bool ProducesOwnedHeapContainer(
        BlockBody body,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (body.Value is null)
        {
            return false;
        }

        if (body.Value is NameExpression name)
        {
            for (var i = body.Statements.Count - 1; i >= 0; i--)
            {
                if (body.Statements[i] is BindingStatement binding
                    && StringComparer.Ordinal.Equals(binding.Name, name.Name))
                {
                    return ProducesOwnedHeapContainer(binding.Value, functions);
                }
            }
        }

        return ProducesOwnedHeapContainer(body.Value, functions);
    }

    private static bool UsesOwnerReadOnly(
        IReadOnlyList<Statement> statements,
        int start,
        Expression? result,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        for (var i = start; i < statements.Count; i++)
        {
            if (!UsesOwnerReadOnly(statements[i], ownerName, kind, functions))
            {
                return false;
            }
        }

        return result is null || UsesOwnerReadOnly(result, ownerName, kind, functions);
    }

    private static bool UsesOwnerReadOnly(
        Statement statement,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return statement switch
        {
            BindingStatement binding => UsesOwnerReadOnly(binding.Value, ownerName, kind, functions),
            IndexAssignmentStatement assignment => !StringComparer.Ordinal.Equals(assignment.Name, ownerName)
                && UsesOwnerReadOnly(assignment.Index, ownerName, kind, functions)
                && UsesOwnerReadOnly(assignment.Value, ownerName, kind, functions),
            BlockFunctionCallStatement call => UsesOwnerReadOnly(call, ownerName, kind, functions),
            ExpressionStatement expression => UsesOwnerReadOnly(expression.Expression, ownerName, kind, functions),
            _ => false
        };
    }

    private static bool UsesOwnerReadOnly(
        BlockFunctionCallStatement call,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (IsOwnerName(call.Source, ownerName))
        {
            var path = string.Join('.', call.Target);
            if (kind == PromotedOwnerKind.DynamicArray && path == "each")
            {
                return call.Body.All(statement => UsesOwnerReadOnly(statement, ownerName, kind, functions));
            }

            if (!functions.TryGetValue(path, out var function)
                || !IsReadonlyInputForKind(function, kind))
            {
                return false;
            }
        }
        else if (!UsesOwnerReadOnly(call.Source, ownerName, kind, functions))
        {
            return false;
        }

        return call.Body.All(statement => UsesOwnerReadOnly(statement, ownerName, kind, functions));
    }

    private static bool UsesOwnerReadOnly(
        BlockBody body,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return body.Statements.All(statement => UsesOwnerReadOnly(statement, ownerName, kind, functions))
            && (body.Value is null || UsesOwnerReadOnly(body.Value, ownerName, kind, functions));
    }

    private static bool UsesOwnerReadOnly(
        Expression expression,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        return expression switch
        {
            NameExpression name => !StringComparer.Ordinal.Equals(name.Name, ownerName),
            StringExpression text => text.Segments.All(segment => segment is not InterpolationSegment interpolation
                || UsesOwnerReadOnly(interpolation.Expression, ownerName, kind, functions)),
            NumberExpression or BoolExpression or TypedEmptyArrayExpression or TypedEmptyDictionaryExpression => true,
            AddExpression add => UsesOwnerReadOnly(add.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(add.Right, ownerName, kind, functions),
            SubtractExpression subtract => UsesOwnerReadOnly(subtract.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(subtract.Right, ownerName, kind, functions),
            MultiplyExpression multiply => UsesOwnerReadOnly(multiply.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(multiply.Right, ownerName, kind, functions),
            DivideExpression divide => UsesOwnerReadOnly(divide.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(divide.Right, ownerName, kind, functions),
            ModuloExpression modulo => UsesOwnerReadOnly(modulo.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(modulo.Right, ownerName, kind, functions),
            NegateExpression negate => UsesOwnerReadOnly(negate.Value, ownerName, kind, functions),
            CompareExpression compare => UsesOwnerReadOnly(compare.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(compare.Right, ownerName, kind, functions),
            AndExpression and => UsesOwnerReadOnly(and.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(and.Right, ownerName, kind, functions),
            OrExpression or => UsesOwnerReadOnly(or.Left, ownerName, kind, functions)
                && UsesOwnerReadOnly(or.Right, ownerName, kind, functions),
            NotExpression not => UsesOwnerReadOnly(not.Value, ownerName, kind, functions),
            RangeExpression range => UsesOwnerReadOnly(range.Start, ownerName, kind, functions)
                && UsesOwnerReadOnly(range.End, ownerName, kind, functions),
            FoldExpression fold => UsesFoldOwnerReadOnly(fold, ownerName, kind, functions),
            IfExpression conditional => UsesOwnerReadOnly(conditional.Condition, ownerName, kind, functions)
                && UsesOwnerReadOnly(conditional.Then, ownerName, kind, functions)
                && (conditional.Else is null || UsesOwnerReadOnly(conditional.Else, ownerName, kind, functions)),
            WhenExpression selection => (selection.Subject is null
                    || UsesOwnerReadOnly(selection.Subject, ownerName, kind, functions))
                && selection.Arms.All(arm => UsesOwnerReadOnly(arm.Condition, ownerName, kind, functions)
                    && UsesOwnerReadOnly(arm.Body, ownerName, kind, functions))
                && UsesOwnerReadOnly(selection.Else, ownerName, kind, functions),
            EnumMatchExpression match => UsesOwnerReadOnly(match.Subject, ownerName, kind, functions)
                && match.Arms.All(arm => UsesOwnerReadOnly(arm.Body, ownerName, kind, functions))
                && (match.Else is null || UsesOwnerReadOnly(match.Else, ownerName, kind, functions)),
            EnumPatternExpression => true,
            SubjectCompareExpression subjectCompare => UsesOwnerReadOnly(subjectCompare.Right, ownerName, kind, functions),
            SubjectRangeExpression subjectRange => UsesOwnerReadOnly(subjectRange.Start, ownerName, kind, functions)
                && UsesOwnerReadOnly(subjectRange.End, ownerName, kind, functions),
            FlowExpression flow => UsesFlowOwnerReadOnly(flow, ownerName, kind, functions),
            CallExpression call => UsesCallOwnerReadOnly(call, ownerName, kind, functions),
            ArrayLiteralExpression array => array.Elements.All(element =>
                UsesOwnerReadOnly(element, ownerName, kind, functions)),
            ArrayRepeatExpression repeat => UsesOwnerReadOnly(repeat.Value, ownerName, kind, functions),
            DictionaryLiteralExpression dictionary => dictionary.Entries.All(entry =>
                UsesOwnerReadOnly(entry.Key, ownerName, kind, functions)
                && UsesOwnerReadOnly(entry.Value, ownerName, kind, functions)),
            IndexExpression index => (IsOwnerName(index.Source, ownerName)
                    || UsesOwnerReadOnly(index.Source, ownerName, kind, functions))
                && UsesOwnerReadOnly(index.Index, ownerName, kind, functions),
            StructLiteralExpression structure => structure.Fields.All(field =>
                UsesOwnerReadOnly(field.Value, ownerName, kind, functions)),
            BoxExpression box => UsesOwnerReadOnly(box.Value, ownerName, kind, functions),
            FieldAccessExpression field => UsesOwnerReadOnly(field.Source, ownerName, kind, functions),
            _ => false
        };
    }

    private static bool UsesFoldOwnerReadOnly(
        FoldExpression fold,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (IsOwnerName(fold.Source, ownerName) && kind != PromotedOwnerKind.DynamicArray)
        {
            return false;
        }

        return (IsOwnerName(fold.Source, ownerName)
                || UsesOwnerReadOnly(fold.Source, ownerName, kind, functions))
            && UsesOwnerReadOnly(fold.Initial, ownerName, kind, functions)
            && UsesOwnerReadOnly(fold.Body, ownerName, kind, functions);
    }

    private static bool UsesFlowOwnerReadOnly(
        FlowExpression flow,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (IsOwnerName(flow.Source, ownerName))
        {
            if (flow.Targets.Count == 0 || !IsReadonlyOwnerTarget(flow.Targets[0], kind, functions))
            {
                return false;
            }
        }
        else if (!UsesOwnerReadOnly(flow.Source, ownerName, kind, functions))
        {
            return false;
        }

        return flow.Targets.All(target => target.Arguments.All(argument =>
            UsesOwnerReadOnly(argument, ownerName, kind, functions)));
    }

    private static bool UsesCallOwnerReadOnly(
        CallExpression call,
        string ownerName,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        if (call.Arguments.Count == 1 && IsOwnerName(call.Arguments[0], ownerName))
        {
            var path = string.Join('.', call.Path);
            return functions.TryGetValue(path, out var function)
                && IsReadonlyInputForKind(function, kind);
        }

        return call.Arguments.All(argument => UsesOwnerReadOnly(argument, ownerName, kind, functions));
    }

    private static bool IsReadonlyOwnerTarget(
        FlowTarget target,
        PromotedOwnerKind kind,
        IReadOnlyDictionary<string, BoundFunction> functions)
    {
        var path = string.Join('.', target.Path);
        return path is "len" or "capacity"
            || (functions.TryGetValue(path, out var function)
                && IsReadonlyInputForKind(function, kind));
    }

    private static bool IsReadonlyInputForKind(BoundFunction function, PromotedOwnerKind kind)
    {
        return function.InputOwnership == BoundFunctionInputOwnership.Default
            && function.InputType == (kind == PromotedOwnerKind.DynamicArray
                ? BoundType.IntSlice
                : BoundType.IntDictionaryView);
    }

    private static bool ContainsOwner(Statement statement, string ownerName)
    {
        return statement switch
        {
            BindingStatement binding => ContainsOwner(binding.Value, ownerName),
            IndexAssignmentStatement assignment => StringComparer.Ordinal.Equals(assignment.Name, ownerName)
                || ContainsOwner(assignment.Index, ownerName)
                || ContainsOwner(assignment.Value, ownerName),
            FieldAssignmentStatement assignment => StringComparer.Ordinal.Equals(assignment.Name, ownerName)
                || ContainsOwner(assignment.Value, ownerName),
            BlockFunctionCallStatement call => ContainsOwner(call.Source, ownerName)
                || call.Body.Any(nested => ContainsOwner(nested, ownerName)),
            ExpressionStatement expression => ContainsOwner(expression.Expression, ownerName),
            _ => false
        };
    }

    private static bool ContainsOwner(BlockBody body, string ownerName)
    {
        return body.Statements.Any(statement => ContainsOwner(statement, ownerName))
            || (body.Value is not null && ContainsOwner(body.Value, ownerName));
    }

    private static bool ContainsOwner(Expression expression, string ownerName)
    {
        return expression switch
        {
            NameExpression name => StringComparer.Ordinal.Equals(name.Name, ownerName),
            StringExpression text => text.Segments.OfType<InterpolationSegment>()
                .Any(segment => ContainsOwner(segment.Expression, ownerName)),
            AddExpression add => ContainsOwner(add.Left, ownerName) || ContainsOwner(add.Right, ownerName),
            SubtractExpression subtract => ContainsOwner(subtract.Left, ownerName) || ContainsOwner(subtract.Right, ownerName),
            MultiplyExpression multiply => ContainsOwner(multiply.Left, ownerName) || ContainsOwner(multiply.Right, ownerName),
            DivideExpression divide => ContainsOwner(divide.Left, ownerName) || ContainsOwner(divide.Right, ownerName),
            ModuloExpression modulo => ContainsOwner(modulo.Left, ownerName) || ContainsOwner(modulo.Right, ownerName),
            NegateExpression negate => ContainsOwner(negate.Value, ownerName),
            CompareExpression compare => ContainsOwner(compare.Left, ownerName) || ContainsOwner(compare.Right, ownerName),
            AndExpression and => ContainsOwner(and.Left, ownerName) || ContainsOwner(and.Right, ownerName),
            OrExpression or => ContainsOwner(or.Left, ownerName) || ContainsOwner(or.Right, ownerName),
            NotExpression not => ContainsOwner(not.Value, ownerName),
            RangeExpression range => ContainsOwner(range.Start, ownerName) || ContainsOwner(range.End, ownerName),
            FoldExpression fold => ContainsOwner(fold.Source, ownerName)
                || ContainsOwner(fold.Initial, ownerName)
                || ContainsOwner(fold.Body, ownerName),
            IfExpression conditional => ContainsOwner(conditional.Condition, ownerName)
                || ContainsOwner(conditional.Then, ownerName)
                || (conditional.Else is not null && ContainsOwner(conditional.Else, ownerName)),
            WhenExpression selection => (selection.Subject is not null && ContainsOwner(selection.Subject, ownerName))
                || selection.Arms.Any(arm => ContainsOwner(arm.Condition, ownerName)
                    || ContainsOwner(arm.Body, ownerName))
                || ContainsOwner(selection.Else, ownerName),
            EnumMatchExpression match => ContainsOwner(match.Subject, ownerName)
                || match.Arms.Any(arm => ContainsOwner(arm.Body, ownerName))
                || (match.Else is not null && ContainsOwner(match.Else, ownerName)),
            EnumPatternExpression => false,
            SubjectCompareExpression subjectCompare => ContainsOwner(subjectCompare.Right, ownerName),
            SubjectRangeExpression subjectRange => ContainsOwner(subjectRange.Start, ownerName)
                || ContainsOwner(subjectRange.End, ownerName),
            FlowExpression flow => ContainsOwner(flow.Source, ownerName)
                || flow.Targets.Any(target => target.Arguments.Any(argument => ContainsOwner(argument, ownerName))),
            CallExpression call => call.Arguments.Any(argument => ContainsOwner(argument, ownerName)),
            ArrayLiteralExpression array => array.Elements.Any(element => ContainsOwner(element, ownerName)),
            ArrayRepeatExpression repeat => ContainsOwner(repeat.Value, ownerName),
            StructLiteralExpression structure => structure.Fields.Any(field => ContainsOwner(field.Value, ownerName)),
            BoxExpression box => ContainsOwner(box.Value, ownerName),
            FieldAccessExpression field => ContainsOwner(field.Source, ownerName),
            DictionaryLiteralExpression dictionary => dictionary.Entries.Any(entry =>
                ContainsOwner(entry.Key, ownerName) || ContainsOwner(entry.Value, ownerName)),
            IndexExpression index => ContainsOwner(index.Source, ownerName) || ContainsOwner(index.Index, ownerName),
            _ => false
        };
    }

    private static bool IsOwnerName(Expression expression, string ownerName)
    {
        return expression is NameExpression name
            && StringComparer.Ordinal.Equals(name.Name, ownerName);
    }

    private enum PromotedOwnerKind
    {
        StaticArray,
        DynamicArray,
        Dictionary
    }

    private sealed record StackCandidate(
        object AllocationUnit,
        int Size,
        int StartPosition,
        int EndPosition,
        object EndUnit,
        bool EmitAutomaticEnd = true);

    private sealed record AcceptedStackCandidate(int SlotIndex, int EndPosition);

    private sealed class MutableStackSlot(int index, int size, int alignment)
    {
        public int Index { get; } = index;

        public int Size { get; set; } = size;

        public int Alignment { get; } = alignment;
    }
}
