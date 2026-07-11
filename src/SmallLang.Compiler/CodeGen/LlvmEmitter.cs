using System.Globalization;
using System.Text;
using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private readonly BoundProgram _program;
    private readonly LlvmRuntimePlatform _platform;
    private readonly bool _usesProcessArguments;
    private readonly List<string> _globals = [];
    private readonly List<string> _functions = [];
    private readonly Dictionary<string, RuntimeValue> _locals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mutableLocals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _borrowedMutableLocals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _borrowedOwnedLocals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableContainerSlot> _mutableContainerSlots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mutableStructSlots = new(StringComparer.Ordinal);
    private readonly List<BoundFunction> _inlineFunctionStack = [];
    private StackFramePlan _currentStackFramePlan = StackFramePlan.Empty;
    private RuntimeBlockInvocation? _currentBlockInvocation;
    private BoundFunction? _currentFunction;
    private IReadOnlyDictionary<string, BoundFunction> _currentFunctions;
    private int _stringId;
    private int _tempId;
    private int _labelId;
    private string _mainOk = "true";
    private string _currentBlockLabel = "entry";

    public LlvmEmitter(BoundProgram program, LlvmRuntimePlatform platform)
    {
        _program = program;
        _platform = platform;
        _currentFunctions = program.Functions;
        _usesProcessArguments = program.MainStatements.Any(UsesProcessArguments);
    }

    private bool UsesProcessArguments(Statement statement) => statement switch
    {
        BindingStatement binding => UsesProcessArguments(binding.Value),
        ExpressionStatement expression => UsesProcessArguments(expression.Expression),
        IndexAssignmentStatement assignment => UsesProcessArguments(assignment.Index) || UsesProcessArguments(assignment.Value),
        FieldAssignmentStatement assignment => UsesProcessArguments(assignment.Value),
        BlockFunctionCallStatement block => UsesProcessArguments(block.Source)
            || block.Body.Any(UsesProcessArguments),
        _ => false
    };

    private bool UsesProcessArguments(Expression expression)
    {
        if (expression is FieldAccessExpression { Source: NameExpression owner } field
            && _program.Functions.TryGetValue(owner.Name + "." + field.FieldName, out var function)
            && function.Kind == BoundFunctionKind.RuntimeArguments)
        {
            return true;
        }

        return expression switch
        {
            StringExpression text => text.Segments.OfType<InterpolationSegment>()
                .Any(segment => UsesProcessArguments(segment.Expression)),
            AddExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            SubtractExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            MultiplyExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            DivideExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            ModuloExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            NegateExpression value => UsesProcessArguments(value.Value),
            CompareExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            AndExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            OrExpression value => UsesProcessArguments(value.Left) || UsesProcessArguments(value.Right),
            NotExpression value => UsesProcessArguments(value.Value),
            RangeExpression value => UsesProcessArguments(value.Start) || UsesProcessArguments(value.End),
            FlowExpression value => UsesProcessArguments(value.Source)
                || value.Targets.SelectMany(target => target.Arguments).Any(UsesProcessArguments),
            CallExpression value => value.Arguments.Any(UsesProcessArguments),
            ArrayLiteralExpression value => value.Elements.Any(UsesProcessArguments),
            ArrayRepeatExpression value => UsesProcessArguments(value.Value),
            DictionaryLiteralExpression value => value.Entries.Any(entry =>
                UsesProcessArguments(entry.Key) || UsesProcessArguments(entry.Value)),
            IndexExpression value => UsesProcessArguments(value.Source) || UsesProcessArguments(value.Index),
            StructLiteralExpression value => value.Fields.Any(field => UsesProcessArguments(field.Value)),
            BoxExpression value => UsesProcessArguments(value.Value),
            FieldAccessExpression value => UsesProcessArguments(value.Source),
            TryExpression value => UsesProcessArguments(value.Value),
            MapExpression value => UsesProcessArguments(value.Path)
                || (value.Offset is not null && UsesProcessArguments(value.Offset))
                || (value.Length is not null && UsesProcessArguments(value.Length))
                || (value.FileSize is not null && UsesProcessArguments(value.FileSize)),
            IfExpression value => UsesProcessArguments(value.Condition)
                || UsesProcessArguments(value.Then)
                || (value.Else is not null && UsesProcessArguments(value.Else)),
            WhenExpression value => (value.Subject is not null && UsesProcessArguments(value.Subject))
                || value.Arms.Any(arm => UsesProcessArguments(arm.Condition) || UsesProcessArguments(arm.Body))
                || UsesProcessArguments(value.Else),
            EnumMatchExpression value => UsesProcessArguments(value.Subject)
                || value.Arms.Any(arm => UsesProcessArguments(arm.Body))
                || (value.Else is not null && UsesProcessArguments(value.Else)),
            FoldExpression value => UsesProcessArguments(value.Source)
                || UsesProcessArguments(value.Initial)
                || UsesProcessArguments(value.Body),
            _ => false
        };
    }

    private bool UsesProcessArguments(BlockBody body) =>
        body.Statements.Any(UsesProcessArguments)
        || (body.Value is not null && UsesProcessArguments(body.Value));

    public string Emit()
    {
        var header = $$"""
            target triple = "{{_platform.TargetTriple}}"

            %smalllang.text = type { ptr, i64 }
            %smalllang.int_slice = type { ptr, i64 }
            %smalllang.mutable_container = type { ptr, ptr, ptr }
            %smalllang.dynamic_int_array = type { ptr, i64, i64 }
            %smalllang.int_dictionary = type { ptr, i64, i64 }
            %smalllang.read_int_result = type { i64, i32 }
            %smalllang.file_int_result = type { i64, i32 }
            %smalllang.file_count_result = type { i64, i32 }
            %smalllang.mapped_bytes = type { ptr, i64, ptr, i64, i1 }

            """;
        header += EmitStructTypeDefinitions();

        EmitPlatformGlobalBlock(_platform.EmitGlobals);
        EmitGlobalLine("@smalllang_random_state = internal global i64 88172645463393265");
        EmitGlobalLine("@smalllang_writer_buffer = internal global [8192 x i64] zeroinitializer, align 8");
        EmitGlobalLine("@smalllang_writer_buffer_count = internal global i64 0");
        EmitGlobalLine();

        EmitPlatformFunctionBlock(_platform.EmitExternalDeclarations);
        EmitPlatformFunctionBlock(_platform.EmitMemoryDeclarations);
        EmitFunctionLine("declare void @llvm.trap()");
        EmitFunctionLine("declare void @llvm.memset.p0.i64(ptr nocapture writeonly, i8, i64, i1 immarg)");
        EmitFunctionLine("declare void @llvm.memcpy.p0.p0.i64(ptr nocapture writeonly, ptr nocapture readonly, i64, i1 immarg)");
        EmitFunctionLine("declare void @llvm.lifetime.start.p0(i64 immarg, ptr nocapture)");
        EmitFunctionLine("declare void @llvm.lifetime.end.p0(i64 immarg, ptr nocapture)");
        EmitFunctionLine();

        EmitOwnedDropHelpers();
        EmitUserFunctions();
        EmitRuntimeHelpers();
        EmitMain();
        EmitFunctionLine("attributes #0 = { nounwind }");

        return header + string.Concat(_globals) + string.Concat(_functions);
    }
}
