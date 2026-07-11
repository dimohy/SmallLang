using System.Globalization;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private bool TryEmitEnumConstructor(CallExpression expression, out RuntimeValue value)
    {
        value = null!;
        if (expression.Path.Count != 2
            || !_program.Types.TryResolve(expression.Path[0], out var type)
            || !_program.Types.IsEnum(type))
        {
            return false;
        }

        var definition = _program.Types.GetEnum(type);
        var variant = definition.Variants.FirstOrDefault(candidate => candidate.Name == expression.Path[1])
            ?? throw new SmallLangException($"enum '{definition.Name}' has no variant '{expression.Path[1]}'");
        var payloadType = variant.PayloadType
            ?? throw new SmallLangException($"payload-free variant '{definition.Name}.{variant.Name}' uses member syntax without parentheses");
        var payload = EmitExpression(expression.Arguments[0]);
        EnsureRuntimeType(payload, payloadType, $"{definition.Name}.{variant.Name}");
        value = EmitEnumValue(type, variant, payload);
        if (_program.Types.ContainsOwnedStorage(payloadType)
            && expression.Arguments[0] is NameExpression sourceName)
        {
            RemoveLocal(sourceName.Name);
        }
        return true;
    }

    private bool TryEmitPayloadlessEnumVariant(FieldAccessExpression expression, out RuntimeValue value)
    {
        value = null!;
        if (expression.Source is not NameExpression typeName
            || !_program.Types.TryResolve(typeName.Name, out var type)
            || !_program.Types.IsEnum(type))
        {
            return false;
        }

        var definition = _program.Types.GetEnum(type);
        var variant = definition.Variants.FirstOrDefault(candidate => candidate.Name == expression.FieldName)
            ?? throw new SmallLangException($"enum '{definition.Name}' has no variant '{expression.FieldName}'");
        if (variant.PayloadType is not null)
        {
            throw new SmallLangException($"variant '{definition.Name}.{variant.Name}' requires a payload argument");
        }

        value = EmitEnumValue(type, variant, payload: null);
        return true;
    }

    private RuntimeEnum EmitEnumValue(BoundType type, BoundEnumVariant variant, RuntimeValue? payload)
    {
        var llvmType = LlvmEnumType(type);
        var slot = NextTemp("enum_init_slot");
        EmitAlloca(slot, llvmType, 8);
        EmitStore(llvmType, "zeroinitializer", slot, 8);
        var tagAddress = NextTemp("enum_tag_addr");
        EmitAssign(tagAddress, $"getelementptr inbounds {llvmType}, ptr {slot}, i32 0, i32 0");
        EmitStore("i32", variant.Tag.ToString(CultureInfo.InvariantCulture), tagAddress, 4);

        if (payload is not null)
        {
            var payloadAddress = NextTemp("enum_payload_addr");
            EmitAssign(payloadAddress, $"getelementptr inbounds {llvmType}, ptr {slot}, i32 0, i32 1");
            var materialized = MaterializeAggregateValue(payload);
            EmitStore(materialized.TypeName, materialized.ValueName, payloadAddress, RuntimeAlignment(payload.Type));
        }

        var aggregate = NextTemp("enum_value");
        EmitLoad(aggregate, llvmType, slot, 8);
        return new RuntimeEnum(type, aggregate);
    }

    private RuntimeValue EmitEnumMatchExpression(EnumMatchExpression expression)
    {
        var subject = EmitExpression(expression.Subject) as RuntimeEnum
            ?? throw new SmallLangException("enum when expects a runtime enum subject");
        var definition = _program.Types.GetEnum(subject.Type);
        var tag = NextTemp("enum_tag");
        EmitAssign(tag, $"extractvalue {LlvmEnumType(subject.Type)} {subject.ValueName}, 0");

        var endLabel = NextLabel("enum_when_end");
        var valueResults = new List<(RuntimeValue Value, string Label)>();
        var nextConditionLabel = _currentBlockLabel;
        foreach (var arm in expression.Arms)
        {
            _currentBlockLabel = nextConditionLabel;
            var pattern = (EnumPatternExpression)arm.Condition;
            var variant = definition.Variants.First(candidate => candidate.Name == pattern.VariantName);
            var armLabel = NextLabel("enum_when_arm");
            var nextLabel = NextLabel("enum_when_next");
            var matches = NextTemp("enum_matches");
            EmitCompare(matches, "eq", "i32", tag, variant.Tag.ToString(CultureInfo.InvariantCulture));
            EmitConditionalBranch(matches, armLabel, nextLabel);

            EmitLabel(armLabel);
            _currentBlockLabel = armLabel;
            RuntimeValue? payload = null;
            if (variant.PayloadType is { } payloadType)
            {
                payload = ExtractEnumPayload(subject, payloadType);
            }

            var armResult = EmitEnumArmBody(arm.Body, pattern.BindingName, payload);
            if (armResult.Value is not null)
            {
                valueResults.Add((armResult.Value, armResult.EndLabel));
            }
            EmitBranch(endLabel);

            EmitLabel(nextLabel);
            nextConditionLabel = nextLabel;
        }

        _currentBlockLabel = nextConditionLabel;
        if (expression.Else is not null)
        {
            var elseResult = EmitScopedBlockBody(expression.Else);
            if (elseResult.Value is not null)
            {
                valueResults.Add((elseResult.Value, elseResult.EndLabel));
            }
            EmitBranch(endLabel);
        }
        else
        {
            EmitInstruction("call void @llvm.trap()");
            EmitInstruction("unreachable");
        }

        EmitLabel(endLabel);
        _currentBlockLabel = endLabel;
        return valueResults.Count == 0
            ? RuntimeUnit.Instance
            : EmitPhiValue("enum_when", valueResults);
    }

    private BlockResult EmitEnumArmBody(BlockBody body, string? bindingName, RuntimeValue? payload)
    {
        var outerLocals = CaptureLocals();
        try
        {
            if (bindingName is not null && payload is not null)
            {
                _locals[bindingName] = payload;
                if (_program.Types.ContainsOwnedStorage(payload.Type))
                {
                    _borrowedOwnedLocals.Add(bindingName);
                }
            }
            return EmitScopedBlockBody(body);
        }
        finally
        {
            RestoreLocals(outerLocals);
        }
    }

    private RuntimeValue ExtractEnumPayload(RuntimeEnum value, BoundType payloadType)
    {
        var llvmType = LlvmEnumType(value.Type);
        var slot = NextTemp("enum_match_slot");
        EmitAlloca(slot, llvmType, 8);
        EmitStore(llvmType, value.ValueName, slot, 8);
        var payloadAddress = NextTemp("enum_payload_addr");
        EmitAssign(payloadAddress, $"getelementptr inbounds {llvmType}, ptr {slot}, i32 0, i32 1");
        var payload = NextTemp("enum_payload");
        EmitLoad(payload, LlvmType(payloadType), payloadAddress, RuntimeAlignment(payloadType));
        return DematerializeAggregateValue(payloadType, payload);
    }

    private RuntimeValue EmitTryExpression(TryExpression expression)
    {
        var result = EmitExpression(expression.Value) as RuntimeEnum
            ?? throw new SmallLangException("'?' expects a Result value");
        if (!_program.Types.TryGetResultTypes(result.Type, out var operandTypes))
        {
            throw new SmallLangException("'?' expects a Result value");
        }
        var function = _currentFunction
            ?? throw new SmallLangException("'?' requires an enclosing Result function");
        if (!_program.Types.TryGetResultTypes(function.ReturnType, out var outerTypes)
            || outerTypes.Error != operandTypes.Error)
        {
            throw new SmallLangException("'?' enclosing function has an incompatible Result error type");
        }
        if ((_program.Types.ContainsOwnedStorage(operandTypes.Ok)
                || _program.Types.ContainsOwnedStorage(operandTypes.Error))
            && expression.Value is NameExpression consumedName)
        {
            RemoveLocal(consumedName.Name);
        }

        var tag = NextTemp("try_tag");
        EmitAssign(tag, $"extractvalue {LlvmEnumType(result.Type)} {result.ValueName}, 0");
        var isError = NextTemp("try_is_error");
        EmitCompare(isError, "eq", "i32", tag, "1");
        var errorLabel = NextLabel("try_error");
        var okLabel = NextLabel("try_ok");
        EmitConditionalBranch(isError, errorLabel, okLabel);

        EmitLabel(errorLabel);
        _currentBlockLabel = errorLabel;
        var errorPayload = ExtractEnumPayload(result, operandTypes.Error);
        var outerDefinition = _program.Types.GetEnum(function.ReturnType);
        var errorVariant = outerDefinition.Variants.First(variant => variant.Name == "Err");
        var propagated = EmitEnumValue(function.ReturnType, errorVariant, errorPayload);
        DropOwnedLocals();
        var materialized = MaterializeAggregateValue(propagated);
        EmitRet(materialized.TypeName, materialized.ValueName);

        EmitLabel(okLabel);
        _currentBlockLabel = okLabel;
        return ExtractEnumPayload(result, operandTypes.Ok);
    }

    private int RuntimeAlignment(BoundType type)
    {
        return type switch
        {
            BoundType.Bool or BoundType.Int8 or BoundType.UInt8 => 1,
            BoundType.Int16 or BoundType.UInt16 => 2,
            BoundType.Int or BoundType.UInt32 or BoundType.Float32 => 4,
            BoundType.Size or BoundType.UIntSize => _platform.PointerBitWidth / 8,
            _ => 8
        };
    }
}
