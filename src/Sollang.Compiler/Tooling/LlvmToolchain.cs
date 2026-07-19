using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Tooling;

internal sealed record LlvmToolchain(
    string Home,
    string Clang,
    string LldLink,
    string LlvmLib,
    string LlvmSplit,
    string Opt,
    string Llc,
    string WasmLd)
{
    public static LlvmToolchain From(string? llvmHome)
    {
        var home = llvmHome ?? Environment.GetEnvironmentVariable("SOLLANG_LLVM_HOME");
        if (home is null && OperatingSystem.IsLinux())
        {
            home = "/usr";
        }
        if (home is null)
        {
            throw new SollangException(
                "LLVM toolchain not found. Set SOLLANG_LLVM_HOME or pass --llvm <dir>. "
                + "Repository builds can run scripts\\sollang.ps1 to download LLVM locally.");
        }

        var bin = Path.Combine(home, "bin");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var clang = Path.Combine(bin, "clang" + suffix);
        var lldLink = Path.Combine(bin, "lld-link" + suffix);
        var llvmLib = Path.Combine(bin, "llvm-lib" + suffix);
        var llvmSplit = Path.Combine(bin, "llvm-split" + suffix);
        var opt = Path.Combine(bin, "opt" + suffix);
        var llc = Path.Combine(bin, "llc" + suffix);
        var wasmLd = Path.Combine(bin, "wasm-ld" + suffix);

        RequireFile(clang, "clang" + suffix);

        return new LlvmToolchain(home, clang, lldLink, llvmLib, llvmSplit, opt, llc, wasmLd);
    }

    private static void RequireFile(string path, string name)
    {
        if (!File.Exists(path))
        {
            throw new SollangException($"required LLVM tool '{name}' was not found at {path}");
        }
    }
}
