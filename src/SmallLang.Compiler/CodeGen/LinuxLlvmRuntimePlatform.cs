using System.Text;

namespace SmallLang.Compiler.CodeGen;

internal sealed class LinuxLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "x86_64-unknown-linux-gnu";

    public override string EntryPointName => "main";

    public override string EntryPointParameters => "i32 %argc, ptr %argv";

    public override bool SupportsAsync => true;

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@smalllang_file_writer_fd = internal global i32 -1");
        globals.AppendLine("@smalllang_file_reader_fd = internal global i32 -1");
        globals.AppendLine("@smalllang_argument_count_value = internal global i64 0");
        globals.AppendLine("@smalllang_argument_vector = internal global ptr null");
        if (UsesAsyncFile)
        {
            globals.AppendLine("@smalllang_file_request_event_fd = internal global i32 -1");
            globals.AppendLine("@smalllang_file_completion_event_fd = internal global i32 -1");
            globals.AppendLine("@smalllang_file_worker_thread = internal global i64 0");
        }
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare i64 @write(i32, ptr, i64)");
        functions.AppendLine("declare i64 @read(i32, ptr, i64)");
        functions.AppendLine("declare i64 @pread(i32, ptr, i64, i64)");
        functions.AppendLine("declare i64 @pwrite(i32, ptr, i64, i64)");
        functions.AppendLine("declare i32 @open(ptr, i32, i32)");
        functions.AppendLine("declare i32 @close(i32)");
        functions.AppendLine("declare i32 @dup(i32)");
        functions.AppendLine("declare i32 @fsync(i32)");
        functions.AppendLine("declare i64 @lseek(i32, i64, i32)");
        functions.AppendLine("declare i32 @ftruncate(i32, i64)");
        functions.AppendLine("declare ptr @mmap(ptr, i64, i32, i32, i32, i64)");
        functions.AppendLine("declare i32 @munmap(ptr, i64)");
        functions.AppendLine("declare i32 @msync(ptr, i64, i32)");
        functions.AppendLine("declare i32 @clock_gettime(i32, ptr)");
        functions.AppendLine("declare i32 @nanosleep(ptr, ptr)");
        functions.AppendLine("declare ptr @getenv(ptr)");
        functions.AppendLine("declare i32 @posix_spawnp(ptr, ptr, ptr, ptr, ptr, ptr)");
        functions.AppendLine("declare i32 @waitpid(i32, ptr, i32)");
        if (UsesAsyncFile)
        {
            functions.AppendLine("declare i32 @eventfd(i32, i32)");
            functions.AppendLine("declare i32 @poll(ptr, i64, i32)");
            functions.AppendLine("declare i32 @pthread_create(ptr, ptr, ptr, ptr)");
            functions.AppendLine("declare i32 @pthread_join(i64, ptr)");
        }
        functions.AppendLine("@environ = external global ptr");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare ptr @malloc(i64)");
        functions.AppendLine("declare void @free(ptr)");
    }

    public override void EmitAsyncPrimitives(StringBuilder functions)
    {
        base.EmitAsyncPrimitives(functions);
        if (!UsesAsyncFile)
        {
            return;
        }
        functions.AppendLine("""
            define internal ptr @smalllang_linux_file_worker(ptr %unused) #0 {
            entry:
              call void @smalllang_file_worker_run()
              ret ptr null
            }

            define internal i1 @smalllang_platform_file_worker_start() #0 {
            entry:
              %request_fd = call i32 @eventfd(i32 0, i32 0)
              %request_ok = icmp sge i32 %request_fd, 0
              br i1 %request_ok, label %completion, label %fail

            completion:
              store i32 %request_fd, ptr @smalllang_file_request_event_fd, align 4
              %completion_fd = call i32 @eventfd(i32 0, i32 0)
              %completion_ok = icmp sge i32 %completion_fd, 0
              br i1 %completion_ok, label %thread, label %fail

            thread:
              store i32 %completion_fd, ptr @smalllang_file_completion_event_fd, align 4
              %thread_slot = alloca i64, align 8
              %create = call i32 @pthread_create(ptr %thread_slot, ptr null, ptr @smalllang_linux_file_worker, ptr null)
              %thread_ok = icmp eq i32 %create, 0
              br i1 %thread_ok, label %ready, label %fail

            ready:
              %worker_thread = load i64, ptr %thread_slot, align 8
              store i64 %worker_thread, ptr @smalllang_file_worker_thread, align 8
              ret i1 true

            fail:
              ret i1 false
            }

            define internal void @smalllang_platform_file_worker_signal_request() #0 {
            entry:
              %value = alloca i64, align 8
              store i64 1, ptr %value, align 8
              %fd = load i32, ptr @smalllang_file_request_event_fd, align 4
              %ignored = call i64 @write(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @smalllang_platform_file_worker_wait_request() #0 {
            entry:
              %value = alloca i64, align 8
              %fd = load i32, ptr @smalllang_file_request_event_fd, align 4
              %ignored = call i64 @read(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @smalllang_platform_file_worker_signal_completion() #0 {
            entry:
              %value = alloca i64, align 8
              store i64 1, ptr %value, align 8
              %fd = load i32, ptr @smalllang_file_completion_event_fd, align 4
              %ignored = call i64 @write(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @smalllang_platform_file_worker_clear_completion() #0 {
            entry:
              %value = alloca i64, align 8
              %fd = load i32, ptr @smalllang_file_completion_event_fd, align 4
              %ignored = call i64 @read(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @smalllang_platform_file_worker_wait_completion(i64 %requested) #0 {
            entry:
              %pollfd = alloca [8 x i8], align 4
              %fd = load i32, ptr @smalllang_file_completion_event_fd, align 4
              store i32 %fd, ptr %pollfd, align 4
              %events_slot = getelementptr i8, ptr %pollfd, i64 4
              store i16 1, ptr %events_slot, align 2
              %revents_slot = getelementptr i8, ptr %pollfd, i64 6
              store i16 0, ptr %revents_slot, align 2
              %infinite = icmp slt i64 %requested, 0
              %too_large = icmp sgt i64 %requested, 2147483647
              %bounded = select i1 %too_large, i64 2147483647, i64 %requested
              %finite = trunc i64 %bounded to i32
              %timeout = select i1 %infinite, i32 -1, i32 %finite
              %ignored = call i32 @poll(ptr %pollfd, i64 1, i32 %timeout)
              ret void
            }

            define internal void @smalllang_platform_file_worker_join() #0 {
            entry:
              %thread = load i64, ptr @smalllang_file_worker_thread, align 8
              %joined = call i32 @pthread_join(i64 %thread, ptr null)
              %request_fd = load i32, ptr @smalllang_file_request_event_fd, align 4
              %closed_request = call i32 @close(i32 %request_fd)
              %completion_fd = load i32, ptr @smalllang_file_completion_event_fd, align 4
              %closed_completion = call i32 @close(i32 %completion_fd)
              store i64 0, ptr @smalllang_file_worker_thread, align 8
              store i32 -1, ptr @smalllang_file_request_event_fd, align 4
              store i32 -1, ptr @smalllang_file_completion_event_fd, align 4
              ret void
            }

            """);
    }

    public override void EmitTimePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @smalllang_now_millis() #0 {
            entry:
              %ts = alloca [2 x i64], align 8
              %ignored = call i32 @clock_gettime(i32 1, ptr %ts)
              %sec_ptr = getelementptr inbounds [2 x i64], ptr %ts, i64 0, i64 0
              %nsec_ptr = getelementptr inbounds [2 x i64], ptr %ts, i64 0, i64 1
              %sec = load i64, ptr %sec_ptr, align 8
              %nsec = load i64, ptr %nsec_ptr, align 8
              %sec_ms = mul i64 %sec, 1000
              %nsec_ms = udiv i64 %nsec, 1000000
              %millis = add i64 %sec_ms, %nsec_ms
              ret i64 %millis
            }

            define internal void @smalllang_wait_millis(i64 %millis) #0 {
            entry:
              %positive = icmp sgt i64 %millis, 0
              br i1 %positive, label %wait, label %done

            wait:
              %ts = alloca [2 x i64], align 8
              %seconds = sdiv i64 %millis, 1000
              %remaining = srem i64 %millis, 1000
              %nanoseconds = mul i64 %remaining, 1000000
              %sec_ptr = getelementptr inbounds [2 x i64], ptr %ts, i64 0, i64 0
              %nsec_ptr = getelementptr inbounds [2 x i64], ptr %ts, i64 0, i64 1
              store i64 %seconds, ptr %sec_ptr, align 8
              store i64 %nanoseconds, ptr %nsec_ptr, align 8
              %ignored = call i32 @nanosleep(ptr %ts, ptr null)
              br label %done

            done:
              ret void
            }

            """);
    }

    public override void EmitMemoryPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal ptr @smalllang_alloc(i64 %bytes) #0 {
            entry:
              %ptr = call ptr @malloc(i64 %bytes)
              ret ptr %ptr
            }

            define internal void @smalllang_free(ptr %ptr) #0 {
            entry:
              %is_null = icmp eq ptr %ptr, null
              br i1 %is_null, label %done, label %free

            free:
              call void @free(ptr %ptr)
              br label %done

            done:
              ret void
            }

            """);
    }

    public override void EmitProcessPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @smalllang_argument_count() #0 {
            entry:
              %count = load i64, ptr @smalllang_argument_count_value, align 8
              ret i64 %count
            }

            define internal %smalllang.text @smalllang_argument(i64 %index) #0 {
            entry:
              %argv = load ptr, ptr @smalllang_argument_vector, align 8
              %slot = getelementptr ptr, ptr %argv, i64 %index
              %value = load ptr, ptr %slot, align 8
              br label %length

            length:
              %i = phi i64 [ 0, %entry ], [ %next, %more ]
              %byte_ptr = getelementptr i8, ptr %value, i64 %i
              %byte = load i8, ptr %byte_ptr, align 1
              %done = icmp eq i8 %byte, 0
              br i1 %done, label %result, label %more

            more:
              %next = add i64 %i, 1
              br label %length

            result:
              %r0 = insertvalue %smalllang.text poison, ptr %value, 0
              %r1 = insertvalue %smalllang.text %r0, i64 %i, 1
              ret %smalllang.text %r1
            }

            define internal %smalllang.process_result @smalllang_run_process(ptr %records, i64 %count) #0 {
            entry:
              %has_program = icmp ugt i64 %count, 0
              br i1 %has_program, label %allocate, label %spawn_error

            allocate:
              %slots = add i64 %count, 1
              %argv_bytes = mul i64 %slots, 8
              %argv = call ptr @smalllang_alloc(i64 %argv_bytes)
              %argv_ok = icmp ne ptr %argv, null
              br i1 %argv_ok, label %copy_loop, label %spawn_error

            copy_loop:
              %i = phi i64 [ 0, %allocate ], [ %next, %copy_store ]
              %copy_done = icmp eq i64 %i, %count
              br i1 %copy_done, label %terminate, label %copy_alloc

            copy_alloc:
              %record = getelementptr %smalllang.text, ptr %records, i64 %i
              %src_ptr_slot = getelementptr inbounds %smalllang.text, ptr %record, i32 0, i32 0
              %src = load ptr, ptr %src_ptr_slot, align 8
              %src_len_slot = getelementptr inbounds %smalllang.text, ptr %record, i32 0, i32 1
              %len = load i64, ptr %src_len_slot, align 8
              %bytes = add i64 %len, 1
              %arg = call ptr @smalllang_alloc(i64 %bytes)
              %arg_ok = icmp ne ptr %arg, null
              br i1 %arg_ok, label %copy_store, label %copy_fail

            copy_store:
              call void @llvm.memcpy.p0.p0.i64(ptr %arg, ptr %src, i64 %len, i1 false)
              %end = getelementptr i8, ptr %arg, i64 %len
              store i8 0, ptr %end, align 1
              %slot = getelementptr ptr, ptr %argv, i64 %i
              store ptr %arg, ptr %slot, align 8
              %next = add i64 %i, 1
              br label %copy_loop

            copy_fail:
              br label %cleanup_failure

            cleanup_failure:
              %failure_j = phi i64 [ %i, %copy_fail ], [ %failure_prev, %cleanup_failure_item ]
              %failure_done = icmp eq i64 %failure_j, 0
              br i1 %failure_done, label %free_argv_error, label %cleanup_failure_item

            cleanup_failure_item:
              %failure_prev = sub i64 %failure_j, 1
              %failure_slot = getelementptr ptr, ptr %argv, i64 %failure_prev
              %failure_arg = load ptr, ptr %failure_slot, align 8
              call void @smalllang_free(ptr %failure_arg)
              br label %cleanup_failure

            free_argv_error:
              call void @smalllang_free(ptr %argv)
              br label %spawn_error

            terminate:
              %null_slot = getelementptr ptr, ptr %argv, i64 %count
              store ptr null, ptr %null_slot, align 8
              %program = load ptr, ptr %argv, align 8
              %pid_slot = alloca i32, align 4
              %env = load ptr, ptr @environ, align 8
              %spawn_code = call i32 @posix_spawnp(ptr %pid_slot, ptr %program, ptr null, ptr null, ptr %argv, ptr %env)
              br label %cleanup

            cleanup:
              %j = phi i64 [ %count, %terminate ], [ %prev, %cleanup_item ]
              %cleanup_done = icmp eq i64 %j, 0
              br i1 %cleanup_done, label %free_argv, label %cleanup_item

            cleanup_item:
              %prev = sub i64 %j, 1
              %old_slot = getelementptr ptr, ptr %argv, i64 %prev
              %old_arg = load ptr, ptr %old_slot, align 8
              call void @smalllang_free(ptr %old_arg)
              br label %cleanup

            free_argv:
              call void @smalllang_free(ptr %argv)
              %spawn_ok = icmp eq i32 %spawn_code, 0
              br i1 %spawn_ok, label %wait, label %spawn_error

            wait:
              %pid = load i32, ptr %pid_slot, align 4
              %status_slot = alloca i32, align 4
              %waited = call i32 @waitpid(i32 %pid, ptr %status_slot, i32 0)
              %wait_ok = icmp eq i32 %waited, %pid
              br i1 %wait_ok, label %decode, label %wait_error

            decode:
              %status = load i32, ptr %status_slot, align 4
              %term_bits = and i32 %status, 127
              %exited = icmp eq i32 %term_bits, 0
              br i1 %exited, label %success, label %signal_error

            success:
              %shifted = lshr i32 %status, 8
              %exit_code = and i32 %shifted, 255
              %ok0 = insertvalue %smalllang.process_result poison, i32 %exit_code, 0
              %ok1 = insertvalue %smalllang.process_result %ok0, i32 0, 1
              ret %smalllang.process_result %ok1

            spawn_error:
              %spawn0 = insertvalue %smalllang.process_result poison, i32 0, 0
              %spawn1 = insertvalue %smalllang.process_result %spawn0, i32 1, 1
              ret %smalllang.process_result %spawn1

            wait_error:
              %wait0 = insertvalue %smalllang.process_result poison, i32 0, 0
              %wait1 = insertvalue %smalllang.process_result %wait0, i32 2, 1
              ret %smalllang.process_result %wait1

            signal_error:
              %signal0 = insertvalue %smalllang.process_result poison, i32 0, 0
              %signal1 = insertvalue %smalllang.process_result %signal0, i32 3, 1
              ret %smalllang.process_result %signal1
            }

            """);
        functions.AppendLine("""
            define internal %smalllang.environment_result @smalllang_environment(ptr %name, i64 %name_len) #0 {
            entry:
              %bytes = add i64 %name_len, 1
              %key = call ptr @smalllang_alloc(i64 %bytes)
              %allocated = icmp ne ptr %key, null
              br i1 %allocated, label %copy, label %error

            copy:
              %i = phi i64 [ 0, %entry ], [ %next, %store_byte ]
              %done = icmp eq i64 %i, %name_len
              br i1 %done, label %terminate, label %copy_byte

            copy_byte:
              %source = getelementptr i8, ptr %name, i64 %i
              %byte = load i8, ptr %source, align 1
              %valid = icmp ne i8 %byte, 0
              br i1 %valid, label %store_byte, label %invalid_key

            store_byte:
              %destination = getelementptr i8, ptr %key, i64 %i
              store i8 %byte, ptr %destination, align 1
              %next = add i64 %i, 1
              br label %copy

            terminate:
              %end = getelementptr i8, ptr %key, i64 %name_len
              store i8 0, ptr %end, align 1
              %value = call ptr @getenv(ptr %key)
              call void @smalllang_free(ptr %key)
              %found = icmp ne ptr %value, null
              br i1 %found, label %length, label %missing

            length:
              %j = phi i64 [ 0, %terminate ], [ %j_next, %length_more ]
              %value_byte_ptr = getelementptr i8, ptr %value, i64 %j
              %value_byte = load i8, ptr %value_byte_ptr, align 1
              %length_done = icmp eq i8 %value_byte, 0
              br i1 %length_done, label %present, label %length_more

            length_more:
              %j_next = add i64 %j, 1
              br label %length

            present:
              %p0 = insertvalue %smalllang.environment_result poison, ptr %value, 0
              %p1 = insertvalue %smalllang.environment_result %p0, i64 %j, 1
              %p2 = insertvalue %smalllang.environment_result %p1, i1 true, 2
              %p3 = insertvalue %smalllang.environment_result %p2, i1 true, 3
              ret %smalllang.environment_result %p3

            missing:
              %m0 = insertvalue %smalllang.environment_result zeroinitializer, i1 true, 3
              ret %smalllang.environment_result %m0

            invalid_key:
              call void @smalllang_free(ptr %key)
              br label %error

            error:
              ret %smalllang.environment_result zeroinitializer
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @smalllang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %written64 = call i64 @write(i32 1, ptr %data, i64 %len64)
              %written32 = trunc i64 %written64 to i32
              store i32 %written32, ptr %written, align 4
              %ok1 = icmp sge i64 %written64, 0
              %ok2 = icmp eq i64 %written64, %len64
              %ok = and i1 %ok1, %ok2
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32
            }

            define internal i32 @smalllang_read_stdin(ptr %stdin, ptr %data, i64 %len64, ptr %read) #0 {
            entry:
              %read64 = call i64 @read(i32 0, ptr %data, i64 %len64)
              %read32 = trunc i64 %read64 to i32
              store i32 %read32, ptr %read, align 4
              %ok = icmp sgt i64 %read64, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32
            }

            """);
    }

    public override void EmitFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal %smalllang.file_handle_result @smalllang_platform_open_owned_read_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 0, i32 0)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              %handle = sext i32 %fd to i64
              %ok0 = insertvalue %smalllang.file_handle_result poison, i64 %handle, 0
              %ok1 = insertvalue %smalllang.file_handle_result %ok0, i32 1, 1
              ret %smalllang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %smalllang.file_handle_result %fail0, i32 0, 1
              ret %smalllang.file_handle_result %fail1
            }

            define internal %smalllang.file_handle_result @smalllang_platform_open_owned_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [4096 x i8], align 1
              %buf_ptr = getelementptr inbounds [4096 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 577, i32 438)
              %valid = icmp sge i32 %fd, 0
              br i1 %valid, label %success, label %fail

            success:
              %handle = sext i32 %fd to i64
              %ok0 = insertvalue %smalllang.file_handle_result poison, i64 %handle, 0
              %ok1 = insertvalue %smalllang.file_handle_result %ok0, i32 1, 1
              ret %smalllang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %smalllang.file_handle_result %fail0, i32 0, 1
              ret %smalllang.file_handle_result %fail1
            }

            define internal i64 @smalllang_platform_duplicate_owned_file(i64 %source) #0 {
            entry:
              %source_fd = trunc i64 %source to i32
              %fd = call i32 @dup(i32 %source_fd)
              %handle = sext i32 %fd to i64
              ret i64 %handle
            }

            define internal %smalllang.file_count_result @smalllang_platform_read_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
            entry:
              %valid_handle = icmp sge i64 %handle, 0
              %valid_offset = icmp ule i64 %offset, 9223372036854775807
              %ready = and i1 %valid_handle, %valid_offset
              br i1 %ready, label %read_file, label %fail

            read_file:
              %fd = trunc i64 %handle to i32
              %count = call i64 @pread(i32 %fd, ptr %data, i64 %len, i64 %offset)
              %ok = icmp sge i64 %count, 0
              br i1 %ok, label %success, label %fail

            success:
              %ok0 = insertvalue %smalllang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %smalllang.file_count_result %ok0, i32 1, 1
              ret %smalllang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_count_result %fail0, i32 0, 1
              ret %smalllang.file_count_result %fail1
            }

            define internal %smalllang.file_count_result @smalllang_platform_write_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
            entry:
              %fd = trunc i64 %handle to i32
              %valid_fd = icmp sge i32 %fd, 0
              %valid_offset = icmp ule i64 %offset, 9223372036854775807
              %ready = and i1 %valid_fd, %valid_offset
              br i1 %ready, label %write_file, label %fail

            write_file:
              %count = call i64 @pwrite(i32 %fd, ptr %data, i64 %len, i64 %offset)
              %ok = icmp sge i64 %count, 0
              br i1 %ok, label %success, label %fail

            success:
              %ok0 = insertvalue %smalllang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %smalllang.file_count_result %ok0, i32 1, 1
              ret %smalllang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_count_result %fail0, i32 0, 1
              ret %smalllang.file_count_result %fail1
            }

            define internal i32 @smalllang_platform_sync_owned_file(i64 %handle) #0 {
            entry:
              %fd = trunc i64 %handle to i32
              %valid = icmp sge i32 %fd, 0
              br i1 %valid, label %sync_file, label %fail

            sync_file:
              %status = call i32 @fsync(i32 %fd)
              %ok = icmp eq i32 %status, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32

            fail:
              ret i32 0
            }

            define internal void @smalllang_platform_close_owned_file(i64 %handle) #0 {
            entry:
              %valid = icmp sge i64 %handle, 0
              br i1 %valid, label %close_file, label %done

            close_file:
              %fd = trunc i64 %handle to i32
              %ignored = call i32 @close(i32 %fd)
              br label %done

            done:
              ret void
            }

            define internal i32 @smalllang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 577, i32 420)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 %fd, ptr @smalllang_file_writer_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @smalllang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_writer_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %write_file, label %fail

            write_file:
              %written = call i64 @write(i32 %fd, ptr %data, i64 %len64)
              %ok1 = icmp sge i64 %written, 0
              %ok2 = icmp eq i64 %written, %len64
              %ok = and i1 %ok1, %ok2
              %result = zext i1 %ok to i32
              ret i32 %result

            fail:
              ret i32 0
            }

            define internal i32 @smalllang_platform_close_write_file() #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_writer_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %close_file, label %fail

            close_file:
              %close_result = call i32 @close(i32 %fd)
              %ok = icmp eq i32 %close_result, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 -1, ptr @smalllang_file_writer_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @smalllang_platform_open_read_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 0, i32 0)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 %fd, ptr @smalllang_file_reader_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            define internal %smalllang.file_count_result @smalllang_platform_i64_file_count() #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %size, label %fail

            size:
              %end = call i64 @lseek(i32 %fd, i64 0, i32 2)
              %size_ok = icmp sge i64 %end, 0
              br i1 %size_ok, label %check, label %fail

            check:
              %rem = urem i64 %end, 8
              %aligned = icmp eq i64 %rem, 0
              br i1 %aligned, label %success, label %fail

            success:
              %count = udiv i64 %end, 8
              %ok0 = insertvalue %smalllang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %smalllang.file_count_result %ok0, i32 1, 1
              ret %smalllang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_count_result %fail0, i32 0, 1
              ret %smalllang.file_count_result %fail1
            }

            define internal %smalllang.file_int_result @smalllang_platform_read_i64_at(i64 %index) #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %seek, label %fail

            seek:
              %offset = mul i64 %index, 8
              %pos = call i64 @lseek(i32 %fd, i64 %offset, i32 0)
              %seek_ok = icmp eq i64 %pos, %offset
              br i1 %seek_ok, label %read_file, label %fail

            read_file:
              %value_ptr = alloca i64, align 8
              %read_bytes = call i64 @read(i32 %fd, ptr %value_ptr, i64 8)
              %read_ok = icmp eq i64 %read_bytes, 8
              br i1 %read_ok, label %success, label %fail

            success:
              %value = load i64, ptr %value_ptr, align 8
              %ok0 = insertvalue %smalllang.file_int_result poison, i64 %value, 0
              %ok1 = insertvalue %smalllang.file_int_result %ok0, i32 1, 1
              ret %smalllang.file_int_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_int_result %fail0, i32 0, 1
              ret %smalllang.file_int_result %fail1
            }

            define internal %smalllang.file_count_result @smalllang_platform_read_file_bytes(ptr %data, i64 %len) #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %read_file, label %fail

            read_file:
              %count = call i64 @read(i32 %fd, ptr %data, i64 %len)
              %ok = icmp sge i64 %count, 0
              br i1 %ok, label %success, label %fail

            success:
              %ok0 = insertvalue %smalllang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %smalllang.file_count_result %ok0, i32 1, 1
              ret %smalllang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_count_result %fail0, i32 0, 1
              ret %smalllang.file_count_result %fail1
            }

            define internal i32 @smalllang_platform_close_read_file() #0 {
            entry:
              %fd = load i32, ptr @smalllang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %close_file, label %fail

            close_file:
              %close_result = call i32 @close(i32 %fd)
              %ok = icmp eq i32 %close_result, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 -1, ptr @smalllang_file_reader_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            """);
    }

    public override void EmitMappedFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal %smalllang.mapped_bytes @smalllang_map_file(ptr %path, i64 %path_len, i64 %offset, i64 %requested_len, i64 %requested_size, i1 %writable) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %path_len, ptr %buf_ptr)
              %path_ok = icmp ne i32 %copy_ok, 0
              br i1 %path_ok, label %open_file, label %fail

            open_file:
              %flags = select i1 %writable, i32 66, i32 0
              %fd = call i32 @open(ptr %buf_ptr, i32 %flags, i32 420)
              %fd_ok = icmp sge i32 %fd, 0
              br i1 %fd_ok, label %resize_check, label %fail

            resize_check:
              %has_requested_size = icmp ne i64 %requested_size, 0
              %resize = and i1 %writable, %has_requested_size
              br i1 %resize, label %resize_file, label %read_size

            resize_file:
              %truncate_result = call i32 @ftruncate(i32 %fd, i64 %requested_size)
              %truncate_ok = icmp eq i32 %truncate_result, 0
              br i1 %truncate_ok, label %read_size, label %close_fail

            read_size:
              %file_size = call i64 @lseek(i32 %fd, i64 0, i32 2)
              %size_ok = icmp sge i64 %file_size, 0
              br i1 %size_ok, label %bounds, label %close_fail

            bounds:
              %offset_ok = icmp ule i64 %offset, %file_size
              %remaining = sub i64 %file_size, %offset
              %whole = icmp eq i64 %requested_len, 0
              %view_len = select i1 %whole, i64 %remaining, i64 %requested_len
              %len_nonzero = icmp ne i64 %view_len, 0
              %len_ok = icmp ule i64 %view_len, %remaining
              %bounds1 = and i1 %offset_ok, %len_nonzero
              %bounds_ok = and i1 %bounds1, %len_ok
              br i1 %bounds_ok, label %view, label %close_fail

            view:
              %aligned = and i64 %offset, -4096
              %delta = sub i64 %offset, %aligned
              %mapped_len = add i64 %delta, %view_len
              %protect_extra = select i1 %writable, i32 2, i32 0
              %protect = or i32 1, %protect_extra
              %base = call ptr @mmap(ptr null, i64 %mapped_len, i32 %protect, i32 1, i32 %fd, i64 %aligned)
              %base_int = ptrtoint ptr %base to i64
              %base_ok = icmp ne i64 %base_int, -1
              br i1 %base_ok, label %success, label %close_fail

            success:
              %data = getelementptr i8, ptr %base, i64 %delta
              %ignored_close = call i32 @close(i32 %fd)
              %r0 = insertvalue %smalllang.mapped_bytes poison, ptr %data, 0
              %r1 = insertvalue %smalllang.mapped_bytes %r0, i64 %view_len, 1
              %r2 = insertvalue %smalllang.mapped_bytes %r1, ptr %base, 2
              %r3 = insertvalue %smalllang.mapped_bytes %r2, i64 %mapped_len, 3
              %r4 = insertvalue %smalllang.mapped_bytes %r3, i1 %writable, 4
              ret %smalllang.mapped_bytes %r4

            close_fail:
              %ignored_close_fail = call i32 @close(i32 %fd)
              br label %fail

            fail:
              %f0 = insertvalue %smalllang.mapped_bytes zeroinitializer, i1 %writable, 4
              ret %smalllang.mapped_bytes %f0
            }

            define internal i32 @smalllang_mapped_flush(ptr %base, i64 %mapped_len) #0 {
            entry:
              %result = call i32 @msync(ptr %base, i64 %mapped_len, i32 4)
              %ok = icmp eq i32 %result, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32
            }

            define internal void @smalllang_mapped_unmap(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ignored = call i32 @munmap(ptr %base, i64 %mapped_len)
              ret void
            }

            """);
    }

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = inttoptr i64 0 to ptr");
        functions.AppendLine("  %stdout = inttoptr i64 1 to ptr");
    }

    public override void EmitProcessEntry(StringBuilder functions)
    {
        functions.AppendLine("  %argc64 = zext i32 %argc to i64");
        functions.AppendLine("  store i64 %argc64, ptr @smalllang_argument_count_value, align 8");
        functions.AppendLine("  store ptr %argv, ptr @smalllang_argument_vector, align 8");
    }
}
