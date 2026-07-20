using System.Text;
using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal static class PackageLock
{
    public const string FileName = "sollang.lock";

    public static string PathFor(ProjectBuild project) => System.IO.Path.Combine(
        project.Workspace?.Directory ?? project.RootPackage.Manifest.Directory,
        FileName);

    public static string Ensure(ProjectBuild project, bool locked)
    {
        var path = PathFor(project);
        var expected = Render(project, System.IO.Path.GetDirectoryName(path)!);
        if (File.Exists(path)
            && string.Equals(
                File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal),
                expected,
                StringComparison.Ordinal))
        {
            return path;
        }
        if (locked)
        {
            throw new SollangException(
                $"package lock is missing or out of date: {path}; run 'sollang resolve' without --locked");
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public static int Resolve(string[] args)
    {
        string? projectPath = null;
        string? workspacePath = null;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--project":
                    projectPath = RequireValue(args, ref index, "--project");
                    break;
                case "--workspace":
                    workspacePath = RequireValue(args, ref index, "--workspace");
                    break;
                default:
                    throw new SollangException(
                        $"unknown resolve option '{args[index]}'; usage: sollang resolve [--project <path> | --workspace <path>]");
            }
        }
        if (projectPath is not null && workspacePath is not null)
        {
            throw new SollangException("--project cannot be combined with --workspace");
        }
        if (projectPath is null && workspacePath is null)
        {
            var current = Directory.GetCurrentDirectory();
            projectPath = ProjectManifest.FindFrom(current);
            workspacePath = WorkspaceManifest.FindFrom(current);
            if (workspacePath is not null)
            {
                projectPath = null;
            }
        }

        var graph = workspacePath is not null
            ? ProjectBuild.LoadWorkspaceForResolution(workspacePath)
            : projectPath is not null
                ? ProjectBuild.LoadProjectForResolution(projectPath)
                : throw new SollangException(
                    $"no {ProjectManifest.FileName} or {WorkspaceManifest.FileName} was found");
        var path = Ensure(graph, locked: false);
        Console.WriteLine($"Resolved {graph.Packages.Count} package(s) into {path}");
        return 0;
    }

    private static string Render(ProjectBuild project, string lockDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("lock {");
        builder.AppendLine("    format: 2");
        builder.AppendLine("    packages: [");
        foreach (var package in project.Packages
                     .OrderBy(static package => package.Identity, StringComparer.Ordinal)
                     .ThenBy(static package => package.Manifest.Path, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine("        {");
            builder.Append("            id: \"").Append(Escape(package.Identity)).AppendLine("\"");
            switch (package.Source)
            {
                case PathPackageSource:
                    var relative = System.IO.Path.GetRelativePath(lockDirectory, package.Manifest.Directory)
                        .Replace('\\', '/');
                    builder.Append("            source: \"path:").Append(Escape(relative)).AppendLine("\"");
                    break;
                case GitPackageSource git:
                    builder.Append("            source: \"git:")
                        .Append(Escape(git.Location))
                        .Append('#')
                        .Append(git.Revision)
                        .AppendLine("\"");
                    builder.Append("            checksum: \"").Append(git.Checksum).AppendLine("\"");
                    break;
                default:
                    throw new InvalidOperationException("unknown package source");
            }
            builder.Append("            dependencies: [");
            var dependencies = package.Dependencies.Values
                .Select(static dependency => dependency.Identity)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (dependencies.Length > 0)
            {
                builder.AppendLine();
                foreach (var dependency in dependencies)
                {
                    builder.Append("                \"").Append(Escape(dependency)).AppendLine("\"");
                }
                builder.Append("            ");
            }
            builder.AppendLine("]");
            builder.AppendLine("        }");
        }
        builder.AppendLine("    ]");
        builder.AppendLine("}");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new SollangException($"missing value for {option}");
        }
        return args[++index];
    }
}
