using SmallLang.Compiler.Semantics;

namespace SmallLang.Compiler.CodeGen;

internal static class LlvmIrGenerator
{
    public static string GenerateProgram(BoundProgram program, CompilationTarget target)
    {
        return new LlvmEmitter(program, LlvmRuntimePlatform.Create(target)).Emit();
    }
}
