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

    public bool UsesAsyncFile { get; set; }

    public virtual void EmitAsyncPrimitives(StringBuilder functions)
    {
        if (UsesAsyncFile)
        {
            functions.AppendLine("""
            define internal void @smalllang_file_push_request(ptr %control) #0 {
            entry:
              %next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 16
              br label %push

            push:
              %head = load atomic ptr, ptr @smalllang_file_request_head acquire, align 8
              store ptr %head, ptr %next_slot, align 8
              %exchange = cmpxchg ptr @smalllang_file_request_head, ptr %head, ptr %control release monotonic
              %pushed = extractvalue { ptr, i1 } %exchange, 1
              br i1 %pushed, label %signal, label %push

            signal:
              call void @smalllang_platform_file_worker_signal_request()
              ret void
            }

            define internal void @smalllang_file_push_completion(ptr %control) #0 {
            entry:
              %next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 16
              br label %push

            push:
              %head = load atomic ptr, ptr @smalllang_file_completion_head acquire, align 8
              store ptr %head, ptr %next_slot, align 8
              %exchange = cmpxchg ptr @smalllang_file_completion_head, ptr %head, ptr %control release monotonic
              %pushed = extractvalue { ptr, i1 } %exchange, 1
              br i1 %pushed, label %done, label %push

            done:
              ret void
            }

            define internal void @smalllang_file_worker_run() #0 {
            entry:
              br label %wait

            wait:
              call void @smalllang_platform_file_worker_wait_request()
              %stopping_value = load atomic i32, ptr @smalllang_file_worker_stopping acquire, align 4
              %stopping = icmp ne i32 %stopping_value, 0
              br i1 %stopping, label %stopped, label %take_batch

            take_batch:
              %batch = atomicrmw xchg ptr @smalllang_file_request_head, ptr null acq_rel
              br label %reverse

            reverse:
              %current = phi ptr [ %batch, %take_batch ], [ %next, %reverse_one ]
              %reversed = phi ptr [ null, %take_batch ], [ %current, %reverse_one ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %reverse_one, label %process

            reverse_one:
              %current_next_slot = getelementptr %smalllang.task_control, ptr %current, i32 0, i32 16
              %next = load ptr, ptr %current_next_slot, align 8
              store ptr %reversed, ptr %current_next_slot, align 8
              br label %reverse

            process:
              %request = phi ptr [ %reversed, %reverse ], [ %next_request_value, %process_bridge ]
              %has_request = icmp ne ptr %request, null
              br i1 %has_request, label %read, label %signal

            read:
              %request_next_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 16
              %request_next = load ptr, ptr %request_next_slot, align 8
              %phase_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled_before_read = icmp eq i32 %phase, 3
              br i1 %cancelled_before_read, label %record_cancelled, label %perform_read

            perform_read:
              %size_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 11
              %size = load i32, ptr %size_slot, align 4
              %size64 = zext i32 %size to i64
              %data_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 13
              %explicit_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 19
              %explicit = load i32, ptr %explicit_slot, align 4
              %is_explicit = icmp ne i32 %explicit, 0
              br i1 %is_explicit, label %inspect_owned_operation, label %perform_compatibility_read

            perform_compatibility_read:
              %compatibility_result = call %smalllang.file_count_result @smalllang_platform_read_file_bytes(ptr %data_slot, i64 %size64)
              br label %record_read

            inspect_owned_operation:
              %operation_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 20
              %operation = load i32, ptr %operation_slot, align 4
              %is_write = icmp eq i32 %operation, 1
              br i1 %is_write, label %perform_owned_write, label %perform_owned_read

            perform_owned_read:
              %owned_handle_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 17
              %owned_handle = load i64, ptr %owned_handle_slot, align 8
              %owned_offset_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 18
              %owned_offset = load i64, ptr %owned_offset_slot, align 8
              %owned_result = call %smalllang.file_count_result @smalllang_platform_read_owned_file_at(i64 %owned_handle, ptr %data_slot, i64 %size64, i64 %owned_offset)
              br label %record_read

            perform_owned_write:
              %write_handle_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 17
              %write_handle = load i64, ptr %write_handle_slot, align 8
              %write_offset_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 18
              %write_offset = load i64, ptr %write_offset_slot, align 8
              %write_result = call %smalllang.file_count_result @smalllang_platform_write_owned_file_at(i64 %write_handle, ptr %data_slot, i64 %size64, i64 %write_offset)
              br label %record_read

            record_read:
              %result = phi %smalllang.file_count_result [ %compatibility_result, %perform_compatibility_read ], [ %owned_result, %perform_owned_read ], [ %write_result, %perform_owned_write ]
              %count = extractvalue %smalllang.file_count_result %result, 0
              %ok = extractvalue %smalllang.file_count_result %result, 1
              %count_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 14
              store i64 %count, ptr %count_slot, align 8
              %ok_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 15
              store i32 %ok, ptr %ok_slot, align 4
              %phase_after_read = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled_after_read = icmp eq i32 %phase_after_read, 3
              br i1 %cancelled_after_read, label %queue_completion, label %record_complete

            record_complete:
              store atomic i32 2, ptr %phase_slot release, align 4
              br label %queue_completion

            record_cancelled:
              %cancel_count_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 14
              store i64 0, ptr %cancel_count_slot, align 8
              %cancel_ok_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 15
              store i32 0, ptr %cancel_ok_slot, align 4
              br label %queue_completion

            queue_completion:
              %completion_explicit_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 19
              %completion_explicit = load i32, ptr %completion_explicit_slot, align 4
              %completion_is_explicit = icmp ne i32 %completion_explicit, 0
              br i1 %completion_is_explicit, label %close_owned_request, label %push_completed_request

            close_owned_request:
              %completion_handle_slot = getelementptr %smalllang.task_control, ptr %request, i32 0, i32 17
              %completion_handle = load i64, ptr %completion_handle_slot, align 8
              call void @smalllang_platform_close_owned_file(i64 %completion_handle)
              store i32 0, ptr %completion_explicit_slot, align 4
              br label %push_completed_request

            push_completed_request:
              call void @smalllang_file_push_completion(ptr %request)
              br label %next_request

            next_request:
              br label %process_next

            process_next:
              %next_request_value = phi ptr [ %request_next, %next_request ]
              %next_request_present = icmp ne ptr %next_request_value, null
              br i1 %next_request_present, label %process_bridge, label %signal

            process_bridge:
              br label %process

            signal:
              call void @smalllang_platform_file_worker_signal_completion()
              br label %wait

            stopped:
              ret void
            }

            define internal i1 @smalllang_file_drain_completions() #0 {
            entry:
              %batch = atomicrmw xchg ptr @smalllang_file_completion_head, ptr null acq_rel
              %has_batch = icmp ne ptr %batch, null
              br i1 %has_batch, label %clear_signal, label %none

            clear_signal:
              call void @smalllang_platform_file_worker_clear_completion()
              br label %reverse

            reverse:
              %current = phi ptr [ %batch, %clear_signal ], [ %next, %reverse_one ]
              %reversed = phi ptr [ null, %clear_signal ], [ %current, %reverse_one ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %reverse_one, label %drain

            reverse_one:
              %current_next_slot = getelementptr %smalllang.task_control, ptr %current, i32 0, i32 16
              %next = load ptr, ptr %current_next_slot, align 8
              store ptr %reversed, ptr %current_next_slot, align 8
              br label %reverse

            drain:
              %control = phi ptr [ %reversed, %reverse ], [ %control_next, %continue ]
              %has_control = icmp ne ptr %control, null
              br i1 %has_control, label %inspect, label %done

            inspect:
              %control_next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 16
              %control_next = load ptr, ptr %control_next_slot, align 8
              store ptr null, ptr %control_next_slot, align 8
              %phase_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled = icmp eq i32 %phase, 3
              br i1 %cancelled, label %destroy_cancelled, label %wake

            destroy_cancelled:
              %cancel_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 6
              %cancel = load ptr, ptr %cancel_slot, align 8
              call void %cancel(ptr %control)
              call void @smalllang_free(ptr %control)
              br label %continue

            wake:
              call void @smalllang_task_enqueue_ready(ptr %control)
              br label %continue

            continue:
              %remaining = atomicrmw sub ptr @smalllang_file_outstanding, i64 1 acq_rel
              br label %drain

            done:
              ret i1 true

            none:
              ret i1 false
            }

            define internal void @smalllang_file_submit(ptr %control, i32 %size) #0 {
            entry:
              %started = load i1, ptr @smalllang_file_worker_started, align 1
              br i1 %started, label %initialize, label %start

            start:
              %start_ok = call i1 @smalllang_platform_file_worker_start()
              br i1 %start_ok, label %mark_started, label %failed

            mark_started:
              store i1 true, ptr @smalllang_file_worker_started, align 1
              br label %initialize

            initialize:
              %size_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 11
              store i32 %size, ptr %size_slot, align 4
              %phase_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 12
              store atomic i32 1, ptr %phase_slot release, align 4
              %status_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 4
              store i32 6, ptr %status_slot, align 4
              %old_count = atomicrmw add ptr @smalllang_file_outstanding, i64 1 acq_rel
              call void @smalllang_file_push_request(ptr %control)
              ret void

            failed:
              call void @llvm.trap()
              unreachable
            }

            define internal void @smalllang_file_wait_idle() #0 {
            entry:
              br label %check

            check:
              %outstanding = load atomic i64, ptr @smalllang_file_outstanding acquire, align 8
              %idle = icmp eq i64 %outstanding, 0
              br i1 %idle, label %done, label %wait

            wait:
              call void @smalllang_platform_file_worker_wait_completion(i64 -1)
              %progress = call i1 @smalllang_file_drain_completions()
              br label %check

            done:
              ret void
            }

            define internal void @smalllang_async_shutdown() #0 {
            entry:
              %started = load i1, ptr @smalllang_file_worker_started, align 1
              br i1 %started, label %stop, label %done

            stop:
              call void @smalllang_file_wait_idle()
              store atomic i32 1, ptr @smalllang_file_worker_stopping release, align 4
              call void @smalllang_platform_file_worker_signal_request()
              call void @smalllang_platform_file_worker_join()
              store i1 false, ptr @smalllang_file_worker_started, align 1
              br label %done

            done:
              ret void
            }

            define internal i1 @smalllang_file_read_task_worker(ptr %control) #0 {
            entry:
              %phase_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %initial = icmp eq i32 %phase, 0
              br i1 %initial, label %submit, label %inspect

            submit:
              %size_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 11
              %size = load i32, ptr %size_slot, align 4
              call void @smalllang_file_submit(ptr %control, i32 %size)
              ret i1 false

            inspect:
              %complete = icmp eq i32 %phase, 2
              ret i1 %complete
            }

            define internal void @smalllang_file_read_task_cancel(ptr %control) #0 {
            entry:
              %explicit_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 19
              %explicit = load i32, ptr %explicit_slot, align 4
              %owns_handle = icmp ne i32 %explicit, 0
              br i1 %owns_handle, label %close_handle, label %destroy_context

            close_handle:
              %handle_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 17
              %handle = load i64, ptr %handle_slot, align 8
              call void @smalllang_platform_close_owned_file(i64 %handle)
              store i32 0, ptr %explicit_slot, align 4
              br label %destroy_context

            destroy_context:
              %context_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void @smalllang_free(ptr %context)
              ret void
            }

            """);
        }
        else
        {
            functions.AppendLine("""
            define internal i1 @smalllang_file_drain_completions() #0 {
            entry:
              ret i1 false
            }

            define internal void @smalllang_platform_file_worker_wait_completion(i64 %requested) #0 {
            entry:
              ret void
            }

            define internal void @smalllang_file_wait_idle() #0 {
            entry:
              ret void
            }

            define internal void @smalllang_async_shutdown() #0 {
            entry:
              ret void
            }

            """);
        }

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
              %file_progress = call i1 @smalllang_file_drain_completions()
              call void @smalllang_timer_wake_due()
              %ready = load ptr, ptr @smalllang_task_ready_head, align 8
              %has_ready = icmp ne ptr %ready, null
              br i1 %has_ready, label %progress, label %inspect_timer

            inspect_timer:
              %timer = load ptr, ptr @smalllang_task_timer_head, align 8
              %has_timer = icmp ne ptr %timer, null
              %file_outstanding = load atomic i64, ptr @smalllang_file_outstanding acquire, align 8
              %has_file = icmp ne i64 %file_outstanding, 0
              br i1 %has_timer, label %timer_timeout, label %inspect_file

            inspect_file:
              br i1 %has_file, label %wait_file_only, label %stalled

            timer_timeout:
              %deadline_slot = getelementptr %smalllang.task_control, ptr %timer, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @smalllang_now_millis()
              %remaining_raw = sub i64 %deadline, %now
              %positive = icmp sgt i64 %remaining_raw, 0
              %remaining = select i1 %positive, i64 %remaining_raw, i64 0
              br i1 %has_file, label %wait_file_or_timer, label %wait_timer_only

            wait_file_or_timer:
              call void @smalllang_platform_file_worker_wait_completion(i64 %remaining)
              %file_timer_progress = call i1 @smalllang_file_drain_completions()
              call void @smalllang_timer_wake_due()
              br label %progress

            wait_file_only:
              call void @smalllang_platform_file_worker_wait_completion(i64 -1)
              %file_only_progress = call i1 @smalllang_file_drain_completions()
              br label %progress

            wait_timer_only:
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
              %control = call ptr @smalllang_alloc(i64 152)
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
              %file_size_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 11
              store i32 0, ptr %file_size_slot, align 4
              %file_phase_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 12
              store i32 0, ptr %file_phase_slot, align 4
              %file_data_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 13
              store i64 0, ptr %file_data_slot, align 8
              %file_count_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 14
              store i64 0, ptr %file_count_slot, align 8
              %file_ok_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 15
              store i32 0, ptr %file_ok_slot, align 4
              %file_next_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 16
              store ptr null, ptr %file_next_slot, align 8
              %file_handle_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 17
              store i64 -1, ptr %file_handle_slot, align 8
              %file_offset_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 18
              store i64 0, ptr %file_offset_slot, align 8
              %file_explicit_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 19
              store i32 0, ptr %file_explicit_slot, align 4
              %file_operation_slot = getelementptr %smalllang.task_control, ptr %control, i32 0, i32 20
              store i32 0, ptr %file_operation_slot, align 4
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
              br i1 %waiting_child, label %detach_child, label %check_file_waiting

            check_file_waiting:
              %waiting_file = icmp eq i32 %status, 6
              br i1 %waiting_file, label %cancel_file, label %fail

            cancel_file:
              %file_phase_slot = getelementptr %smalllang.task_control, ptr %handle, i32 0, i32 12
              store atomic i32 3, ptr %file_phase_slot release, align 4
              store i32 3, ptr %status_slot, align 4
              ret i1 true

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
