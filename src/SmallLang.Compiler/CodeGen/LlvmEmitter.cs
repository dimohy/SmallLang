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
    private readonly List<string> _globals = [];
    private readonly List<string> _functions = [];
    private readonly Dictionary<string, RuntimeValue> _locals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mutableLocals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableContainerSlot> _mutableContainerSlots = new(StringComparer.Ordinal);
    private readonly List<BoundFunction> _inlineFunctionStack = [];
    private RuntimeBlockInvocation? _currentBlockInvocation;
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
    }

    public string Emit()
    {
        var header = $$"""
            target triple = "{{_platform.TargetTriple}}"

            %smalllang.text = type { ptr, i64 }
            %smalllang.read_int_result = type { i64, i32 }
            %smalllang.file_int_result = type { i64, i32 }
            %smalllang.file_count_result = type { i64, i32 }

            """;

        EmitPlatformGlobalBlock(_platform.EmitGlobals);
        EmitGlobalLine("@smalllang_random_state = internal global i64 88172645463393265");
        EmitGlobalLine("@smalllang_writer_buffer = internal global [8192 x i64] zeroinitializer, align 8");
        EmitGlobalLine("@smalllang_writer_buffer_count = internal global i64 0");
        EmitGlobalLine();

        EmitPlatformFunctionBlock(_platform.EmitExternalDeclarations);
        EmitPlatformFunctionBlock(_platform.EmitMemoryDeclarations);
        EmitFunctionLine("declare void @llvm.trap()");
        EmitFunctionLine("declare void @llvm.memset.p0.i64(ptr nocapture writeonly, i8, i64, i1 immarg)");
        EmitFunctionLine();

        EmitUserFunctions();
        EmitRuntimeHelpers();
        EmitMain();
        EmitFunctionLine("attributes #0 = { noinline optnone }");

        return header + string.Concat(_globals) + string.Concat(_functions);
    }
}
