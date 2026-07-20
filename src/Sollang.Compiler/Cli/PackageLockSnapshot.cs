using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal sealed record LockedRegistryPackage(
    string Name,
    SemanticVersion Version,
    string Registry,
    string Checksum);

internal sealed class PackageLockSnapshot
{
    private readonly IReadOnlyDictionary<string, LockedRegistryPackage> _registryPackages;

    private PackageLockSnapshot(IReadOnlyDictionary<string, LockedRegistryPackage> registryPackages)
    {
        _registryPackages = registryPackages;
    }

    public static PackageLockSnapshot? Load(string path, bool required)
    {
        if (!File.Exists(path))
        {
            if (required)
            {
                throw new SollangException(
                    $"package lock is missing: {path}; run 'sollang resolve' without --locked");
            }
            return null;
        }

        try
        {
            var packages = new Dictionary<string, LockedRegistryPackage>(StringComparer.Ordinal);
            string? id = null;
            string? source = null;
            string? checksum = null;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("id: \"", StringComparison.Ordinal))
                {
                    Commit();
                    id = QuotedValue(line, "id");
                    source = null;
                    checksum = null;
                }
                else if (line.StartsWith("source: \"", StringComparison.Ordinal))
                {
                    source = QuotedValue(line, "source");
                }
                else if (line.StartsWith("checksum: \"", StringComparison.Ordinal))
                {
                    checksum = QuotedValue(line, "checksum");
                }
            }
            Commit();
            return new PackageLockSnapshot(packages);

            void Commit()
            {
                if (id is null || source is null || !source.StartsWith("registry:", StringComparison.Ordinal))
                {
                    return;
                }
                if (checksum is null || !RegistryPackageCache.IsSha256(checksum))
                {
                    throw new SollangException($"registry package '{id}' has no valid checksum in {path}");
                }
                var at = id.LastIndexOf('@');
                var hash = source.LastIndexOf('#');
                if (at <= 0 || hash <= "registry:".Length || hash + 1 >= source.Length)
                {
                    throw new SollangException($"invalid registry package entry '{id}' in {path}");
                }
                var name = id[..at];
                var version = SemanticVersion.Parse(id[(at + 1)..], $"package lock {path}");
                var sourceVersion = SemanticVersion.Parse(source[(hash + 1)..], $"package lock {path}");
                if (version.CompareTo(sourceVersion) != 0)
                {
                    throw new SollangException($"registry source version does not match package '{id}' in {path}");
                }
                if (!packages.TryAdd(
                        name,
                        new LockedRegistryPackage(name, version, source["registry:".Length..hash], checksum)))
                {
                    throw new SollangException($"duplicate locked registry package name '{name}' in {path}");
                }
            }
        }
        catch (SollangException) when (!required)
        {
            return null;
        }
    }

    public LockedRegistryPackage? Find(
        string name,
        string registry,
        VersionRequirement requirement,
        bool required)
    {
        if (!_registryPackages.TryGetValue(name, out var package))
        {
            if (required)
            {
                throw new SollangException($"package lock has no registry resolution for '{name}'");
            }
            return null;
        }
        if (!string.Equals(package.Registry, registry, StringComparison.Ordinal)
            || !requirement.Accepts(package.Version)
            || (package.Version.PreRelease is not null && !requirement.AllowsPreRelease))
        {
            if (required)
            {
                throw new SollangException(
                    $"locked registry package '{package.Name}@{package.Version}' does not satisfy "
                    + $"registry '{registry}' and requirement '{requirement.Text}'");
            }
            return null;
        }
        return package;
    }

    private static string QuotedValue(string line, string field)
    {
        var prefix = field + ": \"";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith('"'))
        {
            throw new SollangException($"invalid package lock field '{field}'");
        }
        return line[prefix.Length..^1]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
