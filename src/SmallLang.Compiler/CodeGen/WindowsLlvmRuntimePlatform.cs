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
        functions.AppendLine("declare dllimport ptr @GetProcessHeap()");
        functions.AppendLine("declare dllimport ptr @HeapAlloc(ptr, i32, i64)");
        functions.AppendLine("declare dllimport i32 @HeapFree(ptr, i32, ptr)");
        functions.AppendLine("declare dllimport i64 @GetTickCount64()");
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

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = call ptr @GetStdHandle(i32 -10)");
        functions.AppendLine("  %stdout = call ptr @GetStdHandle(i32 -11)");
    }
}
