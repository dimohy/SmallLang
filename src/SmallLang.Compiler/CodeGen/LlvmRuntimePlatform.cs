using System.Text;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.CodeGen;

internal abstract class LlvmRuntimePlatform
{
    public abstract string TargetTriple { get; }

    public abstract string EntryPointName { get; }

    public virtual string EntryPointParameters => "";

    public virtual int PointerBitWidth => 64;

    public virtual void EmitGlobals(StringBuilder globals)
    {
    }

    public abstract void EmitExternalDeclarations(StringBuilder functions);

    public abstract void EmitIoPrimitives(StringBuilder functions);

    public abstract void EmitFilePrimitives(StringBuilder functions);

    public abstract void EmitMappedFilePrimitives(StringBuilder functions);

    public abstract void EmitTimePrimitives(StringBuilder functions);

    public abstract void EmitProcessPrimitives(StringBuilder functions);

    public virtual void EmitEnvironmentPrimitives(StringBuilder functions)
    {
    }

    public abstract void EmitEntryHandles(StringBuilder functions);

    public virtual void EmitProcessEntry(StringBuilder functions)
    {
    }

    public virtual bool SupportsHeapAllocation => true;

    public virtual bool SupportsMemoryMapping => true;

    public virtual bool SupportsProcessArguments => true;

    public virtual bool SupportsEnvironment => true;

    public virtual bool SupportsChildProcesses => true;

    public virtual bool SupportsAsync => false;

    public virtual void EmitAsyncPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal void @smalllang_task_enqueue_ready(ptr %control) #0 {
            entry:
              %status_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 4
              store i32 0, ptr %status_slot, align 4
              %next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 3
              store ptr null, ptr %next_slot, align 8
              %tail = load ptr, ptr @smalllang_task_ready_tail, align 8
              %empty = icmp eq ptr %tail, null
              br i1 %empty, label %first, label %append

            first:
              store ptr %control, ptr @smalllang_task_ready_head, align 8
              br label %queued

            append:
              %tail_next_slot = getelementptr %smalllang.task_control, ptr %tail, i32 0, i32 3
              store ptr %control, ptr %tail_next_slot, align 8
              br label %queued

            queued:
              store ptr %control, ptr @smalllang_task_ready_tail, align 8
              ret void
            }

            define internal void @smalllang_task_park_on(ptr %parent, ptr %child) #0 {
            entry:
              %child_status_slot = getelementptr %smalllang.task_control, ptr %child, i32 0, i32 4
              %child_status = load i32, ptr %child_status_slot, align 4
              %completed = icmp eq i32 %child_status, 2
              br i1 %completed, label %done, label %park

            park:
              %child_waiter_slot = getelementptr %smalllang.task_control, ptr %child, i32 0, i32 9
              %existing_waiter = load ptr, ptr %child_waiter_slot, align 8
              %available = icmp eq ptr %existing_waiter, null
              br i1 %available, label %attach, label %invalid

            attach:
              store ptr %parent, ptr %child_waiter_slot, align 8
              %parent_child_slot = getelementptr %smalllang.task_control, ptr %parent, i32 0, i32 10
              store ptr %child, ptr %parent_child_slot, align 8
              %parent_status_slot = getelementptr %smalllang.task_control, ptr %parent, i32 0, i32 4
              store i32 5, ptr %parent_status_slot, align 4
              br label %done

            invalid:
              call void @llvm.trap()
              unreachable

            done:
              ret void
            }

            define internal void @smalllang_task_wake_waiter(ptr %child) #0 {
            entry:
              %waiter_slot = getelementptr %smalllang.task_control, ptr %child, i32 0, i32 9
              %waiter = load ptr, ptr %waiter_slot, align 8
              %has_waiter = icmp ne ptr %waiter, null
              br i1 %has_waiter, label %wake, label %done

            wake:
              store ptr null, ptr %waiter_slot, align 8
              %waiter_child_slot = getelementptr %smalllang.task_control, ptr %waiter, i32 0, i32 10
              store ptr null, ptr %waiter_child_slot, align 8
              call void @smalllang_task_enqueue_ready(ptr %waiter)
              br label %done

            done:
              ret void
            }

            define internal void @smalllang_timer_park(ptr %control, i64 %deadline) #0 {
            entry:
              %status_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 4
              store i32 4, ptr %status_slot, align 4
              %deadline_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 8
              store i64 %deadline, ptr %deadline_slot, align 8
              %head = load ptr, ptr @smalllang_task_timer_head, align 8
              br label %scan

            scan:
              %previous = phi ptr [ null, %entry ], [ %current, %advance ]
              %current = phi ptr [ %head, %entry ], [ %next, %advance ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %compare, label %insert

            compare:
              %current_deadline_slot = getelementptr %smalllang.task_control, ptr %current, i32 0, i32 8
              %current_deadline = load i64, ptr %current_deadline_slot, align 8
              %before_current = icmp slt i64 %deadline, %current_deadline
              br i1 %before_current, label %insert, label %advance

            advance:
              %current_next_slot = getelementptr %smalllang.task_control, ptr %current, i32 0, i32 7
              %next = load ptr, ptr %current_next_slot, align 8
              br label %scan

            insert:
              %control_next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 7
              store ptr %current, ptr %control_next_slot, align 8
              %at_head = icmp eq ptr %previous, null
              br i1 %at_head, label %insert_head, label %insert_after

            insert_head:
              store ptr %control, ptr @smalllang_task_timer_head, align 8
              ret void

            insert_after:
              %previous_next_slot = getelementptr %smalllang.task_control, ptr %previous, i32 0, i32 7
              store ptr %control, ptr %previous_next_slot, align 8
              ret void
            }

            define internal void @smalllang_timer_wake_due() #0 {
            entry:
              br label %scan

            scan:
              %head = load ptr, ptr @smalllang_task_timer_head, align 8
              %has_head = icmp ne ptr %head, null
              br i1 %has_head, label %check, label %done

            check:
              %deadline_slot = getelementptr %smalllang.task_control, ptr %head, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @smalllang_now_millis()
              %due = icmp sge i64 %now, %deadline
              br i1 %due, label %wake, label %done

            wake:
              %timer_next_slot = getelementptr %smalllang.task_control, ptr %head, i32 0, i32 7
              %timer_next = load ptr, ptr %timer_next_slot, align 8
              store ptr %timer_next, ptr @smalllang_task_timer_head, align 8
              store ptr null, ptr %timer_next_slot, align 8
              call void @smalllang_task_enqueue_ready(ptr %head)
              br label %scan

            done:
              ret void
            }

            define internal i1 @smalllang_timer_wait_next() #0 {
            entry:
              call void @smalllang_timer_wake_due()
              %ready = load ptr, ptr @smalllang_task_ready_head, align 8
              %has_ready = icmp ne ptr %ready, null
              br i1 %has_ready, label %progress, label %inspect_timer

            inspect_timer:
              %timer = load ptr, ptr @smalllang_task_timer_head, align 8
              %has_timer = icmp ne ptr %timer, null
              br i1 %has_timer, label %wait, label %stalled

            wait:
              %deadline_slot = getelementptr %smalllang.task_control, ptr %timer, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @smalllang_now_millis()
              %remaining = sub i64 %deadline, %now
              call void @smalllang_wait_millis(i64 %remaining)
              call void @smalllang_timer_wake_due()
              br label %progress

            progress:
              ret i1 true

            stalled:
              ret i1 false
            }

            define internal i1 @smalllang_sleep_worker(ptr %control) #0 {
            entry:
              %deadline_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @smalllang_now_millis()
              %ready = icmp sge i64 %now, %deadline
              br i1 %ready, label %complete, label %pending

            complete:
              ret i1 true

            pending:
              call void @smalllang_timer_park(ptr %control, i64 %deadline)
              ret i1 false
            }

            define internal void @smalllang_sleep_cancel(ptr %control) #0 {
            entry:
              %context_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void @smalllang_free(ptr %context)
              ret void
            }

            define internal ptr @smalllang_task_start(ptr %worker, ptr %destroy, ptr %cancel, ptr %context) #0 {
            entry:
              %control = call ptr @smalllang_alloc(i64 80)
              %allocated = icmp ne ptr %control, null
              br i1 %allocated, label %initialize, label %fail

            initialize:
              %context_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 0
              store ptr %context, ptr %context_slot, align 8
              %resume_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 1
              store ptr %worker, ptr %resume_slot, align 8
              %destroy_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 2
              store ptr %destroy, ptr %destroy_slot, align 8
              %next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 3
              store ptr null, ptr %next_slot, align 8
              %status_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 4
              store i32 0, ptr %status_slot, align 4
              %state_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 5
              store i32 0, ptr %state_slot, align 4
              %cancel_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 6
              store ptr %cancel, ptr %cancel_slot, align 8
              %timer_next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 7
              store ptr null, ptr %timer_next_slot, align 8
              %deadline_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 8
              store i64 0, ptr %deadline_slot, align 8
              %waiter_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 9
              store ptr null, ptr %waiter_slot, align 8
              %waiting_child_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 10
              store ptr null, ptr %waiting_child_slot, align 8
              %tail = load ptr, ptr @smalllang_task_ready_tail, align 8
              %empty = icmp eq ptr %tail, null
              br i1 %empty, label %first, label %append

            first:
              store ptr %control, ptr @smalllang_task_ready_head, align 8
              br label %queued

            append:
              %tail_next_slot = getelementptr %smalllang.task_control, ptr %tail, i32 0, i32 3
              store ptr %control, ptr %tail_next_slot, align 8
              br label %queued

            queued:
              store ptr %control, ptr @smalllang_task_ready_tail, align 8
              ret ptr %control

            fail:
              ret ptr null
            }

            define internal i1 @smalllang_task_join(ptr %handle) #0 {
            entry:
              br label %poll

            poll:
              %target_status_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 4
              %target_status = load i32, ptr %target_status_slot, align 4
              %completed = icmp eq i32 %target_status, 2
              br i1 %completed, label %success, label %dequeue

            dequeue:
              %ready = load ptr, ptr @smalllang_task_ready_head, align 8
              %has_ready = icmp ne ptr %ready, null
              br i1 %has_ready, label %run, label %wait

            wait:
              %timer_progress = call i1 @smalllang_timer_wait_next()
              br i1 %timer_progress, label %poll, label %fail

            run:
              %next_slot = getelementptr %smalllang.task_control, ptr %ready, i32 0, i32 3
              %next = load ptr, ptr %next_slot, align 8
              store ptr %next, ptr @smalllang_task_ready_head, align 8
              %became_empty = icmp eq ptr %next, null
              br i1 %became_empty, label %clear_tail, label %invoke

            clear_tail:
              store ptr null, ptr @smalllang_task_ready_tail, align 8
              br label %invoke

            invoke:
              %ready_status_slot = getelementptr %smalllang.task_control, ptr %ready, i32 0, i32 4
              store i32 1, ptr %ready_status_slot, align 4
              %resume_slot = getelementptr %smalllang.task_control, ptr %ready, i32 0, i32 1
              %worker = load ptr, ptr %resume_slot, align 8
              %worker_complete = call i1 %worker(ptr %ready)
              br i1 %worker_complete, label %complete, label %requeue

            complete:
              store i32 2, ptr %ready_status_slot, align 4
              call void @smalllang_task_wake_waiter(ptr %ready)
              br label %poll

            requeue:
              %pending_status = load i32, ptr %ready_status_slot, align 4
              %waiting = icmp uge i32 %pending_status, 4
              br i1 %waiting, label %poll, label %requeue_ready

            requeue_ready:
              store i32 0, ptr %ready_status_slot, align 4
              store ptr null, ptr %next_slot, align 8
              %tail = load ptr, ptr @smalllang_task_ready_tail, align 8
              %queue_empty = icmp eq ptr %tail, null
              br i1 %queue_empty, label %requeue_first, label %requeue_append

            requeue_first:
              store ptr %ready, ptr @smalllang_task_ready_head, align 8
              br label %requeued

            requeue_append:
              %tail_next_slot = getelementptr %smalllang.task_control, ptr %tail, i32 0, i32 3
              store ptr %ready, ptr %tail_next_slot, align 8
              br label %requeued

            requeued:
              store ptr %ready, ptr @smalllang_task_ready_tail, align 8
              br label %poll

            success:
              ret i1 true

            fail:
              ret i1 false
            }

            define internal i1 @smalllang_task_release(ptr %handle) #0 {
            entry:
              %status_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 4
              %status = load i32, ptr %status_slot, align 4
              %completed = icmp eq i32 %status, 2
              br i1 %completed, label %destroy, label %fail

            destroy:
              %destroy_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 2
              %destroy_fn = load ptr, ptr %destroy_slot, align 8
              %context_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void %destroy_fn(ptr %context)
              call void @smalllang_free(ptr %handle)
              ret i1 true

            fail:
              ret i1 false
            }

            define internal i1 @smalllang_task_cancel(ptr %handle) #0 {
            entry:
              %status_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 4
              %status = load i32, ptr %status_slot, align 4
              %completed = icmp eq i32 %status, 2
              br i1 %completed, label %destroy, label %check_queued

            check_queued:
              %queued = icmp eq i32 %status, 0
              br i1 %queued, label %find, label %check_waiting

            check_waiting:
              %waiting_timer = icmp eq i32 %status, 4
              br i1 %waiting_timer, label %timer_find, label %check_child_waiting

            check_child_waiting:
              %waiting_child = icmp eq i32 %status, 5
              br i1 %waiting_child, label %detach_child, label %fail

            detach_child:
              %waiting_child_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 10
              %child = load ptr, ptr %waiting_child_slot, align 8
              %has_child = icmp ne ptr %child, null
              br i1 %has_child, label %clear_child_waiter, label %fail

            clear_child_waiter:
              %child_waiter_slot = getelementptr %smalllang.task_control, ptr %child, i32 0, i32 9
              store ptr null, ptr %child_waiter_slot, align 8
              store ptr null, ptr %waiting_child_slot, align 8
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            find:
              %head = load ptr, ptr @smalllang_task_ready_head, align 8
              br label %scan

            scan:
              %previous = phi ptr [ null, %find ], [ %current, %advance ]
              %current = phi ptr [ %head, %find ], [ %next, %advance ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %compare, label %fail

            compare:
              %found = icmp eq ptr %current, %handle
              br i1 %found, label %unlink, label %advance

            advance:
              %current_next_slot = getelementptr %smalllang.task_control, ptr %current, i32 0, i32 3
              %next = load ptr, ptr %current_next_slot, align 8
              br label %scan

            unlink:
              %handle_next_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 3
              %handle_next = load ptr, ptr %handle_next_slot, align 8
              %is_head = icmp eq ptr %previous, null
              br i1 %is_head, label %unlink_head, label %unlink_after

            unlink_head:
              store ptr %handle_next, ptr @smalllang_task_ready_head, align 8
              br label %repair_tail

            unlink_after:
              %previous_next_slot = getelementptr %smalllang.task_control, ptr %previous, i32 0, i32 3
              store ptr %handle_next, ptr %previous_next_slot, align 8
              br label %repair_tail

            repair_tail:
              %tail = load ptr, ptr @smalllang_task_ready_tail, align 8
              %is_tail = icmp eq ptr %tail, %handle
              br i1 %is_tail, label %replace_tail, label %mark_cancelled

            replace_tail:
              store ptr %previous, ptr @smalllang_task_ready_tail, align 8
              br label %mark_cancelled

            mark_cancelled:
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            timer_find:
              %timer_head = load ptr, ptr @smalllang_task_timer_head, align 8
              br label %timer_scan

            timer_scan:
              %timer_previous = phi ptr [ null, %timer_find ], [ %timer_current, %timer_advance ]
              %timer_current = phi ptr [ %timer_head, %timer_find ], [ %timer_next, %timer_advance ]
              %timer_has_current = icmp ne ptr %timer_current, null
              br i1 %timer_has_current, label %timer_compare, label %fail

            timer_compare:
              %timer_found = icmp eq ptr %timer_current, %handle
              br i1 %timer_found, label %timer_unlink, label %timer_advance

            timer_advance:
              %timer_current_next_slot = getelementptr %smalllang.task_control, ptr %timer_current, i32 0, i32 7
              %timer_next = load ptr, ptr %timer_current_next_slot, align 8
              br label %timer_scan

            timer_unlink:
              %handle_timer_next_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 7
              %handle_timer_next = load ptr, ptr %handle_timer_next_slot, align 8
              %timer_is_head = icmp eq ptr %timer_previous, null
              br i1 %timer_is_head, label %timer_unlink_head, label %timer_unlink_after

            timer_unlink_head:
              store ptr %handle_timer_next, ptr @smalllang_task_timer_head, align 8
              br label %timer_mark_cancelled

            timer_unlink_after:
              %timer_previous_next_slot = getelementptr %smalllang.task_control, ptr %timer_previous, i32 0, i32 7
              store ptr %handle_timer_next, ptr %timer_previous_next_slot, align 8
              br label %timer_mark_cancelled

            timer_mark_cancelled:
              store ptr null, ptr %handle_timer_next_slot, align 8
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            destroy:
              %cancel_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 6
              %cancel_fn = load ptr, ptr %cancel_slot, align 8
              call void %cancel_fn(ptr %handle)
              call void @smalllang_free(ptr %handle)
              ret i1 true

            fail:
              ret i1 false
            }

            """);
    }

    public virtual void EmitExitCleanup(StringBuilder functions)
    {
    }

    public virtual void EmitEnvironmentCleanup(StringBuilder functions)
    {
    }

    public abstract void EmitMemoryDeclarations(StringBuilder functions);

    public abstract void EmitMemoryPrimitives(StringBuilder functions);

    public static LlvmRuntimePlatform Create(CompilationTarget target)
    {
        return target switch
        {
            CompilationTarget.WindowsX64 => new WindowsLlvmRuntimePlatform(),
            CompilationTarget.LinuxX64 => new LinuxLlvmRuntimePlatform(),
            CompilationTarget.Wasm32Browser => new WasmBrowserLlvmRuntimePlatform(),
            _ => throw new SmallLangException($"unsupported target '{target}'")
        };
    }
}
