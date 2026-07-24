using System.Text;

namespace Sollang.Compiler.CodeGen;

internal sealed class WasmBrowserLlvmRuntimePlatform : LlvmRuntimePlatform
{
    public override string TargetTriple => "wasm32-unknown-unknown-wasm";

    public override string EntryPointName => "sollang_start";

    public override int PointerBitWidth => 32;

    public override bool SupportsHeapAllocation => true;

    // Browser compiler builds retain file-oriented helper functions for
    // self-host parity. Their platform stubs fail at runtime, while the
    // browser entry point uses the in-memory Text source path exclusively.
    public override bool SupportsMemoryMapping => true;

    public override bool SupportsProcessArguments => true;

    public override bool SupportsEnvironment => false;

    public override bool SupportsChildProcesses => false;

    public override bool SupportsDirectoryTraversal => false;

    // A browser main thread cannot synchronously pull a future DOM event
    // without blocking the HTML event loop. EventStream requires a separate
    // host-driven callback lowering before it can be supported here.
    public override bool SupportsEventStreams => false;

    public override void EmitProcessPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @sollang_argument_count() #0 {
            entry:
              %source_count32 = call i32 @sollang_browser_source_count()
              %source_count = zext i32 %source_count32 to i64
              %argument_count = add i64 %source_count, 1
              ret i64 %argument_count
            }

            define internal %sollang.text @sollang_argument(i64 %index) #0 {
            entry:
              %source_count32 = call i32 @sollang_browser_source_count()
              %source_count = zext i32 %source_count32 to i64
              %source_index64 = sub i64 %index, 1
              %has_source_index = icmp ugt i64 %index, 0
              %source_index_in_bounds = icmp ult i64 %source_index64, %source_count
              %is_source = and i1 %has_source_index, %source_index_in_bounds
              br i1 %is_source, label %source, label %program

            source:
              %source_index = trunc i64 %source_index64 to i32
              %source_ptr32 = call i32 @sollang_browser_source_pointer(i32 %source_index)
              %source_ptr = inttoptr i32 %source_ptr32 to ptr
              %source_len32 = call i32 @sollang_browser_source_length(i32 %source_index)
              %source_len = zext i32 %source_len32 to i64
              %source0 = insertvalue %sollang.text poison, ptr %source_ptr, 0
              %source1 = insertvalue %sollang.text %source0, i64 %source_len, 1
              ret %sollang.text %source1

            program:
              %program0 = insertvalue %sollang.text poison, ptr @sollang_browser_program_name, 0
              %program1 = insertvalue %sollang.text %program0, i64 8, 1
              ret %sollang.text %program1
            }

            """);
    }

    public override void EmitMappedFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal %sollang.mapped_bytes @sollang_map_file(ptr %path, i64 %path_len, i64 %offset, i64 %requested_len, i64 %requested_size, i1 %writable) #0 {
            entry:
              ret %sollang.mapped_bytes zeroinitializer
            }

            define internal i32 @sollang_mapped_flush(ptr %base, i64 %mapped_len) #0 {
            entry:
              ret i32 0
            }

            define internal void @sollang_mapped_unmap(ptr %base, i64 %mapped_len) #0 {
            entry:
              ret void
            }

            """);
    }

    public override void EmitDirectoryPrimitives(StringBuilder functions)
    {
    }

    public override void EmitExternalDeclarations(StringBuilder functions)
    {
        if (UsesProcessExit)
        {
            functions.AppendLine("declare void @exit(i32)");
        }
        functions.AppendLine("declare i32 @sollang_browser_write(ptr, i32)");
        functions.AppendLine("declare void @sollang_browser_panic(ptr, i32)");
        functions.AppendLine("declare i64 @sollang_browser_now_millis()");
        functions.AppendLine("declare i32 @sollang_browser_source_count()");
        functions.AppendLine("declare i32 @sollang_browser_source_pointer(i32)");
        functions.AppendLine("declare i32 @sollang_browser_source_length(i32)");
    }

    public override void EmitGlobals(StringBuilder globals)
    {
        globals.AppendLine("@sollang_browser_program_name = private constant [8 x i8] c\"sollangc\"");
    }

    public override void EmitMemoryDeclarations(StringBuilder functions)
    {
        functions.AppendLine("declare i32 @sollang_browser_alloc(i64)");
    }

    public override void EmitMemoryPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define ptr @sollang_alloc(i64 %bytes) #0 {
            entry:
              %ptr32 = call i32 @sollang_browser_alloc(i64 %bytes)
              %ptr = inttoptr i32 %ptr32 to ptr
              ret ptr %ptr
            }

            define internal void @sollang_free(ptr %ptr) #0 {
            entry:
              ret void
            }

            """);
    }

    public override void EmitTimePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i64 @sollang_now_millis() #0 {
            entry:
              %millis = call i64 @sollang_browser_now_millis()
              ret i64 %millis
            }

            """);
    }

    public override void EmitIoPrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %too_large = icmp ugt i64 %len64, 2147483647
              br i1 %too_large, label %fail, label %write

            write:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @sollang_browser_write(ptr %data, i32 %len)
              %is_ok = icmp ne i32 %ok, 0
              br i1 %is_ok, label %success, label %fail

            success:
              store i32 %len, ptr %written, align 4
              ret i32 1

            fail:
              store i32 0, ptr %written, align 4
              ret i32 0
            }

            define internal i32 @sollang_read_stdin(ptr %stdin, ptr %data, i64 %len64, ptr %read) #0 {
            entry:
              store i32 0, ptr %read, align 4
              ret i32 0
            }

            """);
    }

    public override void EmitFilePrimitives(StringBuilder functions)
    {
        functions.AppendLine("""
            define internal i32 @sollang_platform_atomic_replace_file(ptr %temporary, i64 %temporary_len, ptr %destination, i64 %destination_len) #0 {
            entry:
              ret i32 0
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_read_file(ptr %path, i64 %len) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal %sollang.file_handle_result @sollang_platform_open_owned_write_file(ptr %path, i64 %len) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_handle_result poison, i64 -1, 0
              %fail1 = insertvalue %sollang.file_handle_result %fail0, i32 0, 1
              ret %sollang.file_handle_result %fail1
            }

            define internal i64 @sollang_platform_duplicate_owned_file(i64 %source) #0 {
            entry:
              ret i64 -1
            }

            define internal %sollang.file_count_result @sollang_platform_read_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_write_owned_file_at(i64 %handle, ptr %data, i64 %len, i64 %offset) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal void @sollang_platform_close_owned_file(i64 %handle) #0 {
            entry:
              ret void
            }

            define internal i32 @sollang_platform_open_write_file(ptr %path, i64 %len) #0 {
            entry:
              ret i32 0
            }

            define internal i32 @sollang_platform_write_file_bytes(ptr %data, i64 %len64) #0 {
            entry:
              ret i32 0
            }

            define internal i32 @sollang_platform_close_write_file() #0 {
            entry:
              ret i32 0
            }

            define internal i32 @sollang_platform_open_read_file(ptr %path, i64 %len) #0 {
            entry:
              ret i32 0
            }

            define internal %sollang.file_count_result @sollang_platform_i64_file_count() #0 {
            entry:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal %sollang.file_int_result @sollang_platform_read_i64_at(i64 %index) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_int_result %fail0, i32 0, 1
              ret %sollang.file_int_result %fail1
            }

            define internal %sollang.file_count_result @sollang_platform_read_file_bytes(ptr %data, i64 %len) #0 {
            entry:
              %fail0 = insertvalue %sollang.file_count_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_count_result %fail0, i32 0, 1
              ret %sollang.file_count_result %fail1
            }

            define internal i32 @sollang_platform_close_read_file() #0 {
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
