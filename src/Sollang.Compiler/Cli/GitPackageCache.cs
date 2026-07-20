using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal static class GitPackageCache
{
    public static ResolvedDependency Materialize(GitProjectDependencySource source, string cacheRoot)
    {
        var locationHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Location)))
            .ToLowerInvariant()[..32];
        var repository = Path.Combine(cacheRoot, "git", locationHash, "repository.git");
        var revisionRoot = Path.Combine(cacheRoot, "git", locationHash, source.Revision);
        var content = Path.Combine(revisionRoot, "source");
        var checksumPath = Path.Combine(revisionRoot, "source.sha256");

        Directory.CreateDirectory(Path.GetDirectoryName(repository)!);
        using var cacheLock = AcquireCacheLock(Path.Combine(cacheRoot, "git", locationHash, "cache.lock"));
        if (!Directory.Exists(repository))
        {
            RunGit(cacheRoot, "init", "--bare", repository);
        }
        if (!Directory.Exists(content))
        {
            FetchAndCheckout(source, repository, revisionRoot, content);
        }

        var checksum = "sha256:" + ComputeTreeChecksum(content);
        if (File.Exists(checksumPath))
        {
            var recorded = File.ReadAllText(checksumPath).Trim();
            if (!string.Equals(recorded, checksum, StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"cached git dependency content changed for {source.Location}#{source.Revision}; "
                    + $"remove '{revisionRoot}' and resolve again");
            }
        }
        else
        {
            File.WriteAllText(checksumPath, checksum + "\n", new UTF8Encoding(false));
        }

        var manifest = ProjectManifest.Load(content);
        return new ResolvedDependency(
            manifest,
            new GitPackageSource(source.Location, source.Revision, checksum, content));
    }

    private static void FetchAndCheckout(
        GitProjectDependencySource source,
        string repository,
        string revisionRoot,
        string content)
    {
        RunGit(repository, "--git-dir", repository, "fetch", "--no-tags", "--depth=1", source.Location, source.Revision);
        var resolved = RunGit(repository, "--git-dir", repository, "rev-parse", "FETCH_HEAD^{commit}").Trim();
        if (!string.Equals(resolved, source.Revision, StringComparison.OrdinalIgnoreCase))
        {
            throw new SollangException(
                $"git dependency resolved to '{resolved}' instead of required revision '{source.Revision}'");
        }

        var temporary = Path.Combine(revisionRoot, "source.tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            RunGit(repository, "--git-dir", repository, "--work-tree", temporary, "checkout", "--force", resolved, "--", ".");
            RejectLinks(temporary);
            Directory.CreateDirectory(revisionRoot);
            Directory.Move(temporary, content);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
            throw;
        }
    }

    private static string ComputeTreeChecksum(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> integer = stackalloc byte[sizeof(long)];
        foreach (var entry in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Select(path => new
                     {
                         Path = path,
                         Relative = Path.GetRelativePath(root, path).Replace('\\', '/')
                     })
                     .OrderBy(static entry => entry.Relative, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(entry.Relative);
            BinaryPrimitives.WriteInt64LittleEndian(integer, name.Length);
            hash.AppendData(integer);
            hash.AppendData(name);
            using var stream = File.OpenRead(entry.Path);
            BinaryPrimitives.WriteInt64LittleEndian(integer, stream.Length);
            hash.AppendData(integer);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void RejectLinks(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new SollangException($"git dependencies cannot contain symbolic links: {path}");
            }
        }
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)
            ?? throw new SollangException("failed to start git; install Git and ensure it is on PATH");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);
        var output = outputTask.Result;
        var error = errorTask.Result;
        if (process.ExitCode != 0)
        {
            throw new SollangException(
                $"git dependency command failed with exit code {process.ExitCode}: {error.Trim()}");
        }
        return output;
    }

    private static FileStream AcquireCacheLock(string path)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 399)
            {
                Thread.Sleep(25);
            }
        }
        throw new SollangException($"timed out waiting for git dependency cache lock: {path}");
    }
}
