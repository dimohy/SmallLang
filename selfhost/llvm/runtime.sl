namespace smalllang.compiler.llvm.runtime

# Target runtime definitions used by LLVM emitted from the self-host compiler.
# The module index is discovered from the analyzed package, so file ordering
# never becomes part of the runtime ABI.
public emitWindows runtimeModule: Int -> Unit uses Console {
    """
    declare i32 @getchar()
    declare dllimport ptr @CreateFileA(ptr, i32, i32, ptr, i32, i32, ptr)
    declare dllimport i32 @CloseHandle(ptr)
    declare dllimport i32 @GetFileSizeEx(ptr, ptr)
    declare dllimport ptr @CreateFileMappingA(ptr, ptr, i32, i32, i32, ptr)
    declare dllimport ptr @MapViewOfFile(ptr, i32, i32, i32, i64)
    declare dllimport i32 @UnmapViewOfFile(ptr)
    declare dllimport i64 @GetTickCount64()
    declare void @llvm.trap()
    @sl_stage2_random_state = internal global i64 88172645463325252
    """ -> println
    "define void @sl_m$(runtimeModule)_s0(%sl.text %arg) {" -> println
    """
    entry:
      %data = extractvalue %sl.text %arg, 0
      %length = extractvalue %sl.text %arg, 1
      call void @sl_runtime_print(ptr %data, i64 %length, i1 false)
      ret void
    }
    """ -> println
    "define void @sl_m$(runtimeModule)_s1(%sl.text %arg) {" -> println
    """
    entry:
      %data = extractvalue %sl.text %arg, 0
      %length = extractvalue %sl.text %arg, 1
      call void @sl_runtime_print(ptr %data, i64 %length, i1 true)
      ret void
    }
    """ -> println
    "define i32 @sl_m$(runtimeModule)_s2(%sl.text %arg) {" -> println
    """
    entry:
      %data = extractvalue %sl.text %arg, 0
      %length = extractvalue %sl.text %arg, 1
      call void @sl_runtime_print(ptr %data, i64 %length, i1 false)
      %value_slot = alloca i32, align 4
      %started_slot = alloca i1, align 1
      %negative_slot = alloca i1, align 1
      store i32 0, ptr %value_slot, align 4
      store i1 false, ptr %started_slot, align 1
      store i1 false, ptr %negative_slot, align 1
      br label %read
    read:
      %character = call i32 @getchar()
      %eof = icmp eq i32 %character, -1
      br i1 %eof, label %done, label %classify
    classify:
      %digit_low = icmp sge i32 %character, 48
      %digit_high = icmp sle i32 %character, 57
      %digit = and i1 %digit_low, %digit_high
      br i1 %digit, label %append, label %non_digit
    append:
      %previous = load i32, ptr %value_slot, align 4
      %scaled = mul i32 %previous, 10
      %digit_value = sub i32 %character, 48
      %updated = add i32 %scaled, %digit_value
      store i32 %updated, ptr %value_slot, align 4
      store i1 true, ptr %started_slot, align 1
      br label %read
    non_digit:
      %started = load i1, ptr %started_slot, align 1
      br i1 %started, label %done, label %prefix
    prefix:
      %minus = icmp eq i32 %character, 45
      br i1 %minus, label %mark_negative, label %read
    mark_negative:
      store i1 true, ptr %negative_slot, align 1
      br label %read
    done:
      %magnitude = load i32, ptr %value_slot, align 4
      %negative = load i1, ptr %negative_slot, align 1
      %negated = sub i32 0, %magnitude
      %result = select i1 %negative, i32 %negated, i32 %magnitude
      ret i32 %result
    }
    """ -> println
    "define void @sl_m$(runtimeModule)_s3(i32 %arg) {" -> println
    """
    entry:
      %seed = sext i32 %arg to i64
      store i64 %seed, ptr @sl_stage2_random_state, align 8
      ret void
    }
    """ -> println
    "define i32 @sl_m$(runtimeModule)_s4(i32 %arg) {" -> println
    """
    entry:
      %valid = icmp sgt i32 %arg, 0
      br i1 %valid, label %next, label %failure
    next:
      %state = load i64, ptr @sl_stage2_random_state, align 8
      %product = mul i64 %state, 6364136223846793005
      %updated = add i64 %product, 1442695040888963407
      store i64 %updated, ptr @sl_stage2_random_state, align 8
      %bound = zext i32 %arg to i64
      %value = urem i64 %updated, %bound
      %result = trunc i64 %value to i32
      ret i32 %result
    failure:
      ret i32 0
    }

    ; Legacy sorted-Int demo intrinsics remain linkable in a stage-2 compiler.
    ; They are not used by the compiler pipeline; the typed File API supersedes
    ; them and will receive its target runtime in the following runtime slice.
    """ -> println
    "define void @sl_m$(runtimeModule)_s5(%sl.text %arg) { ret void }" -> println
    "define void @sl_m$(runtimeModule)_s6(i32 %arg) { ret void }" -> println
    "define void @sl_m$(runtimeModule)_s7() { ret void }" -> println
    "define void @sl_m$(runtimeModule)_s8(%sl.text %arg) { ret void }" -> println
    "define i32 @sl_m$(runtimeModule)_s9(i32 %arg) { ret i32 %arg }" -> println
    "define void @sl_m$(runtimeModule)_s10() { ret void }" -> println
    "define i64 @sl_m$(runtimeModule)_s11() {" -> println
    """
    entry:
      %value = call i64 @GetTickCount64()
      ret i64 %value
    }
    """ -> println
}

public emitWindowsSourceTextDeclarations: -> Unit uses Console {
    """
    declare dllimport ptr @CreateFileA(ptr, i32, i32, ptr, i32, i32, ptr)
    declare dllimport i32 @CloseHandle(ptr)
    declare dllimport i32 @GetFileSizeEx(ptr, ptr)
    declare dllimport ptr @CreateFileMappingA(ptr, ptr, i32, i32, i32, ptr)
    declare dllimport ptr @MapViewOfFile(ptr, i32, i32, i32, i64)
    declare dllimport i32 @UnmapViewOfFile(ptr)
    declare void @llvm.trap()
    """ -> println
}

public emitWindowsSourceText: -> Unit uses Console {
    """
    define %sl.source_text @sl_runtime_map_text(%sl.text %path) {
    entry:
      %path_data = extractvalue %sl.text %path, 0
      %path_length = extractvalue %sl.text %path, 1
      %buffer_length = add i64 %path_length, 1
      %buffer = call ptr @malloc(i64 %buffer_length)
      %allocated = icmp ne ptr %buffer, null
      br i1 %allocated, label %copy, label %failure
    copy:
      %index = phi i64 [ 0, %entry ], [ %next, %copy_body ]
      %copy_done = icmp eq i64 %index, %path_length
      br i1 %copy_done, label %terminate, label %copy_body
    copy_body:
      %source = getelementptr i8, ptr %path_data, i64 %index
      %byte = load i8, ptr %source, align 1
      %destination = getelementptr i8, ptr %buffer, i64 %index
      store i8 %byte, ptr %destination, align 1
      %next = add i64 %index, 1
      br label %copy
    terminate:
      %terminator = getelementptr i8, ptr %buffer, i64 %path_length
      store i8 0, ptr %terminator, align 1
      %file = call ptr @CreateFileA(ptr %buffer, i32 -2147483648, i32 1, ptr null, i32 3, i32 128, ptr null)
      call void @free(ptr %buffer)
      %file_value = ptrtoint ptr %file to i64
      %opened = icmp ne i64 %file_value, -1
      br i1 %opened, label %size, label %failure
    size:
      %size_slot = alloca i64, align 8
      %size_status = call i32 @GetFileSizeEx(ptr %file, ptr %size_slot)
      %size_ok = icmp ne i32 %size_status, 0
      br i1 %size_ok, label %size_known, label %close_failure
    size_known:
      %length = load i64, ptr %size_slot, align 8
      %empty = icmp eq i64 %length, 0
      br i1 %empty, label %empty_success, label %mapping
    empty_success:
      %empty_closed = call i32 @CloseHandle(ptr %file)
      ret %sl.source_text zeroinitializer
    mapping:
      %mapping_handle = call ptr @CreateFileMappingA(ptr %file, ptr null, i32 2, i32 0, i32 0, ptr null)
      %mapping_ok = icmp ne ptr %mapping_handle, null
      br i1 %mapping_ok, label %view, label %close_failure
    view:
      %base = call ptr @MapViewOfFile(ptr %mapping_handle, i32 4, i32 0, i32 0, i64 %length)
      %base_ok = icmp ne ptr %base, null
      br i1 %base_ok, label %success, label %mapping_failure
    success:
      %mapping_closed = call i32 @CloseHandle(ptr %mapping_handle)
      %file_closed = call i32 @CloseHandle(ptr %file)
      %result0 = insertvalue %sl.source_text poison, ptr %base, 0
      %result1 = insertvalue %sl.source_text %result0, i64 %length, 1
      %result2 = insertvalue %sl.source_text %result1, ptr %base, 2
      %result3 = insertvalue %sl.source_text %result2, i64 %length, 3
      ret %sl.source_text %result3
    mapping_failure:
      %failed_mapping_closed = call i32 @CloseHandle(ptr %mapping_handle)
      br label %close_failure
    close_failure:
      %failed_file_closed = call i32 @CloseHandle(ptr %file)
      br label %failure
    failure:
      call void @llvm.trap()
      unreachable
    }

    define void @sl_runtime_unmap_text(%sl.source_text %source) {
    entry:
      %base = extractvalue %sl.source_text %source, 2
      %owned = icmp ne ptr %base, null
      br i1 %owned, label %unmap, label %done
    unmap:
      %ignored = call i32 @UnmapViewOfFile(ptr %base)
      br label %done
    done:
      ret void
    }
    """ -> println
}
