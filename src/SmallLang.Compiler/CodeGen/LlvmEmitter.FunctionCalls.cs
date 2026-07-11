using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeValue EmitFunctionCall(CallExpression expression)
    {
        if (TryEmitEnumConstructor(expression, out var enumValue))
        {
            return enumValue;
        }

        var path = string.Join('.', expression.Path);
        string? methodReceiverName = null;
        if (!_program.ResolvedGenericCalls.TryGetValue(expression, out var function)
            && !TryResolveFunction(expression.Path, out function))
        {
            if (!TryResolveInstanceMethodCall(expression.Path, out function, out methodReceiverName))
            {
                throw new SmallLangException($"unknown runtime function or method '{path}'");
            }
        }

        if (TryGetRuntimeWrapperKind(function, out var wrapperKind))
        {
            return EmitRuntimeWrapperCall(expression, wrapperKind, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            _mainOk = EmitPrintArgument(expression.Arguments[0], _mainOk);
            if (function.Kind == BoundFunctionKind.RuntimePrintLine)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
            }

            return RuntimeUnit.Instance;
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadInt)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one Text prompt");
            }

            var prompt = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(prompt, BoundType.Text, path);
            return EmitReadIntPrompt(prompt);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"{path} does not accept arguments");
            }

            return EmitRuntimeNowMillisIntrinsic(path);
        }

        if (function.Kind is BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeOpenIntReader)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            var runtimeArgument = EmitExpression(expression.Arguments[0]);
            EmitRuntimeUnitIntrinsic(function, runtimeArgument, path);
            return RuntimeUnit.Instance;
        }

        if (function.Kind is BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeClosestInt)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SmallLangException($"{path} expects exactly one argument");
            }

            var runtimeArgument = EmitExpression(expression.Arguments[0]);
            return EmitRuntimeIntIntrinsic(function, runtimeArgument, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimeCloseIntWriter
            or BoundFunctionKind.RuntimeCloseIntReader)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"{path} does not accept arguments");
            }

            EmitRuntimeUnitIntrinsic(function, argument: null, path);
            return RuntimeUnit.Instance;
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SmallLangException($"unsupported runtime function kind '{function.Kind}'");
        }

        RuntimeValue? argument = null;
        if (methodReceiverName is not null)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SmallLangException($"method '{path}' does not accept additional arguments in this slice");
            }

            argument = ResolveLocal(methodReceiverName);
            EnsureFunctionArgumentRuntimeType(argument, function.InputType!.Value, path);
        }
        else if (function.InputType is null)
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

            if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
            {
                argument = CreateMutableBorrowArgument(expression.Arguments[0], function, path);
            }
            else
            {
                argument = EmitExpression(expression.Arguments[0]);
                EnsureFunctionArgumentRuntimeType(argument, function.InputType.Value, path);
            }
        }

        var value = EmitFunctionCall(function, argument);
        RemoveOwnedParameterArgumentIfNeeded(function, expression.Arguments);
        return value;
    }

    private RuntimeValue EmitRuntimeWrapperCall(
        CallExpression expression,
        BoundFunctionKind wrapperKind,
        string path)
    {
        if (expression.Arguments.Count != 1)
        {
            throw new SmallLangException($"{path} expects exactly one argument");
        }

        return wrapperKind switch
        {
            BoundFunctionKind.RuntimePrint => EmitRuntimePrintCall(expression.Arguments[0], appendNewLine: false),
            BoundFunctionKind.RuntimePrintLine => EmitRuntimePrintCall(expression.Arguments[0], appendNewLine: true),
            BoundFunctionKind.RuntimeReadInt => EmitReadIntPromptExpression(expression.Arguments[0]),
            BoundFunctionKind.RuntimeSeedRandom
                or BoundFunctionKind.RuntimeOpenIntWriter
                or BoundFunctionKind.RuntimeWriteInt
                or BoundFunctionKind.RuntimeOpenIntReader
                => EmitRuntimeUnitWrapperCall(expression.Arguments[0], wrapperKind, path),
            BoundFunctionKind.RuntimeRandomBelow
                or BoundFunctionKind.RuntimeClosestInt
                => EmitRuntimeIntWrapperCall(expression.Arguments[0], wrapperKind, path),
            _ => throw new SmallLangException($"unsupported runtime wrapper kind '{wrapperKind}'")
        };
    }

    private RuntimeUnit EmitRuntimeUnitWrapperCall(Expression argument, BoundFunctionKind kind, string path)
    {
        var value = EmitExpression(argument);
        EmitRuntimeUnitIntrinsic(kind, value, path);
        return RuntimeUnit.Instance;
    }

    private RuntimeInt EmitRuntimeIntWrapperCall(Expression argument, BoundFunctionKind kind, string path)
    {
        var value = EmitExpression(argument);
        return EmitRuntimeIntIntrinsic(kind, value, path);
    }

    private RuntimeUnit EmitRuntimePrintCall(Expression argument, bool appendNewLine)
    {
        _mainOk = EmitPrintArgument(argument, _mainOk);
        if (appendNewLine)
        {
            _mainOk = EmitWriteText("\n", _mainOk);
        }

        return RuntimeUnit.Instance;
    }

    private RuntimeValue EmitFlowFunctionCall(BoundFunction function, RuntimeValue argument, Expression source)
    {
        if (function.InputType is null)
        {
            throw new SmallLangException($"function '{function.Name}' does not accept a flowed input");
        }

        var functionArgument = function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow
            ? CreateMutableBorrowArgument(source, function, function.Name)
            : argument;
        EnsureFunctionArgumentRuntimeType(functionArgument, function.InputType.Value, function.Name);
        var value = EmitFunctionCall(function, functionArgument);
        RemoveOwnedParameterFlowSourceIfNeeded(function, source);
        return value;
    }

    private bool TryResolveInstanceMethodCall(
        IReadOnlyList<string> path,
        out BoundFunction function,
        out string? receiverName)
    {
        function = null!;
        receiverName = null;
        if (path.Count != 2 || !_locals.TryGetValue(path[0], out var receiver))
        {
            return false;
        }

        if (!TryResolveInstanceMethod(receiver.Type, path[1], out function))
        {
            return false;
        }

        receiverName = path[0];
        return true;
    }

    private bool TryResolveInstanceMethod(BoundType receiverType, string methodName, out BoundFunction function)
    {
        function = null!;
        if (!_program.Types.IsStruct(receiverType))
        {
            return false;
        }

        var typeName = _program.Types.GetStruct(receiverType).Name;
        if (methodName.Contains('.', StringComparison.Ordinal))
        {
            var separator = methodName.LastIndexOf('.');
            var traitName = methodName[..separator];
            var memberName = methodName[(separator + 1)..];
            return _currentFunctions.TryGetValue(traitName + "." + typeName + "." + memberName, out function!)
                && function.InputType == receiverType;
        }

        if (_currentFunctions.TryGetValue(typeName + "." + methodName, out function!)
            && function.InputType == receiverType)
        {
            return true;
        }

        var candidates = _currentFunctions.Values
            .Where(candidate => candidate.TraitName is not null
                && candidate.InputType == receiverType
                && candidate.Name.EndsWith("." + methodName, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (candidates.Length != 1)
        {
            return false;
        }

        function = candidates[0];
        return true;
    }

    private RuntimeValue EmitInlineFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        if (function.Body is null && function.ReturnType != BoundType.Unit)
        {
            throw new SmallLangException($"function '{function.Name}' has no body");
        }

        if (_inlineFunctionStack.Any(candidate => ReferenceEquals(candidate, function)))
        {
            throw new SmallLangException($"recursive inline function '{function.Name}' is not supported in the current runtime slice");
        }

        var outerLocals = CaptureLocals();
        var previousFunctions = _currentFunctions;
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        _inlineFunctionStack.Add(function);
        try
        {
            var functionLocals = CaptureLocals();
            if (function.InputType is null)
            {
                if (argument is not null)
                {
                    throw new SmallLangException($"function '{function.Name}' does not accept arguments");
                }
            }
            else
            {
                if (argument is null)
                {
                    throw new SmallLangException($"function '{function.Name}' expects exactly one argument");
                }

                EnsureFunctionArgumentRuntimeType(argument, function.InputType.Value, function.Name);
                if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
                {
                    BindInlineMutableBorrowFunctionParameter(function, argument);
                }
                else
                {
                    var inputName = function.InputName ?? "it";
                    _locals[inputName] = function.InputType switch
                    {
                        BoundType.IntSlice => CreateRuntimeIntSlice(argument),
                        BoundType.IntDictionaryView => CreateRuntimeIntDictionaryView(argument),
                        _ => argument
                    };
                    if (function.InputOwnership == BoundFunctionInputOwnership.Default
                        && _program.Types.ContainsOwnedStorage(function.InputType.Value))
                    {
                        _borrowedOwnedLocals.Add(inputName);
                    }
                }
            }

            EmitStatements(function.BlockBody);
            var value = function.Body is null
                ? RuntimeUnit.Instance
                : EmitExpression(function.Body);
            EnsureRuntimeType(value, function.ReturnType, function.Name);
            var transferredOwnerName = function.Body is not null && IsOwnedContainerRuntimeValue(value)
                ? GetFunctionResultTransferredOwnerName(function, function.Body)
                : null;
            DropOwnedLocalsCreatedSince(functionLocals, transferredOwnerName);
            return value;
        }
        finally
        {
            _inlineFunctionStack.RemoveAt(_inlineFunctionStack.Count - 1);
            _currentFunctions = previousFunctions;
            RestoreLocals(outerLocals);
        }
    }

    private RuntimeValue EmitFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Text value");
            }

            EnsureRuntimeType(argument, BoundType.Text, function.Name);
            _mainOk = EmitWriteValue(argument, _mainOk);
            if (function.Kind == BoundFunctionKind.RuntimePrintLine)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
            }

            return RuntimeUnit.Instance;
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadInt)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Text prompt");
            }

            EnsureRuntimeType(argument, BoundType.Text, function.Name);
            return EmitReadIntPrompt(argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (argument is not null)
            {
                throw new SmallLangException($"{function.Name} does not accept an argument");
            }

            return EmitRuntimeNowMillisIntrinsic(function.Name);
        }

        if (function.Kind is BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeCloseIntWriter
            or BoundFunctionKind.RuntimeOpenIntReader
            or BoundFunctionKind.RuntimeCloseIntReader)
        {
            return EmitRuntimeUnitIntrinsic(function, argument, function.Name);
        }

        if (function.Kind is BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeClosestInt)
        {
            if (argument is null)
            {
                throw new SmallLangException($"{function.Name} expects exactly one Int argument");
            }

            return EmitRuntimeIntIntrinsic(function, argument, function.Name);
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SmallLangException($"function '{function.Name}' does not produce a runtime value");
        }

        if (function.IsStandardLibrary || function.IsLocal)
        {
            return EmitInlineFunctionCall(function, argument);
        }

        return function.ReturnType switch
        {
            BoundType.Unit => EmitUnitFunctionCall(function, argument),
            BoundType.Text => EmitTextFunctionCall(function, argument),
            BoundType.Int => EmitIntFunctionCall(function, argument),
            BoundType.Bool => EmitBoolFunctionCall(function, argument),
            BoundType.DynamicIntArray => EmitDynamicIntArrayFunctionCall(function, argument),
            _ when _program.Types.IsDynamicArray(function.ReturnType) => EmitDynamicInlineArrayFunctionCall(function, argument),
            BoundType.IntDictionary => EmitIntDictionaryFunctionCall(function, argument),
            _ when _program.Types.IsDictionary(function.ReturnType) => EmitInlineDictionaryFunctionCall(function, argument),
            _ when _program.Types.IsStruct(function.ReturnType)
                || _program.Types.IsEnum(function.ReturnType)
                || _program.Types.IsBox(function.ReturnType)
                => EmitStructFunctionCall(function, argument),
            _ => throw new SmallLangException($"unsupported function return type {function.ReturnType}")
        };
    }

    private RuntimeValue EmitStructFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("struct_call");
        var arguments = FunctionCallArgumentList(function, argument);
        var llvmType = LlvmType(function.ReturnType);
        EmitCall(value, llvmType, SymbolForFunction(function.Name)[1..], arguments);
        return DematerializeAggregateValue(function.ReturnType, value);
    }

    private RuntimeUnit EmitUnitFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(target: null, "void", SymbolForFunction(function.Name)[1..], arguments);
        return RuntimeUnit.Instance;
    }

    private RuntimeText EmitTextFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("text");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(aggregate, "%smalllang.text", SymbolForFunction(function.Name)[1..], arguments);

        var pointer = NextTemp("text_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.text {aggregate}, 0");

        var length = NextTemp("text_len");
        EmitAssign(length, $"extractvalue %smalllang.text {aggregate}, 1");

        return new RuntimeText(pointer, length);
    }

    private RuntimeInt EmitIntFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(value, "i64", SymbolForFunction(function.Name)[1..], arguments);
        return new RuntimeInt(value);
    }

    private RuntimeBool EmitBoolFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(value, "i1", SymbolForFunction(function.Name)[1..], arguments);
        return new RuntimeBool(value);
    }

    private RuntimeDynamicIntArray EmitDynamicIntArrayFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("array");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(aggregate, "%smalllang.dynamic_int_array", SymbolForFunction(function.Name)[1..], arguments);

        var pointer = NextTemp("array_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 0");

        var length = NextTemp("array_len");
        EmitAssign(length, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 1");

        var capacity = NextTemp("array_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 2");

        return new RuntimeDynamicIntArray(pointer, length, capacity);
    }

    private RuntimeDynamicInlineArray EmitDynamicInlineArrayFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("generic_array");
        EmitCall(aggregate, "%smalllang.dynamic_int_array", SymbolForFunction(function.Name)[1..],
            FunctionCallArgumentList(function, argument));
        var pointer = NextTemp("generic_array_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 0");
        var length = NextTemp("generic_array_len");
        EmitAssign(length, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 1");
        var capacity = NextTemp("generic_array_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.dynamic_int_array {aggregate}, 2");
        var definition = _program.Types.GetDynamicArray(function.ReturnType);
        return new RuntimeDynamicInlineArray(function.ReturnType, definition.ElementType,
            pointer, length, capacity);
    }

    private RuntimeIntDictionary EmitIntDictionaryFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("dict");
        var arguments = FunctionCallArgumentList(function, argument);
        EmitCall(aggregate, "%smalllang.int_dictionary", SymbolForFunction(function.Name)[1..], arguments);

        var pointer = NextTemp("dict_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.int_dictionary {aggregate}, 0");

        var length = NextTemp("dict_len");
        EmitAssign(length, $"extractvalue %smalllang.int_dictionary {aggregate}, 1");

        var capacity = NextTemp("dict_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.int_dictionary {aggregate}, 2");

        return new RuntimeIntDictionary(pointer, length, capacity);
    }

    private RuntimeInlineDictionary EmitInlineDictionaryFunctionCall(BoundFunction function, RuntimeValue? argument)
    {
        var aggregate = NextTemp("generic_dict");
        EmitCall(aggregate, "%smalllang.int_dictionary", SymbolForFunction(function.Name)[1..],
            FunctionCallArgumentList(function, argument));
        var pointer = NextTemp("generic_dict_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.int_dictionary {aggregate}, 0");
        var length = NextTemp("generic_dict_len");
        EmitAssign(length, $"extractvalue %smalllang.int_dictionary {aggregate}, 1");
        var capacity = NextTemp("generic_dict_capacity");
        EmitAssign(capacity, $"extractvalue %smalllang.int_dictionary {aggregate}, 2");
        var definition = _program.Types.GetDictionary(function.ReturnType);
        return new RuntimeInlineDictionary(function.ReturnType, definition.KeyType, definition.ValueType,
            pointer, length, capacity);
    }

    private string FunctionCallArgumentList(BoundFunction function, RuntimeValue? argument)
    {
        if (function.InputType is null)
        {
            if (argument is not null)
            {
                throw new SmallLangException($"function '{function.Name}' does not accept arguments");
            }

            return "";
        }

        if (argument is null)
        {
            throw new SmallLangException($"function '{function.Name}' expects exactly one argument");
        }

        if (function.HasValueGenericFixedArrayInput)
        {
            if (argument is not RuntimeStaticIntArray fixedArray)
            {
                throw new SmallLangException(
                    $"value-generic function '{function.Name}' requires a fixed Int array input");
            }
            if (function.SpecializedValue is not { } expectedLength
                || !int.TryParse(fixedArray.LengthName, out var actualLength)
                || actualLength != expectedLength)
            {
                throw new SmallLangException(
                    $"function '{function.Name}' expects [Int; {function.SpecializedValue}] but received [Int; {fixedArray.LengthName}]");
            }
        }

        if (argument is RuntimeDynamicInlineArray inlineArray
            && function.InputType == inlineArray.ArrayType)
        {
            return $"%smalllang.dynamic_int_array {BuildDynamicArrayAggregate(inlineArray.PointerName, inlineArray.LengthName, inlineArray.CapacityName)}";
        }
        if (argument is RuntimeInlineDictionary inlineDictionary
            && function.InputType == inlineDictionary.DictionaryType)
        {
            return $"%smalllang.int_dictionary {BuildDictionaryAggregate(inlineDictionary.PointerName, inlineDictionary.LengthName, inlineDictionary.CapacityName)}";
        }

        return argument switch
        {
            RuntimeInt integer when function.InputType == BoundType.Int => $"i64 {integer.ValueName}",
            RuntimeBool boolean when function.InputType == BoundType.Bool => $"i1 {boolean.ValueName}",
            RuntimeIntSlice slice when function.InputType == BoundType.IntSlice => BuildIntSliceArgument(slice.PointerName, slice.LengthName),
            RuntimeStaticIntArray array when function.InputType == BoundType.IntSlice => BuildStaticIntArraySliceArgument(array),
            RuntimeDynamicIntArray array when function.InputType == BoundType.IntSlice => BuildIntSliceArgument(array.PointerName, array.LengthName),
            RuntimeMutableStructReference reference when function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow => $"ptr {reference.PointerAddress}",
            RuntimeMutableContainerReference reference when function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow => BuildMutableContainerArgument(reference),
            RuntimeDynamicIntArray array when function.InputType == BoundType.DynamicIntArray => BuildDynamicIntArrayArgument(array),
            RuntimeIntDictionaryView dictionary when function.InputType == BoundType.IntDictionaryView => BuildIntDictionaryArgument(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName),
            RuntimeIntDictionary dictionary when function.InputType == BoundType.IntDictionaryView => BuildIntDictionaryArgument(dictionary.PointerName, dictionary.LengthName, dictionary.CapacityName),
            RuntimeIntDictionary dictionary when function.InputType == BoundType.IntDictionary => BuildIntDictionaryArgument(dictionary),
            RuntimeStruct structure when function.InputType == structure.Type => $"{LlvmStructType(structure.Type)} {structure.ValueName}",
            RuntimeEnum enumeration when function.InputType == enumeration.Type => $"{LlvmEnumType(enumeration.Type)} {enumeration.ValueName}",
            RuntimeBox box when function.InputType == box.Type => $"ptr {box.PointerName}",
            _ => throw new SmallLangException($"function '{function.Name}' expects {function.InputType} but received {argument.Type}")
        };
    }

    private RuntimeValue CreateMutableBorrowArgument(
        Expression argument,
        BoundFunction function,
        string path)
    {
        if (argument is not NameExpression name)
        {
            throw new SmallLangException($"function '{path}' mutably borrows a value, so the argument must be a named mutable owner");
        }

        if (!_mutableLocals.Contains(name.Name))
        {
            throw new SmallLangException($"function '{path}' mutably borrows a value; use a mutable owner binding such as '{name.Name.TrimEnd('!')}!'");
        }

        var value = ResolveLocal(name.Name);
        EnsureRuntimeType(value, function.InputType!.Value, path);
        if (_mutableStructSlots.TryGetValue(name.Name, out var structPointer))
        {
            return new RuntimeMutableStructReference(function.InputType.Value, structPointer);
        }

        if (!_mutableContainerSlots.TryGetValue(name.Name, out var slot))
        {
            throw new SmallLangException($"mutable owner '{name.Name}' has no addressable storage");
        }

        return new RuntimeMutableContainerReference(
            function.InputType.Value,
            slot.PointerAddress,
            slot.LengthAddress,
            slot.CapacityAddress);
    }

    private void BindInlineMutableBorrowFunctionParameter(BoundFunction function, RuntimeValue argument)
    {
        if (argument is RuntimeMutableStructReference structReference)
        {
            var structName = function.InputName ?? "it";
            _locals[structName] = new RuntimeStruct(structReference.TargetType, "");
            _mutableLocals.Add(structName);
            _borrowedMutableLocals.Add(structName);
            _mutableStructSlots[structName] = structReference.PointerAddress;
            return;
        }

        if (argument is not RuntimeMutableContainerReference reference)
        {
            throw new SmallLangException($"function '{function.Name}' expects a mutable borrow argument");
        }

        var name = function.InputName ?? "it";
        _locals[name] = reference.TargetType switch
        {
            BoundType.DynamicIntArray => new RuntimeDynamicIntArray("", "", ""),
            _ when _program.Types.IsDynamicArray(reference.TargetType) => CreateEmptyRuntimeDynamicInlineArray(reference.TargetType),
            BoundType.IntDictionary => new RuntimeIntDictionary("", "", ""),
            _ when _program.Types.IsDictionary(reference.TargetType) => CreateEmptyRuntimeInlineDictionary(reference.TargetType),
            _ => throw new SmallLangException("unsupported mutable borrow input type")
        };
        _mutableLocals.Add(name);
        _borrowedMutableLocals.Add(name);
        _mutableContainerSlots[name] = new MutableContainerSlot(
            reference.PointerAddress,
            reference.LengthAddress,
            reference.CapacityAddress,
            StackAllocation: null);
    }

    private string BuildMutableContainerArgument(RuntimeMutableContainerReference reference)
    {
        var aggregate0 = NextTemp("mutable_arg");
        EmitAssign(aggregate0, $"insertvalue %smalllang.mutable_container poison, ptr {reference.PointerAddress}, 0");
        var aggregate1 = NextTemp("mutable_arg");
        EmitAssign(aggregate1, $"insertvalue %smalllang.mutable_container {aggregate0}, ptr {reference.LengthAddress}, 1");
        var aggregate2 = NextTemp("mutable_arg");
        EmitAssign(aggregate2, $"insertvalue %smalllang.mutable_container {aggregate1}, ptr {reference.CapacityAddress}, 2");
        return $"%smalllang.mutable_container {aggregate2}";
    }

    private string BuildStaticIntArraySliceArgument(RuntimeStaticIntArray array)
    {
        var pointer = NextTemp("slice_ptr");
        EmitAssign(pointer, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i64], ptr {array.PointerName}, i64 0, i64 0");
        return BuildIntSliceArgument(pointer, array.LengthName);
    }

    private RuntimeIntSlice CreateRuntimeIntSlice(RuntimeValue value)
    {
        return value switch
        {
            RuntimeIntSlice slice => slice,
            RuntimeDynamicIntArray array => new RuntimeIntSlice(array.PointerName, array.LengthName),
            RuntimeStaticIntArray array => CreateRuntimeIntSlice(array),
            _ => throw new SmallLangException($"expected Int array view but received {value.Type}")
        };
    }

    private RuntimeIntSlice CreateRuntimeIntSlice(RuntimeStaticIntArray array)
    {
        var pointer = NextTemp("slice_ptr");
        EmitAssign(pointer, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i64], ptr {array.PointerName}, i64 0, i64 0");
        return new RuntimeIntSlice(pointer, array.LengthName);
    }

    private static RuntimeIntDictionaryView CreateRuntimeIntDictionaryView(RuntimeValue value)
    {
        return value switch
        {
            RuntimeIntDictionaryView view => view,
            RuntimeIntDictionary dictionary => new RuntimeIntDictionaryView(
                dictionary.PointerName,
                dictionary.LengthName,
                dictionary.CapacityName),
            _ => throw new SmallLangException($"expected Int dictionary view but received {value.Type}")
        };
    }

    private string BuildIntSliceArgument(string pointer, string length)
    {
        var aggregate0 = NextTemp("slice_arg");
        EmitAssign(aggregate0, $"insertvalue %smalllang.int_slice poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("slice_arg");
        EmitAssign(aggregate1, $"insertvalue %smalllang.int_slice {aggregate0}, i64 {length}, 1");
        return $"%smalllang.int_slice {aggregate1}";
    }

    private string BuildDynamicIntArrayArgument(RuntimeDynamicIntArray array)
    {
        var aggregate0 = NextTemp("array_arg");
        EmitAssign(aggregate0, $"insertvalue %smalllang.dynamic_int_array poison, ptr {array.PointerName}, 0");
        var aggregate1 = NextTemp("array_arg");
        EmitAssign(aggregate1, $"insertvalue %smalllang.dynamic_int_array {aggregate0}, i64 {array.LengthName}, 1");
        var aggregate2 = NextTemp("array_arg");
        EmitAssign(aggregate2, $"insertvalue %smalllang.dynamic_int_array {aggregate1}, i64 {array.CapacityName}, 2");
        return $"%smalllang.dynamic_int_array {aggregate2}";
    }

    private string BuildDynamicArrayAggregate(string pointer, string length, string capacity)
    {
        var aggregate0 = NextTemp("generic_array_value");
        EmitAssign(aggregate0, $"insertvalue %smalllang.dynamic_int_array poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("generic_array_value");
        EmitAssign(aggregate1, $"insertvalue %smalllang.dynamic_int_array {aggregate0}, i64 {length}, 1");
        var aggregate2 = NextTemp("generic_array_value");
        EmitAssign(aggregate2, $"insertvalue %smalllang.dynamic_int_array {aggregate1}, i64 {capacity}, 2");
        return aggregate2;
    }

    private RuntimeDynamicInlineArray CreateEmptyRuntimeDynamicInlineArray(BoundType type)
    {
        var definition = _program.Types.GetDynamicArray(type);
        return new RuntimeDynamicInlineArray(type, definition.ElementType, "", "", "");
    }

    private string BuildIntDictionaryArgument(RuntimeIntDictionary dictionary)
    {
        return BuildIntDictionaryArgument(
            dictionary.PointerName,
            dictionary.LengthName,
            dictionary.CapacityName);
    }

    private string BuildIntDictionaryArgument(string pointer, string length, string capacity)
    {
        var aggregate0 = NextTemp("dict_arg");
        EmitAssign(aggregate0, $"insertvalue %smalllang.int_dictionary poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("dict_arg");
        EmitAssign(aggregate1, $"insertvalue %smalllang.int_dictionary {aggregate0}, i64 {length}, 1");
        var aggregate2 = NextTemp("dict_arg");
        EmitAssign(aggregate2, $"insertvalue %smalllang.int_dictionary {aggregate1}, i64 {capacity}, 2");
        return $"%smalllang.int_dictionary {aggregate2}";
    }

    private RuntimeInlineDictionary CreateEmptyRuntimeInlineDictionary(BoundType type)
    {
        var definition = _program.Types.GetDictionary(type);
        return new RuntimeInlineDictionary(type, definition.KeyType, definition.ValueType, "", "", "");
    }

    private void RemoveOwnedParameterArgumentIfNeeded(BoundFunction function, IReadOnlyList<Expression> arguments)
    {
        if (!FunctionConsumesOwnedHeapInput(function)
            || arguments.Count != 1
            || arguments[0] is not NameExpression name)
        {
            return;
        }

        RemoveLocal(name.Name);
    }

    private void RemoveOwnedParameterFlowSourceIfNeeded(BoundFunction function, Expression source)
    {
        if (!FunctionConsumesOwnedHeapInput(function)
            || source is not NameExpression name)
        {
            return;
        }

        RemoveLocal(name.Name);
    }

    private static bool FunctionConsumesOwnedHeapInput(BoundFunction function)
    {
        return function.InputOwnership == BoundFunctionInputOwnership.Move
            && function.InputType is not null;
    }

    private static void EnsureRuntimeType(RuntimeValue value, BoundType expected, string path)
    {
        if (value.Type != expected)
        {
            throw new SmallLangException($"function '{path}' expects {expected} but received {value.Type}");
        }
    }

    private static void EnsureFunctionArgumentRuntimeType(RuntimeValue value, BoundType expected, string path)
    {
        if (expected == BoundType.IntSlice
            && value.Type is BoundType.IntSlice or BoundType.StaticIntArray or BoundType.DynamicIntArray)
        {
            return;
        }

        if (expected == BoundType.IntDictionaryView
            && value.Type is BoundType.IntDictionaryView or BoundType.IntDictionary)
        {
            return;
        }

        EnsureRuntimeType(value, expected, path);
    }

    private bool TryGetRuntimePrinterKind(BoundFunction function, out BoundFunctionKind kind)
    {
        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            kind = function.Kind;
            return true;
        }

        if (TryGetRuntimeWrapperKind(function, out kind)
            && kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            return true;
        }

        kind = default;
        return false;
    }

    private bool TryGetRuntimeWrapperKind(BoundFunction function, out BoundFunctionKind kind)
    {
        if (!function.IsStandardLibrary
            || function.Body is not FlowExpression flow
            || flow.Source is not NameExpression name
            || name.Name != (function.InputName ?? "it")
            || flow.Targets.Count != 1
            || !TryResolveFunction(flow.Targets[0].Path, out var target))
        {
            kind = default;
            return false;
        }

        if (target.Kind is BoundFunctionKind.RuntimePrint
            or BoundFunctionKind.RuntimePrintLine
            or BoundFunctionKind.RuntimeReadInt
            or BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeRandomBelow
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeOpenIntReader
            or BoundFunctionKind.RuntimeClosestInt)
        {
            kind = target.Kind;
            return true;
        }

        kind = default;
        return false;
    }

}

