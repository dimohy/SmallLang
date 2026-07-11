using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private void EmitUserFunctions()
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in _program.Functions.Values)
        {
            if (function.Kind != BoundFunctionKind.User
                || function.IsStandardLibrary
                || function.IsLocal
                || (function.GenericParameterName is not null
                    && function.SpecializedType is null
                    && function.SpecializedValue is null)
                || !emitted.Add(function.Name))
            {
                continue;
            }

            _currentFunction = function;
            switch (function.ReturnType)
            {
                case BoundType.Unit:
                    EmitUnitFunction(function);
                    break;
                case BoundType.Text:
                    EmitTextFunction(function);
                    break;
                case BoundType.Int:
                    EmitIntFunction(function);
                    break;
                case BoundType.Bool:
                    EmitBoolFunction(function);
                    break;
                case BoundType.DynamicIntArray:
                    EmitDynamicIntArrayFunction(function);
                    break;
                case BoundType.IntDictionary:
                    EmitIntDictionaryFunction(function);
                    break;
                default:
                    if (_program.Types.IsDynamicArray(function.ReturnType))
                    {
                        EmitDynamicInlineArrayFunction(function);
                        break;
                    }
                    if (_program.Types.IsDictionary(function.ReturnType))
                    {
                        EmitInlineDictionaryFunction(function);
                        break;
                    }
                    if (_program.Types.IsStruct(function.ReturnType)
                        || _program.Types.IsEnum(function.ReturnType)
                        || _program.Types.IsBox(function.ReturnType))
                    {
                        EmitStructFunction(function);
                        break;
                    }

                    throw new SmallLangException($"unsupported function return type {function.ReturnType}");
            }
        }

        _currentFunction = null;
    }

    private void EmitStructFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            var llvmType = LlvmType(function.ReturnType);
            EmitFunctionLine($"define internal {llvmType} {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = IsOwnedContainerRuntimeValue(value)
                ? GetFunctionResultTransferredOwnerName(function, function.Body)
                : null;
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var aggregate = MaterializeAggregateValue(value);
            EmitRet(aggregate.TypeName, aggregate.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitUnitFunction(BoundFunction function)
    {
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal void {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            if (function.Body is not null)
            {
                var value = EmitExpression(function.Body);
                EnsureRuntimeType(value, BoundType.Unit, function.Name);
            }

            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            EmitInstruction("ret void");
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitTextFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.text {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.Text, function.Name);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            var text = (RuntimeText)value;
            var aggregate0 = NextTemp("text_ret");
            EmitAssign(aggregate0, $"insertvalue %smalllang.text poison, ptr {text.PointerName}, 0");
            var aggregate1 = NextTemp("text_ret");
            EmitAssign(aggregate1, $"insertvalue %smalllang.text {aggregate0}, i64 {text.LengthName}, 1");
            EmitRet("%smalllang.text", aggregate1);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitIntFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal i64 {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitIntExpression(function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            EmitRet("i64", value.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitBoolFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal i1 {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitBoolExpression(function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName: null);
            EmitRet("i1", value.ValueName);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitDynamicIntArrayFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.dynamic_int_array {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.DynamicIntArray, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var array = (RuntimeDynamicIntArray)value;
            var aggregate0 = NextTemp("array_ret");
            EmitAssign(aggregate0, $"insertvalue %smalllang.dynamic_int_array poison, ptr {array.PointerName}, 0");
            var aggregate1 = NextTemp("array_ret");
            EmitAssign(aggregate1, $"insertvalue %smalllang.dynamic_int_array {aggregate0}, i64 {array.LengthName}, 1");
            var aggregate2 = NextTemp("array_ret");
            EmitAssign(aggregate2, $"insertvalue %smalllang.dynamic_int_array {aggregate1}, i64 {array.CapacityName}, 2");
            EmitRet("%smalllang.dynamic_int_array", aggregate2);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitDynamicInlineArrayFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.dynamic_int_array {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);
            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var array = (RuntimeDynamicInlineArray)value;
            EmitRet("%smalllang.dynamic_int_array", BuildDynamicArrayAggregate(
                array.PointerName, array.LengthName, array.CapacityName));
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitIntDictionaryFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.int_dictionary {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);

            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, BoundType.IntDictionary, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var dictionary = (RuntimeIntDictionary)value;
            var aggregate0 = NextTemp("dict_ret");
            EmitAssign(aggregate0, $"insertvalue %smalllang.int_dictionary poison, ptr {dictionary.PointerName}, 0");
            var aggregate1 = NextTemp("dict_ret");
            EmitAssign(aggregate1, $"insertvalue %smalllang.int_dictionary {aggregate0}, i64 {dictionary.LengthName}, 1");
            var aggregate2 = NextTemp("dict_ret");
            EmitAssign(aggregate2, $"insertvalue %smalllang.int_dictionary {aggregate1}, i64 {dictionary.CapacityName}, 2");
            EmitRet("%smalllang.int_dictionary", aggregate2);
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private void EmitInlineDictionaryFunction(BoundFunction function)
    {
        if (function.Body is null)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_program.Functions, function.LocalFunctions);
        ClearLocalState();
        SelectStackFrame(function);
        try
        {
            EmitFunctionLine($"define internal %smalllang.int_dictionary {SymbolForFunction(function.Name)}({ParameterListForFunction(function)}) #0 {{");
            EmitFunctionLine("entry:");
            EmitStackFrameAllocations();
            _currentBlockLabel = "entry";
            var functionLocals = CaptureLocals();
            BindFunctionParameter(function);
            EmitStatements(function.BlockBody);
            var value = EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = GetFunctionResultTransferredOwnerName(function, function.Body);
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            var dictionary = (RuntimeInlineDictionary)value;
            EmitRet("%smalllang.int_dictionary", BuildDictionaryAggregate(
                dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName));
            EmitFunctionLine("}");
            EmitFunctionLine();
        }
        finally
        {
            _currentFunctions = previousFunctions;
        }
    }

    private string ParameterListForFunction(BoundFunction function)
    {
        if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
        {
            if (function.InputType is { } mutableType && _program.Types.IsStruct(mutableType))
            {
                return "ptr %it";
            }

            return function.InputType switch
            {
                BoundType.DynamicIntArray => "%smalllang.mutable_container %it",
                _ when function.InputType is { } type && _program.Types.IsDynamicArray(type) => "%smalllang.mutable_container %it",
                BoundType.IntDictionary => "%smalllang.mutable_container %it",
                _ when function.InputType is { } type && _program.Types.IsDictionary(type) => "%smalllang.mutable_container %it",
                _ => throw new SmallLangException("unsupported mutable borrow input type")
            };
        }

        if (function.InputType is { } inputType
            && (_program.Types.IsStruct(inputType)
                || _program.Types.IsEnum(inputType)
                || _program.Types.IsBox(inputType)))
        {
            return $"{LlvmType(inputType)} %it";
        }

        if (function.InputType is { } dynamicArrayType && _program.Types.IsDynamicArray(dynamicArrayType))
        {
            return "%smalllang.dynamic_int_array %it";
        }
        if (function.InputType is { } dictionaryType && _program.Types.IsDictionary(dictionaryType))
        {
            return "%smalllang.int_dictionary %it";
        }

        return function.InputType switch
        {
            null => "",
            BoundType.Int => "i64 %it",
            BoundType.Bool => "i1 %it",
            BoundType.IntSlice => "%smalllang.int_slice %it",
            BoundType.DynamicIntArray => "%smalllang.dynamic_int_array %it",
            BoundType.IntDictionaryView => "%smalllang.int_dictionary %it",
            BoundType.IntDictionary => "%smalllang.int_dictionary %it",
            _ => throw new SmallLangException("unsupported function input type")
        };
    }

    private void BindFunctionParameter(BoundFunction function)
    {
        if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
        {
            BindMutableBorrowFunctionParameter(function);
            return;
        }

        if (function.InputType is { } borrowedType
            && function.InputOwnership == BoundFunctionInputOwnership.Default
            && _program.Types.ContainsOwnedStorage(borrowedType))
        {
            _borrowedOwnedLocals.Add(function.InputName ?? "it");
        }

        if (function.InputType is { } inputType && _program.Types.IsStruct(inputType))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeStruct(inputType, "%it"));
            return;
        }
        if (function.InputType is { } enumType && _program.Types.IsEnum(enumType))
        {
            _locals.Add(function.InputName ?? "it", new RuntimeEnum(enumType, "%it"));
            return;
        }
        if (function.InputType is { } boxType && _program.Types.IsBox(boxType))
        {
            var definition = _program.Types.GetBox(boxType);
            _locals.Add(
                function.InputName ?? "it",
                new RuntimeBox(boxType, definition.ElementType, "%it"));
            return;
        }
        if (function.InputType is { } dictionaryType && _program.Types.IsDictionary(dictionaryType))
        {
            var definition = _program.Types.GetDictionary(dictionaryType);
            var pointer = NextTemp("param_generic_dict_ptr");
            EmitAssign(pointer, "extractvalue %smalllang.int_dictionary %it, 0");
            var length = NextTemp("param_generic_dict_len");
            EmitAssign(length, "extractvalue %smalllang.int_dictionary %it, 1");
            var capacity = NextTemp("param_generic_dict_capacity");
            EmitAssign(capacity, "extractvalue %smalllang.int_dictionary %it, 2");
            _locals.Add(function.InputName ?? "it", new RuntimeInlineDictionary(
                dictionaryType, definition.KeyType, definition.ValueType, pointer, length, capacity));
            return;
        }
        if (function.InputType is { } dynamicArrayType && _program.Types.IsDynamicArray(dynamicArrayType))
        {
            var definition = _program.Types.GetDynamicArray(dynamicArrayType);
            var pointer = NextTemp("param_generic_array_ptr");
            EmitAssign(pointer, "extractvalue %smalllang.dynamic_int_array %it, 0");
            var length = NextTemp("param_generic_array_len");
            EmitAssign(length, "extractvalue %smalllang.dynamic_int_array %it, 1");
            var capacity = NextTemp("param_generic_array_capacity");
            EmitAssign(capacity, "extractvalue %smalllang.dynamic_int_array %it, 2");
            _locals.Add(function.InputName ?? "it", new RuntimeDynamicInlineArray(
                dynamicArrayType, definition.ElementType, pointer, length, capacity));
            return;
        }

        switch (function.InputType)
        {
            case null:
                return;
            case BoundType.Int:
                _locals.Add(function.InputName ?? "it", new RuntimeInt("%it"));
                return;
            case BoundType.Bool:
                _locals.Add(function.InputName ?? "it", new RuntimeBool("%it"));
                return;
            case BoundType.IntSlice:
                {
                    var pointer = NextTemp("param_slice_ptr");
                    EmitAssign(pointer, "extractvalue %smalllang.int_slice %it, 0");
                    var length = NextTemp("param_slice_len");
                    EmitAssign(length, "extractvalue %smalllang.int_slice %it, 1");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntSlice(pointer, length));
                    return;
                }
            case BoundType.DynamicIntArray:
                {
                    var pointer = NextTemp("param_array_ptr");
                    EmitAssign(pointer, "extractvalue %smalllang.dynamic_int_array %it, 0");
                    var length = NextTemp("param_array_len");
                    EmitAssign(length, "extractvalue %smalllang.dynamic_int_array %it, 1");
                    var capacity = NextTemp("param_array_capacity");
                    EmitAssign(capacity, "extractvalue %smalllang.dynamic_int_array %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeDynamicIntArray(pointer, length, capacity));
                    return;
                }
            case BoundType.IntDictionary:
                {
                    var pointer = NextTemp("param_dict_ptr");
                    EmitAssign(pointer, "extractvalue %smalllang.int_dictionary %it, 0");
                    var length = NextTemp("param_dict_len");
                    EmitAssign(length, "extractvalue %smalllang.int_dictionary %it, 1");
                    var capacity = NextTemp("param_dict_capacity");
                    EmitAssign(capacity, "extractvalue %smalllang.int_dictionary %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntDictionary(pointer, length, capacity));
                    return;
                }
            case BoundType.IntDictionaryView:
                {
                    var pointer = NextTemp("param_dict_view_ptr");
                    EmitAssign(pointer, "extractvalue %smalllang.int_dictionary %it, 0");
                    var length = NextTemp("param_dict_view_len");
                    EmitAssign(length, "extractvalue %smalllang.int_dictionary %it, 1");
                    var capacity = NextTemp("param_dict_view_capacity");
                    EmitAssign(capacity, "extractvalue %smalllang.int_dictionary %it, 2");
                    _locals.Add(function.InputName ?? "it", new RuntimeIntDictionaryView(pointer, length, capacity));
                    return;
                }
            default:
                throw new SmallLangException("unsupported function input type");
        }
    }

    private void BindMutableBorrowFunctionParameter(BoundFunction function)
    {
        if (function.InputType is { } structType && _program.Types.IsStruct(structType))
        {
            var structName = function.InputName ?? "it";
            _locals.Add(structName, new RuntimeStruct(structType, ""));
            _mutableLocals.Add(structName);
            _borrowedMutableLocals.Add(structName);
            _mutableStructSlots.Add(structName, "%it");
            return;
        }

        if (function.InputType is not (BoundType.DynamicIntArray or BoundType.IntDictionary)
            && (function.InputType is not { } dynamicType || !_program.Types.IsDynamicArray(dynamicType))
            && (function.InputType is not { } mutableType || !_program.Types.IsDictionary(mutableType)))
        {
            throw new SmallLangException("unsupported mutable borrow input type");
        }

        var pointerAddress = NextTemp("param_mut_ptr_addr");
        EmitAssign(pointerAddress, "extractvalue %smalllang.mutable_container %it, 0");
        var lengthAddress = NextTemp("param_mut_len_addr");
        EmitAssign(lengthAddress, "extractvalue %smalllang.mutable_container %it, 1");
        var capacityAddress = NextTemp("param_mut_capacity_addr");
        EmitAssign(capacityAddress, "extractvalue %smalllang.mutable_container %it, 2");

        var name = function.InputName ?? "it";
        RuntimeValue mutableValue;
        if (function.InputType is { } genericArray && _program.Types.IsDynamicArray(genericArray))
        {
            var definition = _program.Types.GetDynamicArray(genericArray);
            mutableValue = new RuntimeDynamicInlineArray(
                genericArray, definition.ElementType, "", "", "");
        }
        else if (function.InputType is { } genericDictionary && _program.Types.IsDictionary(genericDictionary))
        {
            var definition = _program.Types.GetDictionary(genericDictionary);
            mutableValue = new RuntimeInlineDictionary(
                genericDictionary, definition.KeyType, definition.ValueType, "", "", "");
        }
        else
        {
            mutableValue = function.InputType switch
            {
                BoundType.DynamicIntArray => new RuntimeDynamicIntArray("", "", ""),
                BoundType.IntDictionary => new RuntimeIntDictionary("", "", ""),
                _ => throw new SmallLangException("unsupported mutable borrow input type")
            };
        }
        _locals.Add(name, mutableValue);
        _mutableLocals.Add(name);
        _borrowedMutableLocals.Add(name);
        _mutableContainerSlots.Add(
            name,
            new MutableContainerSlot(pointerAddress, lengthAddress, capacityAddress, StackAllocation: null));
    }

}

