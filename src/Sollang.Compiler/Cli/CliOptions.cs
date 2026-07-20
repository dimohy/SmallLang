using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal sealed record CliOptions(
    IReadOnlyList<string> SourcePaths,
    ProjectBuild? Project,
    string OutputPath,
    string? LlvmHome,
    CompilationTarget Target,
    bool KeepTemps,
    string? OptimizationLevel,
    bool Locked)
{
    public static CliOptions Parse(string[] args)
    {
        if (args is not ["build", ..])
        {
            throw new SollangException(Usage);
        }

        var sources = new List<string>();
        string? output = null;
        string? llvmHome = null;
        string? projectPath = null;
        string? workspacePath = null;
        string? packageName = null;
        string? productName = null;
        var target = CompilationTarget.WindowsX64;
        var keepTemps = false;
        var locked = false;
        string? optimizationLevel = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-o":
                case "--output":
                    output = RequireValue(args, ref i, arg);
                    break;
                case "--llvm":
                    llvmHome = RequireValue(args, ref i, arg);
                    break;
                case "--project":
                    projectPath = RequireValue(args, ref i, arg);
                    break;
                case "--workspace":
                    workspacePath = RequireValue(args, ref i, arg);
                    break;
                case "--package":
                    packageName = RequireValue(args, ref i, arg);
                    break;
                case "--product":
                    productName = RequireValue(args, ref i, arg);
                    break;
                case "--target":
                    target = ParseTarget(RequireValue(args, ref i, arg));
                    break;
                case "--keep-temps":
                    keepTemps = true;
                    break;
                case "--locked":
                    locked = true;
                    break;
                case "-O0":
                case "-O1":
                case "-O2":
                case "-O3":
                    optimizationLevel = arg;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new SollangException($"unknown option '{arg}'");
                    }

                    sources.Add(arg);
                    break;
            }
        }

        if (projectPath is not null && workspacePath is not null)
        {
            throw new SollangException("--project cannot be combined with --workspace");
        }
        if (sources.Count > 0 && (projectPath is not null || workspacePath is not null))
        {
            throw new SollangException("--project/--workspace cannot be combined with explicit source files");
        }
        if (sources.Count > 0 && productName is not null)
        {
            throw new SollangException("--product requires a project build");
        }
        if (sources.Count > 0 && packageName is not null)
        {
            throw new SollangException("--package requires a workspace build");
        }
        if (packageName is not null && projectPath is not null)
        {
            throw new SollangException("--package cannot be combined with --project");
        }
        if (locked && sources.Count > 0)
        {
            throw new SollangException("--locked requires a project or workspace build");
        }

        ProjectBuild? project = null;
        if (sources.Count == 0)
        {
            if (projectPath is null && workspacePath is null)
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                projectPath = ProjectManifest.FindFrom(currentDirectory);
                workspacePath = WorkspaceManifest.FindFrom(currentDirectory);
                if (projectPath is not null && workspacePath is not null)
                {
                    packageName ??= ProjectManifest.Load(projectPath).Name;
                    projectPath = null;
                }
            }
            if (workspacePath is not null)
            {
                project = ProjectBuild.LoadWorkspace(workspacePath, packageName, productName, locked);
            }
            else if (projectPath is not null)
            {
                if (packageName is not null)
                {
                    throw new SollangException("--package requires a workspace build");
                }
                project = ProjectBuild.LoadProject(projectPath, productName, locked);
            }
            else
            {
                throw new SollangException(
                    $"no source file, {ProjectManifest.FileName}, or {WorkspaceManifest.FileName} was found; {Usage}");
            }
            sources.Add(project.Product.RootSource);
            if (project.Workspace is not null
                || locked
                || (string.Equals(
                        Path.GetFileName(project.RootPackage.Manifest.Path),
                        ProjectManifest.FileName,
                        StringComparison.OrdinalIgnoreCase)
                    && File.Exists(PackageLock.PathFor(project))))
            {
                PackageLock.Ensure(project, locked);
            }
        }

        output ??= project is null
            ? DefaultSourceOutput(sources[0], target)
            : DefaultProjectOutput(project, target);

        return new CliOptions(
            sources.Select(Path.GetFullPath).ToArray(),
            project,
            Path.GetFullPath(output),
            llvmHome is null ? null : Path.GetFullPath(llvmHome),
            target,
            keepTemps,
            optimizationLevel,
            locked);
    }

    private static string DefaultSourceOutput(string source, CompilationTarget target) =>
        target switch
        {
            CompilationTarget.WindowsX64 => Path.ChangeExtension(source, ".exe"),
            CompilationTarget.LinuxX64 => Path.Combine(
                Path.GetDirectoryName(source) ?? Directory.GetCurrentDirectory(),
                Path.GetFileNameWithoutExtension(source)),
            CompilationTarget.Wasm32Browser => Path.ChangeExtension(source, ".wasm"),
            _ => throw new SollangException($"unsupported target '{target}'")
        };

    private static string DefaultProjectOutput(ProjectBuild project, CompilationTarget target) =>
        project.Workspace is null
            ? Path.Combine(
                project.RootPackage.Manifest.Directory,
                "build",
                OutputFileName(project.Product.Name, target))
            : Path.Combine(
                project.Workspace.Directory,
                "build",
                TargetName(target),
                project.RootPackage.Manifest.Name,
                OutputFileName(project.Product.Name, target));

    private static string OutputFileName(string productName, CompilationTarget target) => target switch
    {
        CompilationTarget.WindowsX64 => productName + ".exe",
        CompilationTarget.LinuxX64 => productName,
        CompilationTarget.Wasm32Browser => productName + ".wasm",
        _ => throw new SollangException($"unsupported target '{target}'")
    };

    private static string TargetName(CompilationTarget target) => target switch
    {
        CompilationTarget.WindowsX64 => "windows-x64",
        CompilationTarget.LinuxX64 => "linux-x64",
        CompilationTarget.Wasm32Browser => "wasm32-browser",
        _ => throw new SollangException($"unsupported target '{target}'")
    };

    private const string Usage =
        "usage: sollang build [<source.slg> ... | --project <sollang.project|directory> "
        + "| --workspace <sollang.workspace|directory> --package <name>] [--product <name>] "
        + "[-o <output>] [--target windows-x64|linux-x64|wasm32-browser] "
        + "[--llvm <dir>] [-O0|-O1|-O2|-O3] [--keep-temps] [--locked]";

    private static CompilationTarget ParseTarget(string value)
    {
        return value switch
        {
            "windows-x64" => CompilationTarget.WindowsX64,
            "linux-x64" => CompilationTarget.LinuxX64,
            "wasm32-browser" => CompilationTarget.Wasm32Browser,
            _ => throw new SollangException($"unknown target '{value}'")
        };
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new SollangException($"missing value for {option}");
        }

        index++;
        return args[index];
    }
}
