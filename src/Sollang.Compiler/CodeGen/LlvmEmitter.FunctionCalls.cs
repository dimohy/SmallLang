using System.Globalization;
using System.Numerics;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Semantics;
using Sollang.Compiler.Syntax;

namespace Sollang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeValue EmitFunctionCall(CallExpression expression)
    {
        if (TryEmitArenaConstructor(expression, out var arena))
        {
            return arena;
        }

        if (TryEmitEnumConstructor(expression, out var enumValue))
        {
            return enumValue;
        }

        if (TryEmitNumericConversion(expression, out var numericValue))
        {
            return numericValue;
        }

        var path = string.Join('.', expression.Path);
        string? methodReceiverName = null;
        if (!_program.ResolvedGenericCalls.TryGetValue(expression, out var function)
            && !TryResolveFunction(expression.Path, out function))
        {
            if (!TryResolveInstanceMethodCall(expression.Path, out function, out methodReceiverName))
            {
                throw new SollangException($"unknown runtime function or method '{path}'");
            }
        }

        if (TryGetRuntimeWrapperKind(function, out var wrapperKind))
        {
            if (wrapperKind == BoundFunctionKind.RuntimePrintLine
                && expression.Arguments.Count == 0)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
                return RuntimeUnit.Instance;
            }

            return EmitRuntimeWrapperCall(expression, wrapperKind, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (function.Kind == BoundFunctionKind.RuntimePrintLine
                && expression.Arguments.Count == 0)
            {
                _mainOk = EmitWriteText("\n", _mainOk);
                return RuntimeUnit.Instance;
            }

            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one argument");
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
                throw new SollangException($"{path} expects exactly one Text prompt");
            }

            var prompt = EmitExpression(expression.Arguments[0]);
            EnsureRuntimeType(prompt, BoundType.Text, path);
            return EmitReadIntPrompt(prompt);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }

            return EmitRuntimeNowMillisIntrinsic(path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeRangeStream)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Range argument");
            }
            return EmitRuntimeRangeStream(function, EmitExpression(expression.Arguments[0]), path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeMouseEvents)
        {
            if (expression.Arguments.Count != 2)
            {
                throw new SollangException($"{path} expects capacity and overflow arguments");
            }
            return EmitRuntimeMouseEvents(
                function,
                EmitExpression(expression.Arguments[0]),
                EmitExpression(expression.Arguments[1]),
                path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeExitProcess)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Int exit code");
            }

            return EmitRuntimeExitProcessIntrinsic(EmitExpression(expression.Arguments[0]), path);
        }

        if (function.Kind == BoundFunctionKind.RuntimePathStyle)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }
            return EmitRuntimePathStyle(function);
        }

        if (function.Kind == BoundFunctionKind.RuntimeParallelWorkers)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }
            return EmitRuntimeParallelWorkersIntrinsic(path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeLimitParallelWorkers)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Int argument");
            }
            return EmitRuntimeLimitParallelWorkersIntrinsic(EmitExpression(expression.Arguments[0]), path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeParallelPeakWorkers)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }
            return EmitRuntimeParallelPeakWorkersIntrinsic(path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeSleep)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Duration argument");
            }

            return EmitRuntimeSleepIntrinsic(function, EmitExpression(expression.Arguments[0]), path);
        }

        if (function.Kind == BoundFunctionKind.RuntimeArguments)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }
            return EmitRuntimeArgumentsIntrinsic();
        }

        if (function.Kind == BoundFunctionKind.RuntimeEnvironment)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Text name");
            }
            return EmitRuntimeEnvironmentIntrinsic(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind is BoundFunctionKind.RuntimeBorrowSourceText
            or BoundFunctionKind.RuntimeMapSourceText)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Text value");
            }
            var sourceArgument = EmitExpression(expression.Arguments[0]);
            return function.Kind == BoundFunctionKind.RuntimeBorrowSourceText
                ? EmitBorrowSourceText(sourceArgument)
                : EmitMapSourceText(sourceArgument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeMapSourcePath)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Path value");
            }
            return EmitMapSourcePath(EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind is BoundFunctionKind.RuntimeOpenFile
            or BoundFunctionKind.RuntimeOpenWriteFile)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Text path");
            }
            return EmitRuntimeOpenFile(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadDirectory)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Path argument");
            }
            return EmitRuntimeReadDirectory(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind == BoundFunctionKind.RuntimePathQuery)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Path argument");
            }
            return EmitRuntimePathQuery(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind is BoundFunctionKind.RuntimeSyncFile
            or BoundFunctionKind.RuntimeAtomicReplaceFile)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one argument");
            }
            var fileArgument = EmitExpression(expression.Arguments[0]) as RuntimeStruct
                ?? throw new SollangException($"{path} expects a file runtime struct");
            return function.Kind switch
            {
                BoundFunctionKind.RuntimeSyncFile => EmitRuntimeSyncFile(fileArgument),
                _ => EmitRuntimeAtomicReplaceFile(function, fileArgument)
            };
        }

        if (function.Kind is BoundFunctionKind.RuntimeOpenFileAsync
            or BoundFunctionKind.RuntimeOpenWriteFileAsync)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one Text path");
            }
            return EmitRuntimeOpenFileAsync(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind == BoundFunctionKind.RuntimeWriteScalar)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one scalar value");
            }
            return EmitRuntimeWriteScalar(function, EmitExpression(expression.Arguments[0]));
        }

        if (function.Kind is BoundFunctionKind.RuntimeSeedRandom
            or BoundFunctionKind.RuntimeOpenIntWriter
            or BoundFunctionKind.RuntimeWriteInt
            or BoundFunctionKind.RuntimeOpenIntReader)
        {
            if (expression.Arguments.Count != 1)
            {
                throw new SollangException($"{path} expects exactly one argument");
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
                throw new SollangException($"{path} expects exactly one argument");
            }

            var runtimeArgument = EmitExpression(expression.Arguments[0]);
            return EmitRuntimeIntIntrinsic(function, runtimeArgument, path);
        }

        if (function.Kind is BoundFunctionKind.RuntimeCloseIntWriter
            or BoundFunctionKind.RuntimeCloseIntReader)
        {
            if (expression.Arguments.Count != 0)
            {
                throw new SollangException($"{path} does not accept arguments");
            }

            EmitRuntimeUnitIntrinsic(function, argument: null, path);
            return RuntimeUnit.Instance;
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SollangException($"unsupported runtime function kind '{function.Kind}'");
        }

        RuntimeValue? argument = null;
        var additionalParameterCount = function.AdditionalParameters?.Count ?? 0;
        if (methodReceiverName is not null)
        {
            if (expression.Arguments.Count != additionalParameterCount)
            {
                throw new SollangException(
                    $"method '{path}' expects {additionalParameterCount} additional argument(s)");
            }

            argument = ResolveLocal(methodReceiverName);
            EnsureFunctionArgumentRuntimeType(argument, function.InputType!.Value, path);
        }
        else if (function.InputType is null)
        {
            if (expression.Arguments.Count != additionalParameterCount)
            {
                throw new SollangException(
                    $"function '{path}' expects {additionalParameterCount} argument(s)");
            }
        }
        else
        {
            if (expression.Arguments.Count != 1 + additionalParameterCount)
            {
                throw new SollangException(
                    $"function '{path}' expects {1 + additionalParameterCount} argument(s)");
            }

            if (function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow)
            {
                argument = CreateMutableBorrowArgument(
                    expression.Arguments[0],
                    function.InputType!.Value,
                    path,
                    function.InputName ?? "it");
            }
            else if (_program.Types.IsReference(function.InputType.Value))
            {
                argument = CreateReadonlyReferenceArgument(
                    expression.Arguments[0],
                    function.InputType.Value,
                    path);
            }
            else
            {
                argument = EmitFunctionArgumentExpression(
                    expression.Arguments[0],
                    function.InputType.Value);
                EnsureFunctionArgumentRuntimeType(argument, function.InputType.Value, path);
            }
        }

        var additionalArgumentOffset = methodReceiverName is not null || function.InputType is null ? 0 : 1;
        var additionalArguments = (function.AdditionalParameters ?? [])
            .Select((parameter, index) =>
            {
                var argumentExpression = expression.Arguments[additionalArgumentOffset + index];
                var value = parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow
                    ? CreateMutableBorrowArgument(argumentExpression, parameter.Type, path, parameter.Name)
                    : _program.Types.IsReference(parameter.Type)
                        ? CreateReadonlyReferenceArgument(argumentExpression, parameter.Type, path)
                    : EmitFunctionArgumentExpression(argumentExpression, parameter.Type);
                EnsureFunctionArgumentRuntimeType(value, parameter.Type, path);
                return value;
            })
            .ToArray();
        var value = EmitFunctionCall(function, argument, additionalArguments);
        RemoveOwnedParameterArgumentsIfNeeded(function, expression.Arguments, additionalArgumentOffset);
        return value;
    }

    private RuntimeValue EmitRuntimeWrapperCall(
        CallExpression expression,
        BoundFunctionKind wrapperKind,
        string path)
    {
        if (expression.Arguments.Count != 1)
        {
            throw new SollangException($"{path} expects exactly one argument");
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
            _ => throw new SollangException($"unsupported runtime wrapper kind '{wrapperKind}'")
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

    private RuntimeValue EmitFlowFunctionCall(
        BoundFunction function,
        RuntimeValue argument,
        Expression source,
        IReadOnlyList<Expression> additionalExpressions)
    {
        if (function.InputType is null)
        {
            throw new SollangException($"function '{function.Name}' does not accept a flowed input");
        }

        var functionArgument = function.InputOwnership == BoundFunctionInputOwnership.MutableBorrow
            ? CreateMutableBorrowArgument(
                source,
                function.InputType.Value,
                function.Name,
                function.InputName ?? "it")
            : _program.Types.IsReference(function.InputType.Value)
                ? CreateReadonlyReferenceArgument(source, function.InputType.Value, function.Name)
                : argument;
        EnsureFunctionArgumentRuntimeType(functionArgument, function.InputType.Value, function.Name);
        var additionalArguments = (function.AdditionalParameters ?? [])
            .Select((parameter, index) =>
            {
                var value = parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow
                    ? CreateMutableBorrowArgument(
                        additionalExpressions[index], parameter.Type, function.Name, parameter.Name)
                    : _program.Types.IsReference(parameter.Type)
                        ? CreateReadonlyReferenceArgument(additionalExpressions[index], parameter.Type, function.Name)
                        : EmitFunctionArgumentExpression(additionalExpressions[index], parameter.Type);
                EnsureFunctionArgumentRuntimeType(value, parameter.Type, function.Name);
                return value;
            })
            .ToArray();
        var value = EmitFunctionCall(function, functionArgument, additionalArguments);
        RemoveOwnedParameterFlowArgumentsIfNeeded(function, source, additionalExpressions);
        return value;
    }

    private RuntimeValue EmitFunctionArgumentExpression(Expression expression, BoundType expectedType)
    {
        if (IsIntegerType(expectedType)
            && TryGetIntegerLiteralText(expression, out var integerLiteral))
        {
            return new RuntimeInt(expectedType, integerLiteral);
        }

        return EmitExpression(expression);
    }

    private bool TryEmitNumericConversion(CallExpression expression, out RuntimeValue value)
    {
        value = null!;
        if (expression.Path.Count != 1
            || !_program.Types.TryResolve(expression.Path[0], out var targetType)
            || !IsNumericType(targetType))
        {
            return false;
        }
        if (expression.Arguments.Count != 1)
        {
            throw new SollangException($"numeric conversion '{expression.Path[0]}' expects exactly one argument");
        }
        if (IsIntegerType(targetType)
            && TryGetIntegerLiteralText(expression.Arguments[0], out var integerLiteral))
        {
            value = new RuntimeInt(targetType, integerLiteral);
            return true;
        }
        var source = EmitExpression(expression.Arguments[0]);
        if (!IsNumericType(source.Type))
        {
            throw new SollangException($"numeric conversion '{expression.Path[0]}' expects a numeric value");
        }
        if (source is RuntimeInt checkedInteger && IsIntegerType(targetType))
        {
            EmitCheckedIntegerConversion(checkedInteger, targetType);
        }
        if (source.Type == targetType
            || (NumericBitWidth(source.Type) == NumericBitWidth(targetType)
                && IsIntegerType(source.Type) && IsIntegerType(targetType)))
        {
            value = source switch
            {
                RuntimeInt sameWidthInteger => new RuntimeInt(targetType, sameWidthInteger.ValueName),
                RuntimeFloat floating => new RuntimeFloat(targetType, floating.ValueName),
                _ => throw new SollangException("numeric conversion received a non-numeric runtime value")
            };
            return true;
        }

        var converted = NextTemp("numeric_convert");
        string instruction;
        if (source is RuntimeInt integer && IsIntegerType(targetType))
        {
            instruction = NumericBitWidth(targetType) < NumericBitWidth(source.Type)
                ? "trunc"
                : IsSignedIntegerType(source.Type) ? "sext" : "zext";
            EmitAssign(converted,
                $"{instruction} {LlvmType(source.Type)} {integer.ValueName} to {LlvmType(targetType)}");
            value = new RuntimeInt(targetType, converted);
            return true;
        }
        if (source is RuntimeInt intValue && IsFloatType(targetType))
        {
            instruction = IsSignedIntegerType(source.Type) ? "sitofp" : "uitofp";
            EmitAssign(converted,
                $"{instruction} {LlvmType(source.Type)} {intValue.ValueName} to {LlvmType(targetType)}");
            value = new RuntimeFloat(targetType, converted);
            return true;
        }
        if (source is RuntimeFloat floatValue && IsIntegerType(targetType))
        {
            instruction = IsSignedIntegerType(targetType) ? "fptosi" : "fptoui";
            EmitAssign(converted,
                $"{instruction} {LlvmType(source.Type)} {floatValue.ValueName} to {LlvmType(targetType)}");
            value = new RuntimeInt(targetType, converted);
            return true;
        }
        if (source is RuntimeFloat floatingValue && IsFloatType(targetType))
        {
            instruction = NumericBitWidth(targetType) < NumericBitWidth(source.Type) ? "fptrunc" : "fpext";
            EmitAssign(converted,
                $"{instruction} {LlvmType(source.Type)} {floatingValue.ValueName} to {LlvmType(targetType)}");
            value = new RuntimeFloat(targetType, converted);
            return true;
        }
        throw new SollangException("unsupported numeric conversion");
    }

    private static bool TryGetIntegerLiteralText(Expression expression, out string text)
    {
        switch (expression)
        {
            case NumberExpression number
                when !number.Text.Contains('.', StringComparison.Ordinal)
                    && !number.Text.Contains('e', StringComparison.OrdinalIgnoreCase):
                text = number.Text;
                return true;
            case NegateExpression { Value: NumberExpression number }
                when !number.Text.Contains('.', StringComparison.Ordinal)
                    && !number.Text.Contains('e', StringComparison.OrdinalIgnoreCase):
                text = "-" + number.Text;
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private void EmitCheckedIntegerConversion(RuntimeInt source, BoundType targetType)
    {
        var (sourceMin, sourceMax) = IntegerRange(source.Type);
        var (targetMin, targetMax) = IntegerRange(targetType);
        var llvmType = LlvmType(source.Type);
        var signed = IsSignedIntegerType(source.Type);
        if (sourceMin < targetMin)
        {
            var lower = NextTemp("numeric_lower_bound");
            EmitCompare(lower, signed ? "sge" : "uge", llvmType, source.ValueName,
                targetMin.ToString(CultureInfo.InvariantCulture));
            EmitTrapUnless(lower, "numeric_conversion_lower");
        }
        if (sourceMax > targetMax)
        {
            var upper = NextTemp("numeric_upper_bound");
            EmitCompare(upper, signed ? "sle" : "ule", llvmType, source.ValueName,
                targetMax.ToString(CultureInfo.InvariantCulture));
            EmitTrapUnless(upper, "numeric_conversion_upper");
        }
        if (targetType == BoundType.CodePoint && sourceMax >= 0xD800 && sourceMin <= 0xDFFF)
        {
            var below = NextTemp("codepoint_below_surrogate");
            var above = NextTemp("codepoint_above_surrogate");
            var valid = NextTemp("codepoint_not_surrogate");
            EmitCompare(below, signed ? "slt" : "ult", llvmType, source.ValueName, "55296");
            EmitCompare(above, signed ? "sgt" : "ugt", llvmType, source.ValueName, "57343");
            EmitInstruction($"{valid} = or i1 {below}, {above}");
            EmitTrapUnless(valid, "numeric_conversion_surrogate");
        }
    }

    private (BigInteger Minimum, BigInteger Maximum) IntegerRange(BoundType type)
    {
        var bits = NumericBitWidth(type);
        if (type == BoundType.CodePoint)
        {
            return (BigInteger.Zero, new BigInteger(0x10FFFF));
        }
        return IsSignedIntegerType(type)
            ? (-(BigInteger.One << (bits - 1)), (BigInteger.One << (bits - 1)) - 1)
            : (BigInteger.Zero, (BigInteger.One << bits) - 1);
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

    private RuntimeValue EmitInlineFunctionCall(
        BoundFunction function,
        RuntimeValue? argument,
        IReadOnlyList<RuntimeValue>? additionalArguments = null)
    {
        if (function.Body is null && function.ReturnType != BoundType.Unit)
        {
            throw new SollangException($"function '{function.Name}' has no body");
        }

        if (_inlineFunctionStack.Any(candidate => ReferenceEquals(candidate, function)))
        {
            throw new SollangException($"recursive inline function '{function.Name}' is not supported in the current runtime slice");
        }

        var outerLocals = CaptureLocals();
        var previousFunction = _currentFunction;
        var previousFunctions = _currentFunctions;
        _currentFunction = function;
        _currentFunctions = CreateFunctionScope(_currentFunctions, function.LocalFunctions);
        _inlineFunctionStack.Add(function);
        try
        {
            var functionLocals = CaptureLocals();
            if (function.InputType is null)
            {
                if (argument is not null)
                {
                    throw new SollangException($"function '{function.Name}' does not accept arguments");
                }
            }
            else
            {
                if (argument is null)
                {
                    throw new SollangException($"function '{function.Name}' expects exactly one argument");
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

            var parameters = function.AdditionalParameters ?? [];
            additionalArguments ??= [];
            if (parameters.Count != additionalArguments.Count)
            {
                throw new SollangException(
                    $"function '{function.Name}' expects {parameters.Count} additional argument(s)");
            }
            for (var index = 0; index < parameters.Count; index++)
            {
                var parameter = parameters[index];
                var parameterValue = additionalArguments[index];
                EnsureFunctionArgumentRuntimeType(parameterValue, parameter.Type, function.Name);
                if (parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow)
                {
                    BindInlineMutableBorrowFunctionParameter(
                        parameter.Name, parameter.Type, parameterValue, function.Name);
                    continue;
                }

                _locals[parameter.Name] = parameter.Type switch
                {
                    BoundType.IntSlice => CreateRuntimeIntSlice(parameterValue),
                    BoundType.IntDictionaryView => CreateRuntimeIntDictionaryView(parameterValue),
                    _ => parameterValue
                };
                if (parameter.Ownership == BoundFunctionInputOwnership.Default
                    && _program.Types.ContainsOwnedStorage(parameter.Type))
                {
                    _borrowedOwnedLocals.Add(parameter.Name);
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
            _currentFunction = previousFunction;
            _currentFunctions = previousFunctions;
            RestoreLocals(outerLocals);
        }
    }

    private RuntimeValue EmitFunctionCall(
        BoundFunction function,
        RuntimeValue? argument,
        IReadOnlyList<RuntimeValue>? additionalArguments = null)
    {
        if (function.Kind is BoundFunctionKind.RuntimePrint or BoundFunctionKind.RuntimePrintLine)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Text value");
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
                throw new SollangException($"{function.Name} expects exactly one Text prompt");
            }

            EnsureRuntimeType(argument, BoundType.Text, function.Name);
            return EmitReadIntPrompt(argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeNowMillis)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }

            return EmitRuntimeNowMillisIntrinsic(function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeRangeStream)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Range argument");
            }
            return EmitRuntimeRangeStream(function, argument, function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeMouseEvents)
        {
            if (argument is null || additionalArguments is not { Count: 1 })
            {
                throw new SollangException(
                    $"{function.Name} expects capacity and overflow arguments");
            }
            return EmitRuntimeMouseEvents(
                function,
                argument,
                additionalArguments[0],
                function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimePathStyle)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimePathStyle(function);
        }

        if (function.Kind == BoundFunctionKind.RuntimeParallelWorkers)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimeParallelWorkersIntrinsic(function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeLimitParallelWorkers)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Int argument");
            }
            return EmitRuntimeLimitParallelWorkersIntrinsic(argument, function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeParallelPeakWorkers)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimeParallelPeakWorkersIntrinsic(function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeSleep)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Duration argument");
            }

            return EmitRuntimeSleepIntrinsic(function, argument, function.Name);
        }

        if (function.Kind == BoundFunctionKind.RuntimeArguments)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimeArgumentsIntrinsic();
        }

        if (function.Kind == BoundFunctionKind.RuntimeEnvironment)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Text name");
            }
            return EmitRuntimeEnvironmentIntrinsic(function, argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeRunProcess)
        {
            if (argument is not RuntimeDynamicInlineArray argv)
            {
                throw new SollangException($"{function.Name} expects a dynamic Text argv array");
            }
            return EmitRuntimeRunProcessIntrinsic(function, argv);
        }

        if (function.Kind == BoundFunctionKind.RuntimeRunProcessToFile)
        {
            if (argument is not RuntimeStruct request)
            {
                throw new SollangException($"{function.Name} expects a RunToFileRequest");
            }
            return EmitRuntimeRunProcessToFileIntrinsic(function, request);
        }

        if (function.Kind == BoundFunctionKind.RuntimeExitProcess)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Int exit code");
            }
            return EmitRuntimeExitProcessIntrinsic(argument, function.Name);
        }

        if (function.Kind is BoundFunctionKind.RuntimeBorrowSourceText
            or BoundFunctionKind.RuntimeMapSourceText)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Text value");
            }
            return function.Kind == BoundFunctionKind.RuntimeBorrowSourceText
                ? EmitBorrowSourceText(argument)
                : EmitMapSourceText(argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeMapSourcePath)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Path value");
            }
            return EmitMapSourcePath(argument);
        }

        if (function.Kind is BoundFunctionKind.RuntimeOpenFile
            or BoundFunctionKind.RuntimeOpenWriteFile)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Text path");
            }
            return EmitRuntimeOpenFile(function, argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadDirectory)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Path value");
            }
            return EmitRuntimeReadDirectory(function, argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimePathQuery)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Path value");
            }
            return EmitRuntimePathQuery(function, argument);
        }

        if (function.Kind is BoundFunctionKind.RuntimeSyncFile
            or BoundFunctionKind.RuntimeAtomicReplaceFile)
        {
            if (argument is not RuntimeStruct fileArgument)
            {
                throw new SollangException($"{function.Name} expects a file runtime struct");
            }
            return function.Kind switch
            {
                BoundFunctionKind.RuntimeSyncFile => EmitRuntimeSyncFile(fileArgument),
                _ => EmitRuntimeAtomicReplaceFile(function, fileArgument)
            };
        }

        if (function.Kind is BoundFunctionKind.RuntimeOpenFileAsync
            or BoundFunctionKind.RuntimeOpenWriteFileAsync)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one Text path");
            }
            return EmitRuntimeOpenFileAsync(function, argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeWriteScalar)
        {
            if (argument is null)
            {
                throw new SollangException($"{function.Name} expects exactly one scalar value");
            }
            return EmitRuntimeWriteScalar(function, argument);
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadScalar)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimeReadScalar(function);
        }

        if (function.Kind == BoundFunctionKind.RuntimeReadScalarAsync)
        {
            if (argument is not null)
            {
                throw new SollangException($"{function.Name} does not accept an argument");
            }
            return EmitRuntimeReadScalarAsync(function);
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
                throw new SollangException($"{function.Name} expects exactly one Int argument");
            }

            return EmitRuntimeIntIntrinsic(function, argument, function.Name);
        }

        if (function.Kind != BoundFunctionKind.User)
        {
            throw new SollangException($"function '{function.Name}' does not produce a runtime value");
        }

        if (function.Kind == BoundFunctionKind.UserBlock
            || (function.IsStandardLibrary && !_standaloneStandardLibraryFunctions.Contains(function)))
        {
            return EmitInlineFunctionCall(function, argument, additionalArguments);
        }

        if (function.IsAsync)
        {
            return EmitAsyncFunctionCall(function, argument, additionalArguments);
        }

        if (IsNumericType(function.ReturnType))
        {
            return EmitNumericFunctionCall(function, argument, additionalArguments);
        }

        return function.ReturnType switch
        {
            BoundType.Unit => EmitUnitFunctionCall(function, argument, additionalArguments),
            BoundType.Text => EmitTextFunctionCall(function, argument, additionalArguments),
            BoundType.Int => EmitIntFunctionCall(function, argument, additionalArguments),
            BoundType.Bool => EmitBoolFunctionCall(function, argument, additionalArguments),
            BoundType.DynamicIntArray => EmitDynamicIntArrayFunctionCall(function, argument, additionalArguments),
            _ when _program.Types.IsDynamicArray(function.ReturnType) => EmitDynamicInlineArrayFunctionCall(function, argument, additionalArguments),
            BoundType.IntDictionary => EmitIntDictionaryFunctionCall(function, argument, additionalArguments),
            BoundType.Arena => EmitArenaFunctionCall(function, argument, additionalArguments),
            _ when _program.Types.IsDictionary(function.ReturnType) => EmitInlineDictionaryFunctionCall(function, argument, additionalArguments),
            _ when _program.Types.IsReference(function.ReturnType) => EmitReferenceFunctionCall(function, argument, additionalArguments),
            _ when _program.Types.IsStruct(function.ReturnType)
                || _program.Types.IsEnum(function.ReturnType)
                || _program.Types.IsBox(function.ReturnType)
                => EmitStructFunctionCall(function, argument, additionalArguments),
            _ when _program.Types.IsStream(function.ReturnType)
                || _program.Types.IsEventStream(function.ReturnType)
                => EmitStructFunctionCall(function, argument, additionalArguments),
            _ => throw new SollangException($"unsupported function return type {function.ReturnType}")
        };
    }

    private RuntimeValue EmitStructFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var value = NextTemp("struct_call");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        var llvmType = LlvmType(function.ReturnType);
        EmitCall(value, llvmType, SymbolForFunction(function)[1..], arguments);
        return DematerializeAggregateValue(function.ReturnType, value);
    }

    private RuntimeReference EmitReferenceFunctionCall(
        BoundFunction function,
        RuntimeValue? argument,
        IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var pointer = NextTemp("ref_call");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(pointer, "ptr", SymbolForFunction(function)[1..], arguments);
        var elementType = _program.Types.GetReference(function.ReturnType).ElementType;
        return new RuntimeReference(function.ReturnType, elementType, pointer);
    }

    private RuntimeUnit EmitUnitFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(target: null, "void", SymbolForFunction(function)[1..], arguments);
        return RuntimeUnit.Instance;
    }

    private RuntimeText EmitTextFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("text");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(aggregate, "%sollang.text", SymbolForFunction(function)[1..], arguments);

        var pointer = NextTemp("text_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.text {aggregate}, 0");

        var length = NextTemp("text_len");
        EmitAssign(length, $"extractvalue %sollang.text {aggregate}, 1");

        return new RuntimeText(pointer, length);
    }

    private RuntimeInt EmitIntFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(value, "i64", SymbolForFunction(function)[1..], arguments);
        return new RuntimeInt(value);
    }

    private RuntimeValue EmitNumericFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var value = NextTemp("numeric_call");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(value, LlvmType(function.ReturnType), SymbolForFunction(function)[1..], arguments);
        return IsIntegerType(function.ReturnType)
            ? new RuntimeInt(function.ReturnType, value)
            : new RuntimeFloat(function.ReturnType, value);
    }

    private RuntimeBool EmitBoolFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var value = NextTemp("call");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(value, "i1", SymbolForFunction(function)[1..], arguments);
        return new RuntimeBool(value);
    }

    private RuntimeDynamicIntArray EmitDynamicIntArrayFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("array");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(aggregate, "%sollang.dynamic_int_array", SymbolForFunction(function)[1..], arguments);

        var pointer = NextTemp("array_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.dynamic_int_array {aggregate}, 0");

        var length = NextTemp("array_len");
        EmitAssign(length, $"extractvalue %sollang.dynamic_int_array {aggregate}, 1");

        var capacity = NextTemp("array_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.dynamic_int_array {aggregate}, 2");

        return new RuntimeDynamicIntArray(pointer, length, capacity);
    }

    private RuntimeDynamicInlineArray EmitDynamicInlineArrayFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("generic_array");
        EmitCall(aggregate, "%sollang.dynamic_int_array", SymbolForFunction(function)[1..],
            FunctionCallArgumentList(function, argument, additionalArguments));
        var pointer = NextTemp("generic_array_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.dynamic_int_array {aggregate}, 0");
        var length = NextTemp("generic_array_len");
        EmitAssign(length, $"extractvalue %sollang.dynamic_int_array {aggregate}, 1");
        var capacity = NextTemp("generic_array_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.dynamic_int_array {aggregate}, 2");
        var definition = _program.Types.GetDynamicArray(function.ReturnType);
        return new RuntimeDynamicInlineArray(function.ReturnType, definition.ElementType,
            pointer, length, capacity);
    }

    private RuntimeIntDictionary EmitIntDictionaryFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("dict");
        var arguments = FunctionCallArgumentList(function, argument, additionalArguments);
        EmitCall(aggregate, "%sollang.int_dictionary", SymbolForFunction(function)[1..], arguments);

        var pointer = NextTemp("dict_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.int_dictionary {aggregate}, 0");

        var length = NextTemp("dict_len");
        EmitAssign(length, $"extractvalue %sollang.int_dictionary {aggregate}, 1");

        var capacity = NextTemp("dict_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.int_dictionary {aggregate}, 2");

        return new RuntimeIntDictionary(pointer, length, capacity);
    }

    private RuntimeInlineDictionary EmitInlineDictionaryFunctionCall(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var aggregate = NextTemp("generic_dict");
        EmitCall(aggregate, "%sollang.int_dictionary", SymbolForFunction(function)[1..],
            FunctionCallArgumentList(function, argument, additionalArguments));
        var pointer = NextTemp("generic_dict_ptr");
        EmitAssign(pointer, $"extractvalue %sollang.int_dictionary {aggregate}, 0");
        var length = NextTemp("generic_dict_len");
        EmitAssign(length, $"extractvalue %sollang.int_dictionary {aggregate}, 1");
        var capacity = NextTemp("generic_dict_capacity");
        EmitAssign(capacity, $"extractvalue %sollang.int_dictionary {aggregate}, 2");
        var definition = _program.Types.GetDictionary(function.ReturnType);
        return new RuntimeInlineDictionary(function.ReturnType, definition.KeyType, definition.ValueType,
            pointer, length, capacity);
    }

    private string FunctionCallArgumentList(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments = null)
    {
        const string runtimeContext = "ptr %stdin, ptr %stdout, ptr %written, ptr %read, ptr %ok_state";
        var explicitArguments = string.Join(", ", new[]
            {
                CaptureFunctionCallArgumentList(function),
                ExplicitFunctionCallArgumentList(function, argument, additionalArguments)
            }
            .Where(static part => part.Length > 0));
        return explicitArguments.Length == 0
            ? runtimeContext
            : $"{runtimeContext}, {explicitArguments}";
    }

    private string CaptureFunctionCallArgumentList(BoundFunction function)
    {
        var arguments = new List<string>();
        foreach (var capture in CapturedBindingsForFunction(function))
        {
            var value = ResolveLocal(capture.Key);
            EnsureRuntimeType(value, capture.Value, function.Name);
            var materialized = MaterializeAggregateValue(value);
            if (CaptureUsesBorrowAbi(capture.Value))
            {
                if (_readonlyCaptureBorrowPointers.TryGetValue(capture.Key, out var existingPointer))
                {
                    arguments.Add($"ptr {existingPointer}");
                    continue;
                }
                var pointer = NextTemp("capture_borrow_arg");
                EmitAlloca(pointer, materialized.TypeName, RuntimeAlignment(capture.Value));
                EmitStore(
                    materialized.TypeName,
                    materialized.ValueName,
                    pointer,
                    RuntimeAlignment(capture.Value));
                if (!_mutableLocals.Contains(capture.Key))
                {
                    _readonlyCaptureBorrowPointers.Add(capture.Key, pointer);
                }
                arguments.Add($"ptr {pointer}");
                continue;
            }
            arguments.Add($"{materialized.TypeName} {materialized.ValueName}");
        }

        return string.Join(", ", arguments);
    }

    private string ExplicitFunctionCallArgumentList(BoundFunction function, RuntimeValue? argument, IReadOnlyList<RuntimeValue>? additionalArguments)
    {
        var parts = new List<string>();
        var primary = ExplicitPrimaryFunctionCallArgument(function, argument);
        if (primary.Length > 0)
        {
            parts.Add(primary);
        }

        var parameters = function.AdditionalParameters ?? [];
        additionalArguments ??= [];
        if (additionalArguments.Count != parameters.Count)
        {
            throw new SollangException(
                $"function '{function.Name}' expects {parameters.Count} additional argument(s)");
        }
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var value = additionalArguments[index];
            EnsureFunctionArgumentRuntimeType(value, parameter.Type, function.Name);
            if (parameter.Ownership == BoundFunctionInputOwnership.MutableBorrow)
            {
                parts.Add(value switch
                {
                    RuntimeMutableStructReference reference => $"ptr {reference.PointerAddress}",
                    RuntimeMutableContainerReference reference => BuildMutableContainerArgument(reference),
                    _ => throw new SollangException(
                        $"function '{function.Name}' parameter '{parameter.Name}' requires a mutable borrow")
                });
                continue;
            }
            if (_program.Types.IsReference(parameter.Type))
            {
                if (value is not RuntimeReference reference || reference.Type != parameter.Type)
                {
                    throw new SollangException(
                        $"function '{function.Name}' parameter '{parameter.Name}' requires {parameter.Type}");
                }
                parts.Add($"ptr {reference.PointerName}");
                continue;
            }
            var materialized = MaterializeAggregateValue(value);
            parts.Add($"{materialized.TypeName} {materialized.ValueName}");
        }
        return string.Join(", ", parts);
    }

    private string ExplicitPrimaryFunctionCallArgument(BoundFunction function, RuntimeValue? argument)
    {
        if (function.InputType is null)
        {
            if (argument is not null)
            {
                throw new SollangException($"function '{function.Name}' does not accept arguments");
            }

            return "";
        }

        if (argument is null)
        {
            throw new SollangException($"function '{function.Name}' expects exactly one argument");
        }

        if (function.HasValueGenericFixedArrayInput)
        {
            var (pointer, length, actualType) = argument switch
            {
                RuntimeStaticIntArray array => (array.PointerName, array.LengthName, BoundType.StaticIntArray),
                RuntimeStaticTextArray array => (array.PointerName, array.LengthName, BoundType.StaticTextArray),
                RuntimeStaticInlineArray array => (array.PointerName, array.LengthName, array.ArrayType),
                _ => throw new SollangException(
                    $"value-generic function '{function.Name}' requires a fixed array input")
            };
            if (function.InputType != actualType)
            {
                throw new SollangException(
                    $"function '{function.Name}' expects {function.InputType!.Value} "
                    + $"but received {actualType}");
            }
            if (function.SpecializedValue is not { } expectedLength
                || !int.TryParse(length, out var actualLength)
                || actualLength != expectedLength)
            {
                throw new SollangException(
                    $"function '{function.Name}' expects [{StaticArrayElementType(actualType)}; {function.SpecializedValue}] "
                    + $"but received [{StaticArrayElementType(actualType)}; {length}]");
            }
            return BuildIntSliceArgument(pointer, length);
        }

        if (_program.Types.IsReference(function.InputType.Value))
        {
            if (argument is not RuntimeReference reference || reference.Type != function.InputType.Value)
            {
                throw new SollangException(
                    $"function '{function.Name}' expects {function.InputType.Value} but received {argument.Type}");
            }
            return $"ptr {reference.PointerName}";
        }

        if (argument is RuntimeDynamicInlineArray inlineArray
            && function.InputType == inlineArray.ArrayType)
        {
            return $"%sollang.dynamic_int_array {BuildDynamicArrayAggregate(inlineArray.PointerName, inlineArray.LengthName, inlineArray.CapacityName)}";
        }
        if (argument is RuntimeInlineDictionary inlineDictionary
            && function.InputType == inlineDictionary.DictionaryType)
        {
            return $"%sollang.int_dictionary {BuildDictionaryAggregate(inlineDictionary.PointerName, inlineDictionary.LengthName, inlineDictionary.CapacityName)}";
        }

        if (argument is RuntimeInt numericInteger && function.InputType == numericInteger.Type)
        {
            return $"{LlvmType(numericInteger.Type)} {numericInteger.ValueName}";
        }
        if (argument is RuntimeFloat numericFloat && function.InputType == numericFloat.Type)
        {
            return $"{LlvmType(numericFloat.Type)} {numericFloat.ValueName}";
        }

        return argument switch
        {
            RuntimeText text when function.InputType == BoundType.Text =>
                $"%sollang.text {BuildTextAggregate(text)}",
            RuntimeSourceText source when function.InputType == BoundType.SourceText =>
                $"%sollang.source_text {BuildSourceTextAggregate(source)}",
            RuntimeMappedBytes mapped when function.InputType == mapped.Type =>
                $"%sollang.mapped_bytes {BuildMappedBytesAggregate(mapped)}",
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
            RuntimeArena arena when function.InputType == BoundType.Arena =>
                $"%sollang.dynamic_int_array {BuildDynamicArrayAggregate(arena.PointerName, arena.UsedName, arena.CapacityName)}",
            RuntimeStruct structure when function.InputType == structure.Type => $"{LlvmStructType(structure.Type)} {structure.ValueName}",
            RuntimeEnum enumeration when function.InputType == enumeration.Type => $"{LlvmEnumType(enumeration.Type)} {enumeration.ValueName}",
            RuntimeBox box when function.InputType == box.Type => $"ptr {box.PointerName}",
            RuntimeProducerStream stream when function.InputType == stream.Type =>
                BuildProducerStreamArgument(stream),
            _ => throw new SollangException($"function '{function.Name}' expects {function.InputType} but received {argument.Type}")
        };
    }

    private string BuildProducerStreamArgument(RuntimeProducerStream stream)
    {
        var materialized = MaterializeAggregateValue(stream);
        return $"{materialized.TypeName} {materialized.ValueName}";
    }

    private BoundType StaticArrayElementType(BoundType arrayType)
    {
        if (arrayType == BoundType.StaticIntArray) return BoundType.Int;
        if (arrayType == BoundType.StaticTextArray) return BoundType.Text;
        return _program.Types.GetStaticArray(arrayType).ElementType;
    }

    private RuntimeValue CreateMutableBorrowArgument(
        Expression argument,
        BoundType expectedType,
        string path,
        string parameterName)
    {
        if (argument is not NameExpression name)
        {
            throw new SollangException($"function '{path}' mutably borrows a value, so the argument must be a named mutable owner");
        }

        if (!_mutableLocals.Contains(name.Name))
        {
            throw new SollangException($"function '{path}' mutably borrows a value; use a mutable owner binding such as '{name.Name.TrimEnd('!')}!'");
        }

        var value = ResolveLocal(name.Name);
        EnsureRuntimeType(value, expectedType, path);
        if (_mutableStructSlots.TryGetValue(name.Name, out var structPointer))
        {
            return new RuntimeMutableStructReference(expectedType, structPointer);
        }

        if (!_mutableContainerSlots.TryGetValue(name.Name, out var slot))
        {
            throw new SollangException($"mutable owner '{name.Name}' has no addressable storage");
        }

        return new RuntimeMutableContainerReference(
            expectedType,
            slot.PointerAddress,
            slot.LengthAddress,
            slot.CapacityAddress);
    }

    private RuntimeReference CreateReadonlyReferenceArgument(
        Expression expression,
        BoundType referenceType,
        string path)
    {
        var elementType = _program.Types.GetReference(referenceType).ElementType;
        if (expression is not NameExpression)
        {
            var place = EmitReferencePlace(expression);
            if (place.ElementType != elementType)
            {
                throw new SollangException(
                    $"function '{path}' expects a reference to {elementType} but received {place.ElementType}");
            }
            return new RuntimeReference(referenceType, elementType, place.PointerName);
        }

        var name = (NameExpression)expression;
        if (!_locals.TryGetValue(name.Name, out var stored))
        {
            throw new SollangException(
                $"function '{path}' requires a named addressable owner or reference");
        }
        if (stored is RuntimeReference reference)
        {
            if (reference.ElementType != elementType)
            {
                throw new SollangException(
                    $"function '{path}' expects a reference to {elementType} but received {reference.ElementType}");
            }
            return new RuntimeReference(referenceType, elementType, reference.PointerName);
        }
        if (stored.Type != elementType)
        {
            throw new SollangException(
                $"function '{path}' expects a reference to {elementType} but received {stored.Type}");
        }
        if (_mutableStructSlots.TryGetValue(name.Name, out var mutablePointer))
        {
            return new RuntimeReference(referenceType, elementType, mutablePointer);
        }
        if (_readonlyCaptureBorrowPointers.TryGetValue(name.Name, out var capturePointer))
        {
            return new RuntimeReference(referenceType, elementType, capturePointer);
        }

        var materialized = MaterializeAggregateValue(stored);
        var pointer = NextTemp("ref_arg");
        EmitAlloca(pointer, materialized.TypeName, RuntimeAlignment(elementType));
        EmitStore(
            materialized.TypeName,
            materialized.ValueName,
            pointer,
            RuntimeAlignment(elementType));
        return new RuntimeReference(referenceType, elementType, pointer);
    }

    private void BindInlineMutableBorrowFunctionParameter(BoundFunction function, RuntimeValue argument)
    {
        BindInlineMutableBorrowFunctionParameter(
            function.InputName ?? "it",
            function.InputType!.Value,
            argument,
            function.Name);
    }

    private void BindInlineMutableBorrowFunctionParameter(
        string name,
        BoundType type,
        RuntimeValue argument,
        string functionName)
    {
        if (argument is RuntimeMutableStructReference structReference)
        {
            _locals[name] = new RuntimeStruct(structReference.TargetType, "");
            _mutableLocals.Add(name);
            _borrowedMutableLocals.Add(name);
            _mutableStructSlots[name] = structReference.PointerAddress;
            return;
        }

        if (argument is not RuntimeMutableContainerReference reference)
        {
            throw new SollangException($"function '{functionName}' expects a mutable borrow argument");
        }

        _locals[name] = type switch
        {
            BoundType.DynamicIntArray => new RuntimeDynamicIntArray("", "", ""),
            _ when _program.Types.IsDynamicArray(reference.TargetType) => CreateEmptyRuntimeDynamicInlineArray(reference.TargetType),
            BoundType.IntDictionary => new RuntimeIntDictionary("", "", ""),
            BoundType.Arena => new RuntimeArena("", "", ""),
            _ when _program.Types.IsDictionary(reference.TargetType) => CreateEmptyRuntimeInlineDictionary(reference.TargetType),
            _ => throw new SollangException("unsupported mutable borrow input type")
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
        EmitAssign(aggregate0, $"insertvalue %sollang.mutable_container poison, ptr {reference.PointerAddress}, 0");
        var aggregate1 = NextTemp("mutable_arg");
        EmitAssign(aggregate1, $"insertvalue %sollang.mutable_container {aggregate0}, ptr {reference.LengthAddress}, 1");
        var aggregate2 = NextTemp("mutable_arg");
        EmitAssign(aggregate2, $"insertvalue %sollang.mutable_container {aggregate1}, ptr {reference.CapacityAddress}, 2");
        return $"%sollang.mutable_container {aggregate2}";
    }

    private string BuildStaticIntArraySliceArgument(RuntimeStaticIntArray array)
    {
        var pointer = NextTemp("slice_ptr");
        EmitAssign(pointer, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {array.PointerName}, i64 0, i64 0");
        return BuildIntSliceArgument(pointer, array.LengthName);
    }

    private RuntimeIntSlice CreateRuntimeIntSlice(RuntimeValue value)
    {
        return value switch
        {
            RuntimeIntSlice slice => slice,
            RuntimeDynamicIntArray array => new RuntimeIntSlice(array.PointerName, array.LengthName),
            RuntimeStaticIntArray array => CreateRuntimeIntSlice(array),
            _ => throw new SollangException($"expected Int array view but received {value.Type}")
        };
    }

    private RuntimeIntSlice CreateRuntimeIntSlice(RuntimeStaticIntArray array)
    {
        var pointer = NextTemp("slice_ptr");
        EmitAssign(pointer, $"getelementptr inbounds [{array.AllocatedLength.ToString(CultureInfo.InvariantCulture)} x i32], ptr {array.PointerName}, i64 0, i64 0");
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
            _ => throw new SollangException($"expected Int dictionary view but received {value.Type}")
        };
    }

    private string BuildIntSliceArgument(string pointer, string length)
    {
        var aggregate0 = NextTemp("slice_arg");
        EmitAssign(aggregate0, $"insertvalue %sollang.int_slice poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("slice_arg");
        EmitAssign(aggregate1, $"insertvalue %sollang.int_slice {aggregate0}, i64 {length}, 1");
        return $"%sollang.int_slice {aggregate1}";
    }

    private string BuildDynamicIntArrayArgument(RuntimeDynamicIntArray array)
    {
        var aggregate0 = NextTemp("array_arg");
        EmitAssign(aggregate0, $"insertvalue %sollang.dynamic_int_array poison, ptr {array.PointerName}, 0");
        var aggregate1 = NextTemp("array_arg");
        EmitAssign(aggregate1, $"insertvalue %sollang.dynamic_int_array {aggregate0}, i64 {array.LengthName}, 1");
        var aggregate2 = NextTemp("array_arg");
        EmitAssign(aggregate2, $"insertvalue %sollang.dynamic_int_array {aggregate1}, i64 {array.CapacityName}, 2");
        return $"%sollang.dynamic_int_array {aggregate2}";
    }

    private string BuildDynamicArrayAggregate(string pointer, string length, string capacity)
    {
        var aggregate0 = NextTemp("generic_array_value");
        EmitAssign(aggregate0, $"insertvalue %sollang.dynamic_int_array poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("generic_array_value");
        EmitAssign(aggregate1, $"insertvalue %sollang.dynamic_int_array {aggregate0}, i64 {length}, 1");
        var aggregate2 = NextTemp("generic_array_value");
        EmitAssign(aggregate2, $"insertvalue %sollang.dynamic_int_array {aggregate1}, i64 {capacity}, 2");
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
        EmitAssign(aggregate0, $"insertvalue %sollang.int_dictionary poison, ptr {pointer}, 0");
        var aggregate1 = NextTemp("dict_arg");
        EmitAssign(aggregate1, $"insertvalue %sollang.int_dictionary {aggregate0}, i64 {length}, 1");
        var aggregate2 = NextTemp("dict_arg");
        EmitAssign(aggregate2, $"insertvalue %sollang.int_dictionary {aggregate1}, i64 {capacity}, 2");
        return $"%sollang.int_dictionary {aggregate2}";
    }

    private RuntimeInlineDictionary CreateEmptyRuntimeInlineDictionary(BoundType type)
    {
        var definition = _program.Types.GetDictionary(type);
        return new RuntimeInlineDictionary(type, definition.KeyType, definition.ValueType, "", "", "");
    }

    private void RemoveOwnedParameterArgumentsIfNeeded(
        BoundFunction function,
        IReadOnlyList<Expression> arguments,
        int additionalArgumentOffset)
    {
        if (FunctionConsumesOwnedHeapInput(function)
            && function.InputType is not null
            && additionalArgumentOffset > 0
            && arguments[0] is NameExpression primaryName)
        {
            RemoveLocal(primaryName.Name);
        }

        var parameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].Ownership == BoundFunctionInputOwnership.Move
                && arguments[additionalArgumentOffset + index] is NameExpression name)
            {
                RemoveLocal(name.Name);
            }
        }
    }

    private void RemoveOwnedParameterFlowArgumentsIfNeeded(
        BoundFunction function,
        Expression source,
        IReadOnlyList<Expression> additionalArguments)
    {
        if (FunctionConsumesOwnedHeapInput(function) && source is NameExpression primaryName)
        {
            RemoveLocal(primaryName.Name);
        }

        var parameters = function.AdditionalParameters ?? [];
        for (var index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].Ownership == BoundFunctionInputOwnership.Move
                && additionalArguments[index] is NameExpression name)
            {
                RemoveLocal(name.Name);
            }
        }
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
            throw new SollangException($"function '{path}' expects {expected} but received {value.Type}");
        }
    }

    private void EnsureFunctionArgumentRuntimeType(RuntimeValue value, BoundType expected, string path)
    {
        if (_program.Types.IsReference(expected)
            && value is RuntimeReference reference
            && reference.Type == expected)
        {
            return;
        }
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

    private void CollectStandaloneStandardLibraryFunctions()
    {
        var visited = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        ScanStatementsForStandaloneStandardLibraryFunctions(
            _program.MainStatements,
            _program.Functions,
            caller: null,
            visited);

        foreach (var function in _program.Functions.Values
                     .Where(function => !function.IsStandardLibrary))
        {
            ScanFunctionForStandaloneStandardLibraryFunctions(function, visited);
        }
    }

    private void ScanFunctionForStandaloneStandardLibraryFunctions(
        BoundFunction function,
        HashSet<BoundFunction> visited)
    {
        if (!visited.Add(function))
        {
            return;
        }

        var scope = FunctionScope(function);
        ScanStatementsForStandaloneStandardLibraryFunctions(function.BlockBody, scope, function, visited);
        if (function.Body is not null)
        {
            ScanExpressionForStandaloneStandardLibraryFunctions(function.Body, scope, function, visited);
        }
    }

    private void ScanStatementsForStandaloneStandardLibraryFunctions(
        IEnumerable<Statement> statements,
        IReadOnlyDictionary<string, BoundFunction> scope,
        BoundFunction? caller,
        HashSet<BoundFunction> visited)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BindingStatement binding:
                    ScanExpressionForStandaloneStandardLibraryFunctions(binding.Value, scope, caller, visited);
                    break;
                case IndexAssignmentStatement assignment:
                    ScanExpressionForStandaloneStandardLibraryFunctions(assignment.Index, scope, caller, visited);
                    ScanExpressionForStandaloneStandardLibraryFunctions(assignment.Value, scope, caller, visited);
                    break;
                case FieldAssignmentStatement assignment:
                    ScanExpressionForStandaloneStandardLibraryFunctions(assignment.Value, scope, caller, visited);
                    break;
                case BlockFunctionCallStatement block:
                    ScanExpressionForStandaloneStandardLibraryFunctions(block.Source, scope, caller, visited);
                    ScanStatementsForStandaloneStandardLibraryFunctions(block.Body, scope, caller, visited);
                    break;
                case BlockFunctionPipelineStatement pipeline:
                    foreach (var block in pipeline.Calls)
                    {
                        ScanExpressionForStandaloneStandardLibraryFunctions(block.Source, scope, caller, visited);
                        ScanStatementsForStandaloneStandardLibraryFunctions(block.Body, scope, caller, visited);
                    }
                    break;
                case ExpressionStatement expression:
                    ScanExpressionForStandaloneStandardLibraryFunctions(expression.Expression, scope, caller, visited);
                    break;
                case GuardLoopControlStatement guard:
                    ScanExpressionForStandaloneStandardLibraryFunctions(guard.Condition, scope, caller, visited);
                    break;
                case ReturnStatement { Value: { } value }:
                    ScanExpressionForStandaloneStandardLibraryFunctions(value, scope, caller, visited);
                    break;
            }
        }
    }

    private void ScanBlockForStandaloneStandardLibraryFunctions(
        BlockBody? block,
        IReadOnlyDictionary<string, BoundFunction> scope,
        BoundFunction? caller,
        HashSet<BoundFunction> visited)
    {
        if (block is null)
        {
            return;
        }

        ScanStatementsForStandaloneStandardLibraryFunctions(block.Statements, scope, caller, visited);
        if (block.Value is not null)
        {
            ScanExpressionForStandaloneStandardLibraryFunctions(block.Value, scope, caller, visited);
        }
    }

    private void ScanExpressionForStandaloneStandardLibraryFunctions(
        Expression expression,
        IReadOnlyDictionary<string, BoundFunction> scope,
        BoundFunction? caller,
        HashSet<BoundFunction> visited)
    {
        void RecordTarget(object callSite, IReadOnlyList<string> path)
        {
            BoundFunction? target = null;
            if (_program.ResolvedGenericCalls.TryGetValue(callSite, out var generic))
            {
                target = generic;
            }
            else if (TryResolveFunctionForScan(path, scope, caller, out var resolved))
            {
                target = resolved;
            }

            if (target is not { IsStandardLibrary: true })
            {
                return;
            }

            if (target.Kind is BoundFunctionKind.RuntimeReadDirectory or BoundFunctionKind.RuntimePathQuery)
            {
                _usesDirectoryTraversal = true;
            }

            if (RequiresStandaloneStandardLibraryEmission(target))
            {
                _standaloneStandardLibraryFunctions.Add(target);
            }
            ScanFunctionForStandaloneStandardLibraryFunctions(target, visited);
        }

        switch (expression)
        {
            case CallExpression call:
                RecordTarget(call, call.Path);
                foreach (var argument in call.Arguments)
                {
                    ScanExpressionForStandaloneStandardLibraryFunctions(argument, scope, caller, visited);
                }
                return;
            case FlowExpression flow:
                ScanExpressionForStandaloneStandardLibraryFunctions(flow.Source, scope, caller, visited);
                foreach (var target in flow.Targets)
                {
                    RecordTarget(target, target.Path);
                    foreach (var argument in target.Arguments)
                    {
                        ScanExpressionForStandaloneStandardLibraryFunctions(argument, scope, caller, visited);
                    }
                }
                return;
            case FieldAccessExpression field:
                if (TryBuildQualifiedPath(field, out var fieldPath))
                {
                    RecordTarget(field, fieldPath);
                }
                ScanExpressionForStandaloneStandardLibraryFunctions(field.Source, scope, caller, visited);
                return;
            case IfExpression conditional:
                ScanExpressionForStandaloneStandardLibraryFunctions(conditional.Condition, scope, caller, visited);
                ScanBlockForStandaloneStandardLibraryFunctions(conditional.Then, scope, caller, visited);
                ScanBlockForStandaloneStandardLibraryFunctions(conditional.Else, scope, caller, visited);
                return;
            case WhenExpression whenExpression:
                if (whenExpression.Subject is not null)
                {
                    ScanExpressionForStandaloneStandardLibraryFunctions(whenExpression.Subject, scope, caller, visited);
                }
                foreach (var arm in whenExpression.Arms)
                {
                    ScanExpressionForStandaloneStandardLibraryFunctions(arm.Condition, scope, caller, visited);
                    ScanBlockForStandaloneStandardLibraryFunctions(arm.Body, scope, caller, visited);
                }
                ScanBlockForStandaloneStandardLibraryFunctions(whenExpression.Else, scope, caller, visited);
                return;
            case EnumMatchExpression match:
                ScanExpressionForStandaloneStandardLibraryFunctions(match.Subject, scope, caller, visited);
                foreach (var arm in match.Arms)
                {
                    ScanExpressionForStandaloneStandardLibraryFunctions(arm.Condition, scope, caller, visited);
                    ScanBlockForStandaloneStandardLibraryFunctions(arm.Body, scope, caller, visited);
                }
                ScanBlockForStandaloneStandardLibraryFunctions(match.Else, scope, caller, visited);
                return;
            case FoldExpression fold:
                ScanExpressionForStandaloneStandardLibraryFunctions(fold.Source, scope, caller, visited);
                ScanExpressionForStandaloneStandardLibraryFunctions(fold.Initial, scope, caller, visited);
                ScanBlockForStandaloneStandardLibraryFunctions(fold.Body, scope, caller, visited);
                return;
            case CompileTimeEachExpression each:
                ScanExpressionForStandaloneStandardLibraryFunctions(each.Source, scope, caller, visited);
                ScanExpressionForStandaloneStandardLibraryFunctions(each.Selector, scope, caller, visited);
                if (each.DictionaryValueSelector is not null)
                {
                    ScanExpressionForStandaloneStandardLibraryFunctions(each.DictionaryValueSelector, scope, caller, visited);
                }
                return;
        }

        foreach (var child in ExpressionChildren(expression))
        {
            ScanExpressionForStandaloneStandardLibraryFunctions(child, scope, caller, visited);
        }
    }

    private static bool TryResolveFunctionForScan(
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, BoundFunction> scope,
        BoundFunction? caller,
        out BoundFunction function)
    {
        var name = string.Join('.', path);
        if (scope.TryGetValue(name, out function!))
        {
            return true;
        }

        return !name.Contains('.', StringComparison.Ordinal)
            && caller is { ModuleName.Length: > 0 }
            && scope.TryGetValue(caller.ModuleName + "." + name, out function!);
    }

    private static bool TryBuildQualifiedPath(FieldAccessExpression field, out IReadOnlyList<string> path)
    {
        var segments = new Stack<string>();
        Expression current = field;
        while (current is FieldAccessExpression access)
        {
            segments.Push(access.FieldName);
            current = access.Source;
        }
        if (current is not NameExpression root)
        {
            path = [];
            return false;
        }

        segments.Push(root.Name);
        path = segments.ToArray();
        return true;
    }

    private static IEnumerable<Expression> ExpressionChildren(Expression expression) => expression switch
    {
        StringExpression text => text.Segments.OfType<InterpolationSegment>().Select(segment => segment.Expression),
        AddExpression binary => [binary.Left, binary.Right],
        SubtractExpression binary => [binary.Left, binary.Right],
        MultiplyExpression binary => [binary.Left, binary.Right],
        DivideExpression binary => [binary.Left, binary.Right],
        ModuloExpression binary => [binary.Left, binary.Right],
        CompareExpression binary => [binary.Left, binary.Right],
        AndExpression binary => [binary.Left, binary.Right],
        OrExpression binary => [binary.Left, binary.Right],
        NegateExpression unary => [unary.Value],
        NotExpression unary => [unary.Value],
        RangeExpression range => [range.Start, range.End],
        ArrayLiteralExpression array => array.Elements,
        ArrayRepeatExpression repeat => [repeat.Value],
        DictionaryLiteralExpression dictionary => dictionary.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
        IndexExpression index => [index.Source, index.Index],
        StructLiteralExpression structure => structure.Fields.Select(field => field.Value),
        TryExpression attempt => [attempt.Value],
        BoxExpression box => [box.Value],
        MapExpression map => new[] { map.Path, map.Offset, map.Length, map.FileSize }.OfType<Expression>(),
        SubjectCompareExpression subject => [subject.Right],
        SubjectRangeExpression subject => [subject.Start, subject.End],
        _ => []
    };

    private static bool RequiresStandaloneStandardLibraryEmission(BoundFunction function)
    {
        return FunctionControlFlowFacts.RequiresStandaloneStandardLibraryEmission(function);
    }

}

