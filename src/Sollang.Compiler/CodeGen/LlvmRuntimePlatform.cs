using System.Text;
using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.CodeGen;

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

    public abstract void EmitDirectoryPrimitives(StringBuilder functions);

    public abstract void EmitMappedFilePrimitives(StringBuilder functions);

    public abstract void EmitTimePrimitives(StringBuilder functions);

    public abstract void EmitProcessPrimitives(StringBuilder functions);

    public virtual void EmitEnvironmentPrimitives(StringBuilder functions)
    {
    }

    public abstract void EmitEntryHandles(StringBuilder functions);

    public virtual void EmitExitHandles(StringBuilder functions)
    {
    }

    public virtual void EmitProcessEntry(StringBuilder functions)
    {
    }

    public virtual bool SupportsHeapAllocation => true;

    public virtual bool SupportsMemoryMapping => true;

    public virtual bool SupportsProcessArguments => true;

    public virtual bool SupportsEnvironment => true;

    public virtual bool SupportsChildProcesses => true;

    public virtual bool SupportsDirectoryTraversal => true;

    public virtual bool SupportsAsync => false;

    public virtual bool SupportsComputePool => false;

    public virtual bool SupportsEventStreams => true;

    public bool UsesAsyncFile { get; set; }
    public bool UsesProcessRuntime { get; set; }
    public bool UsesProcessExit { get; set; }
    public bool UsesComputePool { get; set; }
    public bool UsesDirectoryTraversal { get; set; }
    public bool UsesMouseEvents { get; set; }

    public virtual void EmitComputePrimitives(StringBuilder functions)
    {
    }

    public virtual void EmitEventPrimitives(StringBuilder functions)
    {
    }

    protected static void EmitDirectoryNodePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_directory_compare_nodes(ptr %left, ptr %right) #0 {
            entry:
              %left_len_slot = getelementptr %sollang.directory_node, ptr %left, i32 0, i32 1
              %left_len = load i64, ptr %left_len_slot, align 8
              %right_len_slot = getelementptr %sollang.directory_node, ptr %right, i32 0, i32 1
              %right_len = load i64, ptr %right_len_slot, align 8
              %left_name = getelementptr i8, ptr %left, i64 24
              %right_name = getelementptr i8, ptr %right, i64 24
              %left_shorter = icmp ult i64 %left_len, %right_len
              %minimum = select i1 %left_shorter, i64 %left_len, i64 %right_len
              br label %compare

            compare:
              %index = phi i64 [ 0, %entry ], [ %next, %equal_byte ]
              %within = icmp ult i64 %index, %minimum
              br i1 %within, label %compare_byte, label %compare_length

            compare_byte:
              %left_ptr = getelementptr i8, ptr %left_name, i64 %index
              %right_ptr = getelementptr i8, ptr %right_name, i64 %index
              %left_byte = load i8, ptr %left_ptr, align 1
              %right_byte = load i8, ptr %right_ptr, align 1
              %same = icmp eq i8 %left_byte, %right_byte
              br i1 %same, label %equal_byte, label %different_byte

            equal_byte:
              %next = add i64 %index, 1
              br label %compare

            different_byte:
              %less = icmp ult i8 %left_byte, %right_byte
              %byte_result = select i1 %less, i32 -1, i32 1
              ret i32 %byte_result

            compare_length:
              %same_length = icmp eq i64 %left_len, %right_len
              br i1 %same_length, label %equal, label %different_length

            different_length:
              %length_result = select i1 %left_shorter, i32 -1, i32 1
              ret i32 %length_result

            equal:
              ret i32 0
            }

            define internal ptr @sollang_directory_insert_sorted(ptr %head, ptr %node) #0 {
            entry:
              %empty = icmp eq ptr %head, null
              br i1 %empty, label %only, label %compare_head

            only:
              %only_next = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              store ptr null, ptr %only_next, align 8
              ret ptr %node

            compare_head:
              %head_order = call i32 @sollang_directory_compare_nodes(ptr %node, ptr %head)
              %before_head = icmp slt i32 %head_order, 0
              br i1 %before_head, label %prepend, label %scan_start

            prepend:
              %prepend_next = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              store ptr %head, ptr %prepend_next, align 8
              ret ptr %node

            scan_start:
              %head_next_slot = getelementptr %sollang.directory_node, ptr %head, i32 0, i32 0
              %head_next = load ptr, ptr %head_next_slot, align 8
              br label %scan

            scan:
              %previous = phi ptr [ %head, %scan_start ], [ %current, %advance ]
              %current = phi ptr [ %head_next, %scan_start ], [ %next, %advance ]
              %at_end = icmp eq ptr %current, null
              br i1 %at_end, label %append, label %compare_current

            compare_current:
              %order = call i32 @sollang_directory_compare_nodes(ptr %node, ptr %current)
              %before = icmp slt i32 %order, 0
              br i1 %before, label %insert, label %advance

            advance:
              %current_next_slot = getelementptr %sollang.directory_node, ptr %current, i32 0, i32 0
              %next = load ptr, ptr %current_next_slot, align 8
              br label %scan

            insert:
              %node_next = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              store ptr %current, ptr %node_next, align 8
              %previous_next = getelementptr %sollang.directory_node, ptr %previous, i32 0, i32 0
              store ptr %node, ptr %previous_next, align 8
              ret ptr %head

            append:
              %append_node_next = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              store ptr null, ptr %append_node_next, align 8
              %append_previous_next = getelementptr %sollang.directory_node, ptr %previous, i32 0, i32 0
              store ptr %node, ptr %append_previous_next, align 8
              ret ptr %head
            }

            define internal void @sollang_directory_free_nodes(ptr %head) #0 {
            entry:
              br label %loop

            loop:
              %node = phi ptr [ %head, %entry ], [ %next, %body ]
              %done = icmp eq ptr %node, null
              br i1 %done, label %finish, label %body

            body:
              %next_slot = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              %next = load ptr, ptr %next_slot, align 8
              call void @sollang_free(ptr %node)
              br label %loop

            finish:
              ret void
            }

            define internal ptr @sollang_directory_serialize(ptr %head, i64 %total) #0 {
            entry:
              %empty = icmp eq i64 %total, 0
              br i1 %empty, label %empty_result, label %allocate

            empty_result:
              ret ptr null

            allocate:
              %output = call ptr @sollang_alloc(i64 %total)
              %allocated = icmp ne ptr %output, null
              br i1 %allocated, label %loop, label %allocation_failed

            allocation_failed:
              call void @sollang_directory_free_nodes(ptr %head)
              ret ptr null

            loop:
              %node = phi ptr [ %head, %allocate ], [ %next_node, %body ]
              %offset = phi i64 [ 0, %allocate ], [ %next_offset, %body ]
              %done = icmp eq ptr %node, null
              br i1 %done, label %finish, label %body

            body:
              %next_slot = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 0
              %next_node = load ptr, ptr %next_slot, align 8
              %length_slot = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 1
              %length = load i64, ptr %length_slot, align 8
              %kind_slot = getelementptr %sollang.directory_node, ptr %node, i32 0, i32 2
              %kind = load i8, ptr %kind_slot, align 1
              %record = getelementptr i8, ptr %output, i64 %offset
              store i8 %kind, ptr %record, align 1
              %length32 = trunc i64 %length to i32
              %length0 = trunc i32 %length32 to i8
              %length_shift1 = lshr i32 %length32, 8
              %length1 = trunc i32 %length_shift1 to i8
              %length_shift2 = lshr i32 %length32, 16
              %length2 = trunc i32 %length_shift2 to i8
              %length_shift3 = lshr i32 %length32, 24
              %length3 = trunc i32 %length_shift3 to i8
              %length_ptr0 = getelementptr i8, ptr %record, i64 1
              %length_ptr1 = getelementptr i8, ptr %record, i64 2
              %length_ptr2 = getelementptr i8, ptr %record, i64 3
              %length_ptr3 = getelementptr i8, ptr %record, i64 4
              store i8 %length0, ptr %length_ptr0, align 1
              store i8 %length1, ptr %length_ptr1, align 1
              store i8 %length2, ptr %length_ptr2, align 1
              store i8 %length3, ptr %length_ptr3, align 1
              %destination = getelementptr i8, ptr %record, i64 5
              %source = getelementptr i8, ptr %node, i64 24
              call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %length, i1 false)
              %record_size = add i64 %length, 5
              %next_offset = add i64 %offset, %record_size
              call void @sollang_free(ptr %node)
              br label %loop

            finish:
              ret ptr %output
            }

            """);
    }

    public void EmitMemoryOutputSinkPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal void @sollang_memory_output_sink_append(ptr %sink, ptr %data, i64 %len) #0 {
            entry:
              %data_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 0
              %length_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 1
              %capacity_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 2
              %length = load i64, ptr %length_slot, align 8
              %capacity = load i64, ptr %capacity_slot, align 8
              %required = add i64 %length, %len
              %fits = icmp ule i64 %required, %capacity
              br i1 %fits, label %append, label %grow

            grow:
              %doubled = shl i64 %capacity, 1
              %minimum = icmp ult i64 %doubled, 256
              %base_capacity = select i1 %minimum, i64 256, i64 %doubled
              %enough = icmp uge i64 %base_capacity, %required
              %new_capacity = select i1 %enough, i64 %base_capacity, i64 %required
              %new_data = call ptr @sollang_alloc(i64 %new_capacity)
              %old_data = load ptr, ptr %data_slot, align 8
              %has_old = icmp ne ptr %old_data, null
              br i1 %has_old, label %copy_old, label %publish

            copy_old:
              call void @llvm.memcpy.p0.p0.i64(ptr %new_data, ptr %old_data, i64 %length, i1 false)
              call void @sollang_free(ptr %old_data)
              br label %publish

            publish:
              store ptr %new_data, ptr %data_slot, align 8
              store i64 %new_capacity, ptr %capacity_slot, align 8
              br label %append

            append:
              %current_data = load ptr, ptr %data_slot, align 8
              %destination = getelementptr i8, ptr %current_data, i64 %length
              call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %data, i64 %len, i1 false)
              store i64 %required, ptr %length_slot, align 8
              ret void
            }

            define internal void @sollang_memory_output_sink_array_flush_prefix(ptr %sinks, i64 %count, i64 %prefix, ptr %context, ptr %writer) #0 {
            entry:
              br label %flush_loop

            flush_loop:
              %index = phi i64 [ 0, %entry ], [ %next, %flush_one_done ]
              %done = icmp eq i64 %index, %count
              br i1 %done, label %finish, label %flush_one

            flush_one:
              %sink = getelementptr %sollang.output_sink, ptr %sinks, i64 %index
              %data_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 0
              %length_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 1
              %data = load ptr, ptr %data_slot, align 8
              %length = load i64, ptr %length_slot, align 8
              %has_data = icmp ne ptr %data, null
              %in_prefix = icmp ult i64 %index, %prefix
              %should_write = and i1 %has_data, %in_prefix
              br i1 %should_write, label %write, label %dispose_one

            write:
              call void %writer(ptr %context, ptr %data, i64 %length)
              br label %dispose_one

            dispose_one:
              call void @sollang_free(ptr %data)
              br label %flush_one_done

            flush_one_done:
              %next = add i64 %index, 1
              br label %flush_loop

            finish:
              call void @sollang_free(ptr %sinks)
              ret void
            }

            define internal void @sollang_memory_output_sink_array_flush(ptr %sinks, i64 %count, ptr %context, ptr %writer) #0 {
            entry:
              call void @sollang_memory_output_sink_array_flush_prefix(ptr %sinks, i64 %count, i64 %count, ptr %context, ptr %writer)
              ret void
            }

            define internal void @sollang_memory_output_sink_array_dispose(ptr %sinks, i64 %count) #0 {
            entry:
              br label %dispose_loop

            dispose_loop:
              %index = phi i64 [ 0, %entry ], [ %next, %dispose_one ]
              %done = icmp eq i64 %index, %count
              br i1 %done, label %finish, label %dispose_one

            dispose_one:
              %sink = getelementptr %sollang.output_sink, ptr %sinks, i64 %index
              %data_slot = getelementptr %sollang.output_sink, ptr %sink, i32 0, i32 0
              %data = load ptr, ptr %data_slot, align 8
              call void @sollang_free(ptr %data)
              %next = add i64 %index, 1
              br label %dispose_loop

            finish:
              call void @sollang_free(ptr %sinks)
              ret void
            }

            """);
    }

    public virtual void EmitAsyncPrimitives(StringBuilder functions)
    {
        if (UsesAsyncFile)
        {
            functions.AppendLine("""
            define internal void @sollang_file_push_request(ptr %control) #0 {
            entry:
              %next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 16
              br label %push

            push:
              %head = load atomic ptr, ptr @sollang_file_request_head acquire, align 8
              store ptr %head, ptr %next_slot, align 8
              %exchange = cmpxchg ptr @sollang_file_request_head, ptr %head, ptr %control release monotonic
              %pushed = extractvalue { ptr, i1 } %exchange, 1
              br i1 %pushed, label %signal, label %push

            signal:
              call void @sollang_platform_file_worker_signal_request()
              ret void
            }

            define internal void @sollang_file_push_completion(ptr %control) #0 {
            entry:
              %next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 16
              br label %push

            push:
              %head = load atomic ptr, ptr @sollang_file_completion_head acquire, align 8
              store ptr %head, ptr %next_slot, align 8
              %exchange = cmpxchg ptr @sollang_file_completion_head, ptr %head, ptr %control release monotonic
              %pushed = extractvalue { ptr, i1 } %exchange, 1
              br i1 %pushed, label %done, label %push

            done:
              ret void
            }

            define internal void @sollang_file_worker_run() #0 {
            entry:
              br label %wait

            wait:
              call void @sollang_platform_file_worker_wait_request()
              %stopping_value = load atomic i32, ptr @sollang_file_worker_stopping acquire, align 4
              %stopping = icmp ne i32 %stopping_value, 0
              br i1 %stopping, label %stopped, label %take_batch

            take_batch:
              %batch = atomicrmw xchg ptr @sollang_file_request_head, ptr null acq_rel
              br label %reverse

            reverse:
              %current = phi ptr [ %batch, %take_batch ], [ %next, %reverse_one ]
              %reversed = phi ptr [ null, %take_batch ], [ %current, %reverse_one ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %reverse_one, label %process

            reverse_one:
              %current_next_slot = getelementptr %sollang.task_control, ptr %current, i32 0, i32 16
              %next = load ptr, ptr %current_next_slot, align 8
              store ptr %reversed, ptr %current_next_slot, align 8
              br label %reverse

            process:
              %request = phi ptr [ %reversed, %reverse ], [ %next_request_value, %process_bridge ]
              %has_request = icmp ne ptr %request, null
              br i1 %has_request, label %read, label %signal

            read:
              %request_next_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 16
              %request_next = load ptr, ptr %request_next_slot, align 8
              %phase_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled_before_read = icmp eq i32 %phase, 3
              br i1 %cancelled_before_read, label %record_cancelled, label %perform_read

            perform_read:
              %size_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 11
              %size = load i32, ptr %size_slot, align 4
              %size64 = zext i32 %size to i64
              %data_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 13
              %explicit_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 19
              %explicit = load i32, ptr %explicit_slot, align 4
              %is_explicit = icmp ne i32 %explicit, 0
              br i1 %is_explicit, label %inspect_owned_operation, label %perform_compatibility_read

            perform_compatibility_read:
              %compatibility_result = call %sollang.file_count_result @sollang_platform_read_file_bytes(ptr %data_slot, i64 %size64)
              br label %record_read

            inspect_owned_operation:
              %operation_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 20
              %operation = load i32, ptr %operation_slot, align 4
              %is_open_read = icmp eq i32 %operation, 3
              br i1 %is_open_read, label %perform_owned_open_read, label %inspect_owned_open_write

            inspect_owned_open_write:
              %is_open_write = icmp eq i32 %operation, 4
              br i1 %is_open_write, label %perform_owned_open_write, label %inspect_owned_sync

            inspect_owned_sync:
              %is_sync = icmp eq i32 %operation, 2
              br i1 %is_sync, label %perform_owned_sync, label %inspect_owned_transfer

            inspect_owned_transfer:
              %is_write = icmp eq i32 %operation, 1
              br i1 %is_write, label %perform_owned_write, label %perform_owned_read

            perform_owned_read:
              %owned_handle_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 17
              %owned_handle = load i64, ptr %owned_handle_slot, align 8
              %owned_offset_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 18
              %owned_offset = load i64, ptr %owned_offset_slot, align 8
              %owned_result = call %sollang.file_count_result @sollang_platform_read_owned_file_at(i64 %owned_handle, ptr %data_slot, i64 %size64, i64 %owned_offset)
              br label %record_read

            perform_owned_write:
              %write_handle_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 17
              %write_handle = load i64, ptr %write_handle_slot, align 8
              %write_offset_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 18
              %write_offset = load i64, ptr %write_offset_slot, align 8
              %write_result = call %sollang.file_count_result @sollang_platform_write_owned_file_at(i64 %write_handle, ptr %data_slot, i64 %size64, i64 %write_offset)
              br label %record_read

            perform_owned_sync:
              %sync_handle_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 17
              %sync_handle = load i64, ptr %sync_handle_slot, align 8
              %sync_ok = call i32 @sollang_platform_sync_owned_file(i64 %sync_handle)
              %sync_result0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %sync_result = insertvalue %sollang.file_count_result %sync_result0, i32 %sync_ok, 1
              br label %record_read

            perform_owned_open_read:
              %open_read_path = load ptr, ptr %data_slot, align 8
              %open_read_length_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 18
              %open_read_length = load i64, ptr %open_read_length_slot, align 8
              %open_read_result = call %sollang.file_handle_result @sollang_platform_open_owned_read_file(ptr %open_read_path, i64 %open_read_length)
              br label %record_open

            perform_owned_open_write:
              %open_write_path = load ptr, ptr %data_slot, align 8
              %open_write_length_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 18
              %open_write_length = load i64, ptr %open_write_length_slot, align 8
              %open_write_result = call %sollang.file_handle_result @sollang_platform_open_owned_write_file(ptr %open_write_path, i64 %open_write_length)
              br label %record_open

            record_open:
              %open_result = phi %sollang.file_handle_result [ %open_read_result, %perform_owned_open_read ], [ %open_write_result, %perform_owned_open_write ]
              %open_handle = extractvalue %sollang.file_handle_result %open_result, 0
              %open_ok = extractvalue %sollang.file_handle_result %open_result, 1
              %open_handle_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 17
              store i64 %open_handle, ptr %open_handle_slot, align 8
              %open_count_result0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %open_count_result = insertvalue %sollang.file_count_result %open_count_result0, i32 %open_ok, 1
              br label %record_read

            record_read:
              %result = phi %sollang.file_count_result [ %compatibility_result, %perform_compatibility_read ], [ %owned_result, %perform_owned_read ], [ %write_result, %perform_owned_write ], [ %sync_result, %perform_owned_sync ], [ %open_count_result, %record_open ]
              %count = extractvalue %sollang.file_count_result %result, 0
              %ok = extractvalue %sollang.file_count_result %result, 1
              %count_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 14
              store i64 %count, ptr %count_slot, align 8
              %ok_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 15
              store i32 %ok, ptr %ok_slot, align 4
              %phase_after_read = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled_after_read = icmp eq i32 %phase_after_read, 3
              br i1 %cancelled_after_read, label %queue_completion, label %record_complete

            record_complete:
              store atomic i32 2, ptr %phase_slot release, align 4
              br label %queue_completion

            record_cancelled:
              %cancel_count_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 14
              store i64 0, ptr %cancel_count_slot, align 8
              %cancel_ok_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 15
              store i32 0, ptr %cancel_ok_slot, align 4
              br label %queue_completion

            queue_completion:
              %completion_explicit_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 19
              %completion_explicit = load i32, ptr %completion_explicit_slot, align 4
              %completion_is_duplicate = icmp eq i32 %completion_explicit, 1
              br i1 %completion_is_duplicate, label %close_owned_request, label %inspect_transferred_open

            inspect_transferred_open:
              %completion_is_open = icmp eq i32 %completion_explicit, 2
              br i1 %completion_is_open, label %inspect_open_completion, label %push_completed_request

            inspect_open_completion:
              %open_completion_phase = load atomic i32, ptr %phase_slot acquire, align 4
              %open_completion_cancelled = icmp eq i32 %open_completion_phase, 3
              br i1 %open_completion_cancelled, label %close_owned_request, label %push_completed_request

            close_owned_request:
              %completion_handle_slot = getelementptr %sollang.task_control, ptr %request, i32 0, i32 17
              %completion_handle = load i64, ptr %completion_handle_slot, align 8
              call void @sollang_platform_close_owned_file(i64 %completion_handle)
              store i32 0, ptr %completion_explicit_slot, align 4
              br label %push_completed_request

            push_completed_request:
              call void @sollang_file_push_completion(ptr %request)
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
              call void @sollang_platform_file_worker_signal_completion()
              br label %wait

            stopped:
              ret void
            }

            define internal i1 @sollang_file_drain_completions() #0 {
            entry:
              %batch = atomicrmw xchg ptr @sollang_file_completion_head, ptr null acq_rel
              %has_batch = icmp ne ptr %batch, null
              br i1 %has_batch, label %clear_signal, label %none

            clear_signal:
              call void @sollang_platform_file_worker_clear_completion()
              br label %reverse

            reverse:
              %current = phi ptr [ %batch, %clear_signal ], [ %next, %reverse_one ]
              %reversed = phi ptr [ null, %clear_signal ], [ %current, %reverse_one ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %reverse_one, label %drain

            reverse_one:
              %current_next_slot = getelementptr %sollang.task_control, ptr %current, i32 0, i32 16
              %next = load ptr, ptr %current_next_slot, align 8
              store ptr %reversed, ptr %current_next_slot, align 8
              br label %reverse

            drain:
              %control = phi ptr [ %reversed, %reverse ], [ %control_next, %continue ]
              %has_control = icmp ne ptr %control, null
              br i1 %has_control, label %inspect, label %done

            inspect:
              %control_next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 16
              %control_next = load ptr, ptr %control_next_slot, align 8
              store ptr null, ptr %control_next_slot, align 8
              %phase_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %cancelled = icmp eq i32 %phase, 3
              br i1 %cancelled, label %destroy_cancelled, label %wake

            destroy_cancelled:
              %cancel_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 6
              %cancel = load ptr, ptr %cancel_slot, align 8
              call void %cancel(ptr %control)
              call void @sollang_free(ptr %control)
              br label %continue

            wake:
              call void @sollang_task_enqueue_ready(ptr %control)
              br label %continue

            continue:
              %remaining = atomicrmw sub ptr @sollang_file_outstanding, i64 1 acq_rel
              br label %drain

            done:
              ret i1 true

            none:
              ret i1 false
            }

            define internal void @sollang_file_submit(ptr %control, i32 %size) #0 {
            entry:
              %started = load i1, ptr @sollang_file_worker_started, align 1
              br i1 %started, label %initialize, label %start

            start:
              %start_ok = call i1 @sollang_platform_file_worker_start()
              br i1 %start_ok, label %mark_started, label %failed

            mark_started:
              store i1 true, ptr @sollang_file_worker_started, align 1
              br label %initialize

            initialize:
              %size_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 11
              store i32 %size, ptr %size_slot, align 4
              %phase_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 12
              store atomic i32 1, ptr %phase_slot release, align 4
              %status_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 4
              store i32 6, ptr %status_slot, align 4
              %old_count = atomicrmw add ptr @sollang_file_outstanding, i64 1 acq_rel
              call void @sollang_file_push_request(ptr %control)
              ret void

            failed:
              call void @llvm.trap()
              unreachable
            }

            define internal void @sollang_file_wait_idle() #0 {
            entry:
              br label %check

            check:
              %outstanding = load atomic i64, ptr @sollang_file_outstanding acquire, align 8
              %idle = icmp eq i64 %outstanding, 0
              br i1 %idle, label %done, label %wait

            wait:
              call void @sollang_platform_file_worker_wait_completion(i64 -1)
              %progress = call i1 @sollang_file_drain_completions()
              br label %check

            done:
              ret void
            }

            define internal void @sollang_async_shutdown() #0 {
            entry:
              %started = load i1, ptr @sollang_file_worker_started, align 1
              br i1 %started, label %stop, label %done

            stop:
              call void @sollang_file_wait_idle()
              store atomic i32 1, ptr @sollang_file_worker_stopping release, align 4
              call void @sollang_platform_file_worker_signal_request()
              call void @sollang_platform_file_worker_join()
              store i1 false, ptr @sollang_file_worker_started, align 1
              br label %done

            done:
              ret void
            }

            define internal i1 @sollang_file_operation_task_worker(ptr %control) #0 {
            entry:
              %phase_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 12
              %phase = load atomic i32, ptr %phase_slot acquire, align 4
              %initial = icmp eq i32 %phase, 0
              br i1 %initial, label %submit, label %inspect

            submit:
              %size_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 11
              %size = load i32, ptr %size_slot, align 4
              call void @sollang_file_submit(ptr %control, i32 %size)
              ret i1 false

            inspect:
              %complete = icmp eq i32 %phase, 2
              ret i1 %complete
            }

            define internal void @sollang_file_operation_task_cancel(ptr %control) #0 {
            entry:
              %explicit_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 19
              %explicit = load i32, ptr %explicit_slot, align 4
              %owns_handle = icmp ne i32 %explicit, 0
              br i1 %owns_handle, label %close_handle, label %destroy_context

            close_handle:
              %handle_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 17
              %handle = load i64, ptr %handle_slot, align 8
              call void @sollang_platform_close_owned_file(i64 %handle)
              store i32 0, ptr %explicit_slot, align 4
              br label %destroy_context

            destroy_context:
              %context_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void @sollang_free(ptr %context)
              ret void
            }

            """);
        }
        else
        {
            functions.AppendLine("""
            define internal i1 @sollang_file_drain_completions() #0 {
            entry:
              ret i1 false
            }

            define internal void @sollang_platform_file_worker_wait_completion(i64 %requested) #0 {
            entry:
              ret void
            }

            define internal void @sollang_file_wait_idle() #0 {
            entry:
              ret void
            }

            define internal void @sollang_async_shutdown() #0 {
            entry:
              ret void
            }

            """);
        }

        functions.AppendLine("""

            define internal void @sollang_task_enqueue_ready(ptr %control) #0 {
            entry:
              %status_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 4
              store i32 0, ptr %status_slot, align 4
              %next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 3
              store ptr null, ptr %next_slot, align 8
              %tail = load ptr, ptr @sollang_task_ready_tail, align 8
              %empty = icmp eq ptr %tail, null
              br i1 %empty, label %first, label %append

            first:
              store ptr %control, ptr @sollang_task_ready_head, align 8
              br label %queued

            append:
              %tail_next_slot = getelementptr %sollang.task_control, ptr %tail, i32 0, i32 3
              store ptr %control, ptr %tail_next_slot, align 8
              br label %queued

            queued:
              store ptr %control, ptr @sollang_task_ready_tail, align 8
              ret void
            }

            define internal void @sollang_task_park_on(ptr %parent, ptr %child) #0 {
            entry:
              %child_status_slot = getelementptr %sollang.task_control, ptr %child, i32 0, i32 4
              %child_status = load i32, ptr %child_status_slot, align 4
              %completed = icmp eq i32 %child_status, 2
              br i1 %completed, label %done, label %park

            park:
              %child_waiter_slot = getelementptr %sollang.task_control, ptr %child, i32 0, i32 9
              %existing_waiter = load ptr, ptr %child_waiter_slot, align 8
              %available = icmp eq ptr %existing_waiter, null
              br i1 %available, label %attach, label %invalid

            attach:
              store ptr %parent, ptr %child_waiter_slot, align 8
              %parent_child_slot = getelementptr %sollang.task_control, ptr %parent, i32 0, i32 10
              store ptr %child, ptr %parent_child_slot, align 8
              %parent_status_slot = getelementptr %sollang.task_control, ptr %parent, i32 0, i32 4
              store i32 5, ptr %parent_status_slot, align 4
              br label %done

            invalid:
              call void @llvm.trap()
              unreachable

            done:
              ret void
            }

            define internal void @sollang_task_wake_waiter(ptr %child) #0 {
            entry:
              %waiter_slot = getelementptr %sollang.task_control, ptr %child, i32 0, i32 9
              %waiter = load ptr, ptr %waiter_slot, align 8
              %has_waiter = icmp ne ptr %waiter, null
              br i1 %has_waiter, label %wake, label %done

            wake:
              store ptr null, ptr %waiter_slot, align 8
              %waiter_child_slot = getelementptr %sollang.task_control, ptr %waiter, i32 0, i32 10
              store ptr null, ptr %waiter_child_slot, align 8
              call void @sollang_task_enqueue_ready(ptr %waiter)
              br label %done

            done:
              ret void
            }

            define internal void @sollang_timer_park(ptr %control, i64 %deadline) #0 {
            entry:
              %status_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 4
              store i32 4, ptr %status_slot, align 4
              %deadline_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 8
              store i64 %deadline, ptr %deadline_slot, align 8
              %head = load ptr, ptr @sollang_task_timer_head, align 8
              br label %scan

            scan:
              %previous = phi ptr [ null, %entry ], [ %current, %advance ]
              %current = phi ptr [ %head, %entry ], [ %next, %advance ]
              %has_current = icmp ne ptr %current, null
              br i1 %has_current, label %compare, label %insert

            compare:
              %current_deadline_slot = getelementptr %sollang.task_control, ptr %current, i32 0, i32 8
              %current_deadline = load i64, ptr %current_deadline_slot, align 8
              %before_current = icmp slt i64 %deadline, %current_deadline
              br i1 %before_current, label %insert, label %advance

            advance:
              %current_next_slot = getelementptr %sollang.task_control, ptr %current, i32 0, i32 7
              %next = load ptr, ptr %current_next_slot, align 8
              br label %scan

            insert:
              %control_next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 7
              store ptr %current, ptr %control_next_slot, align 8
              %at_head = icmp eq ptr %previous, null
              br i1 %at_head, label %insert_head, label %insert_after

            insert_head:
              store ptr %control, ptr @sollang_task_timer_head, align 8
              ret void

            insert_after:
              %previous_next_slot = getelementptr %sollang.task_control, ptr %previous, i32 0, i32 7
              store ptr %control, ptr %previous_next_slot, align 8
              ret void
            }

            define internal void @sollang_timer_wake_due() #0 {
            entry:
              br label %scan

            scan:
              %head = load ptr, ptr @sollang_task_timer_head, align 8
              %has_head = icmp ne ptr %head, null
              br i1 %has_head, label %check, label %done

            check:
              %deadline_slot = getelementptr %sollang.task_control, ptr %head, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @sollang_now_millis()
              %due = icmp sge i64 %now, %deadline
              br i1 %due, label %wake, label %done

            wake:
              %timer_next_slot = getelementptr %sollang.task_control, ptr %head, i32 0, i32 7
              %timer_next = load ptr, ptr %timer_next_slot, align 8
              store ptr %timer_next, ptr @sollang_task_timer_head, align 8
              store ptr null, ptr %timer_next_slot, align 8
              call void @sollang_task_enqueue_ready(ptr %head)
              br label %scan

            done:
              ret void
            }

            define internal i1 @sollang_timer_wait_next() #0 {
            entry:
              %file_progress = call i1 @sollang_file_drain_completions()
              call void @sollang_timer_wake_due()
              %ready = load ptr, ptr @sollang_task_ready_head, align 8
              %has_ready = icmp ne ptr %ready, null
              br i1 %has_ready, label %progress, label %inspect_timer

            inspect_timer:
              %timer = load ptr, ptr @sollang_task_timer_head, align 8
              %has_timer = icmp ne ptr %timer, null
              %file_outstanding = load atomic i64, ptr @sollang_file_outstanding acquire, align 8
              %has_file = icmp ne i64 %file_outstanding, 0
              br i1 %has_timer, label %timer_timeout, label %inspect_file

            inspect_file:
              br i1 %has_file, label %wait_file_only, label %stalled

            timer_timeout:
              %deadline_slot = getelementptr %sollang.task_control, ptr %timer, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @sollang_now_millis()
              %remaining_raw = sub i64 %deadline, %now
              %positive = icmp sgt i64 %remaining_raw, 0
              %remaining = select i1 %positive, i64 %remaining_raw, i64 0
              br i1 %has_file, label %wait_file_or_timer, label %wait_timer_only

            wait_file_or_timer:
              call void @sollang_platform_file_worker_wait_completion(i64 %remaining)
              %file_timer_progress = call i1 @sollang_file_drain_completions()
              call void @sollang_timer_wake_due()
              br label %progress

            wait_file_only:
              call void @sollang_platform_file_worker_wait_completion(i64 -1)
              %file_only_progress = call i1 @sollang_file_drain_completions()
              br label %progress

            wait_timer_only:
              call void @sollang_wait_millis(i64 %remaining)
              call void @sollang_timer_wake_due()
              br label %progress

            progress:
              ret i1 true

            stalled:
              ret i1 false
            }

            define internal i1 @sollang_sleep_worker(ptr %control) #0 {
            entry:
              %deadline_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 8
              %deadline = load i64, ptr %deadline_slot, align 8
              %now = call i64 @sollang_now_millis()
              %ready = icmp sge i64 %now, %deadline
              br i1 %ready, label %complete, label %pending

            complete:
              ret i1 true

            pending:
              call void @sollang_timer_park(ptr %control, i64 %deadline)
              ret i1 false
            }

            define internal void @sollang_sleep_cancel(ptr %control) #0 {
            entry:
              %context_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void @sollang_free(ptr %context)
              ret void
            }

            define internal ptr @sollang_task_start(ptr %worker, ptr %destroy, ptr %cancel, ptr %context) #0 {
            entry:
              %control = call ptr @sollang_alloc(i64 152)
              %allocated = icmp ne ptr %control, null
              br i1 %allocated, label %initialize, label %fail

            initialize:
              %context_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 0
              store ptr %context, ptr %context_slot, align 8
              %resume_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 1
              store ptr %worker, ptr %resume_slot, align 8
              %destroy_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 2
              store ptr %destroy, ptr %destroy_slot, align 8
              %next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 3
              store ptr null, ptr %next_slot, align 8
              %status_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 4
              store i32 0, ptr %status_slot, align 4
              %state_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 5
              store i32 0, ptr %state_slot, align 4
              %cancel_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 6
              store ptr %cancel, ptr %cancel_slot, align 8
              %timer_next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 7
              store ptr null, ptr %timer_next_slot, align 8
              %deadline_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 8
              store i64 0, ptr %deadline_slot, align 8
              %waiter_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 9
              store ptr null, ptr %waiter_slot, align 8
              %waiting_child_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 10
              store ptr null, ptr %waiting_child_slot, align 8
              %file_size_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 11
              store i32 0, ptr %file_size_slot, align 4
              %file_phase_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 12
              store i32 0, ptr %file_phase_slot, align 4
              %file_data_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 13
              store i64 0, ptr %file_data_slot, align 8
              %file_count_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 14
              store i64 0, ptr %file_count_slot, align 8
              %file_ok_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 15
              store i32 0, ptr %file_ok_slot, align 4
              %file_next_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 16
              store ptr null, ptr %file_next_slot, align 8
              %file_handle_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 17
              store i64 -1, ptr %file_handle_slot, align 8
              %file_offset_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 18
              store i64 0, ptr %file_offset_slot, align 8
              %file_explicit_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 19
              store i32 0, ptr %file_explicit_slot, align 4
              %file_operation_slot = getelementptr %sollang.task_control, ptr %control, i32 0, i32 20
              store i32 0, ptr %file_operation_slot, align 4
              %tail = load ptr, ptr @sollang_task_ready_tail, align 8
              %empty = icmp eq ptr %tail, null
              br i1 %empty, label %first, label %append

            first:
              store ptr %control, ptr @sollang_task_ready_head, align 8
              br label %queued

            append:
              %tail_next_slot = getelementptr %sollang.task_control, ptr %tail, i32 0, i32 3
              store ptr %control, ptr %tail_next_slot, align 8
              br label %queued

            queued:
              store ptr %control, ptr @sollang_task_ready_tail, align 8
              ret ptr %control

            fail:
              ret ptr null
            }

            define internal i1 @sollang_task_join(ptr %handle) #0 {
            entry:
              br label %poll

            poll:
              %target_status_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 4
              %target_status = load i32, ptr %target_status_slot, align 4
              %completed = icmp eq i32 %target_status, 2
              br i1 %completed, label %success, label %dequeue

            dequeue:
              %ready = load ptr, ptr @sollang_task_ready_head, align 8
              %has_ready = icmp ne ptr %ready, null
              br i1 %has_ready, label %run, label %wait

            wait:
              %timer_progress = call i1 @sollang_timer_wait_next()
              br i1 %timer_progress, label %poll, label %fail

            run:
              %next_slot = getelementptr %sollang.task_control, ptr %ready, i32 0, i32 3
              %next = load ptr, ptr %next_slot, align 8
              store ptr %next, ptr @sollang_task_ready_head, align 8
              %became_empty = icmp eq ptr %next, null
              br i1 %became_empty, label %clear_tail, label %invoke

            clear_tail:
              store ptr null, ptr @sollang_task_ready_tail, align 8
              br label %invoke

            invoke:
              %ready_status_slot = getelementptr %sollang.task_control, ptr %ready, i32 0, i32 4
              store i32 1, ptr %ready_status_slot, align 4
              %resume_slot = getelementptr %sollang.task_control, ptr %ready, i32 0, i32 1
              %worker = load ptr, ptr %resume_slot, align 8
              %worker_complete = call i1 %worker(ptr %ready)
              br i1 %worker_complete, label %complete, label %requeue

            complete:
              store i32 2, ptr %ready_status_slot, align 4
              call void @sollang_task_wake_waiter(ptr %ready)
              br label %poll

            requeue:
              %pending_status = load i32, ptr %ready_status_slot, align 4
              %waiting = icmp uge i32 %pending_status, 4
              br i1 %waiting, label %poll, label %requeue_ready

            requeue_ready:
              store i32 0, ptr %ready_status_slot, align 4
              store ptr null, ptr %next_slot, align 8
              %tail = load ptr, ptr @sollang_task_ready_tail, align 8
              %queue_empty = icmp eq ptr %tail, null
              br i1 %queue_empty, label %requeue_first, label %requeue_append

            requeue_first:
              store ptr %ready, ptr @sollang_task_ready_head, align 8
              br label %requeued

            requeue_append:
              %tail_next_slot = getelementptr %sollang.task_control, ptr %tail, i32 0, i32 3
              store ptr %ready, ptr %tail_next_slot, align 8
              br label %requeued

            requeued:
              store ptr %ready, ptr @sollang_task_ready_tail, align 8
              br label %poll

            success:
              ret i1 true

            fail:
              ret i1 false
            }

            define internal i1 @sollang_task_release(ptr %handle) #0 {
            entry:
              %status_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 4
              %status = load i32, ptr %status_slot, align 4
              %completed = icmp eq i32 %status, 2
              br i1 %completed, label %destroy, label %fail

            destroy:
              %destroy_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 2
              %destroy_fn = load ptr, ptr %destroy_slot, align 8
              %context_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 0
              %context = load ptr, ptr %context_slot, align 8
              call void %destroy_fn(ptr %context)
              call void @sollang_free(ptr %handle)
              ret i1 true

            fail:
              ret i1 false
            }

            define internal i1 @sollang_task_cancel(ptr %handle) #0 {
            entry:
              %status_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 4
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
              %file_phase_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 12
              store atomic i32 3, ptr %file_phase_slot release, align 4
              store i32 3, ptr %status_slot, align 4
              ret i1 true

            detach_child:
              %waiting_child_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 10
              %child = load ptr, ptr %waiting_child_slot, align 8
              %has_child = icmp ne ptr %child, null
              br i1 %has_child, label %clear_child_waiter, label %fail

            clear_child_waiter:
              %child_waiter_slot = getelementptr %sollang.task_control, ptr %child, i32 0, i32 9
              store ptr null, ptr %child_waiter_slot, align 8
              store ptr null, ptr %waiting_child_slot, align 8
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            find:
              %head = load ptr, ptr @sollang_task_ready_head, align 8
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
              %current_next_slot = getelementptr %sollang.task_control, ptr %current, i32 0, i32 3
              %next = load ptr, ptr %current_next_slot, align 8
              br label %scan

            unlink:
              %handle_next_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 3
              %handle_next = load ptr, ptr %handle_next_slot, align 8
              %is_head = icmp eq ptr %previous, null
              br i1 %is_head, label %unlink_head, label %unlink_after

            unlink_head:
              store ptr %handle_next, ptr @sollang_task_ready_head, align 8
              br label %repair_tail

            unlink_after:
              %previous_next_slot = getelementptr %sollang.task_control, ptr %previous, i32 0, i32 3
              store ptr %handle_next, ptr %previous_next_slot, align 8
              br label %repair_tail

            repair_tail:
              %tail = load ptr, ptr @sollang_task_ready_tail, align 8
              %is_tail = icmp eq ptr %tail, %handle
              br i1 %is_tail, label %replace_tail, label %mark_cancelled

            replace_tail:
              store ptr %previous, ptr @sollang_task_ready_tail, align 8
              br label %mark_cancelled

            mark_cancelled:
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            timer_find:
              %timer_head = load ptr, ptr @sollang_task_timer_head, align 8
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
              %timer_current_next_slot = getelementptr %sollang.task_control, ptr %timer_current, i32 0, i32 7
              %timer_next = load ptr, ptr %timer_current_next_slot, align 8
              br label %timer_scan

            timer_unlink:
              %handle_timer_next_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 7
              %handle_timer_next = load ptr, ptr %handle_timer_next_slot, align 8
              %timer_is_head = icmp eq ptr %timer_previous, null
              br i1 %timer_is_head, label %timer_unlink_head, label %timer_unlink_after

            timer_unlink_head:
              store ptr %handle_timer_next, ptr @sollang_task_timer_head, align 8
              br label %timer_mark_cancelled

            timer_unlink_after:
              %timer_previous_next_slot = getelementptr %sollang.task_control, ptr %timer_previous, i32 0, i32 7
              store ptr %handle_timer_next, ptr %timer_previous_next_slot, align 8
              br label %timer_mark_cancelled

            timer_mark_cancelled:
              store ptr null, ptr %handle_timer_next_slot, align 8
              store i32 3, ptr %status_slot, align 4
              br label %destroy

            destroy:
              %cancel_slot = getelementptr %sollang.task_control, ptr %handle, i32 0, i32 6
              %cancel_fn = load ptr, ptr %cancel_slot, align 8
              call void %cancel_fn(ptr %handle)
              call void @sollang_free(ptr %handle)
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
            _ => throw new SollangException($"unsupported target '{target}'")
        };
    }
}
