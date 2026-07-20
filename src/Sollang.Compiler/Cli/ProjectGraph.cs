using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal sealed record ProjectBuild(
    ProjectPackage RootPackage,
    ProjectProduct Product,
    IReadOnlyList<ProjectPackage> Packages,
    WorkspaceManifest? Workspace)
{
    public static ProjectBuild LoadProject(string pathOrDirectory, string? productName, bool locked = false)
    {
        var rootManifest = ProjectManifest.Load(pathOrDirectory);
        return Load(rootManifest, productName, workspace: null, locked: locked);
    }

    public static ProjectBuild LoadWorkspace(
        string pathOrDirectory,
        string? packageName,
        string? productName,
        bool locked = false)
    {
        var workspace = WorkspaceManifest.Load(pathOrDirectory);
        var rootManifest = workspace.SelectMember(packageName);
        return Load(rootManifest, productName, workspace, includeAllWorkspaceMembers: true, locked: locked);
    }

    public static ProjectBuild LoadWorkspaceForResolution(string pathOrDirectory)
    {
        var workspace = WorkspaceManifest.Load(pathOrDirectory);
        var rootManifest = workspace.Members[0];
        return Load(
            rootManifest,
            rootManifest.Name,
            workspace,
            includeAllWorkspaceMembers: true,
            useExistingLock: false);
    }

    public static ProjectBuild LoadProjectForResolution(string pathOrDirectory)
    {
        var manifest = ProjectManifest.Load(pathOrDirectory);
        var product = manifest.Products.ContainsKey(manifest.Name)
            ? manifest.Name
            : manifest.Products.Keys.Order(StringComparer.Ordinal).First();
        return Load(manifest, product, workspace: null, useExistingLock: false);
    }

    private static ProjectBuild Load(
        ProjectManifest rootManifest,
        string? productName,
        WorkspaceManifest? workspace,
        bool includeAllWorkspaceMembers = false,
        bool locked = false,
        bool useExistingLock = true)
    {
        var manifestsByPath = new Dictionary<string, ProjectPackage>(StringComparer.OrdinalIgnoreCase);
        var manifestsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var workspaceMembers = workspace?.Members.ToDictionary(
            static member => member.Path,
            StringComparer.OrdinalIgnoreCase);
        var cacheRoot = Path.Combine(
            workspace?.Directory ?? rootManifest.Directory,
            ".sollang",
            "dependencies");
        var lockPath = Path.Combine(
            workspace?.Directory ?? rootManifest.Directory,
            PackageLock.FileName);
        var lockSnapshot = useExistingLock
            ? PackageLockSnapshot.Load(lockPath, required: locked)
            : null;
        var root = LoadPackage(
            rootManifest,
            rootManifest.SelectProduct(productName),
            manifestsByPath,
            manifestsByName,
            states,
            workspaceMembers,
            cacheRoot,
            lockSnapshot,
            locked,
            new PathPackageSource(rootManifest.Directory),
            []);
        if (includeAllWorkspaceMembers)
        {
            foreach (var member in workspace!.Members)
            {
                if (manifestsByPath.ContainsKey(member.Path))
                {
                    continue;
                }
                var memberProduct = member.Products.TryGetValue(member.Name, out var memberRoot)
                    ? new ProjectProduct(member.Name, memberRoot)
                    : new ProjectProduct(
                        member.Products.Keys.Order(StringComparer.Ordinal).First(),
                        member.Products.OrderBy(static pair => pair.Key, StringComparer.Ordinal).First().Value);
                LoadPackage(
                    member,
                    memberProduct,
                    manifestsByPath,
                    manifestsByName,
                    states,
                    workspaceMembers,
                    cacheRoot,
                    lockSnapshot,
                    locked,
                    new PathPackageSource(member.Directory),
                    []);
            }
        }
        return new ProjectBuild(
            root,
            root.Product,
            manifestsByPath.Values
                .OrderBy(static package => package.Manifest.Name, StringComparer.Ordinal)
                .ToArray(),
            workspace);
    }

    private static ProjectPackage LoadPackage(
        ProjectManifest manifest,
        ProjectProduct product,
        IDictionary<string, ProjectPackage> packagesByPath,
        IDictionary<string, string> pathsByName,
        IDictionary<string, VisitState> states,
        IReadOnlyDictionary<string, ProjectManifest>? workspaceMembers,
        string cacheRoot,
        PackageLockSnapshot? lockSnapshot,
        bool locked,
        PackageSource source,
        IReadOnlyList<string> chain)
    {
        if (states.TryGetValue(manifest.Path, out var state))
        {
            if (state == VisitState.Visiting)
            {
                throw new SollangException(
                    "project dependency cycle: " + string.Join(" -> ", chain.Append(manifest.Name)));
            }
            return packagesByPath[manifest.Path];
        }

        if (pathsByName.TryGetValue(manifest.Name, out var existingPath)
            && !StringComparer.OrdinalIgnoreCase.Equals(existingPath, manifest.Path))
        {
            throw new SollangException(
                $"project name '{manifest.Name}' is declared by both '{existingPath}' and '{manifest.Path}'");
        }
        pathsByName[manifest.Name] = manifest.Path;
        states[manifest.Path] = VisitState.Visiting;

        var dependencies = new Dictionary<string, ProjectPackage>(StringComparer.Ordinal);
        var nextChain = chain.Append(manifest.Name).ToArray();
        foreach (var dependency in manifest.Dependencies.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var resolved = dependency.Value.Source switch
            {
                PathProjectDependencySource path => ResolvePathDependency(path, source),
                GitProjectDependencySource git => GitPackageCache.Materialize(git, cacheRoot),
                RegistryProjectDependencySource registry => RegistryPackageCache.Materialize(
                    dependency.Key,
                    dependency.Value.Version,
                    registry,
                    cacheRoot,
                    lockSnapshot?.Find(dependency.Key, registry.Location, dependency.Value.Version, locked),
                    locked),
                _ => throw new InvalidOperationException("unknown project dependency source")
            };
            var dependencyManifest = resolved.Manifest;
            if (workspaceMembers is not null
                && resolved.Source is PathPackageSource
                && !workspaceMembers.ContainsKey(dependencyManifest.Path))
            {
                throw new SollangException(
                    $"workspace package '{manifest.Name}' depends on '{dependencyManifest.Name}' "
                    + $"at '{dependencyManifest.Path}', but that project is not a workspace member");
            }
            if (!string.Equals(dependency.Key, dependencyManifest.Name, StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"dependency '{dependency.Key}' resolves to project '{dependencyManifest.Name}' in '{dependencyManifest.Path}'");
            }
            if (!dependency.Value.Version.Accepts(dependencyManifest.Version))
            {
                throw new SollangException(
                    $"dependency '{dependency.Key}' requires version '{dependency.Value.Version.Text}', "
                    + $"but project '{dependencyManifest.Name}' declares '{dependencyManifest.Version}'");
            }
            dependencies.Add(
                dependency.Key,
                LoadPackage(
                    dependencyManifest,
                    dependencyManifest.SelectProduct(dependencyManifest.Name),
                    packagesByPath,
                    pathsByName,
                    states,
                    workspaceMembers,
                    cacheRoot,
                    lockSnapshot,
                    locked,
                    resolved.Source,
                    nextChain));
        }

        var package = new ProjectPackage(manifest, product, dependencies, source);
        packagesByPath[manifest.Path] = package;
        states[manifest.Path] = VisitState.Visited;
        return package;
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }

    private static ResolvedDependency ResolvePathDependency(
        PathProjectDependencySource dependency,
        PackageSource parentSource)
    {
        if (parentSource is GitPackageSource git
            && !IsWithin(git.ContentRoot, dependency.Path))
        {
            throw new SollangException(
                $"path dependency escapes git source tree '{git.Location}#{git.Revision}': {dependency.Path}");
        }
        if (parentSource is RegistryPackageSource registry
            && !IsWithin(registry.ContentRoot, dependency.Path))
        {
            throw new SollangException(
                $"path dependency escapes registry package '{registry.Location}#{registry.Version}': {dependency.Path}");
        }
        return new ResolvedDependency(
            ProjectManifest.Load(dependency.Path),
            parentSource is GitPackageSource or RegistryPackageSource
                ? parentSource
                : new PathPackageSource(dependency.Path));
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

internal sealed record ProjectPackage(
    ProjectManifest Manifest,
    ProjectProduct Product,
    IReadOnlyDictionary<string, ProjectPackage> Dependencies,
    PackageSource Source)
{
    public string Identity => $"{Manifest.Name}@{Manifest.Version}";

    public string SourceRoot => Path.GetDirectoryName(Product.RootSource)
        ?? System.IO.Directory.GetCurrentDirectory();
}

internal sealed record ResolvedDependency(ProjectManifest Manifest, PackageSource Source);

internal abstract record PackageSource;

internal sealed record PathPackageSource(string Path) : PackageSource;

internal sealed record GitPackageSource(
    string Location,
    string Revision,
    string Checksum,
    string ContentRoot) : PackageSource;

internal sealed record RegistryPackageSource(
    string Location,
    SemanticVersion Version,
    string Checksum,
    string ContentRoot) : PackageSource;
