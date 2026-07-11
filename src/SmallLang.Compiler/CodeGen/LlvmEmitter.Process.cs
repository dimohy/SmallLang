using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeArguments EmitRuntimeArgumentsIntrinsic()
    {
        if (!_platform.SupportsProcessArguments)
        {
            throw new SmallLangException("process arguments are unavailable on the current target");
        }
        var length = NextTemp("argument_count");
        EmitCall(length, "i64", "smalllang_argument_count", "");
        return new RuntimeArguments(length);
    }

    private RuntimeText EmitArgumentLoad(RuntimeArguments arguments, Expression indexExpression)
    {
        var index = EmitMapInteger(indexExpression, BoundType.UIntSize, "argument_index");
        var inBounds = NextTemp("argument_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, arguments.LengthName);
        EmitTrapUnless(inBounds, "argument_bounds");
        return EmitArgumentLoad(index);
    }

    private RuntimeText EmitArgumentLoad(string index)
    {
        var value = NextTemp("argument");
        EmitCall(value, "%smalllang.text", "smalllang_argument", $"i64 {index}");
        var pointer = NextTemp("argument_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.text {value}, 0");
        var length = NextTemp("argument_len");
        EmitAssign(length, $"extractvalue %smalllang.text {value}, 1");
        return new RuntimeText(pointer, length);
    }

    private RuntimeEnum EmitRuntimeEnvironmentIntrinsic(BoundFunction function, RuntimeValue nameValue)
    {
        if (!_platform.SupportsEnvironment)
        {
            throw new SmallLangException("environment access is unavailable on the current target");
        }
        var name = nameValue as RuntimeText
            ?? throw new SmallLangException($"{function.Name} expects Text");
        var raw = NextTemp("environment_result");
        EmitCall(raw, "%smalllang.environment_result", "smalllang_environment",
            $"ptr {name.PointerName}, i64 {name.LengthName}");
        var pointer = NextTemp("environment_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.environment_result {raw}, 0");
        var length = NextTemp("environment_len");
        EmitAssign(length, $"extractvalue %smalllang.environment_result {raw}, 1");
        var found = NextTemp("environment_found");
        EmitAssign(found, $"extractvalue %smalllang.environment_result {raw}, 2");
        var ok = NextTemp("environment_ok");
        EmitAssign(ok, $"extractvalue %smalllang.environment_result {raw}, 3");
        EmitTrapUnless(ok, "environment_lookup");

        var definition = _program.Types.GetEnum(function.ReturnType);
        var noneVariant = definition.Variants.First(variant => variant.Name == "None");
        var someVariant = definition.Variants.First(variant => variant.Name == "Some");
        var someLabel = NextLabel("environment_some");
        var noneLabel = NextLabel("environment_none");
        var endLabel = NextLabel("environment_end");
        EmitConditionalBranch(found, someLabel, noneLabel);

        EmitLabel(someLabel);
        _currentBlockLabel = someLabel;
        var some = EmitEnumValue(function.ReturnType, someVariant, new RuntimeText(pointer, length));
        EmitBranch(endLabel);
        var someExit = _currentBlockLabel;

        EmitLabel(noneLabel);
        _currentBlockLabel = noneLabel;
        var none = EmitEnumValue(function.ReturnType, noneVariant, payload: null);
        EmitBranch(endLabel);
        var noneExit = _currentBlockLabel;

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        return EmitEnumPhi("environment_option", function.ReturnType,
            [(some, someExit), (none, noneExit)]);
    }

    private RuntimeEnum EmitRuntimeRunProcessIntrinsic(BoundFunction function, RuntimeDynamicInlineArray argv)
    {
        if (!_platform.SupportsChildProcesses)
        {
            throw new SmallLangException("child processes are unavailable on the current target");
        }
        if (argv.ElementType != BoundType.Text)
        {
            throw new SmallLangException($"{function.Name} expects Text argv entries");
        }

        var raw = NextTemp("process_result");
        EmitCall(raw, "%smalllang.process_result", "smalllang_run_process",
            $"ptr {argv.PointerName}, i64 {argv.LengthName}");
        var exitCode = NextTemp("process_exit_code");
        EmitAssign(exitCode, $"extractvalue %smalllang.process_result {raw}, 0");
        var errorCode = NextTemp("process_error_code");
        EmitAssign(errorCode, $"extractvalue %smalllang.process_result {raw}, 1");

        var definition = _program.Types.GetEnum(function.ReturnType);
        var okVariant = definition.Variants.First(variant => variant.Name == "Ok");
        var errVariant = definition.Variants.First(variant => variant.Name == "Err");
        var okLabel = NextLabel("process_ok");
        var errorLabel = NextLabel("process_error");
        var spawnLabel = NextLabel("process_spawn_error");
        var waitLabel = NextLabel("process_wait_error");
        var signalLabel = NextLabel("process_signal_error");
        var endLabel = NextLabel("process_end");
        var isOk = NextTemp("process_is_ok");
        EmitCompare(isOk, "eq", "i32", errorCode, "0");
        EmitConditionalBranch(isOk, okLabel, errorLabel);
        var incoming = new List<(RuntimeValue Value, string Label)>();

        EmitLabel(okLabel);
        _currentBlockLabel = okLabel;
        incoming.Add((EmitEnumValue(function.ReturnType, okVariant,
            new RuntimeInt(BoundType.Int, exitCode)), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(errorLabel);
        _currentBlockLabel = errorLabel;
        var isSpawn = NextTemp("process_is_spawn_error");
        EmitCompare(isSpawn, "eq", "i32", errorCode, "1");
        EmitConditionalBranch(isSpawn, spawnLabel, waitLabel);

        EmitLabel(spawnLabel);
        _currentBlockLabel = spawnLabel;
        incoming.Add((EmitEnumValue(function.ReturnType, errVariant,
            EmitProcessErrorText("spawn")), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(waitLabel);
        _currentBlockLabel = waitLabel;
        var isWait = NextTemp("process_is_wait_error");
        EmitCompare(isWait, "eq", "i32", errorCode, "2");
        EmitBranch(signalLabel);

        EmitLabel(signalLabel);
        _currentBlockLabel = signalLabel;
        var errorText = EmitProcessErrorText("signal");
        var waitText = EmitProcessErrorText("wait");
        var selectedText = NextTemp("process_error_text");
        EmitAssign(selectedText, $"select i1 {isWait}, ptr {waitText.PointerName}, ptr {errorText.PointerName}");
        var selectedLength = NextTemp("process_error_length");
        EmitAssign(selectedLength, $"select i1 {isWait}, i64 {waitText.LengthName}, i64 {errorText.LengthName}");
        incoming.Add((EmitEnumValue(function.ReturnType, errVariant,
            new RuntimeText(selectedText, selectedLength)), _currentBlockLabel));
        EmitBranch(endLabel);

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        return EmitEnumPhi("process_run_result", function.ReturnType, incoming);
    }

    private RuntimeText EmitProcessErrorText(string text)
    {
        var global = AddGlobalString(text);
        return new RuntimeText(global.Name, global.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
