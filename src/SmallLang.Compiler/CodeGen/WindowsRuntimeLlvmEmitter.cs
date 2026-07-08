using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed class WindowsRuntimeLlvmEmitter(BoundProgram program)
{
    private readonly StringBuilder _globals = new();
    private readonly StringBuilder _functions = new();
    private readonly Dictionary<string, RuntimeValue> _locals = new(StringComparer.Ordinal);
    private int _stringId;
    private int _tempId;
    private int _labelId;
    private string _mainOk = "true";
    private string _currentBlockLabel = "entry";

    public string Emit()
    {
        var header = new StringBuilder();
        header.AppendLine("target triple = \"x86_64-pc-windows-msvc\"");
        header.AppendLine();
        header.AppendLine("%smalllang.text = type { ptr, i64 }");
        header.AppendLine("%smalllang.read_int_result = type { i64, i32 }");
        header.AppendLine();

        _functions.AppendLine("declare dllimport ptr @GetStdHandle(i32)");
        _functions.AppendLine("declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)");
        _functions.AppendLine("declare dllimport i32 @ReadFile(ptr, ptr, i32, ptr, ptr)");
        _functions.AppendLine();

        EmitUserFunctions();
        EmitRuntimeHelpers();
        EmitMain();
        _functions.AppendLine("attributes #0 = { noinline optnone }");

        return header.ToString() + _globals + _functions;
    }

    private void EmitUserFunctions()
    {
        foreach (var function in program.Functions.Values)
        {
            switch (function.ReturnType)
            {
                case BoundType.Text:
                    EmitTextFunction(function);
                    break;
                case BoundType.Int:
                    EmitIntFunction(function);
                    break;
                default:
                    throw new SmallLangException($"unsupported function return type {function.ReturnType}");
            }
        }
    }

    private void EmitTextFunction(BoundFunction function)
    {
        if (function.InputType is not null)
        {
            throw new SmallLangException("Text-returning functions with input are not in the current runtime slice");
        }

        var text = GetPlainText(function.Body, function.Line, function.Column);
        var global = AddGlobalString(text);

        _functions.Append("define internal %smalllang.text ")
            .Append(SymbolForFunction(function.Name))
            .AppendLine("() #0 {");
        _functions.AppendLine("entry:");
        _functions.Append("  ret %smalllang.text { ptr ")
            .Append(global.Name)
            .Append(", i64 ")
            .Append(global.Length.ToString(CultureInfo.InvariantCulture))
            .AppendLine(" }");
        _functions.AppendLine("}");
        _functions.AppendLine();
    }

    private void EmitIntFunction(BoundFunction function)
    {
        _locals.Clear();
        var parameterList = function.InputType switch
        {
            null => "",
            BoundType.Int => "i64 %it",
            _ => throw new SmallLangException("only Int function input is supported in the current runtime slice")
        };

        _functions.Append("define internal i64 ")
            .Append(SymbolForFunction(function.Name))
            .Append('(')
            .Append(parameterList)
            .AppendLine(") #0 {");
        _functions.AppendLine("entry:");
        if (function.InputType == BoundType.Int)
        {
            _locals.Add("it", new RuntimeInt("%it"));
        }

        var value = EmitIntExpression(function.Body);
        _functions.Append("  ret i64 ").AppendLine(value.ValueName);
        _functions.AppendLine("}");
        _functions.AppendLine();
    }

    private void EmitRuntimeHelpers()
    {
        _functions.AppendLine("""
            define internal i32 @smalllang_write(ptr %stdout, ptr %data, i64 %len64, ptr %written) #0 {
            entry:
              %len = trunc i64 %len64 to i32
              %ok = call i32 @WriteFile(ptr %stdout, ptr %data, i32 %len, ptr %written, ptr null)
              ret i32 %ok
            }

            define internal i32 @smalllang_write_u64(ptr %stdout, i64 %value, ptr %written) #0 {
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
              %ok = call i32 @smalllang_write(ptr %stdout, ptr %next, i64 %len, ptr %written)
              ret i32 %ok
            }

            define internal %smalllang.read_int_result @smalllang_read_i64(ptr %stdin, ptr %read) #0 {
            entry:
              %buf = alloca [64 x i8], align 1
              %ok = call i32 @ReadFile(ptr %stdin, ptr %buf, i32 64, ptr %read, ptr null)
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
              %success0 = insertvalue %smalllang.read_int_result poison, i64 %success_value, 0
              %success1 = insertvalue %smalllang.read_int_result %success0, i32 1, 1
              ret %smalllang.read_int_result %success1

            fail:
              %fail0 = insertvalue %smalllang.read_int_result poison, i64 0, 0
              %fail1 = insertvalue %smalllang.read_int_result %fail0, i32 0, 1
              ret %smalllang.read_int_result %fail1
            }

            """);
    }

    private void EmitMain()
    {
        _locals.Clear();
        _mainOk = "true";
        _currentBlockLabel = "entry";
        _functions.AppendLine("define dso_local i32 @smalllang_start() local_unnamed_addr {");
        _functions.AppendLine("entry:");
        _functions.AppendLine("  %written = alloca i32, align 4");
        _functions.AppendLine("  %read = alloca i32, align 4");
        _functions.AppendLine("  %ok_state = alloca i1, align 1");
        _functions.AppendLine("  store i1 true, ptr %ok_state, align 1");
        _functions.AppendLine("  %stdin = call ptr @GetStdHandle(i32 -10)");
        _functions.AppendLine("  %stdout = call ptr @GetStdHandle(i32 -11)");

        EmitStatements(program.MainStatements);

        var finalOk = NextTemp("final_ok");
        _functions.Append("  ")
            .Append(finalOk)
            .AppendLine(" = load i1, ptr %ok_state, align 1");
        _functions.Append("  ")
            .Append(NextTemp("exit"))
            .Append(" = select i1 ")
            .Append(finalOk)
            .AppendLine(", i32 0, i32 1");
        _functions.Append("  ret i32 ").AppendLine(CurrentTemp("exit"));
        _functions.AppendLine("}");
        _functions.AppendLine();
    }

    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        foreach (var statement in statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BindingStatement binding:
                _locals.Add(binding.Name, EmitExpression(binding.Value));
                break;
            case EachStatement each:
                EmitEachStatement(each);
                break;
            case ExpressionStatement expressionStatement:
                _mainOk = EmitExpressionStatement(expressionStatement.Expression, _mainOk);
                break;
            default:
                throw new SmallLangException($"unsupported runtime statement {statement.GetType().Name}");
        }
    }

    private void EmitEachStatement(EachStatement statement)
    {
        var start = EmitIntExpression(statement.Start);
        var end = EmitIntExpression(statement.End);
        var bodyLabel = NextLabel("each_body");
        var continueLabel = NextLabel("each_continue");
        var endLabel = NextLabel("each_end");
        var entryLabel = _currentBlockLabel;
        var next = NextTemp("each_next");
        var initialDone = NextTemp("each_done");

        _functions.Append("  ")
            .Append(initialDone)
            .Append(" = icmp sgt i64 ")
            .Append(start.ValueName)
            .Append(", ")
            .AppendLine(end.ValueName);
        _functions.Append("  br i1 ")
            .Append(initialDone)
            .Append(", label %")
            .Append(endLabel)
            .Append(", label %")
            .Append(bodyLabel)
            .AppendLine();

        _functions.Append(bodyLabel).AppendLine(":");
        _currentBlockLabel = bodyLabel;
        var item = NextTemp(statement.ItemName);
        _functions.Append("  ")
            .Append(item)
            .Append(" = phi i64 [ ")
            .Append(start.ValueName)
            .Append(", %")
            .Append(entryLabel)
            .Append(" ], [ ")
            .Append(next)
            .Append(", %")
            .Append(continueLabel)
            .AppendLine(" ]");

        var outerLocals = CaptureLocals();
        _locals[statement.ItemName] = new RuntimeInt(item);
        EmitStatements(statement.Body);
        RestoreLocals(outerLocals);

        _functions.Append("  br label %").AppendLine(continueLabel);
        _functions.Append(continueLabel).AppendLine(":");
        _currentBlockLabel = continueLabel;
        _functions.Append("  ")
            .Append(next)
            .Append(" = add i64 ")
            .Append(item)
            .AppendLine(", 1");
        var done = NextTemp("each_done");
        _functions.Append("  ")
            .Append(done)
            .Append(" = icmp sgt i64 ")
            .Append(next)
            .Append(", ")
            .AppendLine(end.ValueName);
        _functions.Append("  br i1 ")
            .Append(done)
            .Append(", label %")
            .Append(endLabel)
            .Append(", label %")
            .Append(bodyLabel)
            .AppendLine();
        _functions.Append(endLabel).AppendLine(":");
        _currentBlockLabel = endLabel;
    }

    private string EmitExpressionStatement(Expression expression, string ok)
    {
        if (expression is CallExpression call)
        {
            return EmitPrintCall(call, ok);
        }

        if (expression is FlowExpression flow)
        {
            var result = EmitFlowExpression(flow, ok, allowBindingTarget: true);
            if (result.Binding is { } binding)
            {
                _locals.Add(binding.Name, binding.Value);
                return result.Ok;
            }

            if (result.Value is null)
            {
                return result.Ok;
            }

            throw new SmallLangException("value-flow expression statements must end in print or bind their result");
        }

        throw new SmallLangException($"unsupported runtime expression statement {expression.GetType().Name}");
    }

    private string EmitPrintCall(CallExpression call, string ok)
    {
        var path = string.Join('.', call.Path);
        if (path is not ("print" or "println") || call.Arguments.Count != 1)
        {
            throw new SmallLangException($"unsupported runtime call '{path}'");
        }

        ok = EmitPrintArgument(call.Arguments[0], ok);
        return path == "println"
            ? EmitWriteText("\n", ok)
            : ok;
    }

    private string EmitPrintArgument(Expression expression, string ok)
    {
        if (expression is StringExpression str)
        {
            foreach (var segment in str.Segments)
            {
                ok = segment switch
                {
                    TextSegment text => EmitWriteText(text.Text, ok),
                    InterpolationSegment interpolation => EmitWriteInterpolation(interpolation, ok),
                    _ => throw new SmallLangException($"unsupported string segment {segment.GetType().Name}")
                };
            }

            return ok;
        }

        var value = EmitExpression(expression);
        return EmitWriteValue(value, ok);
    }

    private string EmitWriteInterpolation(InterpolationSegment interpolation, string ok)
    {
        if (interpolation.Path.Count != 1)
        {
            throw new SmallLangException("path interpolation is reserved until modules are specified");
        }

        if (!_locals.TryGetValue(interpolation.Path[0], out var value))
        {
            throw new SmallLangException($"unknown runtime binding '{interpolation.Path[0]}'");
        }

        return EmitWriteValue(value, ok);
    }

    private string EmitWriteValue(RuntimeValue value, string ok)
    {
        return value switch
        {
            RuntimeText text => EmitWriteTextValue(text, ok),
            RuntimeInt integer => EmitWriteIntegerValue(integer, ok),
            _ => throw new SmallLangException($"unsupported runtime value {value.GetType().Name}")
        };
    }

    private string EmitWriteText(string text, string ok)
    {
        if (text.Length == 0)
        {
            return ok;
        }

        var global = AddGlobalString(text);
        return EmitWriteTextValue(new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture)), ok);
    }

    private string EmitWriteTextValue(RuntimeText text, string ok)
    {
        var write = NextTemp("write");
        _functions.Append("  ")
            .Append(write)
            .Append(" = call i32 @smalllang_write(ptr %stdout, ptr ")
            .Append(text.PointerName)
            .Append(", i64 ")
            .Append(text.LengthName)
            .AppendLine(", ptr %written)");
        return CombineWriteOk(write, ok);
    }

    private string EmitWriteIntegerValue(RuntimeInt value, string ok)
    {
        var write = NextTemp("write");
        _functions.Append("  ")
            .Append(write)
            .Append(" = call i32 @smalllang_write_u64(ptr %stdout, i64 ")
            .Append(value.ValueName)
            .AppendLine(", ptr %written)");
        return CombineWriteOk(write, ok);
    }

    private string CombineWriteOk(string writeResult, string ok)
    {
        var isOk = NextTemp("is_ok");
        _functions.Append("  ")
            .Append(isOk)
            .Append(" = icmp ne i32 ")
            .Append(writeResult)
            .AppendLine(", 0");

        _ = ok;
        var previous = NextTemp("previous_ok");
        _functions.Append("  ")
            .Append(previous)
            .AppendLine(" = load i1, ptr %ok_state, align 1");
        var combined = NextTemp("ok");
        _functions.Append("  ")
            .Append(combined)
            .Append(" = and i1 ")
            .Append(previous)
            .Append(", ")
            .AppendLine(isOk);
        _functions.Append("  store i1 ")
            .Append(combined)
            .AppendLine(", ptr %ok_state, align 1");
        return combined;
    }

    private RuntimeValue EmitExpression(Expression expression)
    {
        return expression switch
        {
            StringExpression str => EmitTextLiteral(str),
            NumberExpression number => new RuntimeInt(ParseNumber(number).ToString(CultureInfo.InvariantCulture)),
            NameExpression name => ResolveLocal(name.Name),
            AddExpression add => EmitAddExpression(add),
            MultiplyExpression multiply => EmitMultiplyExpression(multiply),
            CallExpression call => EmitFunctionCall(call),
            FlowExpression flow => EmitFlowExpressionValue(flow),
            _ => throw new SmallLangException($"unsupported runtime expression {expression.GetType().Name}")
        };
    }

    private RuntimeText EmitTextLiteral(StringExpression expression)
    {
        var text = GetPlainText(expression, expression.Line, expression.Column);
        var global = AddGlobalString(text);
        return new RuntimeText(global.Name, global.Length.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeInt EmitIntExpression(Expression expression)
    {
        var value = EmitExpression(expression);
        return value as RuntimeInt
            ?? throw new SmallLangException("expected runtime integer expression");
    }

    private RuntimeInt EmitAddExpression(AddExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("add");
        _functions.Append("  ")
            .Append(result)
            .Append(" = add nsw i64 ")
            .Append(left.ValueName)
            .Append(", ")
            .AppendLine(right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeInt EmitMultiplyExpression(MultiplyExpression expression)
    {
        var left = EmitIntExpression(expression.Left);
        var right = EmitIntExpression(expression.Right);
        var result = NextTemp("mul");
        _functions.Append("  ")
            .Append(result)
            .Append(" = mul nsw i64 ")
            .Append(left.ValueName)
            .Append(", ")
            .AppendLine(right.ValueName);
        return new RuntimeInt(result);
    }

    private RuntimeValue EmitFlowExpressionValue(FlowExpression expression)
    {
        var result = EmitFlowExpression(expression, ok: "true", allowBindingTarget: false);
        return result.Value
            ?? throw new SmallLangException("value-flow expression does not produce a runtime value");
    }

    private RuntimeFlowResult EmitFlowExpression(FlowExpression expression, string ok, bool allowBindingTarget)
    {
        if (expression.Targets.Count == 1
            && (IsPath(expression.Targets[0], "print") || IsPath(expression.Targets[0], "println")))
        {
            ok = EmitPrintArgument(expression.Source, ok);
            if (IsPath(expression.Targets[0], "println"))
            {
                ok = EmitWriteText("\n", ok);
            }

            return new RuntimeFlowResult(
                Value: null,
                Binding: null,
                Ok: ok);
        }

        var current = EmitFlowSource(expression.Source);
        for (var i = 0; i < expression.Targets.Count; i++)
        {
            var target = expression.Targets[i];
            var isLast = i == expression.Targets.Count - 1;
            var path = string.Join('.', target);

            if (path is "print" or "println")
            {
                if (!isLast)
                {
                    throw new SmallLangException($"{path} must be the final value-flow target");
                }

                ok = EmitWriteValue(current, ok);
                if (path == "println")
                {
                    ok = EmitWriteText("\n", ok);
                }

                return new RuntimeFlowResult(
                    Value: null,
                    Binding: null,
                    Ok: ok);
            }

            if (path == "readInt")
            {
                EnsureRuntimeType(current, BoundType.Text, path);
                current = EmitReadIntPrompt(current);
                ok = _mainOk;
                continue;
            }

            if (program.Functions.TryGetValue(path, out var function))
            {
                current = EmitFlowFunctionCall(function, current);
                continue;
            }

            if (allowBindingTarget && isLast && target.Count == 1)
            {
                return new RuntimeFlowResult(
                    Value: null,
                    Binding: new RuntimeFlowBinding(target[0], current),
                    Ok: ok);
            }

            throw new SmallLangException($"unknown runtime value-flow target '{path}'");
        }

        return new RuntimeFlowResult(
            Value: current,
            Binding: null,
            Ok: ok);
    }

    private RuntimeValue EmitFlowSource(Expression source)
    {
        if (source is NameExpression name
            && !_locals.ContainsKey(name.Name)
            && program.Functions.TryGetValue(name.Name, out var function)
            && function.InputType is null)
        {
            return EmitFunctionCall(function, argument: null);
        }

        return EmitExpression(source);
    }

    private RuntimeInt EmitReadIntPrompt(RuntimeValue prompt)
    {
        EnsureRuntimeType(prompt, BoundType.Text, "readInt");
        _mainOk = EmitWriteValue(prompt, _mainOk);

        var result = NextTemp("read_int");
        _functions.Append("  ")
            .Append(result)
            .AppendLine(" = call %smalllang.read_int_result @smalllang_read_i64(ptr %stdin, ptr %read)");

        var value = NextTemp("read_value");
        _functions.Append("  ")
            .Append(value)
            .Append(" = extractvalue %smalllang.read_int_result ")
            .Append(result)
            .AppendLine(", 0");

        var ok = NextTemp("read_ok");
        _functions.Append("  ")
            .Append(ok)
            .Append(" = extractvalue %smalllang.read_int_result ")
            .Append(result)
            .AppendLine(", 1");

        _mainOk = CombineWriteOk(ok, _mainOk);
        EmitReturnIfReadFailed(ok);
        return new RuntimeInt(value);
    }

    private void EmitReturnIfReadFailed(string readOk)
    {
        var isOk = NextTemp("read_is_ok");
        var failLabel = NextLabel("read_fail");
        var continueLabel = NextLabel("read_continue");

        _functions.Append("  ")
            .Append(isOk)
            .Append(" = icmp ne i32 ")
            .Append(readOk)
            .AppendLine(", 0");
        _functions.Append("  br i1 ")
            .Append(isOk)
            .Append(", label %")
            .Append(continueLabel)
            .Append(", label %")
            .Append(failLabel)
            .AppendLine();
        _functions.Append(failLabel).AppendLine(":");
        _functions.AppendLine("  ret i32 1");
        _functions.Append(continueLabel).AppendLine(":");
        _currentBlockLabel = continueLabel;
    }

    private RuntimeValue EmitFunctionCall(CallExpression expression)
    {
        var path = string.Join('.', expression.Path);
        if (path == "readInt")
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException("readInt expects exactly one Text prompt");
            }

            var prompt = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(prompt, BoundType.Text, path);
            return EmitReadIntPrompt(prompt);
        }

        if (!program.Functions.TryGetValue(path, out var function))
        {
            throw new SmallLangException($"unknown runtime function '{path}'");
        }

        RuntimeValue? argument = null;
        if (function.InputType is null)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"function '{path}' does not accept arguments");
            }
        }
        else
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"function '{path}' expects exactly one argument");
            }

            argument = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(argument, function.InputType.Value, path);
        }

        return EmitFunctionCall(function, argument);
    }

    private RuntimeValue EmitFlowFunctionCall(BoundFunction function, RuntimeValue argument)
    {
        if (function.InputType is null)
        {
            throw new SmallLangException($"function '{function.Name}' does not accept a flowed input");
        }

        EnsureRuntimeType(argument, function.InputType.Value, function.Name);
        return EmitFunctionCall(function, argument);
    }

    private RuntimeValue EmitFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        return function.ReturnType switch
        {
            BoundType.Text => EmitTextFunctionCall(function, argument),
            BoundType.Int => EmitIntFunctionCall(function, argument),
            _ => throw new SmallLangException($"unsupported function return type {function.ReturnType}")
        };
    }

    private RuntimeText EmitTextFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        if (argument is not null)
        {
            throw new SmallLangException("Text-returning functions with input are not in the current runtime slice");
        }

        var aggregate = NextTemp("text");
        _functions.Append("  ")
            .Append(aggregate)
            .Append(" = call %smalllang.text ")
            .Append(SymbolForFunction(function.Name))
            .AppendLine("()");

        var pointer = NextTemp("text_ptr");
        _functions.Append("  ")
            .Append(pointer)
            .Append(" = extractvalue %smalllang.text ")
            .Append(aggregate)
            .AppendLine(", 0");

        var length = NextTemp("text_len");
        _functions.Append("  ")
            .Append(length)
            .Append(" = extractvalue %smalllang.text ")
            .Append(aggregate)
            .AppendLine(", 1");

        return new RuntimeText(pointer, length);
    }

    private RuntimeInt EmitIntFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("call");
        _functions.Append("  ")
            .Append(value)
            .Append(" = call i64 ")
            .Append(SymbolForFunction(function.Name))
            .Append('(');

        if (argument is null)
        {
            _functions.Append(')');
        }
        else if (argument is RuntimeInt integer)
        {
            _functions.Append("i64 ")
                .Append(integer.ValueName)
                .Append(')');
        }
        else
        {
            throw new SmallLangException($"function '{function.Name}' expects an integer argument");
        }

        _functions.AppendLine();
        return new RuntimeInt(value);
    }

    private static void EnsureRuntimeType(RuntimeValue value, BoundType expected, string path)
    {
        if (value.Type != expected)
        {
            throw new SmallLangException($"function '{path}' expects {expected} but received {value.Type}");
        }
    }

    private static bool IsPath(IReadOnlyList<string> path, string name)
    {
        return path.Count == 1 && path[0] == name;
    }

    private RuntimeValue ResolveLocal(string name)
    {
        return _locals.TryGetValue(name, out var value)
            ? value
            : throw new SmallLangException($"unknown runtime binding '{name}'");
    }

    private Dictionary<string, RuntimeValue> CaptureLocals()
    {
        return new Dictionary<string, RuntimeValue>(_locals, StringComparer.Ordinal);
    }

    private void RestoreLocals(Dictionary<string, RuntimeValue> locals)
    {
        _locals.Clear();
        foreach (var (name, value) in locals)
        {
            _locals.Add(name, value);
        }
    }

    private GlobalString AddGlobalString(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var name = "@.smalllang.str." + _stringId.ToString(CultureInfo.InvariantCulture);
        _stringId++;
        _globals.Append(name)
            .Append(" = private unnamed_addr constant [")
            .Append(bytes.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" x i8] c\"")
            .Append(EscapeLlvmBytes(bytes))
            .AppendLine("\", align 1");
        return new GlobalString(name, bytes.Length);
    }

    private static string GetPlainText(Expression expression, int line, int column)
    {
        if (expression is not StringExpression str)
        {
            throw new SmallLangException($"codegen error at {line}:{column}: expected a string literal");
        }

        var builder = new StringBuilder();
        foreach (var segment in str.Segments)
        {
            if (segment is TextSegment text)
            {
                builder.Append(text.Text);
                continue;
            }

            throw new SmallLangException($"codegen error at {line}:{column}: expected a plain string literal");
        }

        return builder.ToString();
    }

    private static long ParseNumber(NumberExpression expression)
    {
        return long.TryParse(
            expression.Text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new SmallLangException($"codegen error at {expression.Line}:{expression.Column}: integer literal is out of range");
    }

    private static string SymbolForFunction(string name)
    {
        var builder = new StringBuilder("@smalllang_fn_");
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    private string NextTemp(string prefix)
    {
        var name = "%" + prefix + _tempId.ToString(CultureInfo.InvariantCulture);
        _tempId++;
        return name;
    }

    private string NextLabel(string prefix)
    {
        var name = prefix + _labelId.ToString(CultureInfo.InvariantCulture);
        _labelId++;
        return name;
    }

    private string CurrentTemp(string prefix)
    {
        return "%" + prefix + (_tempId - 1).ToString(CultureInfo.InvariantCulture);
    }

    private static string EscapeLlvmBytes(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is >= 0x20 and <= 0x7E && b != (byte)'\\' && b != (byte)'"')
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('\\');
                builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private sealed record GlobalString(string Name, int Length);

    private abstract record RuntimeValue(BoundType Type);

    private sealed record RuntimeText(string PointerName, string LengthName) : RuntimeValue(BoundType.Text);

    private sealed record RuntimeInt(string ValueName) : RuntimeValue(BoundType.Int);

    private sealed record RuntimeFlowBinding(string Name, RuntimeValue Value);

    private sealed record RuntimeFlowResult(RuntimeValue? Value, RuntimeFlowBinding? Binding, string Ok);
}
