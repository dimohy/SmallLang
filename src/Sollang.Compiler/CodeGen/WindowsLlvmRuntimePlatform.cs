using System.Text;

namespace Sollang.Compiler.CodeGen;

internal sealed class WindowsLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "x86_64-pc-windows-msvc";

    public override string EntryPointName => "sollang_start";

    public override bool SupportsAsync => true;

    public override bool SupportsComputePool => true;

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@sollang_file_writer = internal global ptr null");
        globals.AppendLine("@sollang_file_reader = internal global ptr null");
        globals.AppendLine("@sollang_argument_count_value = internal global i64 0");
        globals.AppendLine("@sollang_argument_records = internal global ptr null");
        globals.AppendLine("@sollang_process_output_override = internal global ptr null");
        globals.AppendLine("@sollang_environment_allocations = internal global ptr null");
        globals.AppendLine("@sollang_environment_empty = internal constant [1 x i8] zeroinitializer, align 1");
        globals.AppendLine("@sollang_stdout_buffer = internal global [1048576 x i8] zeroinitializer, align 16");
        globals.AppendLine("@sollang_stdout_buffer_count = internal global i64 0");
        globals.AppendLine("@sollang_stdout_line_buffered = internal global i1 false");
        if (UsesAsyncFile)
        {
            globals.AppendLine("@sollang_file_request_event = internal global ptr null");
            globals.AppendLine("@sollang_file_completion_event = internal global ptr null");
            globals.AppendLine("@sollang_file_worker_handle = internal global ptr null");
        }
        if (UsesComputePool)
        {
            globals.AppendLine("@sollang_compute_semaphore = internal global ptr null");
            globals.AppendLine("@sollang_compute_completion_event = internal global ptr null");
            globals.AppendLine("@sollang_compute_worker_count = internal global i32 0");
            globals.AppendLine("@sollang_compute_worker_limit = internal global i32 0");
            globals.AppendLine("@sollang_compute_worker_handles = internal global [64 x ptr] zeroinitializer, align 8");
        }
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        if (UsesProcessExit)
        {
            functions.AppendLine("declare dllimport void @ExitProcess(i32)");
        }
        functions.AppendLine("declare dllimport ptr @GetStdHandle(i32)");
        functions.AppendLine("declare dllimport i32 @GetConsoleMode(ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @ReadFile(ptr, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @GetOverlappedResult(ptr, ptr, ptr, i32)");
        functions.AppendLine("declare dllimport ptr @CreateFileA(ptr, i32, i32, ptr, i32, i32, ptr)");
        functions.AppendLine("declare dllimport i32 @CloseHandle(ptr)");
        functions.AppendLine("declare dllimport ptr @GetCurrentProcess()");
        functions.AppendLine("declare dllimport i32 @DuplicateHandle(ptr, ptr, ptr, ptr, i32, i32, i32)");
        functions.AppendLine("declare dllimport i32 @SetFilePointerEx(ptr, i64, ptr, i32)");
        functions.AppendLine("declare dllimport i32 @GetFileSizeEx(ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @SetEndOfFile(ptr)");
        functions.AppendLine("declare dllimport ptr @CreateFileMappingA(ptr, ptr, i32, i32, i32, ptr)");
        functions.AppendLine("declare dllimport ptr @MapViewOfFile(ptr, i32, i32, i32, i64)");
        functions.AppendLine("declare dllimport i32 @UnmapViewOfFile(ptr)");
        functions.AppendLine("declare dllimport i32 @FlushViewOfFile(ptr, i64)");
        functions.AppendLine("declare dllimport i32 @FlushFileBuffers(ptr)");
        functions.AppendLine("declare dllimport i32 @MoveFileExA(ptr, ptr, i32)");
        functions.AppendLine("declare dllimport ptr @GetProcessHeap()");
        functions.AppendLine("declare dllimport ptr @HeapAlloc(ptr, i32, i64)");
        functions.AppendLine("declare dllimport i32 @HeapFree(ptr, i32, ptr)");
        functions.AppendLine("declare dllimport i64 @GetTickCount64()");
        functions.AppendLine("declare dllimport void @Sleep(i32)");
        functions.AppendLine("declare dllimport ptr @GetCommandLineW()");
        functions.AppendLine("declare dllimport ptr @CommandLineToArgvW(ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @WideCharToMultiByte(i32, i32, ptr, i32, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport ptr @LocalFree(ptr)");
        functions.AppendLine("declare dllimport i32 @MultiByteToWideChar(i32, i32, ptr, i32, ptr, i32)");
        if (UsesProcessRuntime)
        {
            functions.AppendLine("declare dllimport i32 @CreateProcessW(ptr, ptr, ptr, ptr, i32, i32, ptr, ptr, ptr, ptr)");
            functions.AppendLine("declare dllimport i32 @GetExitCodeProcess(ptr, ptr)");
        }
        if (UsesProcessRuntime || UsesAsyncFile || UsesComputePool)
        {
            functions.AppendLine("declare dllimport i32 @WaitForSingleObject(ptr, i32)");
        }
        functions.AppendLine("declare dllimport i32 @GetEnvironmentVariableW(ptr, ptr, i32)");
        functions.AppendLine("declare dllimport i32 @GetLastError()");
        functions.AppendLine("declare dllimport void @SetLastError(i32)");
        if (UsesDirectoryTraversal)
        {
            functions.AppendLine("declare dllimport ptr @FindFirstFileA(ptr, ptr)");
            functions.AppendLine("declare dllimport i32 @FindNextFileA(ptr, ptr)");
            functions.AppendLine("declare dllimport i32 @FindClose(ptr)");
            functions.AppendLine("declare dllimport ptr @CreateFileW(ptr, i32, i32, ptr, i32, i32, ptr)");
            functions.AppendLine("declare dllimport i32 @GetFinalPathNameByHandleW(ptr, ptr, i32, i32)");
            functions.AppendLine("declare dllimport i32 @GetFileInformationByHandle(ptr, ptr)");
        }
        if (UsesAsyncFile || UsesComputePool)
        {
            functions.AppendLine("declare dllimport ptr @CreateThread(ptr, i64, ptr, ptr, i32, ptr)");
            functions.AppendLine("declare dllimport ptr @CreateEventA(ptr, i32, i32, ptr)");
        }
        if (UsesAsyncFile || UsesComputePool)
        {
            functions.AppendLine("declare dllimport i32 @SetEvent(ptr)");
        }
        if (UsesComputePool)
        {
            functions.AppendLine("declare dllimport ptr @CreateSemaphoreA(ptr, i32, i32, ptr)");
            functions.AppendLine("declare dllimport i32 @ReleaseSemaphore(ptr, i32, ptr)");
            functions.AppendLine("declare dllimport i32 @ResetEvent(ptr)");
            functions.AppendLine("declare dllimport i32 @GetActiveProcessorCount(i16)");
        }
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
    }

    public override void EmitAsyncPrimitives(StringBuilder functions)
    {
        base.EmitAsyncPrimitives(functions);
        if (!UsesAsyncFile)
        {
            return;
        }
        functions.AppendLine("""
            define internal i32 @sollang_windows_file_worker(ptr %unused) #0 {
            entry:
              call void @sollang_file_worker_run()
              ret i32 0
            }

            define internal i1 @sollang_platform_file_worker_start() #0 {
            entry:
              %request_event = call ptr @CreateEventA(ptr null, i32 0, i32 0, ptr null)
              %request_ok = icmp ne ptr %request_event, null
              br i1 %request_ok, label %completion, label %fail

            completion:
              store ptr %request_event, ptr @sollang_file_request_event, align 8
              %completion_event = call ptr @CreateEventA(ptr null, i32 0, i32 0, ptr null)
              %completion_ok = icmp ne ptr %completion_event, null
              br i1 %completion_ok, label %thread, label %fail

            thread:
              store ptr %completion_event, ptr @sollang_file_completion_event, align 8
              %worker = call ptr @CreateThread(ptr null, i64 0, ptr @sollang_windows_file_worker, ptr null, i32 0, ptr null)
              %worker_ok = icmp ne ptr %worker, null
              br i1 %worker_ok, label %ready, label %fail

            ready:
              store ptr %worker, ptr @sollang_file_worker_handle, align 8
              ret i1 true

            fail:
              ret i1 false
            }

            define internal void @sollang_platform_file_worker_signal_request() #0 {
            entry:
              %event = load ptr, ptr @sollang_file_request_event, align 8
              %ignored = call i32 @SetEvent(ptr %event)
              ret void
            }

            define internal void @sollang_platform_file_worker_wait_request() #0 {
            entry:
              %event = load ptr, ptr @sollang_file_request_event, align 8
              %ignored = call i32 @WaitForSingleObject(ptr %event, i32 -1)
              ret void
            }

            define internal void @sollang_platform_file_worker_signal_completion() #0 {
            entry:
              %event = load ptr, ptr @sollang_file_completion_event, align 8
              %ignored = call i32 @SetEvent(ptr %event)
              ret void
            }

            define internal void @sollang_platform_file_worker_clear_completion() #0 {
            entry:
              ret void
            }

            define internal void @sollang_platform_file_worker_wait_completion(i64 %requested) #0 {
            entry:
              %infinite = icmp slt i64 %requested, 0
              %too_large = icmp ugt i64 %requested, 4294967294
              %bounded = select i1 %too_large, i64 4294967294, i64 %requested
              %finite = trunc i64 %bounded to i32
              %timeout = select i1 %infinite, i32 -1, i32 %finite
              %event = load ptr, ptr @sollang_file_completion_event, align 8
              %ignored = call i32 @WaitForSingleObject(ptr %event, i32 %timeout)
              ret void
            }

            define internal void @sollang_platform_file_worker_join() #0 {
            entry:
              %worker = load ptr, ptr @sollang_file_worker_handle, align 8
              %waited = call i32 @WaitForSingleObject(ptr %worker, i32 -1)
              %closed_worker = call i32 @CloseHandle(ptr %worker)
              %request_event = load ptr, ptr @sollang_file_request_event, align 8
              %closed_request = call i32 @CloseHandle(ptr %request_event)
              %completion_event = load ptr, ptr @sollang_file_completion_event, align 8
              %closed_completion = call i32 @CloseHandle(ptr %completion_event)
              store ptr null, ptr @sollang_file_worker_handle, align 8
              store ptr null, ptr @sollang_file_request_event, align 8
              store ptr null, ptr @sollang_file_completion_event, align 8
              ret void
            }

            """);

    }

    public override void EmitComputePrimitives(StringBuilder functions)
    {
        if (!UsesComputePool)
        {
            return;
        }

        functions.AppendLine("""
            define internal i32 @sollang_windows_compute_worker(ptr %unused) #0 {
            entry:
              br label %wait

            wait:
              %semaphore = load ptr, ptr @sollang_compute_semaphore, align 8
              %waited = call i32 @WaitForSingleObject(ptr %semaphore, i32 -1)
              %stopping_value = load atomic i32, ptr @sollang_compute_stopping acquire, align 4
              %stopping = icmp ne i32 %stopping_value, 0
              br i1 %stopping, label %stopped, label %take

            take:
              %group = load atomic ptr, ptr @sollang_compute_group_current acquire, align 8
              %count_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 3
              %count = load i64, ptr %count_slot, align 8
              br label %claim

            claim:
              %index = atomicrmw add ptr @sollang_compute_next, i64 1 acq_rel
              %failure_limit_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 11
              %failure_limit = load atomic i64, ptr %failure_limit_slot acquire, align 8
              %within_count = icmp ult i64 %index, %count
              %before_failure = icmp ult i64 %index, %failure_limit
              %has_work = and i1 %within_count, %before_failure
              br i1 %has_work, label %work, label %complete

            work:
              %callback_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 0
              %callback = load ptr, ptr %callback_slot, align 8
              %running_before = atomicrmw add ptr @sollang_compute_running, i32 1 acq_rel
              %running_now = add i32 %running_before, 1
              %peak_before = atomicrmw max ptr @sollang_compute_peak, i32 %running_now acq_rel
              call void %callback(ptr %group, i64 %index)
              %running_after = atomicrmw sub ptr @sollang_compute_running, i32 1 acq_rel
              br label %claim

            complete:
              %previous = atomicrmw sub ptr @sollang_compute_active, i32 1 acq_rel
              %last = icmp eq i32 %previous, 1
              br i1 %last, label %signal, label %barrier

            barrier:
              %barrier_event = load ptr, ptr @sollang_compute_completion_event, align 8
              %barrier_waited = call i32 @WaitForSingleObject(ptr %barrier_event, i32 -1)
              %departed_before = atomicrmw add ptr @sollang_compute_barrier_departed, i32 1 acq_rel
              br label %wait

            signal:
              %event = load ptr, ptr @sollang_compute_completion_event, align 8
              %signalled = call i32 @SetEvent(ptr %event)
              br label %wait

            stopped:
              ret i32 0
            }

            define internal i1 @sollang_compute_start() #0 {
            entry:
              %existing = load i32, ptr @sollang_compute_worker_count, align 4
              %already = icmp sgt i32 %existing, 0
              br i1 %already, label %ready, label %create_sync

            create_sync:
              %semaphore = call ptr @CreateSemaphoreA(ptr null, i32 0, i32 64, ptr null)
              %semaphore_ok = icmp ne ptr %semaphore, null
              br i1 %semaphore_ok, label %create_event, label %fail

            create_event:
              store ptr %semaphore, ptr @sollang_compute_semaphore, align 8
              %event = call ptr @CreateEventA(ptr null, i32 1, i32 0, ptr null)
              %event_ok = icmp ne ptr %event, null
              br i1 %event_ok, label %count, label %fail

            count:
              store ptr %event, ptr @sollang_compute_completion_event, align 8
              %reported = call i32 @GetActiveProcessorCount(i16 -1)
              %positive = icmp sgt i32 %reported, 0
              %at_least_one = select i1 %positive, i32 %reported, i32 1
              %configured = load i32, ptr @sollang_compute_worker_limit, align 4
              %has_configured = icmp sgt i32 %configured, 0
              %selected = select i1 %has_configured, i32 %configured, i32 %at_least_one
              %too_many = icmp sgt i32 %selected, 64
              %bounded = select i1 %too_many, i32 64, i32 %selected
              br label %create_workers

            create_workers:
              %index = phi i32 [ 0, %count ], [ %next, %created ]
              %done = icmp eq i32 %index, %bounded
              br i1 %done, label %publish, label %create_one

            create_one:
              %worker = call ptr @CreateThread(ptr null, i64 0, ptr @sollang_windows_compute_worker, ptr null, i32 0, ptr null)
              %worker_ok = icmp ne ptr %worker, null
              br i1 %worker_ok, label %created, label %publish

            created:
              %slot = getelementptr [64 x ptr], ptr @sollang_compute_worker_handles, i32 0, i32 %index
              store ptr %worker, ptr %slot, align 8
              %next = add i32 %index, 1
              br label %create_workers

            publish:
              %created_count = phi i32 [ %bounded, %create_workers ], [ %index, %create_one ]
              store i32 %created_count, ptr @sollang_compute_worker_count, align 4
              %has_workers = icmp sgt i32 %created_count, 0
              br i1 %has_workers, label %ready, label %fail

            ready:
              ret i1 true

            fail:
              ret i1 false
            }

            define internal void @sollang_compute_execute(ptr %group) #0 {
            entry:
              %started = call i1 @sollang_compute_start()
              br i1 %started, label %submit, label %failed

            submit:
              %count_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 3
              %count = load i64, ptr %count_slot, align 8
              %empty = icmp eq i64 %count, 0
              br i1 %empty, label %cleanup_empty, label %publish

            cleanup_empty:
              %empty_sinks_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 5
              %empty_sinks = load ptr, ptr %empty_sinks_slot, align 8
              call void @sollang_memory_output_sink_array_dispose(ptr %empty_sinks, i64 0)
              br label %done

            publish:
              store atomic i64 0, ptr @sollang_compute_next release, align 8
              store atomic i32 0, ptr @sollang_compute_peak release, align 4
              %workers = load i32, ptr @sollang_compute_worker_count, align 4
              store atomic i32 %workers, ptr @sollang_compute_active release, align 4
              store atomic ptr %group, ptr @sollang_compute_group_current release, align 8
              %event = load ptr, ptr @sollang_compute_completion_event, align 8
              %reset = call i32 @ResetEvent(ptr %event)
              %help_first_index = atomicrmw add ptr @sollang_compute_next, i64 1 acq_rel
              %semaphore = load ptr, ptr @sollang_compute_semaphore, align 8
              %released = call i32 @ReleaseSemaphore(ptr %semaphore, i32 %workers, ptr null)
              br label %help_work

            help_claim:
              %help_claimed_index = atomicrmw add ptr @sollang_compute_next, i64 1 acq_rel
              %help_failure_limit_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 11
              %help_failure_limit = load atomic i64, ptr %help_failure_limit_slot acquire, align 8
              %help_within_count = icmp ult i64 %help_claimed_index, %count
              %help_before_failure = icmp ult i64 %help_claimed_index, %help_failure_limit
              %help_has_work = and i1 %help_within_count, %help_before_failure
              br i1 %help_has_work, label %help_work, label %help_wait

            help_work:
              %help_index = phi i64 [ %help_first_index, %publish ], [ %help_claimed_index, %help_claim ]
              %help_callback_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 0
              %help_callback = load ptr, ptr %help_callback_slot, align 8
              %help_running_before = atomicrmw add ptr @sollang_compute_running, i32 1 acq_rel
              %help_running_now = add i32 %help_running_before, 1
              %help_peak_before = atomicrmw max ptr @sollang_compute_peak, i32 %help_running_now acq_rel
              call void %help_callback(ptr %group, i64 %help_index)
              %help_running_after = atomicrmw sub ptr @sollang_compute_running, i32 1 acq_rel
              br label %help_claim

            help_wait:
              %waited = call i32 @WaitForSingleObject(ptr %event, i32 -1)
              br label %await_departure

            await_departure:
              %departed = load atomic i32, ptr @sollang_compute_barrier_departed acquire, align 4
              %expected_departed = sub i32 %workers, 1
              %all_departed = icmp eq i32 %departed, %expected_departed
              br i1 %all_departed, label %flush_prepare, label %await_departure

            flush_prepare:
              store atomic i32 0, ptr @sollang_compute_barrier_departed release, align 4
              %sinks_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 5
              %sinks = load ptr, ptr %sinks_slot, align 8
              %flush_failure_slot = getelementptr %sollang.compute_group, ptr %group, i32 0, i32 11
              %flush_failure = load atomic i64, ptr %flush_failure_slot acquire, align 8
              %failure_before_end = icmp ult i64 %flush_failure, %count
              %flush_prefix = select i1 %failure_before_end, i64 %flush_failure, i64 %count
              call void @sollang_memory_output_sink_array_flush_prefix(ptr %sinks, i64 %count, i64 %flush_prefix, ptr %group, ptr @sollang_memory_output_sink_write)
              store atomic ptr null, ptr @sollang_compute_group_current release, align 8
              br label %done

            failed:
              call void @llvm.trap()
              unreachable

            done:
              ret void
            }

            define internal i32 @sollang_compute_workers() #0 {
            entry:
              %started = call i1 @sollang_compute_start()
              br i1 %started, label %read, label %failed

            read:
              %workers = load i32, ptr @sollang_compute_worker_count, align 4
              ret i32 %workers

            failed:
              ret i32 0
            }

            define internal i32 @sollang_compute_limit_workers(i32 %requested) #0 {
            entry:
              %existing = load i32, ptr @sollang_compute_worker_count, align 4
              %already_started = icmp sgt i32 %existing, 0
              br i1 %already_started, label %started, label %configure

            configure:
              %positive = icmp sgt i32 %requested, 0
              %at_least_one = select i1 %positive, i32 %requested, i32 1
              %too_many = icmp sgt i32 %at_least_one, 64
              %bounded = select i1 %too_many, i32 64, i32 %at_least_one
              store i32 %bounded, ptr @sollang_compute_worker_limit, align 4
              %started_ok = call i1 @sollang_compute_start()
              br i1 %started_ok, label %read, label %failed

            started:
              ret i32 %existing

            read:
              %workers = load i32, ptr @sollang_compute_worker_count, align 4
              ret i32 %workers

            failed:
              ret i32 0
            }

            define internal i32 @sollang_compute_peak_workers() #0 {
            entry:
              %peak = load atomic i32, ptr @sollang_compute_peak acquire, align 4
              ret i32 %peak
            }

            define internal void @sollang_compute_shutdown() #0 {
            entry:
              %workers = load i32, ptr @sollang_compute_worker_count, align 4
              %started = icmp sgt i32 %workers, 0
              br i1 %started, label %stop, label %done

            stop:
              store atomic i32 1, ptr @sollang_compute_stopping release, align 4
              %semaphore = load ptr, ptr @sollang_compute_semaphore, align 8
              %released = call i32 @ReleaseSemaphore(ptr %semaphore, i32 %workers, ptr null)
              br label %join

            join:
              %index = phi i32 [ 0, %stop ], [ %next, %joined ]
              %all_joined = icmp eq i32 %index, %workers
              br i1 %all_joined, label %cleanup, label %join_one

            join_one:
              %slot = getelementptr [64 x ptr], ptr @sollang_compute_worker_handles, i32 0, i32 %index
              %worker = load ptr, ptr %slot, align 8
              %waited = call i32 @WaitForSingleObject(ptr %worker, i32 -1)
              %closed = call i32 @CloseHandle(ptr %worker)
              store ptr null, ptr %slot, align 8
              br label %joined

            joined:
              %next = add i32 %index, 1
              br label %join

            cleanup:
              %closed_semaphore = call i32 @CloseHandle(ptr %semaphore)
              %event = load ptr, ptr @sollang_compute_completion_event, align 8
              %closed_event = call i32 @CloseHandle(ptr %event)
              store ptr null, ptr @sollang_compute_semaphore, align 8
              store ptr null, ptr @sollang_compute_completion_event, align 8
              store i32 0, ptr @sollang_compute_worker_count, align 4
              br label %done

            done:
              ret void
            }

            """);
    }

    public override void EmitTimePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @sollang_now_millis() #0 {
            entry:
              %millis = call i64 @GetTickCount64()
              ret i64 %millis
            }

            define internal void @sollang_wait_millis(i64 %requested) #0 {
            entry:
              %positive = icmp sgt i64 %requested, 0
              br i1 %positive, label %clamp, label %done

            clamp:
              %too_large = icmp ugt i64 %requested, 4294967294
              %bounded = select i1 %too_large, i64 4294967294, i64 %requested
              %millis = trunc i64 %bounded to i32
              call void @Sleep(i32 %millis)
              br label %done

            done:
              ret void
            }

            """);
    }

    public override void EmitEnvironmentPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i1 @sollang_track_environment_allocation(ptr %allocation) #0 {
            entry:
              %node = call ptr @sollang_alloc(i64 16)
              %ok = icmp ne ptr %node, null
              br i1 %ok, label %store, label %fail

            store:
              store ptr %allocation, ptr %node, align 8
              %next_slot = getelementptr i8, ptr %node, i64 8
              %head = load ptr, ptr @sollang_environment_allocations, align 8
              store ptr %head, ptr %next_slot, align 8
              store ptr %node, ptr @sollang_environment_allocations, align 8
              ret i1 true

            fail:
              ret i1 false
            }

            define internal %sollang.environment_result @sollang_environment(ptr %name, i64 %name_len) #0 {
            entry:
              %empty = icmp eq i64 %name_len, 0
              br i1 %empty, label %missing, label %validate

            validate:
              %vi = phi i64 [ 0, %entry ], [ %vnext, %valid_byte ]
              %validated = icmp eq i64 %vi, %name_len
              br i1 %validated, label %key_size, label %check_byte

            check_byte:
              %name_byte_ptr = getelementptr i8, ptr %name, i64 %vi
              %name_byte = load i8, ptr %name_byte_ptr, align 1
              %name_byte_valid = icmp ne i8 %name_byte, 0
              br i1 %name_byte_valid, label %valid_byte, label %error

            valid_byte:
              %vnext = add i64 %vi, 1
              br label %validate

            key_size:
              %name_len32 = trunc i64 %name_len to i32
              %wide_chars = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %name, i32 %name_len32, ptr null, i32 0)
              %wide_valid = icmp sgt i32 %wide_chars, 0
              br i1 %wide_valid, label %key_alloc, label %error

            key_alloc:
              %wide_chars_plus_null = add i32 %wide_chars, 1
              %wide_chars64 = zext i32 %wide_chars_plus_null to i64
              %key_bytes = mul i64 %wide_chars64, 2
              %wide_key = call ptr @sollang_alloc(i64 %key_bytes)
              %key_ok = icmp ne ptr %wide_key, null
              br i1 %key_ok, label %key_convert, label %error

            key_convert:
              %key_written = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %name, i32 %name_len32, ptr %wide_key, i32 %wide_chars)
              %key_converted = icmp eq i32 %key_written, %wide_chars
              br i1 %key_converted, label %key_terminate, label %free_key_error

            key_terminate:
              %key_end = getelementptr i16, ptr %wide_key, i32 %wide_chars
              store i16 0, ptr %key_end, align 2
              call void @SetLastError(i32 0)
              %required = call i32 @GetEnvironmentVariableW(ptr %wide_key, ptr null, i32 0)
              %last_error = call i32 @GetLastError()
              %has_size = icmp ne i32 %required, 0
              br i1 %has_size, label %value_alloc, label %zero_result

            zero_result:
              call void @sollang_free(ptr %wide_key)
              %not_found = icmp eq i32 %last_error, 203
              br i1 %not_found, label %missing, label %empty_present

            empty_present:
              %empty_ptr = getelementptr inbounds [1 x i8], ptr @sollang_environment_empty, i64 0, i64 0
              %e0 = insertvalue %sollang.environment_result zeroinitializer, ptr %empty_ptr, 0
              %e1 = insertvalue %sollang.environment_result %e0, i1 true, 2
              %e2 = insertvalue %sollang.environment_result %e1, i1 true, 3
              ret %sollang.environment_result %e2

            value_alloc:
              %required64 = zext i32 %required to i64
              %wide_bytes = mul i64 %required64, 2
              %wide_value = call ptr @sollang_alloc(i64 %wide_bytes)
              %wide_value_ok = icmp ne ptr %wide_value, null
              br i1 %wide_value_ok, label %value_read, label %free_key_error

            value_read:
              %value_chars = call i32 @GetEnvironmentVariableW(ptr %wide_key, ptr %wide_value, i32 %required)
              call void @sollang_free(ptr %wide_key)
              %value_empty = icmp eq i32 %value_chars, 0
              br i1 %value_empty, label %free_wide_empty, label %value_read_check

            free_wide_empty:
              call void @sollang_free(ptr %wide_value)
              br label %empty_present

            value_read_check:
              %value_read_ok = icmp ult i32 %value_chars, %required
              br i1 %value_read_ok, label %utf8_size, label %free_wide_error

            utf8_size:
              %utf8_bytes32 = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide_value, i32 %value_chars, ptr null, i32 0, ptr null, ptr null)
              %utf8_size_ok = icmp sgt i32 %utf8_bytes32, 0
              br i1 %utf8_size_ok, label %utf8_alloc, label %free_wide_error

            utf8_alloc:
              %utf8_bytes = zext i32 %utf8_bytes32 to i64
              %utf8 = call ptr @sollang_alloc(i64 %utf8_bytes)
              %utf8_ok = icmp ne ptr %utf8, null
              br i1 %utf8_ok, label %utf8_convert, label %free_wide_error

            utf8_convert:
              %utf8_written = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide_value, i32 %value_chars, ptr %utf8, i32 %utf8_bytes32, ptr null, ptr null)
              call void @sollang_free(ptr %wide_value)
              %utf8_converted = icmp eq i32 %utf8_written, %utf8_bytes32
              br i1 %utf8_converted, label %track, label %free_utf8_error

            track:
              %tracked = call i1 @sollang_track_environment_allocation(ptr %utf8)
              br i1 %tracked, label %present, label %free_utf8_error

            present:
              %p0 = insertvalue %sollang.environment_result poison, ptr %utf8, 0
              %p1 = insertvalue %sollang.environment_result %p0, i64 %utf8_bytes, 1
              %p2 = insertvalue %sollang.environment_result %p1, i1 true, 2
              %p3 = insertvalue %sollang.environment_result %p2, i1 true, 3
              ret %sollang.environment_result %p3

            free_utf8_error:
              call void @sollang_free(ptr %utf8)
              br label %error

            free_wide_error:
              call void @sollang_free(ptr %wide_value)
              br label %error

            free_key_error:
              call void @sollang_free(ptr %wide_key)
              br label %error

            missing:
              %m0 = insertvalue %sollang.environment_result zeroinitializer, i1 true, 3
              ret %sollang.environment_result %m0

            error:
              ret %sollang.environment_result zeroinitializer
            }

            define internal void @sollang_dispose_environment() #0 {
            entry:
              %head = load ptr, ptr @sollang_environment_allocations, align 8
              br label %loop

            loop:
              %node = phi ptr [ %head, %entry ], [ %next, %free_node ]
              %done = icmp eq ptr %node, null
              br i1 %done, label %finish, label %free_node

            free_node:
              %allocation = load ptr, ptr %node, align 8
              %next_slot = getelementptr i8, ptr %node, i64 8
              %next = load ptr, ptr %next_slot, align 8
              call void @sollang_free(ptr %allocation)
              call void @sollang_free(ptr %node)
              br label %loop

            finish:
              ret void
            }

            """);
    }

    public override void EmitMemoryPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal ptr @sollang_alloc(i64 %bytes) #0 {
            entry:
              %heap = call ptr @GetProcessHeap()
              %ptr = call ptr @HeapAlloc(ptr %heap, i32 0, i64 %bytes)
              ret ptr %ptr
            }

            define internal void @sollang_free(ptr %ptr) #0 {
            entry:
              %is_null = icmp eq ptr %ptr, null
              br i1 %is_null, label %done, label %free

            free:
              %heap = call ptr @GetProcessHeap()
              %ignored = call i32 @HeapFree(ptr %heap, i32 0, ptr %ptr)
              br label %done

            done:
              ret void
            }

            """);
    }

    public override void EmitProcessPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_init_arguments() #0 {
            entry:
              %argc_slot = alloca i32, align 4
              %command = call ptr @GetCommandLineW()
              %wide_argv = call ptr @CommandLineToArgvW(ptr %command, ptr %argc_slot)
              %argv_ok = icmp ne ptr %wide_argv, null
              br i1 %argv_ok, label %allocate, label %fail

            allocate:
              %argc32 = load i32, ptr %argc_slot, align 4
              %argc = zext i32 %argc32 to i64
              %record_bytes = mul i64 %argc, 16
              %records = call ptr @sollang_alloc(i64 %record_bytes)
              %records_ok = icmp ne ptr %records, null
              br i1 %records_ok, label %loop, label %free_wide_fail

            loop:
              %i = phi i64 [ 0, %allocate ], [ %next, %stored ]
              %done = icmp eq i64 %i, %argc
              br i1 %done, label %success, label %convert_size

            convert_size:
              %wide_slot = getelementptr ptr, ptr %wide_argv, i64 %i
              %wide = load ptr, ptr %wide_slot, align 8
              %bytes32 = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide, i32 -1, ptr null, i32 0, ptr null, ptr null)
              %bytes_valid = icmp sgt i32 %bytes32, 0
              br i1 %bytes_valid, label %convert_alloc, label %cleanup_partial

            convert_alloc:
              %bytes = zext i32 %bytes32 to i64
              %utf8 = call ptr @sollang_alloc(i64 %bytes)
              %utf8_ok = icmp ne ptr %utf8, null
              br i1 %utf8_ok, label %convert, label %cleanup_partial

            convert:
              %written = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide, i32 -1, ptr %utf8, i32 %bytes32, ptr null, ptr null)
              %converted = icmp eq i32 %written, %bytes32
              br i1 %converted, label %stored, label %free_current

            free_current:
              call void @sollang_free(ptr %utf8)
              br label %cleanup_partial

            stored:
              %record = getelementptr %sollang.text, ptr %records, i64 %i
              %ptr_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 0
              store ptr %utf8, ptr %ptr_slot, align 8
              %len_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 1
              %length = sub i64 %bytes, 1
              store i64 %length, ptr %len_slot, align 8
              %next = add i64 %i, 1
              br label %loop

            cleanup_partial:
              %j = phi i64 [ %i, %convert_size ], [ %i, %convert_alloc ], [ %i, %free_current ], [ %prev, %cleanup_free ]
              %cleanup_done = icmp eq i64 %j, 0
              br i1 %cleanup_done, label %free_records_fail, label %cleanup_free

            cleanup_free:
              %prev = sub i64 %j, 1
              %old_record = getelementptr %sollang.text, ptr %records, i64 %prev
              %old_ptr_slot = getelementptr inbounds %sollang.text, ptr %old_record, i32 0, i32 0
              %old_ptr = load ptr, ptr %old_ptr_slot, align 8
              call void @sollang_free(ptr %old_ptr)
              br label %cleanup_partial

            free_records_fail:
              call void @sollang_free(ptr %records)
              br label %free_wide_fail

            success:
              store i64 %argc, ptr @sollang_argument_count_value, align 8
              store ptr %records, ptr @sollang_argument_records, align 8
              %ignored_wide = call ptr @LocalFree(ptr %wide_argv)
              ret i32 1

            free_wide_fail:
              %ignored_fail = call ptr @LocalFree(ptr %wide_argv)
              br label %fail

            fail:
              ret i32 0
            }

            define internal i64 @sollang_argument_count() #0 {
            entry:
              %count = load i64, ptr @sollang_argument_count_value, align 8
              ret i64 %count
            }

            define internal %sollang.text @sollang_argument(i64 %index) #0 {
            entry:
              %records = load ptr, ptr @sollang_argument_records, align 8
              %record = getelementptr %sollang.text, ptr %records, i64 %index
              %value = load %sollang.text, ptr %record, align 8
              ret %sollang.text %value
            }

            define internal ptr @sollang_quote_windows_arg(ptr %src, i32 %chars) #0 {
            entry:
              %chars64 = zext i32 %chars to i64
              %double = mul i64 %chars64, 2
              %capacity = add i64 %double, 3
              %bytes = mul i64 %capacity, 2
              %out = call ptr @sollang_alloc(i64 %bytes)
              %out_ok = icmp ne ptr %out, null
              br i1 %out_ok, label %init, label %fail

            init:
              store i16 34, ptr %out, align 2
              %i_slot = alloca i32, align 4
              %o_slot = alloca i64, align 8
              %slashes_slot = alloca i64, align 8
              store i32 0, ptr %i_slot, align 4
              store i64 1, ptr %o_slot, align 8
              store i64 0, ptr %slashes_slot, align 8
              br label %loop

            loop:
              %i = load i32, ptr %i_slot, align 4
              %done = icmp eq i32 %i, %chars
              br i1 %done, label %finish_prepare, label %read

            read:
              %char_ptr = getelementptr i16, ptr %src, i32 %i
              %char = load i16, ptr %char_ptr, align 2
              %is_slash = icmp eq i16 %char, 92
              br i1 %is_slash, label %remember_slash, label %flush_prepare

            remember_slash:
              %slashes = load i64, ptr %slashes_slot, align 8
              %more_slashes = add i64 %slashes, 1
              store i64 %more_slashes, ptr %slashes_slot, align 8
              %slash_next_i = add i32 %i, 1
              store i32 %slash_next_i, ptr %i_slot, align 4
              br label %loop

            flush_prepare:
              %pending = load i64, ptr %slashes_slot, align 8
              %is_quote = icmp eq i16 %char, 34
              %doubled_pending = mul i64 %pending, 2
              %quote_escape = add i64 %doubled_pending, 1
              %flush_count = select i1 %is_quote, i64 %quote_escape, i64 %pending
              br label %flush_loop

            flush_loop:
              %flush_i = phi i64 [ 0, %flush_prepare ], [ %flush_next, %flush_body ]
              %flush_done = icmp eq i64 %flush_i, %flush_count
              br i1 %flush_done, label %write_char, label %flush_body

            flush_body:
              %flush_o = load i64, ptr %o_slot, align 8
              %flush_ptr = getelementptr i16, ptr %out, i64 %flush_o
              store i16 92, ptr %flush_ptr, align 2
              %flush_o_next = add i64 %flush_o, 1
              store i64 %flush_o_next, ptr %o_slot, align 8
              %flush_next = add i64 %flush_i, 1
              br label %flush_loop

            write_char:
              %o = load i64, ptr %o_slot, align 8
              %out_char = getelementptr i16, ptr %out, i64 %o
              store i16 %char, ptr %out_char, align 2
              %o_next = add i64 %o, 1
              store i64 %o_next, ptr %o_slot, align 8
              store i64 0, ptr %slashes_slot, align 8
              %next_i = add i32 %i, 1
              store i32 %next_i, ptr %i_slot, align 4
              br label %loop

            finish_prepare:
              %trailing = load i64, ptr %slashes_slot, align 8
              %finish_count = mul i64 %trailing, 2
              br label %finish_loop

            finish_loop:
              %finish_i = phi i64 [ 0, %finish_prepare ], [ %finish_next, %finish_body ]
              %finish_done = icmp eq i64 %finish_i, %finish_count
              br i1 %finish_done, label %close_quote, label %finish_body

            finish_body:
              %finish_o = load i64, ptr %o_slot, align 8
              %finish_ptr = getelementptr i16, ptr %out, i64 %finish_o
              store i16 92, ptr %finish_ptr, align 2
              %finish_o_next = add i64 %finish_o, 1
              store i64 %finish_o_next, ptr %o_slot, align 8
              %finish_next = add i64 %finish_i, 1
              br label %finish_loop

            close_quote:
              %close_o = load i64, ptr %o_slot, align 8
              %quote_ptr = getelementptr i16, ptr %out, i64 %close_o
              store i16 34, ptr %quote_ptr, align 2
              %null_i = add i64 %close_o, 1
              %null_ptr = getelementptr i16, ptr %out, i64 %null_i
              store i16 0, ptr %null_ptr, align 2
              ret ptr %out

            fail:
              ret ptr null
            }

            define internal ptr @sollang_join_windows_args(ptr %argv, i64 %count) #0 {
            entry:
              %i_slot = alloca i64, align 8
              %total_slot = alloca i64, align 8
              store i64 0, ptr %i_slot, align 8
              store i64 0, ptr %total_slot, align 8
              br label %measure_outer

            measure_outer:
              %i = load i64, ptr %i_slot, align 8
              %outer_done = icmp eq i64 %i, %count
              br i1 %outer_done, label %allocate, label %measure_inner

            measure_inner:
              %j = phi i64 [ 0, %measure_outer ], [ %j_next, %measure_more ]
              %arg_slot = getelementptr ptr, ptr %argv, i64 %i
              %arg = load ptr, ptr %arg_slot, align 8
              %char_ptr = getelementptr i16, ptr %arg, i64 %j
              %char = load i16, ptr %char_ptr, align 2
              %arg_done = icmp eq i16 %char, 0
              br i1 %arg_done, label %measure_arg_done, label %measure_more

            measure_more:
              %j_next = add i64 %j, 1
              br label %measure_inner

            measure_arg_done:
              %total = load i64, ptr %total_slot, align 8
              %needs_space = icmp ne i64 %i, 0
              %space = zext i1 %needs_space to i64
              %with_arg = add i64 %total, %j
              %next_total = add i64 %with_arg, %space
              store i64 %next_total, ptr %total_slot, align 8
              %i_next = add i64 %i, 1
              store i64 %i_next, ptr %i_slot, align 8
              br label %measure_outer

            allocate:
              %total_chars = load i64, ptr %total_slot, align 8
              %chars_with_null = add i64 %total_chars, 1
              %bytes = mul i64 %chars_with_null, 2
              %command = call ptr @sollang_alloc(i64 %bytes)
              %allocated = icmp ne ptr %command, null
              br i1 %allocated, label %copy_setup, label %fail

            copy_setup:
              store i64 0, ptr %i_slot, align 8
              store i64 0, ptr %total_slot, align 8
              br label %copy_outer

            copy_outer:
              %copy_i = load i64, ptr %i_slot, align 8
              %copy_done = icmp eq i64 %copy_i, %count
              br i1 %copy_done, label %terminate, label %copy_space

            copy_space:
              %copy_offset = load i64, ptr %total_slot, align 8
              %copy_needs_space = icmp ne i64 %copy_i, 0
              br i1 %copy_needs_space, label %store_space, label %copy_inner

            store_space:
              %space_ptr = getelementptr i16, ptr %command, i64 %copy_offset
              store i16 32, ptr %space_ptr, align 2
              %after_space = add i64 %copy_offset, 1
              store i64 %after_space, ptr %total_slot, align 8
              br label %copy_inner

            copy_inner:
              %copy_j = phi i64 [ 0, %copy_space ], [ 0, %store_space ], [ %copy_j_next, %copy_more ]
              %copy_arg_slot = getelementptr ptr, ptr %argv, i64 %copy_i
              %copy_arg = load ptr, ptr %copy_arg_slot, align 8
              %copy_char_ptr = getelementptr i16, ptr %copy_arg, i64 %copy_j
              %copy_char = load i16, ptr %copy_char_ptr, align 2
              %copy_arg_done = icmp eq i16 %copy_char, 0
              br i1 %copy_arg_done, label %copy_arg_finish, label %copy_more

            copy_more:
              %current_offset = load i64, ptr %total_slot, align 8
              %destination = getelementptr i16, ptr %command, i64 %current_offset
              store i16 %copy_char, ptr %destination, align 2
              %next_offset = add i64 %current_offset, 1
              store i64 %next_offset, ptr %total_slot, align 8
              %copy_j_next = add i64 %copy_j, 1
              br label %copy_inner

            copy_arg_finish:
              %copy_i_next = add i64 %copy_i, 1
              store i64 %copy_i_next, ptr %i_slot, align 8
              br label %copy_outer

            terminate:
              %final_offset = load i64, ptr %total_slot, align 8
              %null_ptr = getelementptr i16, ptr %command, i64 %final_offset
              store i16 0, ptr %null_ptr, align 2
              ret ptr %command

            fail:
              ret ptr null
            }

            define internal ptr @sollang_inherit_windows_handle(ptr %source) #0 {
            entry:
              %process = call ptr @GetCurrentProcess()
              %target_slot = alloca ptr, align 8
              %duplicated = call i32 @DuplicateHandle(ptr %process, ptr %source, ptr %process, ptr %target_slot, i32 0, i32 1, i32 2)
              %ok = icmp ne i32 %duplicated, 0
              br i1 %ok, label %success, label %fail
            success:
              %target = load ptr, ptr %target_slot, align 8
              ret ptr %target
            fail:
              ret ptr null
            }

            define internal i64 @sollang_spawn_windows(ptr %program, ptr %argv, i64 %count) #0 {
            entry:
              %command = call ptr @sollang_join_windows_args(ptr %argv, i64 %count)
              %command_ok = icmp ne ptr %command, null
              br i1 %command_ok, label %handles, label %fail

            handles:
              %stdin_source = call ptr @GetStdHandle(i32 -10)
              %stdout_override = load ptr, ptr @sollang_process_output_override, align 8
              %has_override = icmp ne ptr %stdout_override, null
              %stdout_default = call ptr @GetStdHandle(i32 -11)
              %stdout_source = select i1 %has_override, ptr %stdout_override, ptr %stdout_default
              %stderr_source = call ptr @GetStdHandle(i32 -12)
              %stdin_handle = call ptr @sollang_inherit_windows_handle(ptr %stdin_source)
              %stdout_handle = call ptr @sollang_inherit_windows_handle(ptr %stdout_source)
              %stderr_handle = call ptr @sollang_inherit_windows_handle(ptr %stderr_source)
              %stdin_ok = icmp ne ptr %stdin_handle, null
              %stdout_ok = icmp ne ptr %stdout_handle, null
              %stderr_ok = icmp ne ptr %stderr_handle, null
              %io_ok0 = and i1 %stdin_ok, %stdout_ok
              %io_ok = and i1 %io_ok0, %stderr_ok
              br i1 %io_ok, label %create, label %close_handles_fail

            create:
              %startup = alloca [104 x i8], align 8
              %process_info = alloca [24 x i8], align 8
              call void @llvm.memset.p0.i64(ptr %startup, i8 0, i64 104, i1 false)
              call void @llvm.memset.p0.i64(ptr %process_info, i8 0, i64 24, i1 false)
              store i32 104, ptr %startup, align 4
              %flags_ptr = getelementptr i8, ptr %startup, i64 60
              store i32 256, ptr %flags_ptr, align 4
              %stdin_ptr = getelementptr i8, ptr %startup, i64 80
              store ptr %stdin_handle, ptr %stdin_ptr, align 8
              %stdout_ptr = getelementptr i8, ptr %startup, i64 88
              store ptr %stdout_handle, ptr %stdout_ptr, align 8
              %stderr_ptr = getelementptr i8, ptr %startup, i64 96
              store ptr %stderr_handle, ptr %stderr_ptr, align 8
              %created = call i32 @CreateProcessW(ptr %program, ptr %command, ptr null, ptr null, i32 1, i32 0, ptr null, ptr null, ptr %startup, ptr %process_info)
              %created_ok = icmp ne i32 %created, 0
              br i1 %created_ok, label %wait, label %close_handles_fail

            wait:
              %process_handle = load ptr, ptr %process_info, align 8
              %thread_ptr = getelementptr i8, ptr %process_info, i64 8
              %thread_handle = load ptr, ptr %thread_ptr, align 8
              %waited = call i32 @WaitForSingleObject(ptr %process_handle, i32 -1)
              %exit_slot = alloca i32, align 4
              %exit_read = call i32 @GetExitCodeProcess(ptr %process_handle, ptr %exit_slot)
              %exit_ok = icmp ne i32 %exit_read, 0
              %closed_thread = call i32 @CloseHandle(ptr %thread_handle)
              %closed_process = call i32 @CloseHandle(ptr %process_handle)
              %closed_stdin = call i32 @CloseHandle(ptr %stdin_handle)
              %closed_stdout = call i32 @CloseHandle(ptr %stdout_handle)
              %closed_stderr = call i32 @CloseHandle(ptr %stderr_handle)
              call void @sollang_free(ptr %command)
              br i1 %exit_ok, label %success, label %fail_return

            success:
              %exit_code = load i32, ptr %exit_slot, align 4
              %exit_code64 = zext i32 %exit_code to i64
              ret i64 %exit_code64

            close_handles_fail:
              br i1 %stdin_ok, label %close_stdin_fail, label %close_stdout_fail
            close_stdin_fail:
              %closed_stdin_fail = call i32 @CloseHandle(ptr %stdin_handle)
              br label %close_stdout_fail
            close_stdout_fail:
              br i1 %stdout_ok, label %close_stdout_present, label %close_stderr_fail
            close_stdout_present:
              %closed_stdout_fail = call i32 @CloseHandle(ptr %stdout_handle)
              br label %close_stderr_fail
            close_stderr_fail:
              br i1 %stderr_ok, label %close_stderr_present, label %free_fail
            close_stderr_present:
              %closed_stderr_fail = call i32 @CloseHandle(ptr %stderr_handle)
              br label %free_fail
            free_fail:
              call void @sollang_free(ptr %command)
              br label %fail_return

            fail:
              br label %fail_return
            fail_return:
              ret i64 -1
            }

            define internal %sollang.process_result @sollang_run_process(ptr %records, i64 %count) #0 {
            entry:
              %has_program = icmp ugt i64 %count, 0
              br i1 %has_program, label %allocate, label %spawn_error

            allocate:
              %program_slot = alloca ptr, align 8
              store ptr null, ptr %program_slot, align 8
              %slots = add i64 %count, 1
              %argv_bytes = mul i64 %slots, 8
              %wide_argv = call ptr @sollang_alloc(i64 %argv_bytes)
              %argv_ok = icmp ne ptr %wide_argv, null
              br i1 %argv_ok, label %convert_loop, label %spawn_error

            convert_loop:
              %i = phi i64 [ 0, %allocate ], [ %next, %store_quoted ]
              %convert_done = icmp eq i64 %i, %count
              br i1 %convert_done, label %terminate, label %convert_size

            convert_size:
              %record = getelementptr %sollang.text, ptr %records, i64 %i
              %src_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 0
              %src = load ptr, ptr %src_slot, align 8
              %len_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 1
              %len64 = load i64, ptr %len_slot, align 8
              %len_fits = icmp ule i64 %len64, 2147483647
              br i1 %len_fits, label %convert_measure, label %convert_fail

            convert_measure:
              %len = trunc i64 %len64 to i32
              %chars = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %src, i32 %len, ptr null, i32 0)
              %chars_valid = icmp sgt i32 %chars, 0
              %empty = icmp eq i32 %len, 0
              %valid_or_empty = or i1 %chars_valid, %empty
              br i1 %valid_or_empty, label %convert_alloc, label %convert_fail

            convert_alloc:
              %with_null = add i32 %chars, 1
              %with_null64 = zext i32 %with_null to i64
              %bytes = mul i64 %with_null64, 2
              %wide = call ptr @sollang_alloc(i64 %bytes)
              %wide_ok = icmp ne ptr %wide, null
              br i1 %wide_ok, label %convert_value, label %convert_fail

            convert_value:
              %written = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %src, i32 %len, ptr %wide, i32 %chars)
              %converted = icmp eq i32 %written, %chars
              br i1 %converted, label %convert_store, label %free_current

            free_current:
              call void @sollang_free(ptr %wide)
              br label %convert_fail

            convert_store:
              %wide_end = getelementptr i16, ptr %wide, i32 %chars
              store i16 0, ptr %wide_end, align 2
              %quoted = call ptr @sollang_quote_windows_arg(ptr %wide, i32 %chars)
              %quoted_ok = icmp ne ptr %quoted, null
              br i1 %quoted_ok, label %preserve_program, label %free_current

            preserve_program:
              %is_program = icmp eq i64 %i, 0
              br i1 %is_program, label %store_program, label %free_unquoted

            store_program:
              store ptr %wide, ptr %program_slot, align 8
              br label %store_quoted

            free_unquoted:
              call void @sollang_free(ptr %wide)
              br label %store_quoted

            store_quoted:
              %argv_slot = getelementptr ptr, ptr %wide_argv, i64 %i
              store ptr %quoted, ptr %argv_slot, align 8
              %next = add i64 %i, 1
              br label %convert_loop

            convert_fail:
              br label %cleanup_failure

            cleanup_failure:
              %failure_j = phi i64 [ %i, %convert_fail ], [ %failure_prev, %cleanup_failure_item ]
              %failure_done = icmp eq i64 %failure_j, 0
              br i1 %failure_done, label %free_argv_error, label %cleanup_failure_item

            cleanup_failure_item:
              %failure_prev = sub i64 %failure_j, 1
              %failure_slot = getelementptr ptr, ptr %wide_argv, i64 %failure_prev
              %failure_arg = load ptr, ptr %failure_slot, align 8
              call void @sollang_free(ptr %failure_arg)
              br label %cleanup_failure

            free_argv_error:
              %failed_program = load ptr, ptr %program_slot, align 8
              call void @sollang_free(ptr %failed_program)
              call void @sollang_free(ptr %wide_argv)
              br label %spawn_error

            terminate:
              %null_slot = getelementptr ptr, ptr %wide_argv, i64 %count
              store ptr null, ptr %null_slot, align 8
              %program = load ptr, ptr %program_slot, align 8
              %spawn_result = call i64 @sollang_spawn_windows(ptr %program, ptr %wide_argv, i64 %count)
              br label %cleanup

            cleanup:
              %j = phi i64 [ %count, %terminate ], [ %prev, %cleanup_item ]
              %cleanup_done = icmp eq i64 %j, 0
              br i1 %cleanup_done, label %free_argv, label %cleanup_item

            cleanup_item:
              %prev = sub i64 %j, 1
              %old_slot = getelementptr ptr, ptr %wide_argv, i64 %prev
              %old_arg = load ptr, ptr %old_slot, align 8
              call void @sollang_free(ptr %old_arg)
              br label %cleanup

            free_argv:
              %saved_program = load ptr, ptr %program_slot, align 8
              call void @sollang_free(ptr %saved_program)
              call void @sollang_free(ptr %wide_argv)
              %spawn_ok = icmp ne i64 %spawn_result, -1
              br i1 %spawn_ok, label %success, label %spawn_error

            success:
              %exit_code = trunc i64 %spawn_result to i32
              %ok0 = insertvalue %sollang.process_result poison, i32 %exit_code, 0
              %ok1 = insertvalue %sollang.process_result %ok0, i32 0, 1
              ret %sollang.process_result %ok1

            spawn_error:
              %error0 = insertvalue %sollang.process_result poison, i32 0, 0
              %error1 = insertvalue %sollang.process_result %error0, i32 1, 1
              ret %sollang.process_result %error1
            }

            define internal %sollang.process_result @sollang_run_process_to_file(ptr %records, i64 %count, ptr %path, i64 %path_len) #0 {
            entry:
              %path_buffer = alloca [260 x i8], align 1
              %path_buffer_ptr = getelementptr inbounds [260 x i8], ptr %path_buffer, i64 0, i64 0
              %path_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %path_len, ptr %path_buffer_ptr)
              %path_valid = icmp ne i32 %path_ok, 0
              br i1 %path_valid, label %open, label %error

            open:
              %handle = call ptr @CreateFileA(ptr %path_buffer_ptr, i32 1073741824, i32 1, ptr null, i32 2, i32 128, ptr null)
              %handle_value = ptrtoint ptr %handle to i64
              %open_ok = icmp ne i64 %handle_value, -1
              br i1 %open_ok, label %run, label %error

            run:
              store ptr %handle, ptr @sollang_process_output_override, align 8
              %result = call %sollang.process_result @sollang_run_process(ptr %records, i64 %count)
              store ptr null, ptr @sollang_process_output_override, align 8
              %closed = call i32 @CloseHandle(ptr %handle)
              ret %sollang.process_result %result

            error:
              %error0 = insertvalue %sollang.process_result poison, i32 0, 0
              %error1 = insertvalue %sollang.process_result %error0, i32 1, 1
              ret %sollang.process_result %error1
            }

            define internal void @sollang_dispose_arguments() #0 {
            entry:
              %count = load i64, ptr @sollang_argument_count_value, align 8
              %records = load ptr, ptr @sollang_argument_records, align 8
              br label %loop

            loop:
              %i = phi i64 [ 0, %entry ], [ %next, %free_item ]
              %done = icmp eq i64 %i, %count
              br i1 %done, label %finish, label %free_item

            free_item:
              %record = getelementptr %sollang.text, ptr %records, i64 %i
              %ptr_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 0
              %ptr = load ptr, ptr %ptr_slot, align 8
              call void @sollang_free(ptr %ptr)
              %next = add i64 %i, 1
              br label %loop

            finish:
              call void @sollang_free(ptr %records)
              ret void
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define dso_local void @__chkstk() naked nounwind {
            entry:
              call void asm sideeffect inteldialect "push rcx\0Apush rax\0Alea rcx, [rsp + 24]\0Acmp rax, 4096\0Ajb .Lsollang_chkstk_tail\0A.Lsollang_chkstk_loop:\0Asub rcx, 4096\0Atest byte ptr [rcx], 0\0Asub rax, 4096\0Acmp rax, 4096\0Ajae .Lsollang_chkstk_loop\0A.Lsollang_chkstk_tail:\0Asub rcx, rax\0Atest byte ptr [rcx], 0\0Apop rax\0Apop rcx\0Aret", "~{memory},~{flags}"()
              unreachable
            }

            define internal i32 @sollang_flush_stdout(ptr %stdout, ptr %written) #0 {
            entry:
              %count64 = load i64, ptr @sollang_stdout_buffer_count, align 8
              %has_data = icmp ne i64 %count64, 0
              br i1 %has_data, label %write, label %empty

            write:
              %count = trunc i64 %count64 to i32
              %buffer = getelementptr inbounds [1048576 x i8], ptr @sollang_stdout_buffer, i64 0, i64 0
              %ok = call i32 @WriteFile(ptr %stdout, ptr %buffer, i32 %count, ptr %written, ptr null)
              store i64 0, ptr @sollang_stdout_buffer_count, align 8
              ret i32 %ok

            empty:
              store i32 0, ptr %written, align 4
              ret i32 1
            }

            """);

        if (UsesComputePool)
        {
            functions.AppendLine("""
            define internal i32 @sollang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %stdout_value = ptrtoint ptr %stdout to i64
              %sink_tag = and i64 %stdout_value, 1
              %capturing = icmp ne i64 %sink_tag, 0
              br i1 %capturing, label %capture, label %write_prepare

            capture:
              %sink_value = and i64 %stdout_value, -2
              %sink = inttoptr i64 %sink_value to ptr
              call void @sollang_memory_output_sink_append(ptr %sink, ptr %data, i64 %len64)
              %captured_len = trunc i64 %len64 to i32
              store i32 %captured_len, ptr %written, align 4
              ret i32 1

            write_prepare:
            """);
        }
        else
        {
            functions.AppendLine("""
            define internal i32 @sollang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              br label %write_prepare

            write_prepare:
            """);
        }

        functions.AppendLine("""
              %oversized = icmp ugt i64 %len64, 1048576
              br i1 %oversized, label %write_direct_prepare, label %buffer_prepare

            write_direct_prepare:
              %flushed_direct = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              %direct_len = trunc i64 %len64 to i32
              %direct_ok = call i32 @WriteFile(ptr %stdout, ptr %data, i32 %direct_len, ptr %written, ptr null)
              ret i32 %direct_ok

            buffer_prepare:
              %count = load i64, ptr @sollang_stdout_buffer_count, align 8
              %combined = add i64 %count, %len64
              %needs_flush = icmp ugt i64 %combined, 1048576
              br i1 %needs_flush, label %flush, label %append

            flush:
              %flushed = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              br label %append

            append:
              %offset = phi i64 [ %count, %buffer_prepare ], [ 0, %flush ]
              %destination = getelementptr inbounds [1048576 x i8], ptr @sollang_stdout_buffer, i64 0, i64 %offset
              %copied = call ptr @memcpy(ptr %destination, ptr %data, i64 %len64)
              %next_count = add i64 %offset, %len64
              store i64 %next_count, ptr @sollang_stdout_buffer_count, align 8
              %written32 = trunc i64 %len64 to i32
              store i32 %written32, ptr %written, align 4
              %single_byte = icmp eq i64 %len64, 1
              br i1 %single_byte, label %inspect_newline, label %done

            inspect_newline:
              %byte = load i8, ptr %data, align 1
              %newline = icmp eq i8 %byte, 10
              %line_buffered = load i1, ptr @sollang_stdout_line_buffered, align 1
              %flush_newline = and i1 %newline, %line_buffered
              br i1 %flush_newline, label %flush_line, label %done

            flush_line:
              %line_ok = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              ret i32 %line_ok

            done:
              ret i32 1
            }

            define internal i32 @sollang_read_stdin(ptr %stdin, ptr %data, i64 %len64, ptr %read) #0 {
            entry:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @ReadFile(ptr %stdin, ptr %data, i32 %len, ptr %read, ptr null)
              ret i32 %ok
            }

            """);

        if (UsesComputePool)
        {
            functions.AppendLine("""
            define internal void @sollang_memory_output_sink_write(ptr %context, ptr %data, i64 %len) #0 {
            entry:
              %stdout_slot = getelementptr %sollang.compute_group, ptr %context, i32 0, i32 7
              %stdout = load ptr, ptr %stdout_slot, align 8
              %written_slot = getelementptr %sollang.compute_group, ptr %context, i32 0, i32 8
              %written = load ptr, ptr %written_slot, align 8
              %write_ok = call i32 @sollang_write(ptr %stdout, ptr %data, i64 %len, ptr %written)
              ret void
            }

            """);
        }
    }

    public override void EmitFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_platform_atomic_replace_file(ptr %temporary, i64 %temporary_len, ptr %destination, i64 %destination_len) #0 {
            entry:
              %temporary_buf = alloca [260 x i8], align 1
              %temporary_ptr = getelementptr inbounds [260 x i8], ptr %temporary_buf, i64 0, i64 0
              %temporary_ok = call i32 @sollang_copy_text_to_c_path(ptr %temporary, i64 %temporary_len, ptr %temporary_ptr)
              %temporary_valid = icmp ne i32 %temporary_ok, 0
              br i1 %temporary_valid, label %copy_destination, label %fail

            copy_destination:
              %destination_buf = alloca [260 x i8], align 1
              %destination_ptr = getelementptr inbounds [260 x i8], ptr %destination_buf, i64 0, i64 0
              %destination_ok = call i32 @sollang_copy_text_to_c_path(ptr %destination, i64 %destination_len, ptr %destination_ptr)
              %destination_valid = icmp ne i32 %destination_ok, 0
              br i1 %destination_valid, label %replace, label %fail

            replace:
              %result = call i32 @MoveFileExA(ptr %temporary_ptr, ptr %destination_ptr, i32 9)
              ret i32 %result

            fail:
              ret i32 0
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_read_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 -2147483648, i32 1, ptr null, i32 3, i32 1073741952, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              %ok0 = insertvalue %sollang.file_handle_result poison, i64 %handle_int, 0
              %ok1 = insertvalue %sollang.file_handle_result %ok0, i32 1, 1
              ret %sollang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 1073741824, i32 1, ptr null, i32 2, i32 1073741952, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              %ok0 = insertvalue %sollang.file_handle_result poison, i64 %handle_int, 0
              %ok1 = insertvalue %sollang.file_handle_result %ok0, i32 1, 1
              ret %sollang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal i64 @sollang_platform_duplicate_owned_file(i64 %source_value) #0 {
            entry:
              %source = inttoptr i64 %source_value to ptr
              %process = call ptr @GetCurrentProcess()
              %target_slot = alloca ptr, align 8
              %ok = call i32 @DuplicateHandle(ptr %process, ptr %source, ptr %process, ptr %target_slot, i32 0, i32 0, i32 2)
              %succeeded = icmp ne i32 %ok, 0
              br i1 %succeeded, label %success, label %fail

            success:
              %target = load ptr, ptr %target_slot, align 8
              %target_value = ptrtoint ptr %target to i64
              ret i64 %target_value

            fail:
              ret i64 -1
            }

            define internal %sollang.file_count_result @sollang_platform_read_owned_file_at(i64 %handle_value, ptr %data, i64 %len64, i64 %offset) #0 {
            entry:
              %valid_handle = icmp ne i64 %handle_value, -1
              %valid_offset = icmp ule i64 %offset, 9223372036854775807
              %fits = icmp ule i64 %len64, 4294967295
              %ready0 = and i1 %valid_handle, %valid_offset
              %ready = and i1 %ready0, %fits
              br i1 %ready, label %read, label %fail

            read:
              %handle = inttoptr i64 %handle_value to ptr
              %overlapped = alloca [32 x i8], align 8
              call void @llvm.memset.p0.i64(ptr %overlapped, i8 0, i64 32, i1 false)
              %offset_low_slot = getelementptr i8, ptr %overlapped, i64 16
              %offset_low = trunc i64 %offset to i32
              store i32 %offset_low, ptr %offset_low_slot, align 4
              %offset_high_slot = getelementptr i8, ptr %overlapped, i64 20
              %offset_shifted = lshr i64 %offset, 32
              %offset_high = trunc i64 %offset_shifted to i32
              store i32 %offset_high, ptr %offset_high_slot, align 4
              %count_slot = alloca i32, align 4
              store i32 0, ptr %count_slot, align 4
              %len = trunc i64 %len64 to i32
              %started = call i32 @ReadFile(ptr %handle, ptr %data, i32 %len, ptr null, ptr %overlapped)
              %completed = call i32 @GetOverlappedResult(ptr %handle, ptr %overlapped, ptr %count_slot, i32 1)
              %ok = icmp ne i32 %completed, 0
              br i1 %ok, label %success, label %fail

            success:
              %count32 = load i32, ptr %count_slot, align 4
              %count = zext i32 %count32 to i64
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_write_owned_file_at(i64 %handle_value, ptr %data, i64 %len64, i64 %offset) #0 {
            entry:
              %valid_handle = icmp ne i64 %handle_value, -1
              %valid_offset = icmp ule i64 %offset, 9223372036854775807
              %fits = icmp ule i64 %len64, 4294967295
              %ready0 = and i1 %valid_handle, %valid_offset
              %ready = and i1 %ready0, %fits
              br i1 %ready, label %write, label %fail

            write:
              %handle = inttoptr i64 %handle_value to ptr
              %overlapped = alloca [32 x i8], align 8
              call void @llvm.memset.p0.i64(ptr %overlapped, i8 0, i64 32, i1 false)
              %offset_low_slot = getelementptr i8, ptr %overlapped, i64 16
              %offset_low = trunc i64 %offset to i32
              store i32 %offset_low, ptr %offset_low_slot, align 4
              %offset_high_slot = getelementptr i8, ptr %overlapped, i64 20
              %offset_shifted = lshr i64 %offset, 32
              %offset_high = trunc i64 %offset_shifted to i32
              store i32 %offset_high, ptr %offset_high_slot, align 4
              %count_slot = alloca i32, align 4
              store i32 0, ptr %count_slot, align 4
              %len = trunc i64 %len64 to i32
              %started = call i32 @WriteFile(ptr %handle, ptr %data, i32 %len, ptr null, ptr %overlapped)
              %completed = call i32 @GetOverlappedResult(ptr %handle, ptr %overlapped, ptr %count_slot, i32 1)
              %ok = icmp ne i32 %completed, 0
              br i1 %ok, label %success, label %fail

            success:
              %count32 = load i32, ptr %count_slot, align 4
              %count = zext i32 %count32 to i64
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal i32 @sollang_platform_sync_owned_file(i64 %handle_value) #0 {
            entry:
              %valid = icmp ne i64 %handle_value, -1
              br i1 %valid, label %sync, label %fail

            sync:
              %handle = inttoptr i64 %handle_value to ptr
              %ok = call i32 @FlushFileBuffers(ptr %handle)
              ret i32 %ok

            fail:
              ret i32 0
            }

            define internal void @sollang_platform_close_owned_file(i64 %handle_value) #0 {
            entry:
              %valid = icmp ne i64 %handle_value, -1
              br i1 %valid, label %close, label %done

            close:
              %handle = inttoptr i64 %handle_value to ptr
              %ignored = call i32 @CloseHandle(ptr %handle)
              br label %done

            done:
              ret void
            }

            define internal i32 @sollang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 1073741824, i32 0, ptr null, i32 2, i32 128, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              store ptr %handle, ptr @sollang_file_writer, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @sollang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_writer, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %write, label %fail

            write:
              %written = alloca i32, align 4
              %len = trunc i64 %len64 to i32
              %ok = call i32 @WriteFile(ptr %handle, ptr %data, i32 %len, ptr %written, ptr null)
              %written32 = load i32, ptr %written, align 4
              %written64 = zext i32 %written32 to i64
              %write_ok = icmp ne i32 %ok, 0
              %full = icmp eq i64 %written64, %len64
              %all_ok = and i1 %write_ok, %full
              %result = zext i1 %all_ok to i32
              ret i32 %result

            fail:
              ret i32 0
            }

            define internal i32 @sollang_platform_close_write_file() #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_writer, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %close, label %fail

            close:
              %ok = call i32 @CloseHandle(ptr %handle)
              %close_ok = icmp ne i32 %ok, 0
              br i1 %close_ok, label %success, label %fail

            success:
              store ptr null, ptr @sollang_file_writer, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @sollang_platform_open_read_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 -2147483648, i32 1, ptr null, i32 3, i32 128, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              store ptr %handle, ptr @sollang_file_reader, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            define internal %sollang.file_count_result @sollang_platform_i64_file_count() #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_reader, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %size, label %fail

            size:
              %size_ptr = alloca i64, align 8
              %ok = call i32 @GetFileSizeEx(ptr %handle, ptr %size_ptr)
              %size_ok = icmp ne i32 %ok, 0
              br i1 %size_ok, label %check, label %fail

            check:
              %bytes = load i64, ptr %size_ptr, align 8
              %rem = urem i64 %bytes, 8
              %aligned = icmp eq i64 %rem, 0
              br i1 %aligned, label %success, label %fail

            success:
              %count = udiv i64 %bytes, 8
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal %sollang.file_int_result @sollang_platform_read_i64_at(i64 %index) #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_reader, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %seek, label %fail

            seek:
              %offset = mul i64 %index, 8
              %seek_ok = call i32 @SetFilePointerEx(ptr %handle, i64 %offset, ptr null, i32 0)
              %seek_is_ok = icmp ne i32 %seek_ok, 0
              br i1 %seek_is_ok, label %read, label %fail

            read:
              %value_ptr = alloca i64, align 8
              %read_bytes = alloca i32, align 4
              %ok = call i32 @ReadFile(ptr %handle, ptr %value_ptr, i32 8, ptr %read_bytes, ptr null)
              %read_ok = icmp ne i32 %ok, 0
              %read_count = load i32, ptr %read_bytes, align 4
              %read_full = icmp eq i32 %read_count, 8
              %all_ok = and i1 %read_ok, %read_full
              br i1 %all_ok, label %success, label %fail

            success:
              %value = load i64, ptr %value_ptr, align 8
              %ok0 = insertvalue %sollang.file_int_result poison, i64 %value, 0
              %ok1 = insertvalue %sollang.file_int_result %ok0, i32 1, 1
              ret %sollang.file_int_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_int_result %fail0, i32 0, 1
              ret %sollang.file_int_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_read_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_reader, align 8
              %has_handle = icmp ne ptr %handle, null
              %fits = icmp ule i64 %len64, 4294967295
              %ready = and i1 %has_handle, %fits
              br i1 %ready, label %read, label %fail

            read:
              %len = trunc i64 %len64 to i32
              %count_ptr = alloca i32, align 4
              %result = call i32 @ReadFile(ptr %handle, ptr %data, i32 %len, ptr %count_ptr, ptr null)
              %ok = icmp ne i32 %result, 0
              br i1 %ok, label %success, label %fail

            success:
              %count32 = load i32, ptr %count_ptr, align 4
              %count = zext i32 %count32 to i64
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal i32 @sollang_platform_close_read_file() #0 {
            entry:
              %handle = load ptr, ptr @sollang_file_reader, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %close, label %fail

            close:
              %ok = call i32 @CloseHandle(ptr %handle)
              %close_ok = icmp ne i32 %ok, 0
              br i1 %close_ok, label %success, label %fail

            success:
              store ptr null, ptr @sollang_file_reader, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            """);
    }

    public override void EmitMappedFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal %sollang.mapped_bytes @sollang_map_file(ptr %path, i64 %path_len, i64 %offset, i64 %requested_len, i64 %requested_size, i1 %writable) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %path_len, ptr %buf_ptr)
              %path_ok = icmp ne i32 %copy_ok, 0
              br i1 %path_ok, label %open, label %fail

            open:
              %access = select i1 %writable, i32 -1073741824, i32 -2147483648
              %creation = select i1 %writable, i32 4, i32 3
              %file = call ptr @CreateFileA(ptr %buf_ptr, i32 %access, i32 1, ptr null, i32 %creation, i32 128, ptr null)
              %file_int = ptrtoint ptr %file to i64
              %file_ok = icmp ne i64 %file_int, -1
              br i1 %file_ok, label %resize_check, label %fail

            resize_check:
              %has_requested_size = icmp ne i64 %requested_size, 0
              %resize = and i1 %writable, %has_requested_size
              br i1 %resize, label %resize_file, label %read_size

            resize_file:
              %seek_ok = call i32 @SetFilePointerEx(ptr %file, i64 %requested_size, ptr null, i32 0)
              %end_ok = call i32 @SetEndOfFile(ptr %file)
              %resize_ok = and i32 %seek_ok, %end_ok
              %resize_valid = icmp ne i32 %resize_ok, 0
              br i1 %resize_valid, label %read_size, label %close_fail

            read_size:
              %size_slot = alloca i64, align 8
              %size_ok = call i32 @GetFileSizeEx(ptr %file, ptr %size_slot)
              %size_valid = icmp ne i32 %size_ok, 0
              br i1 %size_valid, label %bounds, label %close_fail

            bounds:
              %file_size = load i64, ptr %size_slot, align 8
              %offset_ok = icmp ule i64 %offset, %file_size
              %remaining = sub i64 %file_size, %offset
              %whole = icmp eq i64 %requested_len, 0
              %view_len = select i1 %whole, i64 %remaining, i64 %requested_len
              %len_nonzero = icmp ne i64 %view_len, 0
              %len_ok = icmp ule i64 %view_len, %remaining
              %bounds1 = and i1 %offset_ok, %len_nonzero
              %bounds_ok = and i1 %bounds1, %len_ok
              br i1 %bounds_ok, label %mapping, label %close_fail

            mapping:
              %protect = select i1 %writable, i32 4, i32 2
              %mapping_handle = call ptr @CreateFileMappingA(ptr %file, ptr null, i32 %protect, i32 0, i32 0, ptr null)
              %mapping_ok = icmp ne ptr %mapping_handle, null
              br i1 %mapping_ok, label %view, label %close_fail

            view:
              %aligned = and i64 %offset, -65536
              %delta = sub i64 %offset, %aligned
              %mapped_len = add i64 %delta, %view_len
              %high64 = lshr i64 %aligned, 32
              %high = trunc i64 %high64 to i32
              %low = trunc i64 %aligned to i32
              %desired = select i1 %writable, i32 2, i32 4
              %base = call ptr @MapViewOfFile(ptr %mapping_handle, i32 %desired, i32 %high, i32 %low, i64 %mapped_len)
              %base_ok = icmp ne ptr %base, null
              br i1 %base_ok, label %success, label %mapping_close_fail

            success:
              %data = getelementptr i8, ptr %base, i64 %delta
              %ignored_mapping = call i32 @CloseHandle(ptr %mapping_handle)
              %ignored_file = call i32 @CloseHandle(ptr %file)
              %r0 = insertvalue %sollang.mapped_bytes poison, ptr %data, 0
              %r1 = insertvalue %sollang.mapped_bytes %r0, i64 %view_len, 1
              %r2 = insertvalue %sollang.mapped_bytes %r1, ptr %base, 2
              %r3 = insertvalue %sollang.mapped_bytes %r2, i64 %mapped_len, 3
              %r4 = insertvalue %sollang.mapped_bytes %r3, i1 %writable, 4
              ret %sollang.mapped_bytes %r4

            mapping_close_fail:
              %ignored_mapping_fail = call i32 @CloseHandle(ptr %mapping_handle)
              br label %close_fail

            close_fail:
              %ignored_close = call i32 @CloseHandle(ptr %file)
              br label %fail

            fail:
              %f0 = insertvalue %sollang.mapped_bytes zeroinitializer, i1 %writable, 4
              ret %sollang.mapped_bytes %f0
            }

            define internal i32 @sollang_mapped_flush(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ok = call i32 @FlushViewOfFile(ptr %base, i64 %mapped_len)
              ret i32 %ok
            }

            define internal void @sollang_mapped_unmap(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ignored = call i32 @UnmapViewOfFile(ptr %base)
              ret void
            }

            """);
    }

    public override void EmitDirectoryPrimitives(StringBuilder functions)
    {
        EmitDirectoryNodePrimitives(functions);
        functions.AppendLine("""
            define internal %sollang.directory_result @sollang_platform_read_directory(ptr %path, i64 %len, i32 %style) #0 {
            entry:
              %style_ok = icmp eq i32 %style, 1
              br i1 %style_ok, label %prepare, label %fail

            prepare:
              %buffer_size = add i64 %len, 3
              %pattern = call ptr @sollang_alloc(i64 %buffer_size)
              %pattern_ok = icmp ne ptr %pattern, null
              br i1 %pattern_ok, label %copy_path, label %fail

            copy_path:
              call void @llvm.memcpy.p0.p0.i64(ptr %pattern, ptr %path, i64 %len, i1 false)
              %has_path = icmp ugt i64 %len, 0
              br i1 %has_path, label %inspect_last, label %append_separator

            inspect_last:
              %last_index = sub i64 %len, 1
              %last_ptr = getelementptr i8, ptr %pattern, i64 %last_index
              %last = load i8, ptr %last_ptr, align 1
              %last_slash = icmp eq i8 %last, 47
              %last_backslash = icmp eq i8 %last, 92
              %has_separator = or i1 %last_slash, %last_backslash
              br i1 %has_separator, label %append_star, label %append_separator

            append_separator:
              %separator_ptr = getelementptr i8, ptr %pattern, i64 %len
              store i8 92, ptr %separator_ptr, align 1
              %separator_star_index = add i64 %len, 1
              %separator_star_ptr = getelementptr i8, ptr %pattern, i64 %separator_star_index
              store i8 42, ptr %separator_star_ptr, align 1
              %separator_zero_index = add i64 %len, 2
              %separator_zero_ptr = getelementptr i8, ptr %pattern, i64 %separator_zero_index
              store i8 0, ptr %separator_zero_ptr, align 1
              br label %open

            append_star:
              %star_ptr = getelementptr i8, ptr %pattern, i64 %len
              store i8 42, ptr %star_ptr, align 1
              %star_zero_index = add i64 %len, 1
              %star_zero_ptr = getelementptr i8, ptr %pattern, i64 %star_zero_index
              store i8 0, ptr %star_zero_ptr, align 1
              br label %open

            open:
              %find_data = alloca [320 x i8], align 8
              %handle = call ptr @FindFirstFileA(ptr %pattern, ptr %find_data)
              call void @sollang_free(ptr %pattern)
              %invalid = icmp eq ptr %handle, inttoptr (i64 -1 to ptr)
              br i1 %invalid, label %fail, label %enumerate

            enumerate:
              %head = phi ptr [ null, %open ], [ %advanced_head, %advance ]
              %count = phi i64 [ 0, %open ], [ %advanced_count, %advance ]
              %total = phi i64 [ 0, %open ], [ %advanced_total, %advance ]
              %name = getelementptr i8, ptr %find_data, i64 44
              br label %name_length

            name_length:
              %name_index = phi i64 [ 0, %enumerate ], [ %name_next, %name_continue ]
              %name_slot = getelementptr i8, ptr %name, i64 %name_index
              %name_byte = load i8, ptr %name_slot, align 1
              %name_end = icmp eq i8 %name_byte, 0
              %name_limit = icmp eq i64 %name_index, 259
              %name_done = or i1 %name_end, %name_limit
              br i1 %name_done, label %inspect_name, label %name_continue

            name_continue:
              %name_next = add i64 %name_index, 1
              br label %name_length

            inspect_name:
              %first = load i8, ptr %name, align 1
              %second_ptr = getelementptr i8, ptr %name, i64 1
              %second = load i8, ptr %second_ptr, align 1
              %length_one = icmp eq i64 %name_index, 1
              %length_two = icmp eq i64 %name_index, 2
              %first_dot = icmp eq i8 %first, 46
              %second_dot = icmp eq i8 %second, 46
              %dot = and i1 %length_one, %first_dot
              %two_dots = and i1 %first_dot, %second_dot
              %dotdot = and i1 %length_two, %two_dots
              %special = or i1 %dot, %dotdot
              br i1 %special, label %skip, label %allocate_node

            allocate_node:
              %node_size = add i64 %name_index, 24
              %node = call ptr @sollang_alloc(i64 %node_size)
              %node_ok = icmp ne ptr %node, null
              br i1 %node_ok, label %initialize_node, label %allocation_failed

            initialize_node:
              %node_next = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              store ptr null, ptr %node_next, align 8
              %node_length = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 1
              store i64 %name_index, ptr %node_length, align 8
              %attributes = load i32, ptr %find_data, align 4
              %reparse_bits = and i32 %attributes, 1024
              %is_reparse = icmp ne i32 %reparse_bits, 0
              %directory_bits = and i32 %attributes, 16
              %is_directory = icmp ne i32 %directory_bits, 0
              %ordinary_kind = select i1 %is_directory, i8 1, i8 0
              %kind = select i1 %is_reparse, i8 2, i8 %ordinary_kind
              %node_kind = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 2
              store i8 %kind, ptr %node_kind, align 1
              %node_name = getelementptr i8, ptr %node, i64 24
              call void @llvm.memcpy.p0.p0.i64(ptr %node_name, ptr %name, i64 %name_index, i1 false)
              %inserted_head = call ptr @sollang_directory_insert_sorted(ptr %head, ptr %node)
              %inserted_count = add i64 %count, 1
              %record_size = add i64 %name_index, 5
              %inserted_total = add i64 %total, %record_size
              br label %advance

            skip:
              br label %advance

            advance:
              %advanced_head = phi ptr [ %head, %skip ], [ %inserted_head, %initialize_node ]
              %advanced_count = phi i64 [ %count, %skip ], [ %inserted_count, %initialize_node ]
              %advanced_total = phi i64 [ %total, %skip ], [ %inserted_total, %initialize_node ]
              call void @SetLastError(i32 0)
              %has_next_status = call i32 @FindNextFileA(ptr %handle, ptr %find_data)
              %has_next = icmp ne i32 %has_next_status, 0
              br i1 %has_next, label %enumerate, label %finish_enumeration

            finish_enumeration:
              %last_error = call i32 @GetLastError()
              %normal_end = icmp eq i32 %last_error, 18
              br i1 %normal_end, label %close_success, label %enumeration_failed

            close_success:
              %closed = call i32 @FindClose(ptr %handle)
              %raw = call ptr @sollang_directory_serialize(ptr %advanced_head, i64 %advanced_total)
              %has_payload = icmp ugt i64 %advanced_total, 0
              %raw_missing = icmp eq ptr %raw, null
              %serialization_failed = and i1 %has_payload, %raw_missing
              br i1 %serialization_failed, label %fail, label %success

            success:
              %success0 = insertvalue %sollang.directory_result poison, ptr %raw, 0
              %success1 = insertvalue %sollang.directory_result %success0, i64 %advanced_total, 1
              %success2 = insertvalue %sollang.directory_result %success1, i64 %advanced_count, 2
              %success3 = insertvalue %sollang.directory_result %success2, i32 1, 3
              ret %sollang.directory_result %success3

            allocation_failed:
              call void @sollang_directory_free_nodes(ptr %head)
              %allocation_closed = call i32 @FindClose(ptr %handle)
              br label %fail

            enumeration_failed:
              call void @sollang_directory_free_nodes(ptr %advanced_head)
              %failure_closed = call i32 @FindClose(ptr %handle)
              br label %fail

            fail:
              %failure0 = insertvalue %sollang.directory_result poison, ptr null, 0
              %failure1 = insertvalue %sollang.directory_result %failure0, i64 0, 1
              %failure2 = insertvalue %sollang.directory_result %failure1, i64 0, 2
              %failure3 = insertvalue %sollang.directory_result %failure2, i32 0, 3
              ret %sollang.directory_result %failure3
            }

            """);
        functions.AppendLine("""
            define internal %sollang.path_query_result @sollang_platform_query_path(ptr %path, i64 %len, i32 %style) #0 {
            entry:
              %style_ok = icmp eq i32 %style, 1
              %len32 = trunc i64 %len to i32
              %len_roundtrip = zext i32 %len32 to i64
              %length_ok = icmp eq i64 %len_roundtrip, %len
              %input_ok = and i1 %style_ok, %length_ok
              br i1 %input_ok, label %measure_input, label %fail

            measure_input:
              %wide_chars = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %path, i32 %len32, ptr null, i32 0)
              %wide_ok = icmp sgt i32 %wide_chars, 0
              br i1 %wide_ok, label %allocate_input, label %fail

            allocate_input:
              %wide_chars64 = zext i32 %wide_chars to i64
              %wide_with_zero = add i64 %wide_chars64, 1
              %wide_bytes = mul i64 %wide_with_zero, 2
              %wide_input = call ptr @sollang_alloc(i64 %wide_bytes)
              %wide_input_ok = icmp ne ptr %wide_input, null
              br i1 %wide_input_ok, label %convert_input, label %fail

            convert_input:
              %converted = call i32 @MultiByteToWideChar(i32 65001, i32 8, ptr %path, i32 %len32, ptr %wide_input, i32 %wide_chars)
              %converted_ok = icmp eq i32 %converted, %wide_chars
              br i1 %converted_ok, label %terminate_input, label %input_fail

            terminate_input:
              %wide_zero = getelementptr i16, ptr %wide_input, i64 %wide_chars64
              store i16 0, ptr %wide_zero, align 2
              %handle = call ptr @CreateFileW(ptr %wide_input, i32 0, i32 7, ptr null, i32 3, i32 33554432, ptr null)
              call void @sollang_free(ptr %wide_input)
              %invalid_handle = inttoptr i64 -1 to ptr
              %handle_ok = icmp ne ptr %handle, %invalid_handle
              br i1 %handle_ok, label %measure_canonical, label %fail

            measure_canonical:
              %canonical_chars = call i32 @GetFinalPathNameByHandleW(ptr %handle, ptr null, i32 0, i32 0)
              %canonical_ok = icmp sgt i32 %canonical_chars, 0
              br i1 %canonical_ok, label %allocate_canonical, label %handle_fail

            allocate_canonical:
              %canonical_chars64 = zext i32 %canonical_chars to i64
              %canonical_capacity = add i32 %canonical_chars, 1
              %canonical_capacity64 = zext i32 %canonical_capacity to i64
              %canonical_wide_bytes = mul i64 %canonical_capacity64, 2
              %canonical_wide = call ptr @sollang_alloc(i64 %canonical_wide_bytes)
              %canonical_wide_ok = icmp ne ptr %canonical_wide, null
              br i1 %canonical_wide_ok, label %read_canonical, label %handle_fail

            read_canonical:
              %canonical_written = call i32 @GetFinalPathNameByHandleW(ptr %handle, ptr %canonical_wide, i32 %canonical_capacity, i32 0)
              %canonical_written_positive = icmp sgt i32 %canonical_written, 0
              %canonical_written_fits = icmp ule i32 %canonical_written, %canonical_chars
              %canonical_written_ok = and i1 %canonical_written_positive, %canonical_written_fits
              br i1 %canonical_written_ok, label %metadata, label %canonical_wide_fail

            metadata:
              %information = alloca [52 x i8], align 8
              %information_status = call i32 @GetFileInformationByHandle(ptr %handle, ptr %information)
              %information_ok = icmp ne i32 %information_status, 0
              br i1 %information_ok, label %measure_utf8, label %canonical_wide_fail

            measure_utf8:
              %utf8_bytes = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %canonical_wide, i32 %canonical_written, ptr null, i32 0, ptr null, ptr null)
              %utf8_ok = icmp sgt i32 %utf8_bytes, 0
              br i1 %utf8_ok, label %allocate_utf8, label %canonical_wide_fail

            allocate_utf8:
              %utf8_bytes64 = zext i32 %utf8_bytes to i64
              %canonical_utf8 = call ptr @sollang_alloc(i64 %utf8_bytes64)
              %canonical_utf8_ok = icmp ne ptr %canonical_utf8, null
              br i1 %canonical_utf8_ok, label %convert_canonical, label %canonical_wide_fail

            convert_canonical:
              %utf8_written = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %canonical_wide, i32 %canonical_written, ptr %canonical_utf8, i32 %utf8_bytes, ptr null, ptr null)
              %utf8_written_ok = icmp eq i32 %utf8_written, %utf8_bytes
              br i1 %utf8_written_ok, label %success, label %utf8_fail

            success:
              %attributes = load i32, ptr %information, align 4
              %directory_bits = and i32 %attributes, 16
              %is_directory = icmp ne i32 %directory_bits, 0
              %kind = select i1 %is_directory, i8 1, i8 0
              %size_high_ptr = getelementptr i8, ptr %information, i64 32
              %size_high32 = load i32, ptr %size_high_ptr, align 4
              %size_low_ptr = getelementptr i8, ptr %information, i64 36
              %size_low32 = load i32, ptr %size_low_ptr, align 4
              %size_high = zext i32 %size_high32 to i64
              %size_low = zext i32 %size_low32 to i64
              %size_shifted = shl i64 %size_high, 32
              %size = or i64 %size_shifted, %size_low
              %time_low_ptr = getelementptr i8, ptr %information, i64 20
              %time_low32 = load i32, ptr %time_low_ptr, align 4
              %time_high_ptr = getelementptr i8, ptr %information, i64 24
              %time_high32 = load i32, ptr %time_high_ptr, align 4
              %time_low = zext i32 %time_low32 to i64
              %time_high = zext i32 %time_high32 to i64
              %time_shifted = shl i64 %time_high, 32
              %file_time = or i64 %time_shifted, %time_low
              %unix_ticks = sub i64 %file_time, 116444736000000000
              %modified_nanos = mul i64 %unix_ticks, 100
              call void @sollang_free(ptr %canonical_wide)
              %closed = call i32 @CloseHandle(ptr %handle)
              %result0 = insertvalue %sollang.path_query_result poison, ptr %canonical_utf8, 0
              %result1 = insertvalue %sollang.path_query_result %result0, i64 %utf8_bytes64, 1
              %result2 = insertvalue %sollang.path_query_result %result1, i8 %kind, 2
              %result3 = insertvalue %sollang.path_query_result %result2, i64 %size, 3
              %result4 = insertvalue %sollang.path_query_result %result3, i64 %modified_nanos, 4
              %result5 = insertvalue %sollang.path_query_result %result4, i32 1, 5
              ret %sollang.path_query_result %result5

            utf8_fail:
              call void @sollang_free(ptr %canonical_utf8)
              br label %canonical_wide_fail

            canonical_wide_fail:
              call void @sollang_free(ptr %canonical_wide)
              br label %handle_fail

            handle_fail:
              %failed_closed = call i32 @CloseHandle(ptr %handle)
              br label %fail

            input_fail:
              call void @sollang_free(ptr %wide_input)
              br label %fail

            fail:
              %failure0 = insertvalue %sollang.path_query_result poison, ptr null, 0
              %failure1 = insertvalue %sollang.path_query_result %failure0, i64 0, 1
              %failure2 = insertvalue %sollang.path_query_result %failure1, i8 3, 2
              %failure3 = insertvalue %sollang.path_query_result %failure2, i64 0, 3
              %failure4 = insertvalue %sollang.path_query_result %failure3, i64 0, 4
              %failure5 = insertvalue %sollang.path_query_result %failure4, i32 0, 5
              ret %sollang.path_query_result %failure5
            }

            """);
    }

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = call ptr @GetStdHandle(i32 -10)");
        functions.AppendLine("  %stdout = call ptr @GetStdHandle(i32 -11)");
        functions.AppendLine("  %stdout_mode = alloca i32, align 4");
        functions.AppendLine("  %stdout_is_console_status = call i32 @GetConsoleMode(ptr %stdout, ptr %stdout_mode)");
        functions.AppendLine("  %stdout_is_console = icmp ne i32 %stdout_is_console_status, 0");
        functions.AppendLine("  store i1 %stdout_is_console, ptr @sollang_stdout_line_buffered, align 1");
    }

    public override void EmitExitHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdout_flushed = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)");
    }

    public override void EmitProcessEntry(StringBuilder functions)
    {
        functions.AppendLine("  %arguments_ok = call i32 @sollang_init_arguments()");
        functions.AppendLine("  %arguments_valid = icmp ne i32 %arguments_ok, 0");
        functions.AppendLine("  store i1 %arguments_valid, ptr %ok_state, align 1");
    }

    public override void EmitExitCleanup(StringBuilder functions)
    {
        functions.AppendLine("  call void @sollang_dispose_arguments()");
    }

    public override void EmitEnvironmentCleanup(StringBuilder functions)
    {
        functions.AppendLine("  call void @sollang_dispose_environment()");
    }
}
