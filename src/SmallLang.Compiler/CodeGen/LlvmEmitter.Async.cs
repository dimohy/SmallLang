using System.Globalization;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitAsyncFunction(BoundFunction function)
    {
        if (function.Body is null && function.ReturnType != BoundType.Unit)
        {
            throw new SmallLangException($"async function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = FunctionScope(function);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            var workerName = AsyncWorkerSymbol(function);
            var hasCfgSuspension = TryGetCfgSuspendPlan(function, out var cfgSuspendPlan);
            BindingStatement? tailAwaitBinding = null;
            AsyncStateMachinePlan? statefulAwaitPlan = null;
            var hasTailAwait = !hasCfgSuspension
                && TryGetTailAwaitBinding(function, out tailAwaitBinding);
            var hasStatefulAwait = !hasCfgSuspension
                && TryGetStatefulAwaitPlan(function, out statefulAwaitPlan);
            EmitFunctionLine($"define internal i1 @{workerName}(ptr %control) #0 {{");
            EmitLabel("entry");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";

            var contextSlot = NextTemp("async_control_context_slot");
            EmitAssign(
                contextSlot,
                "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 0");
            EmitLoad("%context", "ptr", contextSlot, 8);

            EmitAsyncContextLoad("%stdin", "%context", function.InputType, function.ReturnType, 0, "ptr", 8);
            EmitAsyncContextLoad("%stdout", "%context", function.InputType, function.ReturnType, 1, "ptr", 8);
            EmitAsyncContextLoad("%written", "%context", function.InputType, function.ReturnType, 2, "ptr", 8);
            EmitAsyncContextLoad("%read", "%context", function.InputType, function.ReturnType, 3, "ptr", 8);
            EmitAsyncContextLoad("%ok_state", "%context", function.InputType, function.ReturnType, 4, "ptr", 8);
            if (function.InputType is { } inputType)
            {
                EmitAsyncContextLoad(
                    "%it", "%context", function.InputType, function.ReturnType, 5,
                    AsyncStorageLlvmType(inputType), RuntimeAlignment(inputType));
            }
            var additionalParameters = function.AdditionalParameters ?? [];
            for (var parameterIndex = 0; parameterIndex < additionalParameters.Count; parameterIndex++)
            {
                var parameter = additionalParameters[parameterIndex];
                EmitAsyncFunctionContextLoad(
                    $"%arg_{parameterIndex}",
                    "%context",
                    function,
                    10 + parameterIndex,
                    AsyncStorageLlvmType(parameter.Type),
                    RuntimeAlignment(parameter.Type));
            }

            BindFunctionCaptures(function);
            var functionLocals = CaptureLocals();
            BindAllFunctionParameters(function);
            if (hasCfgSuspension)
            {
                EmitCfgSuspendWorker(function, cfgSuspendPlan!, functionLocals);
            }
            else if (hasStatefulAwait)
            {
                EmitStatefulAwaitWorker(function, statefulAwaitPlan!, functionLocals);
            }
            else if (hasTailAwait)
            {
                EmitTailAwaitWorker(function, tailAwaitBinding!, functionLocals);
            }
            else
            {
                EmitAsyncWorkerCompletion(function, functionLocals);
            }
            EmitFunctionLine("}");
            EmitFunctionLine();

            ClearLocalState();
            EmitAsyncCancelFunction(function, hasTailAwait, statefulAwaitPlan, cfgSuspendPlan);

            ClearLocalState();
            EmitFunctionLine($"define internal %smalllang.task {SymbolForFunction(function)}({ParameterListForFunction(function)}) #0 {{");
            EmitLabel("entry");
            var context = NextTemp("async_context");
            EmitCall(context, "ptr", "smalllang_alloc", $"i64 {AsyncContextSize(function)}");
            EmitAsyncContextStore(context, function.InputType, function.ReturnType, 0, "ptr", "%stdin", 8);
            EmitAsyncContextStore(context, function.InputType, function.ReturnType, 1, "ptr", "%stdout", 8);
            EmitAsyncContextStore(context, function.InputType, function.ReturnType, 2, "ptr", "%written", 8);
            EmitAsyncContextStore(context, function.InputType, function.ReturnType, 3, "ptr", "%read", 8);
            EmitAsyncContextStore(context, function.InputType, function.ReturnType, 4, "ptr", "%ok_state", 8);
            EmitAsyncContextStore(
                context, function.InputType, function.ReturnType, 5,
                AsyncStorageLlvmType(function.InputType), function.InputType is null ? "0" : "%it",
                AsyncStorageAlignment(function.InputType));
            EmitAsyncContextStore(
                context, function.InputType, function.ReturnType, 6,
                AsyncStorageLlvmType(function.ReturnType), "zeroinitializer",
                RuntimeAlignment(function.ReturnType));
            EmitAsyncContextStore(
                context, function.InputType, function.ReturnType, 7,
                "ptr", "null", 8);
            EmitAsyncContextStore(
                context, function.InputType, function.ReturnType, 8,
                "ptr", "null", 8);
            EmitAsyncContextStore(
                context, function.InputType, function.ReturnType, 9,
                "ptr", "null", 8);
            for (var parameterIndex = 0; parameterIndex < additionalParameters.Count; parameterIndex++)
            {
                var parameter = additionalParameters[parameterIndex];
                EmitAsyncFunctionContextStore(
                    context,
                    function,
                    10 + parameterIndex,
                    AsyncStorageLlvmType(parameter.Type),
                    $"%arg_{parameterIndex}",
                    RuntimeAlignment(parameter.Type));
            }

            var handle = NextTemp("async_handle");
            EmitCall(
                handle,
                "ptr",
                "smalllang_task_start",
                $"ptr @{workerName}, ptr @smalllang_free, ptr @{AsyncCancelSymbol(function)}, ptr {context}");
            var started = NextTemp("async_started");
            EmitCompare(started, "ne", "ptr", handle, "null");
            var readyLabel = NextLabel("async_ready");
            var failedLabel = NextLabel("async_failed");
            EmitConditionalBranch(started, readyLabel, failedLabel);
            EmitLabel(failedLabel);
            DropFailedAsyncInput(function);
            DropFailedAsyncAdditionalInputs(function);
            EmitCall(target: null, "void", "smalllang_free", $"ptr {context}");
            EmitTrap();
            EmitLabel(readyLabel);

            var withHandle = NextTemp("task");
            EmitAssign(withHandle, $"insertvalue %smalllang.task poison, ptr {handle}, 0");
            var task = NextTemp("task");
            EmitAssign(task, $"insertvalue %smalllang.task {withHandle}, ptr {context}, 1");
            EmitRet("%smalllang.task", task);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitAsyncCancelFunction(
        BoundFunction function,
        bool hasTailAwait,
        AsyncStateMachinePlan? statefulPlan,
        AsyncCfgPlan? cfgPlan)
    {
        EmitFunctionLine($"define internal void @{AsyncCancelSymbol(function)}(ptr %control) #0 {{");
        EmitLabel("entry");
        _currentBlockLabel = "entry";
        var contextSlot = NextTemp("cancel_context_slot");
        EmitAssign(
            contextSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 0");
        EmitLoad("%context", "ptr", contextSlot, 8);
        var statusSlot = NextTemp("cancel_status_slot");
        EmitAssign(
            statusSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 4");
        var status = NextTemp("cancel_status");
        EmitLoad(status, "i32", statusSlot, 4);
        var completed = NextTemp("cancel_completed");
        EmitCompare(completed, "eq", "i32", status, "2");
        var completedLabel = NextLabel("cancel_completed");
        var pendingLabel = NextLabel("cancel_pending");
        var cleanupLabel = NextLabel("cancel_cleanup");
        EmitConditionalBranch(completed, completedLabel, pendingLabel);

        EmitLabel(completedLabel);
        _currentBlockLabel = completedLabel;
        DropCancelledAsyncResult(function);
        EmitBranch(cleanupLabel);

        EmitLabel(pendingLabel);
        _currentBlockLabel = pendingLabel;
        var stateSlot = NextTemp("cancel_state_slot");
        EmitAssign(
            stateSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 5");
        var state = NextTemp("cancel_state");
        EmitLoad(state, "i32", stateSlot, 4);
        var pendingStates = cfgPlan?.Points.Count
            ?? statefulPlan?.Awaits.Count
            ?? (hasTailAwait ? 1 : 0);
        var stateLabels = Enumerable.Range(0, pendingStates + 1)
            .Select(index => NextLabel(index == 0 ? "cancel_initial" : "cancel_suspended"))
            .ToArray();
        var invalidLabel = NextLabel("cancel_invalid");
        var switchCases = string.Join(" ", stateLabels.Select((label, index) =>
            $"i32 {index}, label %{label}"));
        EmitInstruction(
            $"switch i32 {state}, label %{invalidLabel} [ {switchCases} ]");
        _currentBlockTerminated = true;

        EmitLabel(invalidLabel);
        _currentBlockLabel = invalidLabel;
        EmitTrap();

        EmitLabel(stateLabels[0]);
        _currentBlockLabel = stateLabels[0];
        DropCancelledAsyncInput(function);
        DropCancelledAsyncAdditionalInputs(function);
        EmitBranch(cleanupLabel);

        for (var stateIndex = 1; stateIndex <= pendingStates; stateIndex++)
        {
            EmitLabel(stateLabels[stateIndex]);
            _currentBlockLabel = stateLabels[stateIndex];
            if (cfgPlan is not null)
            {
                var point = cfgPlan.Points[stateIndex - 1];
                if (point.OwnsInput)
                {
                    DropCancelledAsyncInput(function);
                    DropCancelledAsyncAdditionalInputs(function);
                }
                if (!point.IsYield)
                {
                    CancelStoredChild(function);
                }
                DropCancelledAsyncSpills(function, point.Spills);
            }
            else
            {
                CancelStoredChild(function);
                if (statefulPlan is not null)
                {
                    DropCancelledAsyncSpills(function, statefulPlan.Awaits[stateIndex - 1].Spills);
                }
            }
            EmitBranch(cleanupLabel);
        }

        EmitLabel(cleanupLabel);
        _currentBlockLabel = cleanupLabel;
        EmitCall(target: null, "void", "smalllang_free", "ptr %context");
        EmitInstruction("ret void");
        _currentBlockTerminated = true;
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void DropCancelledAsyncInput(BoundFunction function)
    {
        if (function.InputOwnership != BoundFunctionInputOwnership.Move
            || function.InputType is not { } inputType
            || !_program.Types.ContainsOwnedStorage(inputType))
        {
            return;
        }

        var inputAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 5, "cancel_input_address");
        var input = NextTemp("cancel_input");
        EmitLoad(input, AsyncStorageLlvmType(inputType), inputAddress, RuntimeAlignment(inputType));
        DropOwnedRuntimeValue(DematerializeAggregateValue(inputType, input));
    }

    private void DropCancelledAsyncResult(BoundFunction function)
    {
        if (!_program.Types.ContainsOwnedStorage(function.ReturnType))
        {
            return;
        }

        var resultAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 6, "cancel_result_address");
        var result = NextTemp("cancel_result");
        EmitLoad(
            result,
            AsyncStorageLlvmType(function.ReturnType),
            resultAddress,
            RuntimeAlignment(function.ReturnType));
        DropOwnedRuntimeValue(DematerializeAsyncResult(function.ReturnType, result));
    }

    private void CancelStoredChild(BoundFunction function)
    {
        var handleAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 7, "cancel_child_handle_address");
        var handle = NextTemp("cancel_child_handle");
        EmitLoad(handle, "ptr", handleAddress, 8);
        EmitCancelTaskHandle(handle);
    }

    private void DropCancelledAsyncSpills(
        BoundFunction function,
        IReadOnlyList<AsyncSpillPlan> spills)
    {
        if (spills.Count == 0)
        {
            return;
        }

        var frameAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 9, "cancel_spill_frame_address");
        var frame = NextTemp("cancel_spill_frame");
        EmitLoad(frame, "ptr", frameAddress, 8);
        var layouts = new List<(AsyncSpillPlan Spill, int Offset, int Alignment)>(spills.Count);
        var offset = 0;
        foreach (var spill in spills)
        {
            var alignment = RuntimeAlignment(spill.Type);
            offset = AlignAsyncSize(offset, alignment);
            layouts.Add((spill, offset, alignment));
            offset = checked(offset + Math.Max(_program.Types.InlineSizeOf(spill.Type), 1));
        }
        foreach (var layout in layouts.AsEnumerable().Reverse())
        {
            var spill = layout.Spill;
            var address = NextTemp("cancel_spill_address");
            EmitAssign(address, $"getelementptr i8, ptr {frame}, i64 {layout.Offset}");
            var loaded = NextTemp("cancel_spill_value");
            EmitLoad(loaded, LlvmType(spill.Type), address, layout.Alignment);
            if (_program.Types.IsTask(spill.Type))
            {
                var handle = NextTemp("cancel_spill_task_handle");
                EmitAssign(handle, $"extractvalue %smalllang.task {loaded}, 0");
                EmitCancelTaskHandle(handle);
            }
            else if (_program.Types.ContainsOwnedStorage(spill.Type))
            {
                DropOwnedRuntimeValue(DematerializeAggregateValue(spill.Type, loaded));
            }
        }
        EmitCall(target: null, "void", "smalllang_free", $"ptr {frame}");
    }

    private void EmitAsyncWorkerCompletion(BoundFunction function, LocalScope functionLocals)
    {
        EmitAsyncWorkerCompletion(function, functionLocals, function.BlockBody);
    }

    private void EmitAsyncWorkerCompletion(
        BoundFunction function,
        LocalScope functionLocals,
        IReadOnlyList<Statement> statements)
    {
        EmitStatements(statements);
        var movedBodySourceName = function.Body is null
            ? null
            : GetMoveConsumingContainerSourceName(function.Body);
        var value = function.Body is null
            ? RuntimeUnit.Instance
            : EmitExpression(function.Body);
        EnsureRuntimeType(value, function.ReturnType, function.Name);
        if (movedBodySourceName is not null)
        {
            RemoveLocal(movedBodySourceName);
        }
        var transferredOwnerName = IsOwnedContainerRuntimeValue(value)
            && function.Body is not null
            ? GetFunctionResultTransferredOwnerName(function, function.Body)
            : null;
        DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
        StoreAsyncResult(function, value);
        EmitRet("i1", "true");
    }

    private void EmitStatefulAwaitWorker(
        BoundFunction function,
        AsyncStateMachinePlan plan,
        LocalScope functionLocals)
    {
        var resumeBaseLocals = CaptureLocals();
        var stateSlot = NextTemp("async_resume_state_slot");
        EmitAssign(
            stateSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 5");
        var state = NextTemp("async_resume_state");
        EmitLoad(state, "i32", stateSlot, 4);
        var stateLabels = Enumerable.Range(0, plan.Awaits.Count + 1)
            .Select(index => NextLabel(index == 0 ? "async_state_start" : "async_state_resume"))
            .ToArray();
        var invalidLabel = NextLabel("async_state_invalid");
        var switchCases = string.Join(" ", stateLabels.Select((label, index) =>
            $"i32 {index}, label %{label}"));
        EmitInstruction(
            $"switch i32 {state}, label %{invalidLabel} [ {switchCases} ]");
        _currentBlockTerminated = true;

        EmitLabel(invalidLabel);
        EmitTrap();

        RuntimeTask? suspendedChild = null;
        IReadOnlyList<RuntimeAsyncSpill> previousSpills = [];
        var segmentStart = 0;
        for (var awaitIndex = 0; awaitIndex < plan.Awaits.Count; awaitIndex++)
        {
            var awaitPoint = plan.Awaits[awaitIndex];
            RestoreLocals(resumeBaseLocals);
            EmitLabel(stateLabels[awaitIndex]);
            if (awaitIndex > 0)
            {
                LoadAsyncSpills(function, previousSpills);
                var resumedValue = EmitStoredChildAwait(function, suspendedChild!);
                _locals.Add(plan.Awaits[awaitIndex - 1].ResultName, resumedValue);
            }

            EmitStatements(function.BlockBody
                .Skip(segmentStart)
                .Take(awaitPoint.StatementIndex - segmentStart)
                .ToArray());
            suspendedChild = ResolveLocal(awaitPoint.TaskName) as RuntimeTask
                ?? throw new SmallLangException(
                    $"await in async function '{function.Name}' did not produce Task<T>");
            previousSpills = BuildRuntimeAsyncSpills(awaitPoint.Spills);
            StoreAsyncSpills(function, previousSpills);
            foreach (var spill in previousSpills)
            {
                RemoveLocal(spill.Name);
            }
            StoreSuspendedChild(function, suspendedChild);
            RemoveLocal(awaitPoint.TaskName);
            DropOwnedLocalsCreatedSince(resumeBaseLocals, transferredOwnerName: null);
            EmitStore("i32", (awaitIndex + 1).ToString(CultureInfo.InvariantCulture), stateSlot, 4);
            EmitRet("i1", "false");
            segmentStart = awaitPoint.StatementIndex + 1;
        }

        RestoreLocals(resumeBaseLocals);
        EmitLabel(stateLabels[^1]);
        LoadAsyncSpills(function, previousSpills);
        var finalAwaitValue = EmitStoredChildAwait(function, suspendedChild!);
        _locals.Add(plan.Awaits[^1].ResultName, finalAwaitValue);
        var remainingStatements = function.BlockBody.Skip(segmentStart).ToArray();
        EmitAsyncWorkerCompletion(function, functionLocals, remainingStatements);
    }

    private void EmitCfgSuspendWorker(
        BoundFunction function,
        AsyncCfgPlan plan,
        LocalScope functionLocals)
    {
        var resumeBaseLocals = CaptureLocals();
        var stateSlot = NextTemp("async_resume_state_slot");
        EmitAssign(
            stateSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 5");
        var state = NextTemp("async_resume_state");
        EmitLoad(state, "i32", stateSlot, 4);
        var startLabel = NextLabel("async_state_start");
        foreach (var point in plan.Points)
        {
            point.ResumeLabel = NextLabel("async_cfg_resume");
        }
        var invalidLabel = NextLabel("async_state_invalid");
        var switchCases = string.Join(" ",
            new[] { $"i32 0, label %{startLabel}" }.Concat(plan.Points.Select(point =>
                $"i32 {point.State.ToString(CultureInfo.InvariantCulture)}, label %{point.ResumeLabel}")));
        EmitInstruction($"switch i32 {state}, label %{invalidLabel} [ {switchCases} ]");
        _currentBlockTerminated = true;

        EmitLabel(invalidLabel);
        EmitTrap();
        EmitLabel(startLabel);
        _currentBlockLabel = startLabel;

        _activeAsyncCfg = new AsyncCfgLowering(function, plan, resumeBaseLocals, stateSlot);
        try
        {
            EmitAsyncWorkerCompletion(function, functionLocals);
        }
        finally
        {
            _activeAsyncCfg = null;
            _asyncScopeSnapshots.Clear();
        }
    }

    private bool TryEmitCfgAwaitBinding(BindingStatement binding)
    {
        if (_activeAsyncCfg is not { } lowering
            || !lowering.Plan.ByBinding.TryGetValue(binding, out var point))
        {
            return false;
        }

        if (!TryGetAwaitTaskName(binding, out var taskName))
        {
            throw new SmallLangException("CFG await plan contains a non-await binding");
        }
        var child = ResolveLocal(taskName) as RuntimeTask
            ?? throw new SmallLangException(
                $"await in async function '{lowering.Function.Name}' did not produce Task<T>");

        var spillPlans = BuildCfgSpillPlans(lowering, excludedName: taskName);
        StoreSuspendedChild(lowering.Function, child);
        RemoveLocal(taskName);
        SuspendAndResumeCfgPoint(lowering, point, spillPlans);
        var resumedValue = EmitStoredChildAwait(lowering.Function, child);
        _locals.Add(binding.Name, resumedValue);
        return true;
    }

    private bool TryEmitCfgYieldStatement(ExpressionStatement statement)
    {
        if (_activeAsyncCfg is not { } lowering
            || !lowering.Plan.ByYield.TryGetValue(statement, out var point))
        {
            return false;
        }

        var spillPlans = BuildCfgSpillPlans(lowering, excludedName: null);
        SuspendAndResumeCfgPoint(lowering, point, spillPlans);
        return true;
    }

    private IReadOnlyList<AsyncSpillPlan> BuildCfgSpillPlans(
        AsyncCfgLowering lowering,
        string? excludedName)
    {
        var spillPlans = new List<AsyncSpillPlan>();
        foreach (var (name, value) in _locals)
        {
            if (string.Equals(name, excludedName, StringComparison.Ordinal)
                || lowering.ResumeBaseLocals.Locals.ContainsKey(name))
            {
                continue;
            }
            if (!IsAsyncSpillType(value.Type))
            {
                throw new SmallLangException(
                    $"binding '{name}' of type {value.Type} cannot cross async suspension");
            }
            var isMutable = _mutableLocals.Contains(name);
            if (isMutable && !IsAsyncMutableSpillType(value.Type))
            {
                throw new SmallLangException(
                    $"mutable binding '{name}' of type {value.Type} cannot cross async suspension");
            }
            spillPlans.Add(new AsyncSpillPlan(name, value.Type, isMutable));
        }
        return spillPlans;
    }

    private void SuspendAndResumeCfgPoint(
        AsyncCfgLowering lowering,
        AsyncCfgSuspendPoint point,
        IReadOnlyList<AsyncSpillPlan> spillPlans)
    {
        var inputName = lowering.Function.InputName ?? "it";
        var ownsInput = lowering.Function.InputOwnership == BoundFunctionInputOwnership.Move
            && lowering.Function.InputType is { } inputType
            && _program.Types.ContainsOwnedStorage(inputType)
            && _locals.ContainsKey(inputName);
        point.SetRuntimeShape(spillPlans, ownsInput);
        var spills = BuildRuntimeAsyncSpills(point.Spills);
        StoreAsyncSpills(lowering.Function, spills);
        foreach (var spill in spills)
        {
            RemoveLocal(spill.Name);
        }
        DropOwnedLocalsCreatedSince(lowering.ResumeBaseLocals, transferredOwnerName: null);
        EmitStore(
            "i32",
            point.State.ToString(CultureInfo.InvariantCulture),
            lowering.StateSlot,
            4);
        EmitRet("i1", "false");

        RestoreLocals(lowering.ResumeBaseLocals);
        if (lowering.Function.InputOwnership == BoundFunctionInputOwnership.Move
            && lowering.Function.InputType is { } resumedInputType
            && _program.Types.ContainsOwnedStorage(resumedInputType)
            && !point.OwnsInput)
        {
            RemoveLocal(inputName);
        }
        EmitLabel(point.ResumeLabel);
        _currentBlockLabel = point.ResumeLabel;
        LoadAsyncSpills(lowering.Function, spills);
        RefreshAsyncScopeSnapshots(spills);
    }

    private void RefreshAsyncScopeSnapshots(IReadOnlyList<RuntimeAsyncSpill> spills)
    {
        foreach (var spill in spills)
        {
            var value = _locals[spill.Name];
            foreach (var scope in _asyncScopeSnapshots)
            {
                if (!scope.Locals.ContainsKey(spill.Name))
                {
                    continue;
                }
                scope.Locals[spill.Name] = value;
                if (!spill.IsMutable)
                {
                    continue;
                }
                if (_mutableContainerSlots.TryGetValue(spill.Name, out var container))
                {
                    scope.MutableContainerSlots[spill.Name] = container;
                }
                if (_mutableStructSlots.TryGetValue(spill.Name, out var structure))
                {
                    scope.MutableStructSlots[spill.Name] = structure;
                }
                if (_mutableScalarSlots.TryGetValue(spill.Name, out var scalar))
                {
                    scope.MutableScalarSlots[spill.Name] = scalar;
                }
            }
        }
    }

    private void StoreSuspendedChild(BoundFunction function, RuntimeTask child)
    {
        EmitAsyncContextStore(
            "%context", function.InputType, function.ReturnType, 7,
            "ptr", child.HandleName, 8);
        EmitAsyncContextStore(
            "%context", function.InputType, function.ReturnType, 8,
            "ptr", child.ContextName, 8);
        EmitCall(
            target: null,
            "void",
            "smalllang_task_park_on",
            $"ptr %control, ptr {child.HandleName}");
    }

    private RuntimeValue EmitStoredChildAwait(BoundFunction function, RuntimeTask child)
    {
        var childHandleAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 7, "async_child_handle_address");
        var childHandle = NextTemp("async_child_handle");
        EmitLoad(childHandle, "ptr", childHandleAddress, 8);
        var childContextAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 8, "async_child_context_address");
        var childContext = NextTemp("async_child_context");
        EmitLoad(childContext, "ptr", childContextAddress, 8);
        return EmitAwaitTask(child with
        {
            HandleName = childHandle,
            ContextName = childContext
        });
    }

    private IReadOnlyList<RuntimeAsyncSpill> BuildRuntimeAsyncSpills(
        IReadOnlyList<AsyncSpillPlan> spillPlans)
    {
        var spills = new List<RuntimeAsyncSpill>(spillPlans.Count);
        var offset = 0;
        foreach (var spill in spillPlans)
        {
            var value = ResolveLocal(spill.Name);
            EnsureRuntimeType(value, spill.Type, spill.Name);
            var materialized = MaterializeAggregateValue(value);
            var alignment = RuntimeAlignment(spill.Type);
            offset = AlignAsyncSize(offset, alignment);
            spills.Add(new RuntimeAsyncSpill(
                spill.Name,
                spill.Type,
                offset,
                alignment,
                materialized.TypeName,
                materialized.ValueName,
                spill.IsMutable,
                value));
            offset = checked(offset + Math.Max(_program.Types.InlineSizeOf(spill.Type), 1));
        }

        return spills;
    }

    private void StoreAsyncSpills(
        BoundFunction function,
        IReadOnlyList<RuntimeAsyncSpill> spills)
    {
        if (spills.Count == 0)
        {
            return;
        }

        var size = spills.Max(spill =>
            checked(spill.Offset + Math.Max(_program.Types.InlineSizeOf(spill.Type), 1)));
        var frame = NextTemp("async_spill_frame");
        EmitCall(frame, "ptr", "smalllang_alloc", $"i64 {size}");
        EmitAsyncContextStore(
            "%context", function.InputType, function.ReturnType, 9,
            "ptr", frame, 8);
        foreach (var spill in spills)
        {
            var address = NextTemp("async_spill_address");
            EmitAssign(address, $"getelementptr i8, ptr {frame}, i64 {spill.Offset}");
            EmitStore(spill.LlvmType, spill.ValueName, address, spill.Alignment);
        }
    }

    private void LoadAsyncSpills(
        BoundFunction function,
        IReadOnlyList<RuntimeAsyncSpill> spills)
    {
        if (spills.Count == 0)
        {
            return;
        }

        var frameAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 9, "async_spill_frame_address");
        var frame = NextTemp("async_spill_frame");
        EmitLoad(frame, "ptr", frameAddress, 8);
        foreach (var spill in spills)
        {
            var address = NextTemp("async_spill_address");
            EmitAssign(address, $"getelementptr i8, ptr {frame}, i64 {spill.Offset}");
            var loaded = NextTemp("async_spill_value");
            EmitLoad(loaded, spill.LlvmType, address, spill.Alignment);
            var value = spill.Template is RuntimeTask task
                ? DematerializeTask(task, loaded)
                : DematerializeAggregateValue(spill.Type, loaded);
            _locals[spill.Name] = value;
            if (spill.IsMutable)
            {
                CreateResumedMutableSlot(spill.Name, value);
            }
        }
        EmitCall(target: null, "void", "smalllang_free", $"ptr {frame}");
        EmitStore("ptr", "null", frameAddress, 8);
    }

    private RuntimeTask DematerializeTask(RuntimeTask template, string aggregate)
    {
        var handle = NextTemp("async_spill_task_handle");
        EmitAssign(handle, $"extractvalue %smalllang.task {aggregate}, 0");
        var context = NextTemp("async_spill_task_context");
        EmitAssign(context, $"extractvalue %smalllang.task {aggregate}, 1");
        return template with
        {
            HandleName = handle,
            ContextName = context
        };
    }

    private void EmitTailAwaitWorker(
        BoundFunction function,
        BindingStatement tailAwaitBinding,
        LocalScope functionLocals)
    {
        var stateSlot = NextTemp("async_resume_state_slot");
        EmitAssign(
            stateSlot,
            "getelementptr %smalllang.task_control, ptr %control, i32 0, i32 5");
        var state = NextTemp("async_resume_state");
        EmitLoad(state, "i32", stateSlot, 4);
        var startLabel = NextLabel("async_state_start");
        var resumeLabel = NextLabel("async_state_resume");
        var invalidLabel = NextLabel("async_state_invalid");
        EmitInstruction(
            $"switch i32 {state}, label %{invalidLabel} [ i32 0, label %{startLabel} i32 1, label %{resumeLabel} ]");
        _currentBlockTerminated = true;

        EmitLabel(invalidLabel);
        EmitTrap();

        EmitLabel(startLabel);
        EmitStatements(function.BlockBody);
        var child = ResolveLocal(tailAwaitBinding.Name) as RuntimeTask
            ?? throw new SmallLangException(
                $"tail await in async function '{function.Name}' did not produce Task<T>");
        EmitAsyncContextStore(
            "%context", function.InputType, function.ReturnType, 7,
            "ptr", child.HandleName, 8);
        EmitAsyncContextStore(
            "%context", function.InputType, function.ReturnType, 8,
            "ptr", child.ContextName, 8);
        RemoveLocal(tailAwaitBinding.Name);
        EmitStore("i32", "1", stateSlot, 4);
        EmitRet("i1", "false");

        EmitLabel(resumeLabel);
        var childHandleAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 7, "async_child_handle_address");
        var childHandle = NextTemp("async_child_handle");
        EmitLoad(childHandle, "ptr", childHandleAddress, 8);
        var childContextAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 8, "async_child_context_address");
        var childContext = NextTemp("async_child_context");
        EmitLoad(childContext, "ptr", childContextAddress, 8);
        var resumedChild = child with
        {
            HandleName = childHandle,
            ContextName = childContext
        };
        var value = EmitAwaitTask(resumedChild);
        EnsureRuntimeType(value, function.ReturnType, function.Name);
        DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
        StoreAsyncResult(function, value);
        EmitRet("i1", "true");
    }

    private void StoreAsyncResult(BoundFunction function, RuntimeValue value)
    {
        var result = MaterializeAsyncResult(value);
        var resultAddress = AsyncContextField(
            "%context", function.InputType, function.ReturnType, 6, "async_result_address");
        EmitStore(result.TypeName, result.ValueName, resultAddress, RuntimeAlignment(function.ReturnType));
    }

    private bool TryGetCfgSuspendPlan(BoundFunction function, out AsyncCfgPlan? plan)
    {
        plan = null;
        var candidates = new List<AsyncCfgCandidate>();
        CollectCfgSuspensions(function.BlockBody, nested: false, candidates);
        if (function.Body is not null)
        {
            CollectCfgSuspensions(function.Body, nested: false, candidates);
        }
        if (!candidates.Any(candidate => candidate.Nested || candidate.IsYield))
        {
            return false;
        }

        var points = candidates
            .Select((candidate, index) => new AsyncCfgSuspendPoint(
                index + 1,
                candidate.Site,
                candidate.IsYield))
            .ToArray();
        plan = new AsyncCfgPlan(points);
        return true;
    }

    private static void CollectCfgSuspensions(
        IReadOnlyList<Statement> statements,
        bool nested,
        List<AsyncCfgCandidate> candidates)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    if (TryGetAwaitTaskName(binding, out _))
                    {
                        candidates.Add(new AsyncCfgCandidate(binding, nested, IsYield: false));
                    }
                    else
                    {
                        CollectCfgSuspensions(binding.Value, nested, candidates);
                    }
                    break;
                case ExpressionStatement { Expression: NameExpression { Name: "yield" } } yield:
                    candidates.Add(new AsyncCfgCandidate(yield, nested, IsYield: true));
                    break;
                case ExpressionStatement expression:
                    CollectCfgSuspensions(expression.Expression, nested, candidates);
                    break;
                case BlockFunctionCallStatement block
                    when block.Target.Count == 1
                        && string.Equals(block.Target[0], "while", StringComparison.Ordinal):
                    CollectCfgSuspensions(block.Body, nested: true, candidates);
                    break;
            }
        }
    }

    private static void CollectCfgSuspensions(
        Expression expression,
        bool nested,
        List<AsyncCfgCandidate> candidates)
    {
        switch (expression)
        {
            case IfExpression conditional:
                CollectCfgSuspensions(conditional.Then.Statements, nested: true, candidates);
                if (conditional.Then.Value is not null)
                {
                    CollectCfgSuspensions(conditional.Then.Value, nested: true, candidates);
                }
                if (conditional.Else is not null)
                {
                    CollectCfgSuspensions(conditional.Else.Statements, nested: true, candidates);
                    if (conditional.Else.Value is not null)
                    {
                        CollectCfgSuspensions(conditional.Else.Value, nested: true, candidates);
                    }
                }
                break;
            case WhenExpression selection:
                foreach (var arm in selection.Arms)
                {
                    CollectCfgSuspensions(arm.Body.Statements, nested: true, candidates);
                    if (arm.Body.Value is not null)
                    {
                        CollectCfgSuspensions(arm.Body.Value, nested: true, candidates);
                    }
                }
                CollectCfgSuspensions(selection.Else.Statements, nested: true, candidates);
                if (selection.Else.Value is not null)
                {
                    CollectCfgSuspensions(selection.Else.Value, nested: true, candidates);
                }
                break;
        }
    }

    private static bool TryGetAwaitTaskName(BindingStatement binding, out string taskName)
    {
        if (!binding.IsMutable
            && binding.Value is FlowExpression
            {
                Source: NameExpression source,
                Targets.Count: 1
            } flow
            && flow.Targets[0].Path.Count == 1
            && string.Equals(flow.Targets[0].Path[0], "await", StringComparison.Ordinal)
            && flow.Targets[0].Arguments.Count == 0)
        {
            taskName = source.Name;
            return true;
        }

        taskName = string.Empty;
        return false;
    }

    private bool TryGetTailAwaitBinding(
        BoundFunction function,
        out BindingStatement? binding)
    {
        binding = null;
        if (function.InputType is { } inputType
            && _program.Types.ContainsOwnedStorage(inputType))
        {
            return false;
        }
        if (function.BlockBody.Count != 1
            || function.BlockBody[0] is not BindingStatement candidate
            || candidate.IsMutable
            || function.Body is not FlowExpression
            {
                Source: NameExpression source,
                Targets.Count: 1
            } flow
            || !string.Equals(source.Name, candidate.Name, StringComparison.Ordinal)
            || flow.Targets[0].Path.Count != 1
            || !string.Equals(flow.Targets[0].Path[0], "await", StringComparison.Ordinal)
            || flow.Targets[0].Arguments.Count != 0)
        {
            return false;
        }

        binding = candidate;
        return true;
    }

    private bool TryGetStatefulAwaitPlan(
        BoundFunction function,
        out AsyncStateMachinePlan? plan)
    {
        plan = null;
        if (function.InputType is { } inputType
            && _program.Types.ContainsOwnedStorage(inputType))
        {
            return false;
        }
        if (!_program.FunctionBindings.TryGetValue(function, out var bindingTypes))
        {
            return false;
        }

        if (function.BlockBody.Any(statement =>
            statement is not (BindingStatement or ExpressionStatement)))
        {
            return false;
        }

        var awaits = new List<(int Index, BindingStatement Binding, string TaskName)>();
        for (var index = 0; index < function.BlockBody.Count; index++)
        {
            if (function.BlockBody[index] is not BindingStatement
                {
                    IsMutable: false,
                    Value: FlowExpression
                    {
                        Source: NameExpression source,
                        Targets.Count: 1
                    } flow
                } awaitBinding
                || flow.Targets[0].Path.Count != 1
                || !string.Equals(flow.Targets[0].Path[0], "await", StringComparison.Ordinal)
                || flow.Targets[0].Arguments.Count != 0)
            {
                continue;
            }

            awaits.Add((index, awaitBinding, source.Name));
        }
        if (awaits.Count == 0)
        {
            return false;
        }

        var awaitPlans = new List<AsyncAwaitPoint>(awaits.Count);
        foreach (var (index, awaitBinding, taskName) in awaits)
        {
            var priorBindings = function.BlockBody.Take(index).OfType<BindingStatement>();
            var postStatements = function.BlockBody.Skip(index + 1).ToArray();
            var spillPlans = new List<AsyncSpillPlan>();
            foreach (var binding in priorBindings)
            {
                if (string.Equals(binding.Name, taskName, StringComparison.Ordinal)
                    || !IsNameReferencedAfterAwait(binding.Name, postStatements, function.Body))
                {
                    continue;
                }
                if (!bindingTypes.TryGetValue(binding.Name, out var type)
                    && !TryGetAsyncTaskBindingType(binding.Value, out type))
                {
                    return false;
                }
                if (!IsAsyncSpillType(type))
                {
                    return false;
                }
                if (binding.IsMutable && !IsAsyncMutableSpillType(type))
                {
                    return false;
                }
                spillPlans.Add(new AsyncSpillPlan(binding.Name, type, binding.IsMutable));
            }

            awaitPlans.Add(new AsyncAwaitPoint(
                index,
                awaitBinding.Name,
                taskName,
                spillPlans));
        }

        plan = new AsyncStateMachinePlan(awaitPlans);
        return true;
    }

    private bool TryGetAsyncTaskBindingType(Expression expression, out BoundType taskType)
    {
        BoundFunction? function = null;
        if (expression is CallExpression call)
        {
            _program.ResolvedGenericCalls.TryGetValue(call, out function);
            if (function is null)
            {
                TryResolveFunction(call.Path, out function);
            }
        }
        else if (expression is NameExpression name)
        {
            TryResolveFunction([name.Name], out function);
        }
        else if (expression is FlowExpression { Targets.Count: > 0 } flow)
        {
            TryResolveFunction(flow.Targets[^1].Path, out function);
        }

        if (function is not { IsAsync: true })
        {
            taskType = default;
            return false;
        }

        taskType = _program.Types.GetOrAddTask(function.ReturnType);
        return true;
    }

    private static bool IsNameReferencedAfterAwait(
        string name,
        IReadOnlyList<Statement> statements,
        Expression? body)
    {
        return statements.Any(statement => StoragePlacementAnalyzer.ReferencesName(statement, name))
            || (body is not null && StoragePlacementAnalyzer.ReferencesName(body, name));
    }

    private bool IsAsyncSpillType(BoundType type)
    {
        return IsAsyncSpillType(type, new HashSet<BoundType>());
    }

    private bool IsAsyncSpillType(BoundType type, HashSet<BoundType> visiting)
    {
        if (IsIntegerType(type)
            || IsFloatType(type)
            || type is BoundType.Bool or BoundType.Text
            || _program.Types.IsBox(type)
            || _program.Types.IsTask(type)
            || type == BoundType.DynamicIntArray
            || type == BoundType.IntDictionary
            || _program.Types.IsDynamicArray(type)
            || _program.Types.IsDictionary(type))
        {
            return true;
        }
        if (!visiting.Add(type))
        {
            return true;
        }

        try
        {
            if (_program.Types.IsStruct(type))
            {
                return _program.Types.GetStruct(type).Fields.All(field =>
                    IsAsyncSpillType(field.Type, visiting));
            }
            if (_program.Types.IsEnum(type))
            {
                return _program.Types.GetEnum(type).Variants.All(variant =>
                    variant.PayloadType is null
                    || IsAsyncSpillType(variant.PayloadType.Value, visiting));
            }
            return false;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private bool IsAsyncMutableSpillType(BoundType type)
    {
        return IsIntegerType(type)
            || IsFloatType(type)
            || type is BoundType.Bool or BoundType.Text
            || _program.Types.IsStruct(type)
            || type == BoundType.DynamicIntArray
            || type == BoundType.IntDictionary
            || _program.Types.IsDynamicArray(type)
            || _program.Types.IsDictionary(type);
    }

    private void CreateResumedMutableSlot(string name, RuntimeValue value)
    {
        _mutableLocals.Add(name);
        if (value is RuntimeStruct structure)
        {
            var pointer = NextTemp("async_mutable_struct_slot");
            EmitAlloca(pointer, LlvmStructType(structure.Type), RuntimeAlignment(structure.Type));
            EmitStore(LlvmStructType(structure.Type), structure.ValueName, pointer, RuntimeAlignment(structure.Type));
            _mutableStructSlots[name] = pointer;
            return;
        }
        if (value is RuntimeInt or RuntimeFloat or RuntimeBool or RuntimeText)
        {
            var materialized = MaterializeAggregateValue(value);
            var pointer = NextTemp("async_mutable_scalar_slot");
            EmitAlloca(pointer, materialized.TypeName, RuntimeAlignment(value.Type));
            EmitStore(materialized.TypeName, materialized.ValueName, pointer, RuntimeAlignment(value.Type));
            _mutableScalarSlots[name] = pointer;
            return;
        }

        var slot = new MutableContainerSlot(
            NextTemp("async_mutable_ptr_slot"),
            NextTemp("async_mutable_len_slot"),
            NextTemp("async_mutable_capacity_slot"),
            StackAllocation: null);
        EmitAlloca(slot.PointerAddress, "ptr", 8);
        EmitAlloca(slot.LengthAddress, "i64", 8);
        EmitAlloca(slot.CapacityAddress, "i64", 8);
        _mutableContainerSlots[name] = slot;
        StoreMutableContainer(name, value);
    }

    private RuntimeTask EmitAsyncFunctionCall(
        BoundFunction function,
        RuntimeValue? argument,
        IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("task_call");
        EmitCall(
            aggregate,
            "%smalllang.task",
            SymbolForFunction(function)[1..],
            FunctionCallArgumentList(function, argument, additionalArguments));
        var handle = NextTemp("task_handle");
        EmitAssign(handle, $"extractvalue %smalllang.task {aggregate}, 0");
        var context = NextTemp("task_context");
        EmitAssign(context, $"extractvalue %smalllang.task {aggregate}, 1");
        return new RuntimeTask(
            _program.Types.GetOrAddTask(function.ReturnType),
            function.InputType,
            function.ReturnType,
            handle,
            context);
    }

    private RuntimeValue EmitAwaitTask(RuntimeTask task, bool discardResult = false)
    {
        var waitSucceeded = NextTemp("task_wait_succeeded");
        EmitCall(waitSucceeded, "i1", "smalllang_task_join", $"ptr {task.HandleName}");
        var waitedLabel = NextLabel("task_waited");
        var waitFailedLabel = NextLabel("task_wait_failed");
        EmitConditionalBranch(waitSucceeded, waitedLabel, waitFailedLabel);
        EmitLabel(waitFailedLabel);
        EmitTrap();
        EmitLabel(waitedLabel);
        if (task.RuntimeFunction is { Kind: BoundFunctionKind.RuntimeReadScalarAsync } fileRead)
        {
            var fileResult = EmitRuntimeReadScalar(fileRead, task.HandleName);
            var fileResultAddress = AsyncContextField(
                task.ContextName, task.InputType, task.ResultType, 6, "file_task_result_address");
            EmitStore(
                LlvmEnumType(fileResult.Type),
                fileResult.ValueName,
                fileResultAddress,
                RuntimeAlignment(fileResult.Type));
        }
        else if (task.RuntimeFunction is { Kind: BoundFunctionKind.RuntimeWriteScalarAtAsync } fileWrite)
        {
            var fileResult = EmitRuntimeCompletedWriteScalarAt(fileWrite, task.HandleName);
            var fileResultAddress = AsyncContextField(
                task.ContextName, task.InputType, task.ResultType, 6, "file_write_task_result_address");
            EmitStore(
                LlvmEnumType(fileResult.Type),
                fileResult.ValueName,
                fileResultAddress,
                RuntimeAlignment(fileResult.Type));
        }
        else if (task.RuntimeFunction is { Kind: BoundFunctionKind.RuntimeSyncFileAsync } fileSync)
        {
            var fileResult = EmitRuntimeCompletedSyncFile(fileSync, task.HandleName);
            var fileResultAddress = AsyncContextField(
                task.ContextName, task.InputType, task.ResultType, 6, "file_sync_task_result_address");
            EmitStore(
                LlvmEnumType(fileResult.Type),
                fileResult.ValueName,
                fileResultAddress,
                RuntimeAlignment(fileResult.Type));
        }
        else if (task.RuntimeFunction is
                 {
                     Kind: BoundFunctionKind.RuntimeOpenFileAsync
                         or BoundFunctionKind.RuntimeOpenWriteFileAsync
                 } fileOpen)
        {
            var fileResult = EmitRuntimeCompletedOpenFile(fileOpen, task.HandleName);
            var fileResultAddress = AsyncContextField(
                task.ContextName, task.InputType, task.ResultType, 6, "file_open_task_result_address");
            EmitStore(
                LlvmEnumType(fileResult.Type),
                fileResult.ValueName,
                fileResultAddress,
                RuntimeAlignment(fileResult.Type));
        }
        var resultAddress = AsyncContextField(
            task.ContextName, task.InputType, task.ResultType, 6, "task_result_address");
        var loaded = NextTemp(discardResult ? "task_discarded_result" : "task_result");
        EmitLoad(loaded, AsyncStorageLlvmType(task.ResultType), resultAddress, RuntimeAlignment(task.ResultType));
        var value = DematerializeAsyncResult(task.ResultType, loaded);
        if (discardResult && _program.Types.ContainsOwnedStorage(task.ResultType))
        {
            DropOwnedRuntimeValue(value);
        }
        var closeSucceeded = NextTemp("task_close_succeeded");
        EmitCall(closeSucceeded, "i1", "smalllang_task_release", $"ptr {task.HandleName}");
        var closedLabel = NextLabel("task_closed");
        var closeFailedLabel = NextLabel("task_close_failed");
        EmitConditionalBranch(closeSucceeded, closedLabel, closeFailedLabel);
        EmitLabel(closeFailedLabel);
        EmitTrap();
        EmitLabel(closedLabel);
        _currentBlockLabel = closedLabel;
        return value;
    }

    private void EmitCancelTask(RuntimeTask task)
    {
        EmitCancelTaskHandle(task.HandleName);
    }

    private void EmitCancelTaskHandle(string handle)
    {
        var cancelled = NextTemp("task_cancel_succeeded");
        EmitCall(cancelled, "i1", "smalllang_task_cancel", $"ptr {handle}");
        var cancelledLabel = NextLabel("task_cancelled");
        var cancelFailedLabel = NextLabel("task_cancel_failed");
        EmitConditionalBranch(cancelled, cancelledLabel, cancelFailedLabel);
        EmitLabel(cancelFailedLabel);
        _currentBlockLabel = cancelFailedLabel;
        EmitTrap();
        EmitLabel(cancelledLabel);
        _currentBlockLabel = cancelledLabel;
    }

    private void EmitAsyncContextLoad(
        string target, string context, BoundType? inputType, BoundType resultType,
        int field, string type, int alignment)
    {
        var address = AsyncContextField(context, inputType, resultType, field, "async_context_field");
        EmitLoad(target, type, address, alignment);
    }

    private void EmitAsyncContextStore(
        string context, BoundType? inputType, BoundType resultType,
        int field, string type, string value, int alignment)
    {
        var address = AsyncContextField(context, inputType, resultType, field, "async_context_field");
        EmitStore(type, value, address, alignment);
    }

    private void EmitAsyncFunctionContextLoad(
        string target,
        string context,
        BoundFunction function,
        int field,
        string type,
        int alignment)
    {
        var address = AsyncFunctionContextField(context, function, field, "async_function_context_field");
        EmitLoad(target, type, address, alignment);
    }

    private void EmitAsyncFunctionContextStore(
        string context,
        BoundFunction function,
        int field,
        string type,
        string value,
        int alignment)
    {
        var address = AsyncFunctionContextField(context, function, field, "async_function_context_field");
        EmitStore(type, value, address, alignment);
    }

    private string AsyncFunctionContextField(
        string context,
        BoundFunction function,
        int field,
        string prefix)
    {
        var address = NextTemp(prefix);
        EmitAssign(address,
            $"getelementptr {AsyncFunctionContextType(function)}, ptr {context}, i32 0, i32 {field}");
        return address;
    }

    private string AsyncContextField(
        string context, BoundType? inputType, BoundType resultType, int field, string prefix)
    {
        var address = NextTemp(prefix);
        EmitAssign(address, $"getelementptr {AsyncContextType(inputType, resultType)}, ptr {context}, i32 0, i32 {field}");
        return address;
    }

    private string AsyncContextType(BoundType? inputType, BoundType resultType) =>
        $"{{ ptr, ptr, ptr, ptr, ptr, {AsyncStorageLlvmType(inputType)}, {AsyncStorageLlvmType(resultType)}, ptr, ptr, ptr }}";

    private string AsyncFunctionContextType(BoundFunction function)
    {
        var fields = new List<string>
        {
            "ptr", "ptr", "ptr", "ptr", "ptr",
            AsyncStorageLlvmType(function.InputType),
            AsyncStorageLlvmType(function.ReturnType),
            "ptr", "ptr", "ptr"
        };
        fields.AddRange((function.AdditionalParameters ?? [])
            .Select(parameter => AsyncStorageLlvmType(parameter.Type)));
        return $"{{ {string.Join(", ", fields)} }}";
    }

    private string AsyncStorageLlvmType(BoundType? type) =>
        type is null or BoundType.Unit ? "i8" : LlvmType(type.Value);

    private int AsyncStorageAlignment(BoundType? type) =>
        type is null ? 1 : RuntimeAlignment(type.Value);

    private (string TypeName, string ValueName) MaterializeAsyncResult(RuntimeValue value) =>
        value is RuntimeUnit ? ("i8", "0") : MaterializeAggregateValue(value);

    private RuntimeValue DematerializeAsyncResult(BoundType type, string value) =>
        type == BoundType.Unit ? RuntimeUnit.Instance : DematerializeAggregateValue(type, value);

    private void DropFailedAsyncInput(BoundFunction function)
    {
        if (function.InputOwnership != BoundFunctionInputOwnership.Move
            || function.InputType is not { } inputType
            || !_program.Types.ContainsOwnedStorage(inputType))
        {
            return;
        }

        DropOwnedRuntimeValue(DematerializeAggregateValue(inputType, "%it"));
    }

    private void DropFailedAsyncAdditionalInputs(BoundFunction function)
    {
        var parameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            if (parameter.Ownership == BoundFunctionInputOwnership.Move
                && _program.Types.ContainsOwnedStorage(parameter.Type))
            {
                DropOwnedRuntimeValue(DematerializeAggregateValue(parameter.Type, $"%arg_{index}"));
            }
        }
    }

    private void DropCancelledAsyncAdditionalInputs(BoundFunction function)
    {
        var parameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            if (parameter.Ownership != BoundFunctionInputOwnership.Move
                || !_program.Types.ContainsOwnedStorage(parameter.Type))
            {
                continue;
            }

            var value = $"%cancel_arg_{index}";
            EmitAsyncFunctionContextLoad(
                value,
                "%context",
                function,
                10 + index,
                AsyncStorageLlvmType(parameter.Type),
                RuntimeAlignment(parameter.Type));
            DropOwnedRuntimeValue(DematerializeAggregateValue(parameter.Type, value));
        }
    }

    private int AsyncContextSize(BoundFunction function)
    {
        var size = AsyncContextSize(function.InputType, function.ReturnType);
        foreach (var parameter in function.AdditionalParameters ?? [])
        {
            var alignment = RuntimeAlignment(parameter.Type);
            size = AlignAsyncSize(size, alignment);
            size += Math.Max(_program.Types.InlineSizeOf(parameter.Type), 1);
        }
        return size;
    }

    private int AsyncContextSize(BoundType? inputType, BoundType resultType)
    {
        var inputAlignment = AsyncStorageAlignment(inputType);
        var inputOffset = AlignAsyncSize(40, inputAlignment);
        var inputSize = inputType is null
            ? 1
            : Math.Max(_program.Types.InlineSizeOf(inputType.Value), 1);
        var resultAlignment = RuntimeAlignment(resultType);
        var resultOffset = AlignAsyncSize(inputOffset + inputSize, resultAlignment);
        var resultSize = Math.Max(_program.Types.InlineSizeOf(resultType), 1);
        var childTaskOffset = AlignAsyncSize(resultOffset + resultSize, 8);
        return childTaskOffset + 24;
    }

    private static int AlignAsyncSize(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);

    private static string AsyncWorkerSymbol(BoundFunction function) =>
        SymbolForFunction(function)[1..] + "_async_worker";

    private static string AsyncCancelSymbol(BoundFunction function) =>
        SymbolForFunction(function)[1..] + "_async_cancel";

    private sealed record AsyncStateMachinePlan(IReadOnlyList<AsyncAwaitPoint> Awaits);

    private sealed class AsyncCfgPlan
    {
        public AsyncCfgPlan(IReadOnlyList<AsyncCfgSuspendPoint> points)
        {
            Points = points;
            BySite = points.ToDictionary(point => point.Site);
            ByBinding = points
                .Where(point => point.Site is BindingStatement)
                .ToDictionary(point => (BindingStatement)point.Site);
            ByYield = points
                .Where(point => point.Site is ExpressionStatement && point.IsYield)
                .ToDictionary(point => (ExpressionStatement)point.Site);
        }

        public IReadOnlyList<AsyncCfgSuspendPoint> Points { get; }

        public IReadOnlyDictionary<Statement, AsyncCfgSuspendPoint> BySite { get; }

        public IReadOnlyDictionary<BindingStatement, AsyncCfgSuspendPoint> ByBinding { get; }

        public IReadOnlyDictionary<ExpressionStatement, AsyncCfgSuspendPoint> ByYield { get; }
    }

    private sealed class AsyncCfgSuspendPoint(int state, Statement site, bool isYield)
    {
        public int State { get; } = state;

        public Statement Site { get; } = site;

        public bool IsYield { get; } = isYield;

        public string ResumeLabel { get; set; } = string.Empty;

        public IReadOnlyList<AsyncSpillPlan> Spills { get; private set; } = [];

        public bool OwnsInput { get; private set; }

        public void SetRuntimeShape(IReadOnlyList<AsyncSpillPlan> spills, bool ownsInput)
        {
            Spills = spills;
            OwnsInput = ownsInput;
        }
    }

    private sealed record AsyncCfgLowering(
        BoundFunction Function,
        AsyncCfgPlan Plan,
        LocalScope ResumeBaseLocals,
        string StateSlot);

    private sealed record AsyncAwaitPoint(
        int StatementIndex,
        string ResultName,
        string TaskName,
        IReadOnlyList<AsyncSpillPlan> Spills);

    private sealed record AsyncCfgCandidate(Statement Site, bool Nested, bool IsYield);

    private sealed record AsyncSpillPlan(string Name, BoundType Type, bool IsMutable);

    private sealed record RuntimeAsyncSpill(
        string Name,
        BoundType Type,
        int Offset,
        int Alignment,
        string LlvmType,
        string ValueName,
        bool IsMutable,
        RuntimeValue Template);
}
