using System.Text;

namespace SmallLang.Compiler.CodeGen;

internal sealed class WasmBrowserLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "wasm32-unknown-unknown-wasm";

    public override string EntryPointName => "smalllang_start";

    public override int PointerBitWidth => 32;

    public override bool SupportsHeapAllocation => false;

    public override bool SupportsMemoryMapping => false;

    public override bool SupportsProcessArguments => false;

    public override bool SupportsEnvironment => false;

    public override void EmitProcessPrimitives(StringBuilder functions)
    {
    }

    public override void EmitMappedFilePrimitives(StringBuilder functions)
    {
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare i32 @smalllang_browser_write(ptr, i32)");
        functions.AppendLine("declare i64 @smalllang_browser_now_millis()");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
    }

    public override void EmitMemoryPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal ptr @smalllang_alloc(i64 %bytes) #0 {
            entry:
              unreachable
            }

            define internal void @smalllang_free(ptr %ptr) #0 {
            entry:
              ret void
            }

            """);
    }

    public override void EmitTimePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @smalllang_now_millis() #0 {
            entry:
              %millis = call i64 @smalllang_browser_now_millis()
              ret i64 %millis
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @smalllang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %too_large = icmp ugt i64 %len64, 2147483647
              br i1 %too_large, label %fail, label %write

            write:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @smalllang_browser_write(ptr %data, i32 %len)
              %is_ok = icmp ne i32 %ok, 0
              br i1 %is_ok, label %success, label %fail

            success:
              store i32 %len, ptr %written, align 4
              ret i32 1

            fail:
              store i32 0, ptr %written, align 4
              ret i32 0
            }

            define internal i32 @smalllang_read_stdin(ptr %stdin, ptr %data, i64 %len64, ptr %read) #0 {
            entry:
              store i32 0, ptr %read, align 4
              ret i32 0
            }

            """);
    }

    public override void EmitFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @smalllang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              ret i32 0
            }

            define internal i32 @smalllang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              ret i32 0
            }

            define internal i32 @smalllang_platform_close_write_file() #0 {
            entry:
              ret i32 0
            }

            define internal i32 @smalllang_platform_open_read_file(ptr %path, i64 %len) #0 {
            entry:
              ret i32 0
            }

            define internal %smalllang.file_count_result @smalllang_platform_i64_file_count() #0 {
            entry:
              %fail0 = insertvalue %smalllang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_count_result %fail0, i32 0, 1
              ret %smalllang.file_count_result %fail1
            }

            define internal %smalllang.file_int_result @smalllang_platform_read_i64_at(i64 %index) #0 {
            entry:
              %fail0 = insertvalue %smalllang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.file_int_result %fail0, i32 0, 1
              ret %smalllang.file_int_result %fail1
            }

            define internal i32 @smalllang_platform_close_read_file() #0 {
            entry:
              ret i32 0
            }

            """);
    }

    public override void EmitEntryHandles(StringBuilder functions)
    {
        functions.AppendLine("  %stdin = inttoptr i32 0 to ptr");
        functions.AppendLine("  %stdout = inttoptr i32 1 to ptr");
    }
}
