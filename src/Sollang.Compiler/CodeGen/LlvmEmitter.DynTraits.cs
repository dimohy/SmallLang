using System.Globalization;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitDynTraitTables()
    {
        var conversions = _program.DynTraitConversions.Values
            .DistinctBy(conversion => (conversion.DynType, conversion.ConcreteType))
            .OrderBy(conversion => conversion.DynType)
            .ThenBy(conversion => conversion.ConcreteType)
            .ToArray();
        foreach (var conversion in conversions)
        {
            var table = DynVtableSymbol(conversion);
            var entries = new List<string> { "ptr @" + DynDropWrapperSymbol(conversion) };
            entries.AddRange(conversion.Methods.Select((_, index) =>
                "ptr @" + DynMethodWrapperSymbol(conversion, index)));
            EmitGlobalLine($"@{table} = internal constant [{entries.Count.ToString(CultureInfo.InvariantCulture)} x ptr] "
                + $"[{string.Join(", ", entries)}], align {_platform.PointerBitWidth / 8}");
        }
        if (conversions.Length > 0)
        {
            EmitGlobalLine();
        }

        foreach (var conversion in conversions)
        {
            EmitDynDropWrapper(conversion);
            for (var index = 0; index < conversion.Methods.Count; index++)
            {
                EmitDynMethodWrapper(conversion, index);
            }
        }
    }

    private RuntimeDynTrait EmitDynTraitConversion(
        RuntimeValue value,
        BoundDynTraitConversion conversion)
    {
        EnsureRuntimeType(value, conversion.ConcreteType, "dyn conversion");
        var materialized = MaterializeAggregateValue(value);
        var data = NextTemp("dyn_data");
        EmitCall(data, "ptr", "sollang_alloc",
            $"i64 {_program.Types.InlineSizeOf(conversion.ConcreteType).ToString(CultureInfo.InvariantCulture)}");
        EmitStore(materialized.TypeName, materialized.ValueName, data,
            RuntimeAlignment(conversion.ConcreteType));
        return new RuntimeDynTrait(
            conversion.DynType,
            data,
            "@" + DynVtableSymbol(conversion));
    }

    private RuntimeValue EmitDynTraitDispatch(
        RuntimeDynTrait value,
        BoundDynTraitDispatch dispatch)
    {
        if (value.Type != dispatch.DynType)
        {
            throw new SollangException("dyn trait dispatch received the wrong trait object type");
        }
        var slot = NextTemp("dyn_method_slot");
        EmitAssign(slot,
            $"getelementptr ptr, ptr {value.VtablePointerName}, i64 {(dispatch.MethodIndex + 1).ToString(CultureInfo.InvariantCulture)}");
        var method = NextTemp("dyn_method");
        EmitLoad(method, "ptr", slot, _platform.PointerBitWidth / 8);
        var returnType = dispatch.Method.ReturnType
            ?? throw new SollangException("dyn trait dispatch requires a concrete return type");
        const string runtimeContext = "ptr %stdin, ptr %stdout, ptr %written, ptr %read, ptr %ok_state";
        var arguments = runtimeContext + $", ptr {value.DataPointerName}";
        if (returnType == BoundType.Unit)
        {
            EmitInstruction($"call void {method}({arguments})");
            return RuntimeUnit.Instance;
        }
        var result = NextTemp("dyn_call");
        EmitAssign(result, $"call {LlvmType(returnType)} {method}({arguments})");
        return DematerializeAggregateValue(returnType, result);
    }

    private void EmitDynDropWrapper(BoundDynTraitConversion conversion)
    {
        EmitFunctionLine($"define internal void @{DynDropWrapperSymbol(conversion)}(ptr %data) #0 {{");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        if (_program.Types.ContainsOwnedStorage(conversion.ConcreteType))
        {
            var loaded = NextTemp("dyn_drop_value");
            EmitLoad(loaded, LlvmType(conversion.ConcreteType), "%data",
                RuntimeAlignment(conversion.ConcreteType));
            EmitOwnedDropCall(conversion.ConcreteType, loaded);
        }
        EmitCall(target: null, "void", "sollang_free", "ptr %data");
        EmitInstruction("ret void");
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private void EmitDynMethodWrapper(BoundDynTraitConversion conversion, int methodIndex)
    {
        var function = conversion.Methods[methodIndex];
        if (_program.FunctionCapturedBindings.TryGetValue(function, out var captures)
            && captures.Count != 0)
        {
            throw new SollangException($"dyn-compatible implementation '{function.Name}' cannot capture locals");
        }
        var returnType = conversion.Trait.Methods[methodIndex].ReturnType
            ?? throw new SollangException("dyn-compatible methods require concrete return types");
        var llvmReturnType = returnType == BoundType.Unit ? "void" : LlvmType(returnType);
        var wrapper = DynMethodWrapperSymbol(conversion, methodIndex);
        EmitFunctionLine($"define internal {llvmReturnType} @{wrapper}("
            + "ptr %stdin, ptr %stdout, ptr %written, ptr %read, ptr %ok_state, ptr %data) #0 {");
        EmitFunctionLine("entry:");
        _currentBlockLabel = "entry";
        var receiver = NextTemp("dyn_receiver");
        EmitLoad(receiver, LlvmType(conversion.ConcreteType), "%data",
            RuntimeAlignment(conversion.ConcreteType));
        var arguments = "ptr %stdin, ptr %stdout, ptr %written, ptr %read, ptr %ok_state, "
            + $"{LlvmType(conversion.ConcreteType)} {receiver}";
        if (returnType == BoundType.Unit)
        {
            EmitInstruction($"call void {SymbolForFunction(function)}({arguments})");
            EmitInstruction("ret void");
        }
        else
        {
            var result = NextTemp("dyn_result");
            EmitAssign(result, $"call {llvmReturnType} {SymbolForFunction(function)}({arguments})");
            EmitRet(llvmReturnType, result);
        }
        EmitFunctionLine("}");
        EmitFunctionLine();
    }

    private static string DynVtableSymbol(BoundDynTraitConversion conversion) =>
        DynSymbolPrefix(conversion) + "_vtable";

    private static string DynDropWrapperSymbol(BoundDynTraitConversion conversion) =>
        DynSymbolPrefix(conversion) + "_drop";

    private static string DynMethodWrapperSymbol(BoundDynTraitConversion conversion, int methodIndex) =>
        DynSymbolPrefix(conversion) + "_method_" + methodIndex.ToString(CultureInfo.InvariantCulture);

    private static string DynSymbolPrefix(BoundDynTraitConversion conversion)
    {
        var builder = new StringBuilder("sollang_dyn_");
        foreach (var character in conversion.Trait.Name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }
        builder.Append('_');
        builder.Append(((int)conversion.ConcreteType).ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
