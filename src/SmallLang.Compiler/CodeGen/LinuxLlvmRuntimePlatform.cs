using System.Text;

namespace SmallLang.Compiler.CodeGen;

internal sealed class LinuxLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "x86_64-unknown-linux-gnu";

    public override string EntryPointName => "main";

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@smalllang_file_writer_fd = internal global i32 -1");
        globals.AppendLine("@smalllang_file_reader_fd = internal global i32 -1");
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare i64 @write(i32, ptr, i64)");
        functions.AppendLine("declare i64 @read(i32, ptr, i64)");
        functions.AppendLine("declare i32 @open(ptr, i32, i32)");
        functions.AppendLine("declare i32 @close(i32)");
        functions.AppendLine("declare i64 @lseek(i32, i64, i32)");
        functions.AppendLine("declare i32 @clock_gettime(i32, ptr)");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare ptr @malloc(i64)");
        functions.AppendLine("declare void @free(ptr)");
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

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = inttoptr i64 0 to ptr");
        functions.AppendLine("  %stdout = inttoptr i64 1 to ptr");
    }
}
