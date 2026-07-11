using System.Text;

namespace SmallLang.Compiler.CodeGen;

internal sealed class WindowsLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "x86_64-pc-windows-msvc";

    public override string EntryPointName => "smalllang_start";

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@smalllang_file_writer = internal global ptr null");
        globals.AppendLine("@smalllang_file_reader = internal global ptr null");
        globals.AppendLine("@smalllang_argument_count_value = internal global i64 0");
        globals.AppendLine("@smalllang_argument_records = internal global ptr null");
        globals.AppendLine("@smalllang_environment_allocations = internal global ptr null");
        globals.AppendLine("@smalllang_environment_empty = internal constant [1 x i8] zeroinitializer, align 1");
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare dllimport ptr @GetStdHandle(i32)");
        functions.AppendLine("declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @ReadFile(ptr, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport ptr @CreateFileA(ptr, i32, i32, ptr, i32, i32, ptr)");
        functions.AppendLine("declare dllimport i32 @CloseHandle(ptr)");
        functions.AppendLine("declare dllimport i32 @SetFilePointerEx(ptr, i64, ptr, i32)");
        functions.AppendLine("declare dllimport i32 @GetFileSizeEx(ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @SetEndOfFile(ptr)");
        functions.AppendLine("declare dllimport ptr @CreateFileMappingA(ptr, ptr, i32, i32, i32, ptr)");
        functions.AppendLine("declare dllimport ptr @MapViewOfFile(ptr, i32, i32, i32, i64)");
        functions.AppendLine("declare dllimport i32 @UnmapViewOfFile(ptr)");
        functions.AppendLine("declare dllimport i32 @FlushViewOfFile(ptr, i64)");
        functions.AppendLine("declare dllimport ptr @GetProcessHeap()");
        functions.AppendLine("declare dllimport ptr @HeapAlloc(ptr, i32, i64)");
        functions.AppendLine("declare dllimport i32 @HeapFree(ptr, i32, ptr)");
        functions.AppendLine("declare dllimport i64 @GetTickCount64()");
        functions.AppendLine("declare dllimport ptr @GetCommandLineW()");
        functions.AppendLine("declare dllimport ptr @CommandLineToArgvW(ptr, ptr)");
        functions.AppendLine("declare dllimport i32 @WideCharToMultiByte(i32, i32, ptr, i32, ptr, i32, ptr, ptr)");
        functions.AppendLine("declare dllimport ptr @LocalFree(ptr)");
        functions.AppendLine("declare dllimport i32 @MultiByteToWideChar(i32, i32, ptr, i32, ptr, i32)");
        functions.AppendLine("declare dllimport i32 @GetEnvironmentVariableW(ptr, ptr, i32)");
        functions.AppendLine("declare dllimport i32 @GetLastError()");
        functions.AppendLine("declare dllimport void @SetLastError(i32)");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
    }

    public override void EmitTimePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @smalllang_now_millis() #0 {
            entry:
              %millis = call i64 @GetTickCount64()
              ret i64 %millis
            }

            """);
    }

    public override void EmitEnvironmentPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i1 @smalllang_track_environment_allocation(ptr %allocation) #0 {
            entry:
              %node = call ptr @smalllang_alloc(i64 16)
              %ok = icmp ne ptr %node, null
              br i1 %ok, label %store, label %fail

            store:
              store ptr %allocation, ptr %node, align 8
              %next_slot = getelementptr i8, ptr %node, i64 8
              %head = load ptr, ptr @smalllang_environment_allocations, align 8
              store ptr %head, ptr %next_slot, align 8
              store ptr %node, ptr @smalllang_environment_allocations, align 8
              ret i1 true

            fail:
              ret i1 false
            }

            define internal %smalllang.environment_result @smalllang_environment(ptr %name, i64 %name_len) #0 {
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
              %wide_key = call ptr @smalllang_alloc(i64 %key_bytes)
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
              call void @smalllang_free(ptr %wide_key)
              %not_found = icmp eq i32 %last_error, 203
              br i1 %not_found, label %missing, label %empty_present

            empty_present:
              %empty_ptr = getelementptr inbounds [1 x i8], ptr @smalllang_environment_empty, i64 0, i64 0
              %e0 = insertvalue %smalllang.environment_result zeroinitializer, ptr %empty_ptr, 0
              %e1 = insertvalue %smalllang.environment_result %e0, i1 true, 2
              %e2 = insertvalue %smalllang.environment_result %e1, i1 true, 3
              ret %smalllang.environment_result %e2

            value_alloc:
              %required64 = zext i32 %required to i64
              %wide_bytes = mul i64 %required64, 2
              %wide_value = call ptr @smalllang_alloc(i64 %wide_bytes)
              %wide_value_ok = icmp ne ptr %wide_value, null
              br i1 %wide_value_ok, label %value_read, label %free_key_error

            value_read:
              %value_chars = call i32 @GetEnvironmentVariableW(ptr %wide_key, ptr %wide_value, i32 %required)
              call void @smalllang_free(ptr %wide_key)
              %value_empty = icmp eq i32 %value_chars, 0
              br i1 %value_empty, label %free_wide_empty, label %value_read_check

            free_wide_empty:
              call void @smalllang_free(ptr %wide_value)
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
              %utf8 = call ptr @smalllang_alloc(i64 %utf8_bytes)
              %utf8_ok = icmp ne ptr %utf8, null
              br i1 %utf8_ok, label %utf8_convert, label %free_wide_error

            utf8_convert:
              %utf8_written = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide_value, i32 %value_chars, ptr %utf8, i32 %utf8_bytes32, ptr null, ptr null)
              call void @smalllang_free(ptr %wide_value)
              %utf8_converted = icmp eq i32 %utf8_written, %utf8_bytes32
              br i1 %utf8_converted, label %track, label %free_utf8_error

            track:
              %tracked = call i1 @smalllang_track_environment_allocation(ptr %utf8)
              br i1 %tracked, label %present, label %free_utf8_error

            present:
              %p0 = insertvalue %smalllang.environment_result poison, ptr %utf8, 0
              %p1 = insertvalue %smalllang.environment_result %p0, i64 %utf8_bytes, 1
              %p2 = insertvalue %smalllang.environment_result %p1, i1 true, 2
              %p3 = insertvalue %smalllang.environment_result %p2, i1 true, 3
              ret %smalllang.environment_result %p3

            free_utf8_error:
              call void @smalllang_free(ptr %utf8)
              br label %error

            free_wide_error:
              call void @smalllang_free(ptr %wide_value)
              br label %error

            free_key_error:
              call void @smalllang_free(ptr %wide_key)
              br label %error

            missing:
              %m0 = insertvalue %smalllang.environment_result zeroinitializer, i1 true, 3
              ret %smalllang.environment_result %m0

            error:
              ret %smalllang.environment_result zeroinitializer
            }

            define internal void @smalllang_dispose_environment() #0 {
            entry:
              %head = load ptr, ptr @smalllang_environment_allocations, align 8
              br label %loop

            loop:
              %node = phi ptr [ %head, %entry ], [ %next, %free_node ]
              %done = icmp eq ptr %node, null
              br i1 %done, label %finish, label %free_node

            free_node:
              %allocation = load ptr, ptr %node, align 8
              %next_slot = getelementptr i8, ptr %node, i64 8
              %next = load ptr, ptr %next_slot, align 8
              call void @smalllang_free(ptr %allocation)
              call void @smalllang_free(ptr %node)
              br label %loop

            finish:
              ret void
            }

            """);
    }

    public override void EmitMemoryPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal ptr @smalllang_alloc(i64 %bytes) #0 {
            entry:
              %heap = call ptr @GetProcessHeap()
              %ptr = call ptr @HeapAlloc(ptr %heap, i32 0, i64 %bytes)
              ret ptr %ptr
            }

            define internal void @smalllang_free(ptr %ptr) #0 {
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
            define internal i32 @smalllang_init_arguments() #0 {
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
              %records = call ptr @smalllang_alloc(i64 %record_bytes)
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
              %utf8 = call ptr @smalllang_alloc(i64 %bytes)
              %utf8_ok = icmp ne ptr %utf8, null
              br i1 %utf8_ok, label %convert, label %cleanup_partial

            convert:
              %written = call i32 @WideCharToMultiByte(i32 65001, i32 128, ptr %wide, i32 -1, ptr %utf8, i32 %bytes32, ptr null, ptr null)
              %converted = icmp eq i32 %written, %bytes32
              br i1 %converted, label %stored, label %free_current

            free_current:
              call void @smalllang_free(ptr %utf8)
              br label %cleanup_partial

            stored:
              %record = getelementptr %smalllang.text, ptr %records, i64 %i
              %ptr_slot = getelementptr inbounds %smalllang.text, ptr %record, i32 0, i32 0
              store ptr %utf8, ptr %ptr_slot, align 8
              %len_slot = getelementptr inbounds %smalllang.text, ptr %record, i32 0, i32 1
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
              %old_record = getelementptr %smalllang.text, ptr %records, i64 %prev
              %old_ptr_slot = getelementptr inbounds %smalllang.text, ptr %old_record, i32 0, i32 0
              %old_ptr = load ptr, ptr %old_ptr_slot, align 8
              call void @smalllang_free(ptr %old_ptr)
              br label %cleanup_partial

            free_records_fail:
              call void @smalllang_free(ptr %records)
              br label %free_wide_fail

            success:
              store i64 %argc, ptr @smalllang_argument_count_value, align 8
              store ptr %records, ptr @smalllang_argument_records, align 8
              %ignored_wide = call ptr @LocalFree(ptr %wide_argv)
              ret i32 1

            free_wide_fail:
              %ignored_fail = call ptr @LocalFree(ptr %wide_argv)
              br label %fail

            fail:
              ret i32 0
            }

            define internal i64 @smalllang_argument_count() #0 {
            entry:
              %count = load i64, ptr @smalllang_argument_count_value, align 8
              ret i64 %count
            }

            define internal %smalllang.text @smalllang_argument(i64 %index) #0 {
            entry:
              %records = load ptr, ptr @smalllang_argument_records, align 8
              %record = getelementptr %smalllang.text, ptr %records, i64 %index
              %value = load %smalllang.text, ptr %record, align 8
              ret %smalllang.text %value
            }

            define internal void @smalllang_dispose_arguments() #0 {
            entry:
              %count = load i64, ptr @smalllang_argument_count_value, align 8
              %records = load ptr, ptr @smalllang_argument_records, align 8
              br label %loop

            loop:
              %i = phi i64 [ 0, %entry ], [ %next, %free_item ]
              %done = icmp eq i64 %i, %count
              br i1 %done, label %finish, label %free_item

            free_item:
              %record = getelementptr %smalllang.text, ptr %records, i64 %i
              %ptr_slot = getelementptr inbounds %smalllang.text, ptr %record, i32 0, i32 0
              %ptr = load ptr, ptr %ptr_slot, align 8
              call void @smalllang_free(ptr %ptr)
              %next = add i64 %i, 1
              br label %loop

            finish:
              call void @smalllang_free(ptr %records)
              ret void
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define dso_local void @__chkstk() #0 {
            entry:
              ret void
            }

            define internal i32 @smalllang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @WriteFile(ptr %stdout, ptr %data, i32 %len, ptr %written, ptr null)
              ret i32 %ok
            }

            define internal i32 @smalllang_read_stdin(ptr %stdin, ptr %data, i64 %len64, ptr %read) #0 {
            entry:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @ReadFile(ptr %stdin, ptr %data, i32 %len, ptr %read, ptr null)
              ret i32 %ok
            }

            """);
    }

    public override void EmitFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @smalllang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              %buf = alloca [260 x i8], align 1
              %buf_ptr = getelementptr inbounds [260 x i8], ptr %buf, i64 0, i64 0
              %copy_ok = call i32 @smalllang_copy_text_to_c_path(ptr %path, i64 %len, ptr %buf_ptr)
              %copy_is_ok = icmp ne i32 %copy_ok, 0
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 1073741824, i32 0, ptr null, i32 2, i32 128, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              store ptr %handle, ptr @smalllang_file_writer, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @smalllang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              %handle = load ptr, ptr @smalllang_file_writer, align 8
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

            define internal i32 @smalllang_platform_close_write_file() #0 {
            entry:
              %handle = load ptr, ptr @smalllang_file_writer, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %close, label %fail

            close:
              %ok = call i32 @CloseHandle(ptr %handle)
              %close_ok = icmp ne i32 %ok, 0
              br i1 %close_ok, label %success, label %fail

            success:
              store ptr null, ptr @smalllang_file_writer, align 8
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
              br i1 %copy_is_ok, label %open, label %fail

            open:
              %handle = call ptr @CreateFileA(ptr %buf_ptr, i32 -2147483648, i32 1, ptr null, i32 3, i32 128, ptr null)
              %handle_int = ptrtoint ptr %handle to i64
              %invalid = icmp eq i64 %handle_int, -1
              br i1 %invalid, label %fail, label %success

            success:
              store ptr %handle, ptr @smalllang_file_reader, align 8
              ret i32 1

            fail:
              ret i32 0
            }

            define internal %smalllang.file_count_result @smalllang_platform_i64_file_count() #0 {
            entry:
              %handle = load ptr, ptr @smalllang_file_reader, align 8
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
              %handle = load ptr, ptr @smalllang_file_reader, align 8
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
              %ok0 = insertvalue %smalllang.file_int_result poison, i64 %value, 0
              %ok1 = insertvalue %smalllang.file_int_result %ok0, i32 1, 1
              ret %smalllang.file_int_result %ok1

            fail:
              %fail0 = insertvalue %smalllang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_int_result %fail0, i32 0, 1
              ret %smalllang.file_int_result %fail1
            }

            define internal i32 @smalllang_platform_close_read_file() #0 {
            entry:
              %handle = load ptr, ptr @smalllang_file_reader, align 8
              %has_handle = icmp ne ptr %handle, null
              br i1 %has_handle, label %close, label %fail

            close:
              %ok = call i32 @CloseHandle(ptr %handle)
              %close_ok = icmp ne i32 %ok, 0
              br i1 %close_ok, label %success, label %fail

            success:
              store ptr null, ptr @smalllang_file_reader, align 8
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
              %r0 = insertvalue %smalllang.mapped_bytes poison, ptr %data, 0
              %r1 = insertvalue %smalllang.mapped_bytes %r0, i64 %view_len, 1
              %r2 = insertvalue %smalllang.mapped_bytes %r1, ptr %base, 2
              %r3 = insertvalue %smalllang.mapped_bytes %r2, i64 %mapped_len, 3
              %r4 = insertvalue %smalllang.mapped_bytes %r3, i1 %writable, 4
              ret %smalllang.mapped_bytes %r4

            mapping_close_fail:
              %ignored_mapping_fail = call i32 @CloseHandle(ptr %mapping_handle)
              br label %close_fail

            close_fail:
              %ignored_close = call i32 @CloseHandle(ptr %file)
              br label %fail

            fail:
              %f0 = insertvalue %smalllang.mapped_bytes zeroinitializer, i1 %writable, 4
              ret %smalllang.mapped_bytes %f0
            }

            define internal i32 @smalllang_mapped_flush(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ok = call i32 @FlushViewOfFile(ptr %base, i64 %mapped_len)
              ret i32 %ok
            }

            define internal void @smalllang_mapped_unmap(ptr %base, i64 %mapped_len) #0 {
            entry:
              %ignored = call i32 @UnmapViewOfFile(ptr %base)
              ret void
            }

            """);
    }

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = call ptr @GetStdHandle(i32 -10)");
        functions.AppendLine("  %stdout = call ptr @GetStdHandle(i32 -11)");
    }

    public override void EmitProcessEntry(StringBuilder functions)
    {
        functions.AppendLine("  %arguments_ok = call i32 @smalllang_init_arguments()");
        functions.AppendLine("  %arguments_valid = icmp ne i32 %arguments_ok, 0");
        functions.AppendLine("  store i1 %arguments_valid, ptr %ok_state, align 1");
    }

    public override void EmitExitCleanup(StringBuilder functions)
    {
        functions.AppendLine("  call void @smalllang_dispose_arguments()");
    }

    public override void EmitEnvironmentCleanup(StringBuilder functions)
    {
        functions.AppendLine("  call void @smalllang_dispose_environment()");
    }
}
