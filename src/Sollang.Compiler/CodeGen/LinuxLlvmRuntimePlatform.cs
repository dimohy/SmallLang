using System.Text;

namespace Sollang.Compiler.CodeGen;

internal sealed class LinuxLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "x86_64-unknown-linux-gnu";

    public override string EntryPointName => "main";

    public override string EntryPointParameters => "i32 %argc, ptr %argv";

    public override bool SupportsAsync => true;

    public override bool SupportsComputePool => true;

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@sollang_file_writer_fd = internal global i32 -1");
        globals.AppendLine("@sollang_file_reader_fd = internal global i32 -1");
        globals.AppendLine("@sollang_argument_count_value = internal global i64 0");
        globals.AppendLine("@sollang_argument_vector = internal global ptr null");
        globals.AppendLine("@sollang_stdout_buffer = internal global [65536 x i8] zeroinitializer, align 16");
        globals.AppendLine("@sollang_stdout_buffer_count = internal global i64 0");
        globals.AppendLine("@sollang_stdout_line_buffered = internal global i1 false");
        if (UsesAsyncFile)
        {
            globals.AppendLine("@sollang_file_request_event_fd = internal global i32 -1");
            globals.AppendLine("@sollang_file_completion_event_fd = internal global i32 -1");
            globals.AppendLine("@sollang_file_worker_thread = internal global i64 0");
        }
        if (UsesComputePool)
        {
            globals.AppendLine("@sollang_compute_work_event_fd = internal global i32 -1");
            globals.AppendLine("@sollang_compute_completion_event_fd = internal global i32 -1");
            globals.AppendLine("@sollang_compute_worker_count = internal global i32 0");
            globals.AppendLine("@sollang_compute_worker_limit = internal global i32 0");
            globals.AppendLine("@sollang_compute_worker_threads = internal global [64 x i64] zeroinitializer, align 8");
            globals.AppendLine("@sollang_compute_generation = internal global i32 0");
        }
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        if (UsesProcessExit)
        {
            functions.AppendLine("declare void @exit(i32)");
        }
        functions.AppendLine("declare i64 @write(i32, ptr, i64)");
        functions.AppendLine("declare i64 @read(i32, ptr, i64)");
        functions.AppendLine("declare i32 @isatty(i32)");
        functions.AppendLine("declare i64 @pread(i32, ptr, i64, i64)");
        functions.AppendLine("declare i64 @pwrite(i32, ptr, i64, i64)");
        functions.AppendLine("declare i32 @open(ptr, i32, i32)");
        functions.AppendLine("declare i32 @close(i32)");
        functions.AppendLine("declare i32 @dup(i32)");
        functions.AppendLine("declare i32 @dup2(i32, i32)");
        functions.AppendLine("declare i32 @fsync(i32)");
        functions.AppendLine("declare i32 @rename(ptr, ptr)");
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
        if (UsesDirectoryTraversal)
        {
            functions.AppendLine("declare ptr @opendir(ptr)");
            functions.AppendLine("declare ptr @readdir(ptr)");
            functions.AppendLine("declare i32 @closedir(ptr)");
            functions.AppendLine("declare ptr @__errno_location()");
            functions.AppendLine("declare ptr @realpath(ptr, ptr)");
            functions.AppendLine("declare i64 @strlen(ptr)");
            functions.AppendLine("declare i32 @stat(ptr, ptr)");
        }
        if (UsesAsyncFile || UsesComputePool || UsesMouseEvents)
        {
            functions.AppendLine("declare i32 @eventfd(i32, i32)");
            functions.AppendLine("declare i32 @pthread_create(ptr, ptr, ptr, ptr)");
            functions.AppendLine("declare i32 @pthread_join(i64, ptr)");
        }
        if (UsesAsyncFile || UsesMouseEvents)
        {
            functions.AppendLine("declare i32 @poll(ptr, i64, i32)");
        }
        if (UsesMouseEvents)
        {
            functions.AppendLine("declare i32 @tcgetattr(i32, ptr)");
            functions.AppendLine("declare i32 @tcsetattr(i32, i32, ptr)");
        }
        if (UsesComputePool)
        {
            functions.AppendLine("declare i64 @sysconf(i32)");
            functions.AppendLine("declare i64 @syscall(i64, ...)");
        }
        functions.AppendLine("@environ = external global ptr");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare ptr @malloc(i64)");
        functions.AppendLine("declare void @free(ptr)");
    }

    public override void EmitEventPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            @sollang_mouse_enable = private constant [16 x i8] c"\1B[?1003h\1B[?1006h"
            @sollang_mouse_disable = private constant [16 x i8] c"\1B[?1003l\1B[?1006l"

            define internal i32 @sollang_linux_mouse_read_byte(ptr %context) #0 {
            entry:
              %poll_record = alloca [8 x i8], align 4
              %byte = alloca i8, align 1
              store i32 0, ptr %poll_record, align 4
              %events = getelementptr i8, ptr %poll_record, i64 4
              store i16 1, ptr %events, align 2
              br label %wait

            wait:
              %cancel_address = getelementptr i8, ptr %context, i64 20
              %cancelled = load atomic i32, ptr %cancel_address acquire, align 4
              %active = icmp eq i32 %cancelled, 0
              br i1 %active, label %poll, label %closed

            poll:
              %ready = call i32 @poll(ptr %poll_record, i64 1, i32 50)
              %has_input = icmp sgt i32 %ready, 0
              br i1 %has_input, label %read_byte, label %wait

            read_byte:
              %read_count = call i64 @read(i32 0, ptr %byte, i64 1)
              %read_ok = icmp eq i64 %read_count, 1
              br i1 %read_ok, label %got_value, label %wait

            got_value:
              %raw = load i8, ptr %byte, align 1
              %byte_value = zext i8 %raw to i32
              ret i32 %byte_value

            closed:
              ret i32 -1
            }

            define internal void @sollang_linux_mouse_signal(ptr %context) #0 {
            entry:
              %one = alloca i64, align 8
              store i64 1, ptr %one, align 8
              %fd_address = getelementptr i8, ptr %context, i64 28
              %fd = load i32, ptr %fd_address, align 4
              %written = call i64 @write(i32 %fd, ptr %one, i64 8)
              ret void
            }

            define internal void @sollang_linux_mouse_enqueue(ptr %context, i32 %x, i32 %y, i32 %delta, i32 %button, i32 %kind) #0 {
            entry:
              br label %queue_lock

            queue_lock:
              %lock_address = getelementptr i8, ptr %context, i64 24
              %lock_result = cmpxchg ptr %lock_address, i32 0, i32 1 acquire monotonic, align 4
              %lock_acquired = extractvalue { i32, i1 } %lock_result, 1
              br i1 %lock_acquired, label %queue_state, label %queue_lock

            queue_state:
              %capacity = load i32, ptr %context, align 4
              %policy_address = getelementptr i8, ptr %context, i64 4
              %policy = load i32, ptr %policy_address, align 4
              %count_address = getelementptr i8, ptr %context, i64 16
              %count = load i32, ptr %count_address, align 4
              %full = icmp uge i32 %count, %capacity
              br i1 %full, label %overflow, label %write

            overflow:
              %drop_newest = icmp eq i32 %policy, 0
              br i1 %drop_newest, label %unlock_drop, label %not_drop_newest

            not_drop_newest:
              %coalesce = icmp eq i32 %policy, 2
              %is_move = icmp eq i32 %kind, 0
              %coalesce_move = and i1 %coalesce, %is_move
              br i1 %coalesce_move, label %replace_move_check, label %drop_oldest

            replace_move_check:
              %tail_address0 = getelementptr i8, ptr %context, i64 12
              %tail0 = load i32, ptr %tail_address0, align 4
              %tail_plus_capacity = add i32 %tail0, %capacity
              %last_unwrapped = sub i32 %tail_plus_capacity, 1
              %last_index = urem i32 %last_unwrapped, %capacity
              %last_index64 = zext i32 %last_index to i64
              %last_offset = mul i64 %last_index64, 20
              %buffer_address0 = getelementptr i8, ptr %context, i64 40
              %buffer0 = load ptr, ptr %buffer_address0, align 8
              %last_record = getelementptr i8, ptr %buffer0, i64 %last_offset
              %last_kind_address = getelementptr i8, ptr %last_record, i64 16
              %last_kind = load i32, ptr %last_kind_address, align 4
              %last_is_move = icmp eq i32 %last_kind, 0
              br i1 %last_is_move, label %replace_move, label %unlock_drop

            replace_move:
              store i32 %x, ptr %last_record, align 4
              %last_y = getelementptr i8, ptr %last_record, i64 4
              store i32 %y, ptr %last_y, align 4
              br label %unlock_signal

            drop_oldest:
              %head_address = getelementptr i8, ptr %context, i64 8
              %head = load i32, ptr %head_address, align 4
              %next_head_unwrapped = add i32 %head, 1
              %next_head = urem i32 %next_head_unwrapped, %capacity
              store i32 %next_head, ptr %head_address, align 4
              %reduced_count = sub i32 %count, 1
              store i32 %reduced_count, ptr %count_address, align 4
              br label %write

            write:
              %tail_address = getelementptr i8, ptr %context, i64 12
              %tail = load i32, ptr %tail_address, align 4
              %tail64 = zext i32 %tail to i64
              %offset = mul i64 %tail64, 20
              %buffer_address = getelementptr i8, ptr %context, i64 40
              %buffer = load ptr, ptr %buffer_address, align 8
              %slot = getelementptr i8, ptr %buffer, i64 %offset
              store i32 %x, ptr %slot, align 4
              %slot_y = getelementptr i8, ptr %slot, i64 4
              store i32 %y, ptr %slot_y, align 4
              %slot_delta = getelementptr i8, ptr %slot, i64 8
              store i32 %delta, ptr %slot_delta, align 4
              %slot_button = getelementptr i8, ptr %slot, i64 12
              store i32 %button, ptr %slot_button, align 4
              %slot_kind = getelementptr i8, ptr %slot, i64 16
              store i32 %kind, ptr %slot_kind, align 4
              %next_tail_unwrapped = add i32 %tail, 1
              %next_tail = urem i32 %next_tail_unwrapped, %capacity
              store i32 %next_tail, ptr %tail_address, align 4
              %current_count = load i32, ptr %count_address, align 4
              %next_count = add i32 %current_count, 1
              store i32 %next_count, ptr %count_address, align 4
              br label %unlock_signal

            unlock_drop:
              store atomic i32 0, ptr %lock_address release, align 4
              ret void

            unlock_signal:
              store atomic i32 0, ptr %lock_address release, align 4
              call void @sollang_linux_mouse_signal(ptr %context)
              ret void
            }

            define internal ptr @sollang_linux_mouse_worker(ptr %context) #0 {
            entry:
              %button_value = alloca i32, align 4
              %x_value = alloca i32, align 4
              %y_value = alloca i32, align 4
              br label %seek_escape

            seek_escape:
              %escape = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %escape_closed = icmp slt i32 %escape, 0
              br i1 %escape_closed, label %done, label %check_escape

            check_escape:
              %is_escape = icmp eq i32 %escape, 27
              br i1 %is_escape, label %expect_bracket, label %seek_escape

            expect_bracket:
              %bracket = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %is_bracket = icmp eq i32 %bracket, 91
              br i1 %is_bracket, label %expect_less, label %seek_escape

            expect_less:
              %less = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %is_less = icmp eq i32 %less, 60
              br i1 %is_less, label %button_start, label %seek_escape

            button_start:
              store i32 0, ptr %button_value, align 4
              br label %button_digit

            button_digit:
              %button_char = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %button_is_digit_low = icmp sge i32 %button_char, 48
              %button_is_digit_high = icmp sle i32 %button_char, 57
              %button_is_digit = and i1 %button_is_digit_low, %button_is_digit_high
              br i1 %button_is_digit, label %button_accumulate, label %button_separator

            button_accumulate:
              %button_old = load i32, ptr %button_value, align 4
              %button_times_ten = mul i32 %button_old, 10
              %button_digit_value = sub i32 %button_char, 48
              %button_next = add i32 %button_times_ten, %button_digit_value
              store i32 %button_next, ptr %button_value, align 4
              br label %button_digit

            button_separator:
              %button_has_separator = icmp eq i32 %button_char, 59
              br i1 %button_has_separator, label %x_start, label %seek_escape

            x_start:
              store i32 0, ptr %x_value, align 4
              br label %x_digit

            x_digit:
              %x_char = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %x_is_digit_low = icmp sge i32 %x_char, 48
              %x_is_digit_high = icmp sle i32 %x_char, 57
              %x_is_digit = and i1 %x_is_digit_low, %x_is_digit_high
              br i1 %x_is_digit, label %x_accumulate, label %x_separator

            x_accumulate:
              %x_old = load i32, ptr %x_value, align 4
              %x_times_ten = mul i32 %x_old, 10
              %x_digit_value = sub i32 %x_char, 48
              %x_next = add i32 %x_times_ten, %x_digit_value
              store i32 %x_next, ptr %x_value, align 4
              br label %x_digit

            x_separator:
              %x_has_separator = icmp eq i32 %x_char, 59
              br i1 %x_has_separator, label %y_start, label %seek_escape

            y_start:
              store i32 0, ptr %y_value, align 4
              br label %y_digit

            y_digit:
              %y_char = call i32 @sollang_linux_mouse_read_byte(ptr %context)
              %y_is_digit_low = icmp sge i32 %y_char, 48
              %y_is_digit_high = icmp sle i32 %y_char, 57
              %y_is_digit = and i1 %y_is_digit_low, %y_is_digit_high
              br i1 %y_is_digit, label %y_accumulate, label %finish_event

            y_accumulate:
              %y_old = load i32, ptr %y_value, align 4
              %y_times_ten = mul i32 %y_old, 10
              %y_digit_value = sub i32 %y_char, 48
              %y_next = add i32 %y_times_ten, %y_digit_value
              store i32 %y_next, ptr %y_value, align 4
              br label %y_digit

            finish_event:
              %is_press = icmp eq i32 %y_char, 77
              %is_release = icmp eq i32 %y_char, 109
              %valid_final = or i1 %is_press, %is_release
              br i1 %valid_final, label %classify, label %seek_escape

            classify:
              %raw_button = load i32, ptr %button_value, align 4
              %raw_x = load i32, ptr %x_value, align 4
              %raw_y = load i32, ptr %y_value, align 4
              %x = sub i32 %raw_x, 1
              %y = sub i32 %raw_y, 1
              %wheel_bit = and i32 %raw_button, 64
              %is_wheel = icmp ne i32 %wheel_bit, 0
              br i1 %is_wheel, label %wheel, label %non_wheel

            wheel:
              %wheel_direction = and i32 %raw_button, 1
              %wheel_down = icmp ne i32 %wheel_direction, 0
              %delta = select i1 %wheel_down, i32 -120, i32 120
              call void @sollang_linux_mouse_enqueue(ptr %context, i32 %x, i32 %y, i32 %delta, i32 -1, i32 3)
              br label %seek_escape

            non_wheel:
              %motion_bit = and i32 %raw_button, 32
              %is_motion = icmp ne i32 %motion_bit, 0
              br i1 %is_motion, label %move, label %button

            move:
              call void @sollang_linux_mouse_enqueue(ptr %context, i32 %x, i32 %y, i32 0, i32 -1, i32 0)
              br label %seek_escape

            button:
              %button_index = and i32 %raw_button, 3
              %kind = select i1 %is_release, i32 2, i32 1
              call void @sollang_linux_mouse_enqueue(ptr %context, i32 %x, i32 %y, i32 0, i32 %button_index, i32 %kind)
              br label %seek_escape

            done:
              call void @sollang_linux_mouse_signal(ptr %context)
              ret ptr null
            }

            define internal ptr @sollang_mouse_event_stream_create(i32 %requested_capacity, i32 %overflow) #0 {
            entry:
              %capacity_low = icmp slt i32 %requested_capacity, 2
              %capacity_high = icmp sgt i32 %requested_capacity, 65536
              %capacity_invalid = or i1 %capacity_low, %capacity_high
              %overflow_invalid = icmp ugt i32 %overflow, 2
              %invalid = or i1 %capacity_invalid, %overflow_invalid
              br i1 %invalid, label %fail, label %tty

            tty:
              %interactive = call i32 @isatty(i32 0)
              %is_tty = icmp ne i32 %interactive, 0
              br i1 %is_tty, label %allocate_context, label %fail

            allocate_context:
              %context = call ptr @sollang_alloc(i64 64)
              %context_ok = icmp ne ptr %context, null
              br i1 %context_ok, label %allocate_buffer, label %fail

            allocate_buffer:
              %capacity64 = zext i32 %requested_capacity to i64
              %buffer_bytes = mul i64 %capacity64, 20
              %buffer = call ptr @sollang_alloc(i64 %buffer_bytes)
              %buffer_ok = icmp ne ptr %buffer, null
              br i1 %buffer_ok, label %allocate_termios, label %free_context

            allocate_termios:
              %original = call ptr @sollang_alloc(i64 64)
              %original_ok = icmp ne ptr %original, null
              br i1 %original_ok, label %read_termios, label %free_buffer

            read_termios:
              %got_termios = call i32 @tcgetattr(i32 0, ptr %original)
              %termios_ok = icmp eq i32 %got_termios, 0
              br i1 %termios_ok, label %configure_termios, label %free_termios

            configure_termios:
              %current = alloca [64 x i8], align 8
              call void @llvm.memcpy.p0.p0.i64(ptr %current, ptr %original, i64 64, i1 false)
              %lflag_address = getelementptr i8, ptr %current, i64 12
              %lflag = load i32, ptr %lflag_address, align 4
              %raw_lflag = and i32 %lflag, -11
              store i32 %raw_lflag, ptr %lflag_address, align 4
              %vtime = getelementptr i8, ptr %current, i64 22
              store i8 0, ptr %vtime, align 1
              %vmin = getelementptr i8, ptr %current, i64 23
              store i8 1, ptr %vmin, align 1
              %set_termios = call i32 @tcsetattr(i32 0, i32 0, ptr %current)
              %termios_set = icmp eq i32 %set_termios, 0
              br i1 %termios_set, label %create_event, label %free_termios

            create_event:
              %event_fd = call i32 @eventfd(i32 0, i32 0)
              %event_ok = icmp sge i32 %event_fd, 0
              br i1 %event_ok, label %initialize, label %restore

            initialize:
              store i32 %requested_capacity, ptr %context, align 4
              %policy_address = getelementptr i8, ptr %context, i64 4
              store i32 %overflow, ptr %policy_address, align 4
              %head_address = getelementptr i8, ptr %context, i64 8
              store i32 0, ptr %head_address, align 4
              %tail_address = getelementptr i8, ptr %context, i64 12
              store i32 0, ptr %tail_address, align 4
              %count_address = getelementptr i8, ptr %context, i64 16
              store i32 0, ptr %count_address, align 4
              %cancel_address = getelementptr i8, ptr %context, i64 20
              store atomic i32 0, ptr %cancel_address release, align 4
              %lock_address = getelementptr i8, ptr %context, i64 24
              store atomic i32 0, ptr %lock_address release, align 4
              %event_address = getelementptr i8, ptr %context, i64 28
              store i32 %event_fd, ptr %event_address, align 4
              %buffer_address = getelementptr i8, ptr %context, i64 40
              store ptr %buffer, ptr %buffer_address, align 8
              %termios_address = getelementptr i8, ptr %context, i64 48
              store ptr %original, ptr %termios_address, align 8
              %enabled = call i64 @write(i32 1, ptr @sollang_mouse_enable, i64 16)
              %thread_slot = alloca i64, align 8
              %created = call i32 @pthread_create(ptr %thread_slot, ptr null, ptr @sollang_linux_mouse_worker, ptr %context)
              %thread_ok = icmp eq i32 %created, 0
              br i1 %thread_ok, label %ready, label %close_event

            ready:
              %thread = load i64, ptr %thread_slot, align 8
              %thread_address = getelementptr i8, ptr %context, i64 32
              store i64 %thread, ptr %thread_address, align 8
              ret ptr %context

            close_event:
              %closed_event = call i32 @close(i32 %event_fd)
              br label %restore

            restore:
              %restored = call i32 @tcsetattr(i32 0, i32 0, ptr %original)
              br label %free_termios

            free_termios:
              call void @sollang_free(ptr %original)
              br label %free_buffer

            free_buffer:
              call void @sollang_free(ptr %buffer)
              br label %free_context

            free_context:
              call void @sollang_free(ptr %context)
              br label %fail

            fail:
              ret ptr null
            }

            define internal i1 @sollang_mouse_event_next_raw(ptr %context, ptr %x, ptr %y, ptr %delta, ptr %button, ptr %kind) #0 {
            entry:
              br label %queue_lock

            queue_lock:
              %lock_address = getelementptr i8, ptr %context, i64 24
              %lock_result = cmpxchg ptr %lock_address, i32 0, i32 1 acquire monotonic, align 4
              %lock_acquired = extractvalue { i32, i1 } %lock_result, 1
              br i1 %lock_acquired, label %queue_state, label %queue_lock

            queue_state:
              %count_address = getelementptr i8, ptr %context, i64 16
              %count = load i32, ptr %count_address, align 4
              %available = icmp ugt i32 %count, 0
              br i1 %available, label %read, label %unlock_empty

            unlock_empty:
              store atomic i32 0, ptr %lock_address release, align 4
              %cancel_address = getelementptr i8, ptr %context, i64 20
              %cancelled = load atomic i32, ptr %cancel_address acquire, align 4
              %active = icmp eq i32 %cancelled, 0
              br i1 %active, label %wait, label %closed

            wait:
              %counter = alloca i64, align 8
              %event_address = getelementptr i8, ptr %context, i64 28
              %event_fd = load i32, ptr %event_address, align 4
              %read_signal = call i64 @read(i32 %event_fd, ptr %counter, i64 8)
              br label %queue_lock

            read:
              %capacity = load i32, ptr %context, align 4
              %head_address = getelementptr i8, ptr %context, i64 8
              %head = load i32, ptr %head_address, align 4
              %head64 = zext i32 %head to i64
              %offset = mul i64 %head64, 20
              %buffer_address = getelementptr i8, ptr %context, i64 40
              %buffer = load ptr, ptr %buffer_address, align 8
              %slot = getelementptr i8, ptr %buffer, i64 %offset
              %x_value = load i32, ptr %slot, align 4
              store i32 %x_value, ptr %x, align 4
              %slot_y = getelementptr i8, ptr %slot, i64 4
              %y_value = load i32, ptr %slot_y, align 4
              store i32 %y_value, ptr %y, align 4
              %slot_delta = getelementptr i8, ptr %slot, i64 8
              %delta_value = load i32, ptr %slot_delta, align 4
              store i32 %delta_value, ptr %delta, align 4
              %slot_button = getelementptr i8, ptr %slot, i64 12
              %button_value = load i32, ptr %slot_button, align 4
              store i32 %button_value, ptr %button, align 4
              %slot_kind = getelementptr i8, ptr %slot, i64 16
              %kind_value = load i32, ptr %slot_kind, align 4
              store i32 %kind_value, ptr %kind, align 4
              %next_head_unwrapped = add i32 %head, 1
              %next_head = urem i32 %next_head_unwrapped, %capacity
              store i32 %next_head, ptr %head_address, align 4
              %next_count = sub i32 %count, 1
              store i32 %next_count, ptr %count_address, align 4
              store atomic i32 0, ptr %lock_address release, align 4
              ret i1 true

            closed:
              ret i1 false
            }

            define internal void @sollang_mouse_event_stream_drop(ptr %context) #0 {
            entry:
              %cancel_address = getelementptr i8, ptr %context, i64 20
              store atomic i32 1, ptr %cancel_address release, align 4
              %thread_address = getelementptr i8, ptr %context, i64 32
              %thread = load i64, ptr %thread_address, align 8
              %joined = call i32 @pthread_join(i64 %thread, ptr null)
              %disabled = call i64 @write(i32 1, ptr @sollang_mouse_disable, i64 16)
              %termios_address = getelementptr i8, ptr %context, i64 48
              %original = load ptr, ptr %termios_address, align 8
              %restored = call i32 @tcsetattr(i32 0, i32 0, ptr %original)
              %event_address = getelementptr i8, ptr %context, i64 28
              %event_fd = load i32, ptr %event_address, align 4
              %closed_event = call i32 @close(i32 %event_fd)
              %buffer_address = getelementptr i8, ptr %context, i64 40
              %buffer = load ptr, ptr %buffer_address, align 8
              call void @sollang_free(ptr %original)
              call void @sollang_free(ptr %buffer)
              call void @sollang_free(ptr %context)
              ret void
            }

            """);
    }

    public override void EmitAsyncPrimitives(StringBuilder functions)
    {
        base.EmitAsyncPrimitives(functions);
        if (!UsesAsyncFile)
        {
            return;
        }
        functions.AppendLine("""
            define internal ptr @sollang_linux_file_worker(ptr %unused) #0 {
            entry:
              call void @sollang_file_worker_run()
              ret ptr null
            }

            define internal i1 @sollang_platform_file_worker_start() #0 {
            entry:
              %request_fd = call i32 @eventfd(i32 0, i32 0)
              %request_ok = icmp sge i32 %request_fd, 0
              br i1 %request_ok, label %completion, label %fail

            completion:
              store i32 %request_fd, ptr @sollang_file_request_event_fd, align 4
              %completion_fd = call i32 @eventfd(i32 0, i32 0)
              %completion_ok = icmp sge i32 %completion_fd, 0
              br i1 %completion_ok, label %thread, label %fail

            thread:
              store i32 %completion_fd, ptr @sollang_file_completion_event_fd, align 4
              %thread_slot = alloca i64, align 8
              %create = call i32 @pthread_create(ptr %thread_slot, ptr null, ptr @sollang_linux_file_worker, ptr null)
              %thread_ok = icmp eq i32 %create, 0
              br i1 %thread_ok, label %ready, label %fail

            ready:
              %worker_thread = load i64, ptr %thread_slot, align 8
              store i64 %worker_thread, ptr @sollang_file_worker_thread, align 8
              ret i1 true

            fail:
              ret i1 false
            }

            define internal void @sollang_platform_file_worker_signal_request() #0 {
            entry:
              %value = alloca i64, align 8
              store i64 1, ptr %value, align 8
              %fd = load i32, ptr @sollang_file_request_event_fd, align 4
              %ignored = call i64 @write(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @sollang_platform_file_worker_wait_request() #0 {
            entry:
              %value = alloca i64, align 8
              %fd = load i32, ptr @sollang_file_request_event_fd, align 4
              %ignored = call i64 @read(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @sollang_platform_file_worker_signal_completion() #0 {
            entry:
              %value = alloca i64, align 8
              store i64 1, ptr %value, align 8
              %fd = load i32, ptr @sollang_file_completion_event_fd, align 4
              %ignored = call i64 @write(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @sollang_platform_file_worker_clear_completion() #0 {
            entry:
              %value = alloca i64, align 8
              %fd = load i32, ptr @sollang_file_completion_event_fd, align 4
              %ignored = call i64 @read(i32 %fd, ptr %value, i64 8)
              ret void
            }

            define internal void @sollang_platform_file_worker_wait_completion(i64 %requested) #0 {
            entry:
              %pollfd = alloca [8 x i8], align 4
              %fd = load i32, ptr @sollang_file_completion_event_fd, align 4
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

            define internal void @sollang_platform_file_worker_join() #0 {
            entry:
              %thread = load i64, ptr @sollang_file_worker_thread, align 8
              %joined = call i32 @pthread_join(i64 %thread, ptr null)
              %request_fd = load i32, ptr @sollang_file_request_event_fd, align 4
              %closed_request = call i32 @close(i32 %request_fd)
              %completion_fd = load i32, ptr @sollang_file_completion_event_fd, align 4
              %closed_completion = call i32 @close(i32 %completion_fd)
              store i64 0, ptr @sollang_file_worker_thread, align 8
              store i32 -1, ptr @sollang_file_request_event_fd, align 4
              store i32 -1, ptr @sollang_file_completion_event_fd, align 4
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
            define internal ptr @sollang_linux_compute_worker(ptr %unused) #0 {
            entry:
              %event_value = alloca i64, align 8
              br label %wait

            wait:
              %work_fd = load i32, ptr @sollang_compute_work_event_fd, align 4
              %waited = call i64 @read(i32 %work_fd, ptr %event_value, i64 8)
              %stopping_value = load atomic i32, ptr @sollang_compute_stopping acquire, align 4
              %stopping = icmp ne i32 %stopping_value, 0
              br i1 %stopping, label %stopped, label %take

            take:
              %generation = load atomic i32, ptr @sollang_compute_generation acquire, align 4
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
              br i1 %last, label %signal, label %barrier_check

            barrier_check:
              %current_generation = load atomic i32, ptr @sollang_compute_generation acquire, align 4
              %generation_advanced = icmp ne i32 %current_generation, %generation
              br i1 %generation_advanced, label %wait, label %barrier_wait

            barrier_wait:
              %futex_waited = call i64 (i64, ...) @syscall(i64 202, ptr @sollang_compute_generation, i32 128, i32 %generation, ptr null, ptr null, i32 0)
              br label %barrier_check

            signal:
              %generation_before = atomicrmw add ptr @sollang_compute_generation, i32 1 acq_rel
              %futex_woken = call i64 (i64, ...) @syscall(i64 202, ptr @sollang_compute_generation, i32 129, i32 2147483647, ptr null, ptr null, i32 0)
              store i64 1, ptr %event_value, align 8
              %completion_fd = load i32, ptr @sollang_compute_completion_event_fd, align 4
              %signalled = call i64 @write(i32 %completion_fd, ptr %event_value, i64 8)
              br label %wait

            stopped:
              ret ptr null
            }

            define internal i1 @sollang_compute_start() #0 {
            entry:
              %existing = load i32, ptr @sollang_compute_worker_count, align 4
              %already = icmp sgt i32 %existing, 0
              br i1 %already, label %ready, label %create_work_event

            create_work_event:
              %work_fd = call i32 @eventfd(i32 0, i32 1)
              %work_ok = icmp sge i32 %work_fd, 0
              br i1 %work_ok, label %create_completion_event, label %fail

            create_completion_event:
              store i32 %work_fd, ptr @sollang_compute_work_event_fd, align 4
              %completion_fd = call i32 @eventfd(i32 0, i32 0)
              %completion_ok = icmp sge i32 %completion_fd, 0
              br i1 %completion_ok, label %count, label %close_work

            count:
              store i32 %completion_fd, ptr @sollang_compute_completion_event_fd, align 4
              %reported = call i64 @sysconf(i32 84)
              %positive = icmp sgt i64 %reported, 0
              %at_least_one = select i1 %positive, i64 %reported, i64 1
              %configured32 = load i32, ptr @sollang_compute_worker_limit, align 4
              %configured = sext i32 %configured32 to i64
              %has_configured = icmp sgt i64 %configured, 0
              %selected = select i1 %has_configured, i64 %configured, i64 %at_least_one
              %too_many = icmp sgt i64 %selected, 64
              %bounded64 = select i1 %too_many, i64 64, i64 %selected
              %bounded = trunc i64 %bounded64 to i32
              br label %create_workers

            create_workers:
              %index = phi i32 [ 0, %count ], [ %next, %created ]
              %done = icmp eq i32 %index, %bounded
              br i1 %done, label %publish, label %create_one

            create_one:
              %slot = getelementptr [64 x i64], ptr @sollang_compute_worker_threads, i32 0, i32 %index
              %create = call i32 @pthread_create(ptr %slot, ptr null, ptr @sollang_linux_compute_worker, ptr null)
              %worker_ok = icmp eq i32 %create, 0
              br i1 %worker_ok, label %created, label %publish

            created:
              %next = add i32 %index, 1
              br label %create_workers

            publish:
              %created_count = phi i32 [ %bounded, %create_workers ], [ %index, %create_one ]
              store i32 %created_count, ptr @sollang_compute_worker_count, align 4
              %has_workers = icmp sgt i32 %created_count, 0
              br i1 %has_workers, label %ready, label %close_both

            close_work:
              %closed_work_only = call i32 @close(i32 %work_fd)
              store i32 -1, ptr @sollang_compute_work_event_fd, align 4
              br label %fail

            close_both:
              %closed_completion = call i32 @close(i32 %completion_fd)
              %closed_work = call i32 @close(i32 %work_fd)
              store i32 -1, ptr @sollang_compute_completion_event_fd, align 4
              store i32 -1, ptr @sollang_compute_work_event_fd, align 4
              br label %fail

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
              %workers64 = zext i32 %workers to i64
              %event_value = alloca i64, align 8
              store i64 %workers64, ptr %event_value, align 8
              %help_first_index = atomicrmw add ptr @sollang_compute_next, i64 1 acq_rel
              %work_fd = load i32, ptr @sollang_compute_work_event_fd, align 4
              %released = call i64 @write(i32 %work_fd, ptr %event_value, i64 8)
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
              %completion_fd = load i32, ptr @sollang_compute_completion_event_fd, align 4
              %waited = call i64 @read(i32 %completion_fd, ptr %event_value, i64 8)
              br label %flush_prepare

            flush_prepare:
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
              %workers64 = zext i32 %workers to i64
              %event_value = alloca i64, align 8
              store i64 %workers64, ptr %event_value, align 8
              %work_fd = load i32, ptr @sollang_compute_work_event_fd, align 4
              %released = call i64 @write(i32 %work_fd, ptr %event_value, i64 8)
              br label %join

            join:
              %index = phi i32 [ 0, %stop ], [ %next, %joined ]
              %all_joined = icmp eq i32 %index, %workers
              br i1 %all_joined, label %cleanup, label %join_one

            join_one:
              %slot = getelementptr [64 x i64], ptr @sollang_compute_worker_threads, i32 0, i32 %index
              %worker = load i64, ptr %slot, align 8
              %joined_status = call i32 @pthread_join(i64 %worker, ptr null)
              store i64 0, ptr %slot, align 8
              br label %joined

            joined:
              %next = add i32 %index, 1
              br label %join

            cleanup:
              %completion_fd = load i32, ptr @sollang_compute_completion_event_fd, align 4
              %closed_completion = call i32 @close(i32 %completion_fd)
              %closed_work = call i32 @close(i32 %work_fd)
              store i32 -1, ptr @sollang_compute_completion_event_fd, align 4
              store i32 -1, ptr @sollang_compute_work_event_fd, align 4
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

            define internal void @sollang_wait_millis(i64 %millis) #0 {
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
            define internal ptr @sollang_alloc(i64 %bytes) #0 {
            entry:
              %ptr = call ptr @malloc(i64 %bytes)
              ret ptr %ptr
            }

            define internal void @sollang_free(ptr %ptr) #0 {
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
            define internal i64 @sollang_argument_count() #0 {
            entry:
              %count = load i64, ptr @sollang_argument_count_value, align 8
              ret i64 %count
            }

            define internal %sollang.text @sollang_argument(i64 %index) #0 {
            entry:
              %argv = load ptr, ptr @sollang_argument_vector, align 8
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
              %r0 = insertvalue %sollang.text poison, ptr %value, 0
              %r1 = insertvalue %sollang.text %r0, i64 %i, 1
              ret %sollang.text %r1
            }

            define internal %sollang.process_result @sollang_run_process(ptr %records, i64 %count) #0 {
            entry:
              %has_program = icmp ugt i64 %count, 0
              br i1 %has_program, label %allocate, label %spawn_error

            allocate:
              %slots = add i64 %count, 1
              %argv_bytes = mul i64 %slots, 8
              %argv = call ptr @sollang_alloc(i64 %argv_bytes)
              %argv_ok = icmp ne ptr %argv, null
              br i1 %argv_ok, label %copy_loop, label %spawn_error

            copy_loop:
              %i = phi i64 [ 0, %allocate ], [ %next, %copy_store ]
              %copy_done = icmp eq i64 %i, %count
              br i1 %copy_done, label %terminate, label %copy_alloc

            copy_alloc:
              %record = getelementptr %sollang.text, ptr %records, i64 %i
              %src_ptr_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 0
              %src = load ptr, ptr %src_ptr_slot, align 8
              %src_len_slot = getelementptr inbounds %sollang.text, ptr %record, i32 0, i32 1
              %len = load i64, ptr %src_len_slot, align 8
              %bytes = add i64 %len, 1
              %arg = call ptr @sollang_alloc(i64 %bytes)
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
              call void @sollang_free(ptr %failure_arg)
              br label %cleanup_failure

            free_argv_error:
              call void @sollang_free(ptr %argv)
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
              call void @sollang_free(ptr %old_arg)
              br label %cleanup

            free_argv:
              call void @sollang_free(ptr %argv)
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
              %ok0 = insertvalue %sollang.process_result poison, i32 %exit_code, 0
              %ok1 = insertvalue %sollang.process_result %ok0, i32 0, 1
              ret %sollang.process_result %ok1

            spawn_error:
              %spawn0 = insertvalue %sollang.process_result poison, i32 0, 0
              %spawn1 = insertvalue %sollang.process_result %spawn0, i32 1, 1
              ret %sollang.process_result %spawn1

            wait_error:
              %wait0 = insertvalue %sollang.process_result poison, i32 0, 0
              %wait1 = insertvalue %sollang.process_result %wait0, i32 2, 1
              ret %sollang.process_result %wait1

            signal_error:
              %signal0 = insertvalue %sollang.process_result poison, i32 0, 0
              %signal1 = insertvalue %sollang.process_result %signal0, i32 3, 1
              ret %sollang.process_result %signal1
            }

            define internal %sollang.process_result @sollang_run_process_to_file(ptr %records, i64 %count, ptr %path, i64 %path_len) #0 {
            entry:
              %opened = call %sollang.file_handle_result @sollang_platform_open_owned_write_file(ptr %path, i64 %path_len)
              %status = extractvalue %sollang.file_handle_result %opened, 1
              %open_ok = icmp eq i32 %status, 1
              br i1 %open_ok, label %save, label %error

            save:
              %handle_value = extractvalue %sollang.file_handle_result %opened, 0
              %fd = trunc i64 %handle_value to i32
              %previous = call i32 @dup(i32 1)
              %saved = icmp sge i32 %previous, 0
              br i1 %saved, label %redirect, label %close_error

            redirect:
              %redirected = call i32 @dup2(i32 %fd, i32 1)
              %redirect_ok = icmp sge i32 %redirected, 0
              br i1 %redirect_ok, label %run, label %close_saved_error

            run:
              %result = call %sollang.process_result @sollang_run_process(ptr %records, i64 %count)
              %restored = call i32 @dup2(i32 %previous, i32 1)
              %closed_previous = call i32 @close(i32 %previous)
              call void @sollang_platform_close_owned_file(i64 %handle_value)
              ret %sollang.process_result %result

            close_saved_error:
              %closed_saved = call i32 @close(i32 %previous)
              br label %close_error

            close_error:
              call void @sollang_platform_close_owned_file(i64 %handle_value)
              br label %error

            error:
              %error0 = insertvalue %sollang.process_result poison, i32 0, 0
              %error1 = insertvalue %sollang.process_result %error0, i32 1, 1
              ret %sollang.process_result %error1
            }

            """);
        functions.AppendLine("""
            define internal %sollang.environment_result @sollang_environment(ptr %name, i64 %name_len) #0 {
            entry:
              %bytes = add i64 %name_len, 1
              %key = call ptr @sollang_alloc(i64 %bytes)
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
              call void @sollang_free(ptr %key)
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
              %p0 = insertvalue %sollang.environment_result poison, ptr %value, 0
              %p1 = insertvalue %sollang.environment_result %p0, i64 %j, 1
              %p2 = insertvalue %sollang.environment_result %p1, i1 true, 2
              %p3 = insertvalue %sollang.environment_result %p2, i1 true, 3
              ret %sollang.environment_result %p3

            missing:
              %m0 = insertvalue %sollang.environment_result zeroinitializer, i1 true, 3
              ret %sollang.environment_result %m0

            invalid_key:
              call void @sollang_free(ptr %key)
              br label %error

            error:
              ret %sollang.environment_result zeroinitializer
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_flush_stdout(ptr %stdout, ptr %written) #0 {
            entry:
              %count = load i64, ptr @sollang_stdout_buffer_count, align 8
              %has_data = icmp ne i64 %count, 0
              br i1 %has_data, label %write_buffer, label %empty

            write_buffer:
              %buffer = getelementptr inbounds [65536 x i8], ptr @sollang_stdout_buffer, i64 0, i64 0
              %written64 = call i64 @write(i32 1, ptr %buffer, i64 %count)
              %written32 = trunc i64 %written64 to i32
              store i32 %written32, ptr %written, align 4
              store i64 0, ptr @sollang_stdout_buffer_count, align 8
              %nonnegative = icmp sge i64 %written64, 0
              %complete = icmp eq i64 %written64, %count
              %ok = and i1 %nonnegative, %complete
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32

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
              %has_sink_tag = icmp ne i64 %sink_tag, 0
              %is_stdout_fd = icmp eq i64 %stdout_value, 1
              %not_stdout_fd = xor i1 %is_stdout_fd, true
              %capturing = and i1 %has_sink_tag, %not_stdout_fd
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
              %oversized = icmp ugt i64 %len64, 65536
              br i1 %oversized, label %write_direct_prepare, label %buffer_prepare

            write_direct_prepare:
              %flushed_direct = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              %written64 = call i64 @write(i32 1, ptr %data, i64 %len64)
              %written32 = trunc i64 %written64 to i32
              store i32 %written32, ptr %written, align 4
              %direct_nonnegative = icmp sge i64 %written64, 0
              %direct_complete = icmp eq i64 %written64, %len64
              %direct_ok = and i1 %direct_nonnegative, %direct_complete
              %direct_ok32 = zext i1 %direct_ok to i32
              ret i32 %direct_ok32

            buffer_prepare:
              %count = load i64, ptr @sollang_stdout_buffer_count, align 8
              %combined = add i64 %count, %len64
              %needs_flush = icmp ugt i64 %combined, 65536
              br i1 %needs_flush, label %flush, label %append

            flush:
              %flushed = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              br label %append

            append:
              %offset = phi i64 [ %count, %buffer_prepare ], [ 0, %flush ]
              %destination = getelementptr inbounds [65536 x i8], ptr @sollang_stdout_buffer, i64 0, i64 %offset
              %copied = call ptr @memcpy(ptr %destination, ptr %data, i64 %len64)
              %next_count = add i64 %offset, %len64
              store i64 %next_count, ptr @sollang_stdout_buffer_count, align 8
              %appended32 = trunc i64 %len64 to i32
              store i32 %appended32, ptr %written, align 4
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
              %stdout = inttoptr i64 1 to ptr
              %written = alloca i32, align 4
              %flushed = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)
              %read64 = call i64 @read(i32 0, ptr %data, i64 %len64)
              %read32 = trunc i64 %read64 to i32
              store i32 %read32, ptr %read, align 4
              %ok = icmp sgt i64 %read64, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32
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
              %write_ok = call i32 @sollang_write(ptr %stdout, ptr %data, i64 %len, ptr %written_slot)
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
              %temporary_buf = alloca [4096 x i8], align 1
              %temporary_ptr = getelementptr inbounds [4096 x i8], ptr %temporary_buf, i64 0, i64 0
              %temporary_ok = call i32 @sollang_copy_text_to_c_path(ptr %temporary, i64 %temporary_len, ptr %temporary_ptr)
              %temporary_valid = icmp ne i32 %temporary_ok, 0
              br i1 %temporary_valid, label %copy_destination, label %fail

            copy_destination:
              %destination_buf = alloca [4096 x i8], align 1
              %destination_ptr = getelementptr inbounds [4096 x i8], ptr %destination_buf, i64 0, i64 0
              %destination_ok = call i32 @sollang_copy_text_to_c_path(ptr %destination, i64 %destination_len, ptr %destination_ptr)
              %destination_valid = icmp ne i32 %destination_ok, 0
              br i1 %destination_valid, label %replace, label %fail

            replace:
              %status = call i32 @rename(ptr %temporary_ptr, ptr %destination_ptr)
              %ok = icmp eq i32 %status, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32

            fail:
              ret i32 0
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_read_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 0, i32 0)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              %handle = sext i32 %fd to i64
              %ok0 = insertvalue %sollang.file_handle_result poison, i64 %handle, 0
              %ok1 = insertvalue %sollang.file_handle_result %ok0, i32 1, 1
              ret %sollang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [4096 x i8], align 1
              %buf_ptr = getelementptr inbounds [4096 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 577, i32 438)
              %valid = icmp sge i32 %fd, 0
              br i1 %valid, label %success, label %fail

            success:
              %handle = sext i32 %fd to i64
              %ok0 = insertvalue %sollang.file_handle_result poison, i64 %handle, 0
              %ok1 = insertvalue %sollang.file_handle_result %ok0, i32 1, 1
              ret %sollang.file_handle_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal i64 @sollang_platform_duplicate_owned_file(i64 %source) #0 {
            entry:
              %source_fd = trunc i64 %source to i32
              %fd = call i32 @dup(i32 %source_fd)
              %handle = sext i32 %fd to i64
              ret i64 %handle
            }

            define internal %sollang.file_count_result @sollang_platform_read_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
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
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_write_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
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
              %ok0 = insertvalue %sollang.file_count_result poison, i64 %count, 0
              %ok1 = insertvalue %sollang.file_count_result %ok0, i32 1, 1
              ret %sollang.file_count_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal i32 @sollang_platform_sync_owned_file(i64 %handle) #0 {
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

            define internal void @sollang_platform_close_owned_file(i64 %handle) #0 {
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

            define internal i32 @sollang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @sollang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 577, i32 420)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 %fd, ptr @sollang_file_writer_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @sollang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              %fd = load i32, ptr @sollang_file_writer_fd, align 4
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

            define internal i32 @sollang_platform_close_write_file() #0 {
            entry:
              %fd = load i32, ptr @sollang_file_writer_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %close_file, label %fail

            close_file:
              %close_result = call i32 @close(i32 %fd)
              %ok = icmp eq i32 %close_result, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 -1, ptr @sollang_file_writer_fd, align 4
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
              br i1 %copy_is_ok, label %open_file, label %fail

            open_file:
              %fd = call i32 @open(ptr %buf_ptr, i32 0, i32 0)
              %ok = icmp sge i32 %fd, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 %fd, ptr @sollang_file_reader_fd, align 4
              ret i32 1

            fail:
              ret i32 0
            }

            define internal %sollang.file_count_result @sollang_platform_i64_file_count() #0 {
            entry:
              %fd = load i32, ptr @sollang_file_reader_fd, align 4
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
              %fd = load i32, ptr @sollang_file_reader_fd, align 4
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
              %ok0 = insertvalue %sollang.file_int_result poison, i64 %value, 0
              %ok1 = insertvalue %sollang.file_int_result %ok0, i32 1, 1
              ret %sollang.file_int_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_int_result %fail0, i32 0, 1
              ret %sollang.file_int_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_read_file_bytes(ptr %data, i64 %len) #0 {
            entry:
              %fd = load i32, ptr @sollang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %read_file, label %fail

            read_file:
              %count = call i64 @read(i32 %fd, ptr %data, i64 %len)
              %ok = icmp sge i64 %count, 0
              br i1 %ok, label %success, label %fail

            success:
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
              %fd = load i32, ptr @sollang_file_reader_fd, align 4
              %has_fd = icmp sge i32 %fd, 0
              br i1 %has_fd, label %close_file, label %fail

            close_file:
              %close_result = call i32 @close(i32 %fd)
              %ok = icmp eq i32 %close_result, 0
              br i1 %ok, label %success, label %fail

            success:
              store i32 -1, ptr @sollang_file_reader_fd, align 4
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
              %r0 = insertvalue %sollang.mapped_bytes poison, ptr %data, 0
              %r1 = insertvalue %sollang.mapped_bytes %r0, i64 %view_len, 1
              %r2 = insertvalue %sollang.mapped_bytes %r1, ptr %base, 2
              %r3 = insertvalue %sollang.mapped_bytes %r2, i64 %mapped_len, 3
              %r4 = insertvalue %sollang.mapped_bytes %r3, i1 %writable, 4
              ret %sollang.mapped_bytes %r4

            close_fail:
              %ignored_close_fail = call i32 @close(i32 %fd)
              br label %fail

            fail:
              %f0 = insertvalue %sollang.mapped_bytes zeroinitializer, i1 %writable, 4
              ret %sollang.mapped_bytes %f0
            }

            define internal i32 @sollang_mapped_flush(ptr %base, i64 %mapped_len) #0 {
            entry:
              %result = call i32 @msync(ptr %base, i64 %mapped_len, i32 4)
              %ok = icmp eq i32 %result, 0
              %ok32 = zext i1 %ok to i32
              ret i32 %ok32
            }

            define internal void @sollang_mapped_unmap(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ignored = call i32 @munmap(ptr %base, i64 %mapped_len)
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
              %style_ok = icmp eq i32 %style, 0
              br i1 %style_ok, label %prepare, label %fail

            prepare:
              %buffer_size = add i64 %len, 1
              %path_buffer = call ptr @sollang_alloc(i64 %buffer_size)
              %path_ok = icmp ne ptr %path_buffer, null
              br i1 %path_ok, label %copy_path, label %fail

            copy_path:
              call void @llvm.memcpy.p0.p0.i64(ptr %path_buffer, ptr %path, i64 %len, i1 false)
              %zero_ptr = getelementptr i8, ptr %path_buffer, i64 %len
              store i8 0, ptr %zero_ptr, align 1
              %directory = call ptr @opendir(ptr %path_buffer)
              call void @sollang_free(ptr %path_buffer)
              %opened = icmp ne ptr %directory, null
              br i1 %opened, label %enumerate, label %fail

            enumerate:
              %head = phi ptr [ null, %copy_path ], [ %advanced_head, %advance ]
              %count = phi i64 [ 0, %copy_path ], [ %advanced_count, %advance ]
              %total = phi i64 [ 0, %copy_path ], [ %advanced_total, %advance ]
              %errno_ptr = call ptr @__errno_location()
              store i32 0, ptr %errno_ptr, align 4
              %entry_value = call ptr @readdir(ptr %directory)
              %at_end = icmp eq ptr %entry_value, null
              br i1 %at_end, label %finish_enumeration, label %scan_entry

            scan_entry:
              %name = getelementptr i8, ptr %entry_value, i64 19
              br label %name_length

            name_length:
              %name_index = phi i64 [ 0, %scan_entry ], [ %name_next, %name_continue ]
              %name_slot = getelementptr i8, ptr %name, i64 %name_index
              %name_byte = load i8, ptr %name_slot, align 1
              %name_done = icmp eq i8 %name_byte, 0
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
              %type_ptr = getelementptr i8, ptr %entry_value, i64 18
              %entry_type = load i8, ptr %type_ptr, align 1
              %is_symlink = icmp eq i8 %entry_type, 10
              %is_directory = icmp eq i8 %entry_type, 4
              %is_file = icmp eq i8 %entry_type, 8
              %file_or_other = select i1 %is_file, i8 0, i8 3
              %directory_or_other = select i1 %is_directory, i8 1, i8 %file_or_other
              %kind = select i1 %is_symlink, i8 2, i8 %directory_or_other
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
              br label %enumerate

            finish_enumeration:
              %errno = load i32, ptr %errno_ptr, align 4
              %normal_end = icmp eq i32 %errno, 0
              br i1 %normal_end, label %close_success, label %enumeration_failed

            close_success:
              %closed = call i32 @closedir(ptr %directory)
              %raw = call ptr @sollang_directory_serialize(ptr %head, i64 %total)
              %has_payload = icmp ugt i64 %total, 0
              %raw_missing = icmp eq ptr %raw, null
              %serialization_failed = and i1 %has_payload, %raw_missing
              br i1 %serialization_failed, label %fail, label %success

            success:
              %success0 = insertvalue %sollang.directory_result poison, ptr %raw, 0
              %success1 = insertvalue %sollang.directory_result %success0, i64 %total, 1
              %success2 = insertvalue %sollang.directory_result %success1, i64 %count, 2
              %success3 = insertvalue %sollang.directory_result %success2, i32 1, 3
              ret %sollang.directory_result %success3

            allocation_failed:
              call void @sollang_directory_free_nodes(ptr %head)
              %allocation_closed = call i32 @closedir(ptr %directory)
              br label %fail

            enumeration_failed:
              call void @sollang_directory_free_nodes(ptr %head)
              %failure_closed = call i32 @closedir(ptr %directory)
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
              %style_ok = icmp eq i32 %style, 0
              br i1 %style_ok, label %prepare, label %fail

            prepare:
              %buffer_size = add i64 %len, 1
              %buffer = call ptr @sollang_alloc(i64 %buffer_size)
              %buffer_ok = icmp ne ptr %buffer, null
              br i1 %buffer_ok, label %copy, label %fail

            copy:
              call void @llvm.memcpy.p0.p0.i64(ptr %buffer, ptr %path, i64 %len, i1 false)
              %zero_ptr = getelementptr i8, ptr %buffer, i64 %len
              store i8 0, ptr %zero_ptr, align 1
              %canonical = call ptr @realpath(ptr %buffer, ptr null)
              call void @sollang_free(ptr %buffer)
              %canonical_ok = icmp ne ptr %canonical, null
              br i1 %canonical_ok, label %metadata, label %fail

            metadata:
              %stat_buffer = alloca [144 x i8], align 8
              %stat_status = call i32 @stat(ptr %canonical, ptr %stat_buffer)
              %stat_ok = icmp eq i32 %stat_status, 0
              br i1 %stat_ok, label %success, label %canonical_fail

            success:
              %canonical_len = call i64 @strlen(ptr %canonical)
              %mode_ptr = getelementptr i8, ptr %stat_buffer, i64 24
              %mode = load i32, ptr %mode_ptr, align 4
              %type = and i32 %mode, 61440
              %is_file = icmp eq i32 %type, 32768
              %is_directory = icmp eq i32 %type, 16384
              %kind_directory_or_other = select i1 %is_directory, i8 1, i8 3
              %kind = select i1 %is_file, i8 0, i8 %kind_directory_or_other
              %size_ptr = getelementptr i8, ptr %stat_buffer, i64 48
              %size = load i64, ptr %size_ptr, align 8
              %modified_seconds_ptr = getelementptr i8, ptr %stat_buffer, i64 88
              %modified_seconds = load i64, ptr %modified_seconds_ptr, align 8
              %modified_fraction_ptr = getelementptr i8, ptr %stat_buffer, i64 96
              %modified_fraction = load i64, ptr %modified_fraction_ptr, align 8
              %modified_seconds_nanos = mul i64 %modified_seconds, 1000000000
              %modified_nanos = add i64 %modified_seconds_nanos, %modified_fraction
              %result0 = insertvalue %sollang.path_query_result poison, ptr %canonical, 0
              %result1 = insertvalue %sollang.path_query_result %result0, i64 %canonical_len, 1
              %result2 = insertvalue %sollang.path_query_result %result1, i8 %kind, 2
              %result3 = insertvalue %sollang.path_query_result %result2, i64 %size, 3
              %result4 = insertvalue %sollang.path_query_result %result3, i64 %modified_nanos, 4
              %result5 = insertvalue %sollang.path_query_result %result4, i32 1, 5
              ret %sollang.path_query_result %result5

            canonical_fail:
              call void @sollang_free(ptr %canonical)
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
        functions.AppendLine("  %stdin = inttoptr i64 0 to ptr");
        functions.AppendLine("  %stdout = inttoptr i64 1 to ptr");
        functions.AppendLine("  %stdout_is_tty_status = call i32 @isatty(i32 1)");
        functions.AppendLine("  %stdout_is_tty = icmp ne i32 %stdout_is_tty_status, 0");
        functions.AppendLine("  store i1 %stdout_is_tty, ptr @sollang_stdout_line_buffered, align 1");
    }

    public override void EmitExitHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdout_flushed = call i32 @sollang_flush_stdout(ptr %stdout, ptr %written)");
    }

    public override void EmitProcessEntry(StringBuilder functions)
    {
        functions.AppendLine("  %argc64 = zext i32 %argc to i64");
        functions.AppendLine("  store i64 %argc64, ptr @sollang_argument_count_value, align 8");
        functions.AppendLine("  store ptr %argv, ptr @sollang_argument_vector, align 8");
    }
}
