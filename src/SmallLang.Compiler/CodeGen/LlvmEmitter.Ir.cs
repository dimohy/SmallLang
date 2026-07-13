using System.Globalization;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitLabel(string label)
    {
        EmitFunctionLine($"{label}:");
        _currentBlockTerminated = false;
    }

    private void EmitInstruction(string instruction)
    {
        EmitFunctionLine($"  {instruction}");
    }

    private void EmitAssign(string target, string expression)
    {
        EmitInstruction($"{target} = {expression}");
    }

    private void EmitBranch(string label)
    {
        EmitInstruction($"br label %{label}");
        _currentBlockTerminated = true;
    }

    private void EmitConditionalBranch(string condition, string trueLabel, string falseLabel)
    {
        EmitInstruction($"br i1 {condition}, label %{trueLabel}, label %{falseLabel}");
        _currentBlockTerminated = true;
    }

    private void EmitRet(string typeName, string value)
    {
        EmitInstruction($"ret {typeName} {value}");
        _currentBlockTerminated = true;
    }

    private void EmitTrap()
    {
        EmitInstruction("call void @llvm.trap()");
        EmitInstruction("unreachable");
        _currentBlockTerminated = true;
    }

    private void EmitAlloca(string target, string typeName, int align)
    {
        EmitAssign(target, $"alloca {typeName}, align {align.ToString(CultureInfo.InvariantCulture)}");
    }

    private void EmitLoad(string target, string typeName, string pointer, int align)
    {
        EmitAssign(target, $"load {typeName}, ptr {pointer}, align {align.ToString(CultureInfo.InvariantCulture)}");
    }

    private void EmitStore(string typeName, string value, string pointer, int align)
    {
        EmitInstruction($"store {typeName} {value}, ptr {pointer}, align {align.ToString(CultureInfo.InvariantCulture)}");
    }

    private void EmitCall(string? target, string returnType, string functionName, string arguments)
    {
        var call = $"call {returnType} @{functionName}({arguments})";
        if (target is null)
        {
            EmitInstruction(call);
            return;
        }

        EmitAssign(target, call);
    }

    private void EmitBinary(string target, string operation, string typeName, string left, string right)
    {
        EmitAssign(target, $"{operation} {typeName} {left}, {right}");
    }

    private void EmitCompare(string target, string predicate, string typeName, string left, string right)
    {
        EmitAssign(target, $"icmp {predicate} {typeName} {left}, {right}");
    }

    private void EmitSelect(string target, string condition, string trueValue, string falseValue)
    {
        EmitAssign(target, $"select i1 {condition}, {trueValue}, {falseValue}");
    }

    private void EmitPhi(string target, string typeName, params (string Value, string Label)[] incoming)
    {
        EmitAssign(target, $"phi {typeName} {FormatPhiIncoming(incoming)}");
    }

    private static string FormatPhiIncoming(IEnumerable<(string Value, string Label)> incoming)
    {
        return string.Join(
            ", ",
            incoming.Select(static item => $"[ {item.Value}, %{item.Label} ]"));
    }
}
