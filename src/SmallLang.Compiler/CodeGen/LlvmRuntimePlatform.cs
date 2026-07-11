using System.Text;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.CodeGen;

internal abstract class LlvmRuntimePlatform
{
    public abstract string TargetTriple { get; }

    public abstract string EntryPointName { get; }

    public virtual string EntryPointParameters => "";

    public virtual int PointerBitWidth => 64;

    public virtual void EmitGlobals(StringBuilder globals)
    {
    }

    public abstract void EmitExternalDeclarations(StringBuilder functions);

    public abstract void EmitIoPrimitives(StringBuilder functions);

    public abstract void EmitFilePrimitives(StringBuilder functions);

    public abstract void EmitMappedFilePrimitives(StringBuilder functions);

    public abstract void EmitTimePrimitives(StringBuilder functions);

    public abstract void EmitProcessPrimitives(StringBuilder functions);

    public virtual void EmitEnvironmentPrimitives(StringBuilder functions)
    {
    }

    public abstract void EmitEntryHandles(StringBuilder functions);

    public virtual void EmitProcessEntry(StringBuilder functions)
    {
    }

    public virtual bool SupportsHeapAllocation => true;

    public virtual bool SupportsMemoryMapping => true;

    public virtual bool SupportsProcessArguments => true;

    public virtual bool SupportsEnvironment => true;

    public virtual bool SupportsChildProcesses => true;

    public virtual void EmitExitCleanup(StringBuilder functions)
    {
    }

    public virtual void EmitEnvironmentCleanup(StringBuilder functions)
    {
    }

    public abstract void EmitMemoryDeclarations(StringBuilder functions);

    public abstract void EmitMemoryPrimitives(StringBuilder functions);

    public static LlvmRuntimePlatform Create(CompilationTarget target)
    {
        return target switch
        {
            CompilationTarget.WindowsX64 => new WindowsLlvmRuntimePlatform(),
            CompilationTarget.LinuxX64 => new LinuxLlvmRuntimePlatform(),
            CompilationTarget.Wasm32Browser => new WasmBrowserLlvmRuntimePlatform(),
            _ => throw new SmallLangException($"unsupported target '{target}'")
        };
    }
}
