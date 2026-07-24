using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitMouseEventStreamAdapter()
    {
        if (!_program.Types.TryResolve("sys.event.MouseEvent", out var mouseEventType)
            || !_program.Types.IsStruct(mouseEventType))
        {
            throw new SollangException("sys.event.mouseEvents requires sys.event.MouseEvent");
        }
        var definition = _program.Types.GetStruct(mouseEventType);
        if (definition.Fields.Count != 5
            || !_program.Types.IsEnum(definition.Fields[4].Type))
        {
            throw new SollangException("sys.event.MouseEvent has an incompatible runtime layout");
        }
        var structType = LlvmStructType(mouseEventType);
        var enumType = LlvmEnumType(definition.Fields[4].Type);
        EmitFunctionBlock($$"""
            define internal i1 @sollang_mouse_event_stream_next(ptr %context, ptr %output) #0 {
            entry:
              %x = alloca i32, align 4
              %y = alloca i32, align 4
              %delta = alloca i32, align 4
              %button = alloca i32, align 4
              %kind = alloca i32, align 4
              %available = call i1 @sollang_mouse_event_next_raw(ptr %context, ptr %x, ptr %y, ptr %delta, ptr %button, ptr %kind)
              br i1 %available, label %write, label %done

            write:
              %x_value = load i32, ptr %x, align 4
              %y_value = load i32, ptr %y, align 4
              %delta_value = load i32, ptr %delta, align 4
              %button_value = load i32, ptr %button, align 4
              %kind_value = load i32, ptr %kind, align 4
              %x_address = getelementptr {{structType}}, ptr %output, i32 0, i32 0
              store i32 %x_value, ptr %x_address, align 4
              %y_address = getelementptr {{structType}}, ptr %output, i32 0, i32 1
              store i32 %y_value, ptr %y_address, align 4
              %delta_address = getelementptr {{structType}}, ptr %output, i32 0, i32 2
              store i32 %delta_value, ptr %delta_address, align 4
              %button_address = getelementptr {{structType}}, ptr %output, i32 0, i32 3
              store i32 %button_value, ptr %button_address, align 4
              %kind0 = insertvalue {{enumType}} poison, i32 %kind_value, 0
              %kind1 = insertvalue {{enumType}} %kind0, [0 x i64] zeroinitializer, 1
              %kind_address = getelementptr {{structType}}, ptr %output, i32 0, i32 4
              store {{enumType}} %kind1, ptr %kind_address, align 8
              ret i1 true

            done:
              ret i1 false
            }

            """);
    }

    private void EmitRuntimeHelpers()
    {
        EmitPlatformFunctionBlock(_platform.EmitIoPrimitives);
        EmitPlatformFunctionBlock(_platform.EmitFilePrimitives);
        if (_usesDirectoryTraversal)
        {
            EmitPlatformFunctionBlock(_platform.EmitDirectoryPrimitives);
        }
        EmitPlatformFunctionBlock(_platform.EmitMappedFilePrimitives);
        EmitPlatformFunctionBlock(_platform.EmitTimePrimitives);
        if (UsesProcessRuntime)
        {
            EmitPlatformFunctionBlock(_platform.EmitProcessPrimitives);
        }
        if (_usesProcessEnvironment)
        {
            EmitPlatformFunctionBlock(_platform.EmitEnvironmentPrimitives);
        }
        EmitPlatformFunctionBlock(_platform.EmitMemoryPrimitives);
        if (_usesMouseEvents)
        {
            EmitPlatformFunctionBlock(_platform.EmitEventPrimitives);
            EmitMouseEventStreamAdapter();
        }
        if (_usesRangeStreams)
        {
            EmitFunctionBlock("""
            define internal i1 @sollang_range_stream_next(ptr %context, ptr %output) #0 {
            entry:
              %done_address = getelementptr i8, ptr %context, i64 8
              %done = load i1, ptr %done_address, align 1
              br i1 %done, label %finished, label %active

            active:
              %current = load i32, ptr %context, align 4
              %end_address = getelementptr i8, ptr %context, i64 4
              %end = load i32, ptr %end_address, align 4
              %past_end = icmp sgt i32 %current, %end
              br i1 %past_end, label %mark_finished, label %yield

            yield:
              store i32 %current, ptr %output, align 4
              %is_last = icmp eq i32 %current, %end
              br i1 %is_last, label %mark_finished_after_yield, label %advance

            advance:
              %next = add i32 %current, 1
              store i32 %next, ptr %context, align 4
              ret i1 true

            mark_finished_after_yield:
              store i1 true, ptr %done_address, align 1
              ret i1 true

            mark_finished:
              store i1 true, ptr %done_address, align 1
              ret i1 false

            finished:
              ret i1 false
            }

            define internal void @sollang_stream_heap_drop(ptr %context) #0 {
            entry:
              call void @sollang_free(ptr %context)
              ret void
            }

            """);
        }
        EmitFunctionBlock("""
            define dso_local ptr @memset(ptr %dest, i32 %value, i64 %count) #0 {
            entry:
              %byte = trunc i32 %value to i8
              br label %loop

            loop:
              %i = phi i64 [ 0, %entry ], [ %next, %body ]
              %active = icmp ult i64 %i, %count
              br i1 %active, label %body, label %done

            body:
              %slot = getelementptr i8, ptr %dest, i64 %i
              store i8 %byte, ptr %slot, align 1
              %next = add i64 %i, 1
              br label %loop

            done:
              ret ptr %dest
            }

            define dso_local ptr @memcpy(ptr %dest, ptr %source, i64 %count) #0 {
            entry:
              br label %loop

            loop:
              %i = phi i64 [ 0, %entry ], [ %next, %body ]
              %active = icmp ult i64 %i, %count
              br i1 %active, label %body, label %done

            body:
              %src_slot = getelementptr i8, ptr %source, i64 %i
              %dst_slot = getelementptr i8, ptr %dest, i64 %i
              %byte = load i8, ptr %src_slot, align 1
              store i8 %byte, ptr %dst_slot, align 1
              %next = add i64 %i, 1
              br label %loop

            done:
              ret ptr %dest
            }

            define internal i64 @sollang_format_u64(ptr %dest, i64 %value) #0 {
            entry:
              br label %count

            count:
              %count_n = phi i64 [ %value, %entry ], [ %count_q, %count_more ]
              %length = phi i64 [ 1, %entry ], [ %next_length, %count_more ]
              %count_q = udiv i64 %count_n, 10
              %count_done = icmp eq i64 %count_q, 0
              br i1 %count_done, label %write_entry, label %count_more

            count_more:
              %next_length = add i64 %length, 1
              br label %count

            write_entry:
              %last_index = sub i64 %length, 1
              br label %write

            write:
              %write_n = phi i64 [ %value, %write_entry ], [ %write_q, %write ]
              %index = phi i64 [ %last_index, %write_entry ], [ %previous_index, %write ]
              %digit = urem i64 %write_n, 10
              %write_q = udiv i64 %write_n, 10
              %digit8 = trunc i64 %digit to i8
              %ascii = add i8 %digit8, 48
              %slot = getelementptr i8, ptr %dest, i64 %index
              store i8 %ascii, ptr %slot, align 1
              %previous_index = sub i64 %index, 1
              %write_done = icmp eq i64 %write_q, 0
              br i1 %write_done, label %return, label %write

            return:
              ret i64 %length
            }

            define internal i64 @sollang_format_i64(ptr %dest, i64 %value) #0 {
            entry:
              %negative = icmp slt i64 %value, 0
              br i1 %negative, label %negative_value, label %positive_value

            negative_value:
              store i8 45, ptr %dest, align 1
              %digits_dest = getelementptr i8, ptr %dest, i64 1
              %magnitude = sub i64 0, %value
              %negative_digits = call i64 @sollang_format_u64(ptr %digits_dest, i64 %magnitude)
              %negative_length = add i64 %negative_digits, 1
              ret i64 %negative_length

            positive_value:
              %positive_length = call i64 @sollang_format_u64(ptr %dest, i64 %value)
              ret i64 %positive_length
            }

            define internal i32 @sollang_write_u64(ptr %stdout, i64 %value, ptr %written) #0 {
            entry:
              %buf = alloca [20 x i8], align 1
              %end = getelementptr inbounds [20 x i8], ptr %buf, i64 0, i64 20
              br label %digits

            digits:
              %n = phi i64 [ %value, %entry ], [ %q, %digits ]
              %p = phi ptr [ %end, %entry ], [ %next, %digits ]
              %digit = urem i64 %n, 10
              %q = udiv i64 %n, 10
              %next = getelementptr i8, ptr %p, i64 -1
              %digit8 = trunc i64 %digit to i8
              %ascii = add i8 %digit8, 48
              store i8 %ascii, ptr %next, align 1
              %done = icmp eq i64 %q, 0
              br i1 %done, label %write, label %digits

            write:
              %start_int = ptrtoint ptr %next to i64
              %end_int = ptrtoint ptr %end to i64
              %len = sub i64 %end_int, %start_int
              %ok = call i32 @sollang_write(ptr %stdout, ptr %next, i64 %len, ptr %written)
              ret i32 %ok
            }

            define internal i32 @sollang_write_i64(ptr %stdout, i64 %value, ptr %written) #0 {
            entry:
              %negative = icmp slt i64 %value, 0
              br i1 %negative, label %write_sign, label %write_digits

            write_sign:
              %sign = alloca i8, align 1
              store i8 45, ptr %sign, align 1
              %sign_ok = call i32 @sollang_write(ptr %stdout, ptr %sign, i64 1, ptr %written)
              %magnitude = sub i64 0, %value
              %digits_ok_negative = call i32 @sollang_write_u64(ptr %stdout, i64 %magnitude, ptr %written)
              %negative_ok = and i32 %sign_ok, %digits_ok_negative
              ret i32 %negative_ok

            write_digits:
              %digits_ok = call i32 @sollang_write_u64(ptr %stdout, i64 %value, ptr %written)
              ret i32 %digits_ok
            }

            define internal %sollang.read_int_result @sollang_read_i64(ptr %stdin, ptr %read) #0 {
            entry:
              %buf = alloca [64 x i8], align 1
              %ok = call i32 @sollang_read_stdin(ptr %stdin, ptr %buf, i64 64, ptr %read)
              %read_ok = icmp ne i32 %ok, 0
              br i1 %read_ok, label %prepare, label %fail

            prepare:
              %read32 = load i32, ptr %read, align 4
              %len = zext i32 %read32 to i64
              br label %skip

            skip:
              %skip_idx = phi i64 [ 0, %prepare ], [ %skip_next, %skip_ws ]
              %skip_has = icmp ult i64 %skip_idx, %len
              br i1 %skip_has, label %skip_char, label %fail

            skip_char:
              %skip_ptr = getelementptr inbounds [64 x i8], ptr %buf, i64 0, i64 %skip_idx
              %skip_ch = load i8, ptr %skip_ptr, align 1
              %skip_sp = icmp eq i8 %skip_ch, 32
              %skip_tab = icmp eq i8 %skip_ch, 9
              %skip_cr = icmp eq i8 %skip_ch, 13
              %skip_lf = icmp eq i8 %skip_ch, 10
              %skip_sp_tab = or i1 %skip_sp, %skip_tab
              %skip_cr_lf = or i1 %skip_cr, %skip_lf
              %skip_is_ws = or i1 %skip_sp_tab, %skip_cr_lf
              br i1 %skip_is_ws, label %skip_ws, label %digit_entry

            skip_ws:
              %skip_next = add i64 %skip_idx, 1
              br label %skip

            digit_entry:
              %first_ge = icmp uge i8 %skip_ch, 48
              %first_le = icmp ule i8 %skip_ch, 57
              %first_digit = and i1 %first_ge, %first_le
              br i1 %first_digit, label %digits, label %fail

            digits:
              %digit_idx = phi i64 [ %skip_idx, %digit_entry ], [ %digit_next, %digit_continue ]
              %value = phi i64 [ 0, %digit_entry ], [ %value_next, %digit_continue ]
              %digit_has = icmp ult i64 %digit_idx, %len
              br i1 %digit_has, label %digit_char, label %success

            digit_char:
              %digit_ptr = getelementptr inbounds [64 x i8], ptr %buf, i64 0, i64 %digit_idx
              %digit_ch = load i8, ptr %digit_ptr, align 1
              %digit_ge = icmp uge i8 %digit_ch, 48
              %digit_le = icmp ule i8 %digit_ch, 57
              %is_digit = and i1 %digit_ge, %digit_le
              br i1 %is_digit, label %digit_continue, label %trail_entry

            digit_continue:
              %digit64 = zext i8 %digit_ch to i64
              %digit_value = sub i64 %digit64, 48
              %value_x10 = mul i64 %value, 10
              %value_next = add i64 %value_x10, %digit_value
              %digit_next = add i64 %digit_idx, 1
              br label %digits

            trail_entry:
              br label %trail

            trail:
              %trail_idx = phi i64 [ %digit_idx, %trail_entry ], [ %trail_next, %trail_ws ]
              %trail_has = icmp ult i64 %trail_idx, %len
              br i1 %trail_has, label %trail_char, label %success

            trail_char:
              %trail_ptr = getelementptr inbounds [64 x i8], ptr %buf, i64 0, i64 %trail_idx
              %trail_ch = load i8, ptr %trail_ptr, align 1
              %trail_sp = icmp eq i8 %trail_ch, 32
              %trail_tab = icmp eq i8 %trail_ch, 9
              %trail_cr = icmp eq i8 %trail_ch, 13
              %trail_lf = icmp eq i8 %trail_ch, 10
              %trail_sp_tab = or i1 %trail_sp, %trail_tab
              %trail_cr_lf = or i1 %trail_cr, %trail_lf
              %trail_is_ws = or i1 %trail_sp_tab, %trail_cr_lf
              br i1 %trail_is_ws, label %trail_ws, label %fail

            trail_ws:
              %trail_next = add i64 %trail_idx, 1
              br label %trail

            success:
              %success_value = phi i64 [ %value, %digits ], [ %value, %trail ]
              %success0 = insertvalue %sollang.read_int_result poison, i64 %success_value, 0
              %success1 = insertvalue %sollang.read_int_result %success0, i32 1, 1
              ret %sollang.read_int_result %success1

            fail:
              %fail0 = insertvalue %sollang.read_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.read_int_result %fail0, i32 0, 1
              ret %sollang.read_int_result %fail1
            }

            define internal i32 @sollang_copy_text_to_c_path(ptr %data, i64 %len, ptr %out) #0 {
            entry:
              %too_long = icmp ugt i64 %len, 259
              br i1 %too_long, label %fail, label %copy

            copy:
              %i = phi i64 [ 0, %entry ], [ %next, %copy_body ]
              %done = icmp eq i64 %i, %len
              br i1 %done, label %nul, label %copy_body

            copy_body:
              %src = getelementptr i8, ptr %data, i64 %i
              %ch = load i8, ptr %src, align 1
              %dst = getelementptr i8, ptr %out, i64 %i
              store i8 %ch, ptr %dst, align 1
              %next = add i64 %i, 1
              br label %copy

            nul:
              %nul_ptr = getelementptr i8, ptr %out, i64 %len
              store i8 0, ptr %nul_ptr, align 1
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @sollang_seed_random(i64 %seed) #0 {
            entry:
              store i64 %seed, ptr @sollang_random_state, align 8
              ret i32 1
            }

            define internal %sollang.file_int_result @sollang_random_below(i64 %max) #0 {
            entry:
              %valid = icmp sgt i64 %max, 0
              br i1 %valid, label %next, label %fail

            next:
              %state = load i64, ptr @sollang_random_state, align 8
              %mul = mul i64 %state, 6364136223846793005
              %new_state = add i64 %mul, 1442695040888963407
              store i64 %new_state, ptr @sollang_random_state, align 8
              %value = urem i64 %new_state, %max
              %ok0 = insertvalue %sollang.file_int_result poison, i64 %value, 0
              %ok1 = insertvalue %sollang.file_int_result %ok0, i32 1, 1
              ret %sollang.file_int_result %ok1

            fail:
              %fail0 = insertvalue %sollang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_int_result %fail0, i32 0, 1
              ret %sollang.file_int_result %fail1
            }

            define internal i32 @sollang_open_write_i64_file(ptr %path, i64 %len) #0 {
            entry:
              store i64 0, ptr @sollang_writer_buffer_count, align 8
              %ok = call i32 @sollang_platform_open_write_file(ptr %path, i64 %len)
              ret i32 %ok
            }

            define internal i32 @sollang_flush_i64_file() #0 {
            entry:
              %count = load i64, ptr @sollang_writer_buffer_count, align 8
              %empty = icmp eq i64 %count, 0
              br i1 %empty, label %success, label %flush

            flush:
              %bytes = mul i64 %count, 8
              %ok = call i32 @sollang_platform_write_file_bytes(ptr @sollang_writer_buffer, i64 %bytes)
              %is_ok = icmp ne i32 %ok, 0
              br i1 %is_ok, label %reset, label %fail

            reset:
              store i64 0, ptr @sollang_writer_buffer_count, align 8
              br label %success

            success:
              ret i32 1

            fail:
              ret i32 0
            }

            define internal i32 @sollang_write_i64_file(i64 %value) #0 {
            entry:
              %count = load i64, ptr @sollang_writer_buffer_count, align 8
              %slot = getelementptr inbounds [8192 x i64], ptr @sollang_writer_buffer, i64 0, i64 %count
              store i64 %value, ptr %slot, align 8
              %next = add i64 %count, 1
              store i64 %next, ptr @sollang_writer_buffer_count, align 8
              %full = icmp eq i64 %next, 8192
              br i1 %full, label %flush, label %success

            flush:
              %ok = call i32 @sollang_flush_i64_file()
              ret i32 %ok

            success:
              ret i32 1
            }

            define internal i32 @sollang_close_write_i64_file() #0 {
            entry:
              %flush_ok = call i32 @sollang_flush_i64_file()
              %flush_is_ok = icmp ne i32 %flush_ok, 0
              br i1 %flush_is_ok, label %close, label %fail

            close:
              %close_ok = call i32 @sollang_platform_close_write_file()
              ret i32 %close_ok

            fail:
              ret i32 0
            }

            define internal %sollang.file_int_result @sollang_closest_i64_file(i64 %target) #0 {
            entry:
              %count_result = call %sollang.file_count_result @sollang_platform_i64_file_count()
              %count = extractvalue %sollang.file_count_result %count_result, 0
              %count_ok = extractvalue %sollang.file_count_result %count_result, 1
              %count_is_ok = icmp ne i32 %count_ok, 0
              %has_values = icmp sgt i64 %count, 0
              %can_search = and i1 %count_is_ok, %has_values
              br i1 %can_search, label %search, label %fail

            search:
              br label %loop

            loop:
              %low = phi i64 [ 0, %search ], [ %next_low, %step ]
              %high = phi i64 [ %count, %search ], [ %next_high, %step ]
              %active = icmp slt i64 %low, %high
              br i1 %active, label %probe, label %choose

            probe:
              %span = sub i64 %high, %low
              %half = sdiv i64 %span, 2
              %mid = add i64 %low, %half
              %mid_result = call %sollang.file_int_result @sollang_platform_read_i64_at(i64 %mid)
              %mid_value = extractvalue %sollang.file_int_result %mid_result, 0
              %mid_ok = extractvalue %sollang.file_int_result %mid_result, 1
              %mid_is_ok = icmp ne i32 %mid_ok, 0
              br i1 %mid_is_ok, label %compare_mid, label %fail

            compare_mid:
              %less = icmp slt i64 %mid_value, %target
              br i1 %less, label %move_low, label %move_high

            move_low:
              %low_plus = add i64 %mid, 1
              br label %step

            move_high:
              br label %step

            step:
              %next_low = phi i64 [ %low_plus, %move_low ], [ %low, %move_high ]
              %next_high = phi i64 [ %high, %move_low ], [ %mid, %move_high ]
              br label %loop

            choose:
              %at_start = icmp eq i64 %low, 0
              br i1 %at_start, label %read_low, label %check_end

            check_end:
              %at_end = icmp eq i64 %low, %count
              br i1 %at_end, label %read_prev_only, label %read_pair

            read_low:
              %first_result = call %sollang.file_int_result @sollang_platform_read_i64_at(i64 0)
              ret %sollang.file_int_result %first_result

            read_prev_only:
              %last_index = sub i64 %count, 1
              %last_result = call %sollang.file_int_result @sollang_platform_read_i64_at(i64 %last_index)
              ret %sollang.file_int_result %last_result

            read_pair:
              %prev_index = sub i64 %low, 1
              %prev_result = call %sollang.file_int_result @sollang_platform_read_i64_at(i64 %prev_index)
              %next_result = call %sollang.file_int_result @sollang_platform_read_i64_at(i64 %low)
              %prev_ok = extractvalue %sollang.file_int_result %prev_result, 1
              %next_ok = extractvalue %sollang.file_int_result %next_result, 1
              %prev_is_ok = icmp ne i32 %prev_ok, 0
              %next_is_ok = icmp ne i32 %next_ok, 0
              %both_ok = and i1 %prev_is_ok, %next_is_ok
              br i1 %both_ok, label %compare_pair, label %fail

            compare_pair:
              %prev_value = extractvalue %sollang.file_int_result %prev_result, 0
              %next_value = extractvalue %sollang.file_int_result %next_result, 0
              %prev_diff = sub i64 %target, %prev_value
              %next_diff = sub i64 %next_value, %target
              %prefer_prev = icmp sle i64 %prev_diff, %next_diff
              br i1 %prefer_prev, label %return_prev, label %return_next

            return_prev:
              ret %sollang.file_int_result %prev_result

            return_next:
              ret %sollang.file_int_result %next_result

            fail:
              %fail0 = insertvalue %sollang.file_int_result poison, i64 0, 0
              %fail1 = insertvalue %sollang.file_int_result %fail0, i32 0, 1
              ret %sollang.file_int_result %fail1
            }

            """);
        EmitFunctionBlock("""
            define internal i64 @sollang_utf8_decode(ptr %data, i64 %len, i64 %index) #0 {
            entry:
              %has_first = icmp ult i64 %index, %len
              br i1 %has_first, label %first, label %invalid

            first:
              %p0 = getelementptr i8, ptr %data, i64 %index
              %b0raw = load i8, ptr %p0, align 1
              %b0 = zext i8 %b0raw to i32
              %ascii = icmp ult i32 %b0, 128
              br i1 %ascii, label %ascii_result, label %classify

            ascii_result:
              %ascii_wide = zext i32 %b0 to i64
              %ascii_packed = or i64 %ascii_wide, 4294967296
              ret i64 %ascii_packed

            classify:
              %is2lo = icmp uge i32 %b0, 194
              %is2hi = icmp ule i32 %b0, 223
              %is2 = and i1 %is2lo, %is2hi
              %is3lo = icmp uge i32 %b0, 224
              %is3hi = icmp ule i32 %b0, 239
              %is3 = and i1 %is3lo, %is3hi
              %is4lo = icmp uge i32 %b0, 240
              %is4hi = icmp ule i32 %b0, 244
              %is4 = and i1 %is4lo, %is4hi
              br i1 %is2, label %decode2, label %check3

            check3:
              br i1 %is3, label %decode3, label %check4

            check4:
              br i1 %is4, label %decode4, label %invalid

            decode2:
              %end2 = add i64 %index, 1
              %has2 = icmp ult i64 %end2, %len
              br i1 %has2, label %load2, label %invalid

            load2:
              %p1_2 = getelementptr i8, ptr %data, i64 %end2
              %b1_2raw = load i8, ptr %p1_2, align 1
              %b1_2 = zext i8 %b1_2raw to i32
              %c1_2lo = icmp uge i32 %b1_2, 128
              %c1_2hi = icmp ule i32 %b1_2, 191
              %c1_2 = and i1 %c1_2lo, %c1_2hi
              br i1 %c1_2, label %result2, label %invalid

            result2:
              %h2 = and i32 %b0, 31
              %h2s = shl i32 %h2, 6
              %l2 = and i32 %b1_2, 63
              %cp2 = or i32 %h2s, %l2
              %cp2wide = zext i32 %cp2 to i64
              %packed2 = or i64 %cp2wide, 8589934592
              ret i64 %packed2

            decode3:
              %end3 = add i64 %index, 2
              %has3 = icmp ult i64 %end3, %len
              br i1 %has3, label %load3, label %invalid

            load3:
              %i1_3 = add i64 %index, 1
              %p1_3 = getelementptr i8, ptr %data, i64 %i1_3
              %p2_3 = getelementptr i8, ptr %data, i64 %end3
              %b1_3raw = load i8, ptr %p1_3, align 1
              %b2_3raw = load i8, ptr %p2_3, align 1
              %b1_3 = zext i8 %b1_3raw to i32
              %b2_3 = zext i8 %b2_3raw to i32
              %c1_3lo = icmp uge i32 %b1_3, 128
              %c1_3hi = icmp ule i32 %b1_3, 191
              %c1_3 = and i1 %c1_3lo, %c1_3hi
              %c2_3lo = icmp uge i32 %b2_3, 128
              %c2_3hi = icmp ule i32 %b2_3, 191
              %c2_3 = and i1 %c2_3lo, %c2_3hi
              %cont3 = and i1 %c1_3, %c2_3
              br i1 %cont3, label %value3, label %invalid

            value3:
              %h3 = and i32 %b0, 15
              %h3s = shl i32 %h3, 12
              %m3 = and i32 %b1_3, 63
              %m3s = shl i32 %m3, 6
              %hm3 = or i32 %h3s, %m3s
              %l3 = and i32 %b2_3, 63
              %cp3 = or i32 %hm3, %l3
              %min3 = icmp uge i32 %cp3, 2048
              %below_surrogate = icmp ult i32 %cp3, 55296
              %above_surrogate = icmp ugt i32 %cp3, 57343
              %not_surrogate = or i1 %below_surrogate, %above_surrogate
              %valid3 = and i1 %min3, %not_surrogate
              br i1 %valid3, label %result3, label %invalid

            result3:
              %cp3wide = zext i32 %cp3 to i64
              %packed3 = or i64 %cp3wide, 12884901888
              ret i64 %packed3

            decode4:
              %end4 = add i64 %index, 3
              %has4 = icmp ult i64 %end4, %len
              br i1 %has4, label %load4, label %invalid

            load4:
              %i1_4 = add i64 %index, 1
              %i2_4 = add i64 %index, 2
              %p1_4 = getelementptr i8, ptr %data, i64 %i1_4
              %p2_4 = getelementptr i8, ptr %data, i64 %i2_4
              %p3_4 = getelementptr i8, ptr %data, i64 %end4
              %b1_4raw = load i8, ptr %p1_4, align 1
              %b2_4raw = load i8, ptr %p2_4, align 1
              %b3_4raw = load i8, ptr %p3_4, align 1
              %b1_4 = zext i8 %b1_4raw to i32
              %b2_4 = zext i8 %b2_4raw to i32
              %b3_4 = zext i8 %b3_4raw to i32
              %c1_4lo = icmp uge i32 %b1_4, 128
              %c1_4hi = icmp ule i32 %b1_4, 191
              %c1_4 = and i1 %c1_4lo, %c1_4hi
              %c2_4lo = icmp uge i32 %b2_4, 128
              %c2_4hi = icmp ule i32 %b2_4, 191
              %c2_4 = and i1 %c2_4lo, %c2_4hi
              %c3_4lo = icmp uge i32 %b3_4, 128
              %c3_4hi = icmp ule i32 %b3_4, 191
              %c3_4 = and i1 %c3_4lo, %c3_4hi
              %cont12_4 = and i1 %c1_4, %c2_4
              %cont4 = and i1 %cont12_4, %c3_4
              br i1 %cont4, label %value4, label %invalid

            value4:
              %h4 = and i32 %b0, 7
              %h4s = shl i32 %h4, 18
              %m1_4 = and i32 %b1_4, 63
              %m1_4s = shl i32 %m1_4, 12
              %m2_4 = and i32 %b2_4, 63
              %m2_4s = shl i32 %m2_4, 6
              %hm1_4 = or i32 %h4s, %m1_4s
              %hm2_4 = or i32 %hm1_4, %m2_4s
              %l4 = and i32 %b3_4, 63
              %cp4 = or i32 %hm2_4, %l4
              %min4 = icmp uge i32 %cp4, 65536
              %max4 = icmp ule i32 %cp4, 1114111
              %valid4 = and i1 %min4, %max4
              br i1 %valid4, label %result4, label %invalid

            result4:
              %cp4wide = zext i32 %cp4 to i64
              %packed4 = or i64 %cp4wide, 17179869184
              ret i64 %packed4

            invalid:
              ret i64 -1
            }

            """);
    }

}

