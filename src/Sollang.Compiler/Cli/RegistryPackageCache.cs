using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Lexing;

namespace Sollang.Compiler.Cli;

internal static class RegistryPackageCache
{
    private const int MaxIndexBytes = 4 * 1024 * 1024;
    private const int MaxArchiveBytes = 256 * 1024 * 1024;
    private const long MaxExpandedBytes = 512L * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static ResolvedDependency Materialize(
        string name,
        VersionRequirement requirement,
        RegistryProjectDependencySource source,
        string cacheRoot,
        LockedRegistryPackage? lockedPackage,
        bool locked)
    {
        RegistryVersion selected;
        if (lockedPackage is not null)
        {
            selected = new RegistryVersion(lockedPackage.Version, lockedPackage.Checksum, Yanked: false);
        }
        else
        {
            if (locked)
            {
                throw new SollangException($"package lock has no usable registry resolution for '{name}'");
            }
            var indexUri = ResourceUri(source.Location, "v1", name, "index.slg");
            var indexBytes = ReadResource(indexUri, MaxIndexBytes, $"registry index for '{name}'");
            var index = RegistryIndexParser.Parse(Encoding.UTF8.GetString(indexBytes), indexUri.ToString());
            if (!string.Equals(index.Package, name, StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"registry index at {indexUri} declares package '{index.Package}' instead of '{name}'");
            }
            selected = index.Versions
                .Where(version => !version.Yanked
                    && requirement.Accepts(version.Version)
                    && (version.Version.PreRelease is null || requirement.AllowsPreRelease))
                .OrderByDescending(static version => version.Version)
                .FirstOrDefault()
                ?? throw new SollangException(
                    $"registry '{source.Location}' has no non-yanked version of '{name}' satisfying '{requirement.Text}'");
        }

        var registryHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(source.Location)))
            .ToLowerInvariant()[..32];
        var checksumHex = selected.Checksum["sha256:".Length..];
        var packageRoot = Path.Combine(
            cacheRoot,
            "registry",
            registryHash,
            name,
            selected.Version.ToString(),
            checksumHex);
        var archivePath = Path.Combine(packageRoot, "package.zip");
        var content = Path.Combine(packageRoot, "source");
        var treeChecksumPath = Path.Combine(packageRoot, "source.sha256");
        Directory.CreateDirectory(packageRoot);
        using var cacheLock = AcquireCacheLock(Path.Combine(packageRoot, "cache.lock"));

        if (!File.Exists(archivePath))
        {
            var archiveUri = ResourceUri(
                source.Location,
                "v1",
                name,
                selected.Version.ToString() + ".zip");
            var archive = ReadResource(archiveUri, MaxArchiveBytes, $"registry archive '{name}@{selected.Version}'");
            VerifyArchiveChecksum(archive, selected.Checksum, archiveUri.ToString());
            var temporary = archivePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, archive);
            File.Move(temporary, archivePath);
        }
        else
        {
            VerifyArchiveChecksum(File.ReadAllBytes(archivePath), selected.Checksum, archivePath);
        }

        if (!Directory.Exists(content))
        {
            ExtractArchive(archivePath, packageRoot, content);
        }
        var treeChecksum = "sha256:" + ComputeTreeChecksum(content);
        if (File.Exists(treeChecksumPath))
        {
            var recorded = File.ReadAllText(treeChecksumPath).Trim();
            if (!string.Equals(recorded, treeChecksum, StringComparison.Ordinal))
            {
                throw new SollangException(
                    $"cached registry package content changed for '{name}@{selected.Version}'; "
                    + $"remove '{packageRoot}' and resolve again");
            }
        }
        else
        {
            File.WriteAllText(treeChecksumPath, treeChecksum + "\n", new UTF8Encoding(false));
        }

        var manifest = ProjectManifest.Load(content);
        return new ResolvedDependency(
            manifest,
            new RegistryPackageSource(
                source.Location,
                selected.Version,
                selected.Checksum,
                content));
    }

    public static bool IsSha256(string value) =>
        value.Length == "sha256:".Length + 64
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value["sha256:".Length..].All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Uri ResourceUri(string location, params string[] segments)
    {
        var builder = new StringBuilder(location.TrimEnd('/'));
        foreach (var segment in segments)
        {
            builder.Append('/').Append(Uri.EscapeDataString(segment));
        }
        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static byte[] ReadResource(Uri uri, int maximumBytes, string description)
    {
        byte[] bytes;
        if (uri.IsFile)
        {
            if (!File.Exists(uri.LocalPath))
            {
                throw new SollangException($"{description} was not found: {uri.LocalPath}");
            }
            var length = new FileInfo(uri.LocalPath).Length;
            if (length > maximumBytes)
            {
                throw new SollangException($"{description} exceeds the {maximumBytes}-byte limit");
            }
            bytes = File.ReadAllBytes(uri.LocalPath);
        }
        else
        {
            try
            {
                using var response = Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                    .GetAwaiter()
                    .GetResult();
                if (!string.Equals(
                        response.RequestMessage?.RequestUri?.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.Ordinal))
                {
                    throw new SollangException($"{description} redirected outside HTTPS: {uri}");
                }
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new SollangException($"{description} was not found: {uri}");
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw new SollangException(
                        $"{description} request failed with HTTP {(int)response.StatusCode}: {uri}");
                }
                if (response.Content.Headers.ContentLength > maximumBytes)
                {
                    throw new SollangException($"{description} exceeds the {maximumBytes}-byte limit");
                }
                using var stream = response.Content.ReadAsStream();
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > maximumBytes)
                    {
                        throw new SollangException($"{description} exceeds the {maximumBytes}-byte limit");
                    }
                    buffer.Write(chunk, 0, read);
                }
                bytes = buffer.ToArray();
            }
            catch (SollangException)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
            {
                throw new SollangException($"failed to read {description} from {uri}: {error.Message}");
            }
        }
        return bytes;
    }

    private static void VerifyArchiveChecksum(byte[] archive, string expected, string source)
    {
        var actual = "sha256:" + Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SollangException(
                $"registry archive checksum mismatch for {source}: expected {expected}, actual {actual}");
        }
    }

    private static void ExtractArchive(string archivePath, string packageRoot, string content)
    {
        var temporary = Path.Combine(packageRoot, "source.tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaxEntries)
            {
                throw new SollangException($"registry archive has more than {MaxEntries} entries: {archivePath}");
            }
            long expandedBytes = 0;
            var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Contains('\\') || IsSymbolicLink(entry))
                {
                    throw new SollangException($"registry archive contains an unsafe entry: {entry.FullName}");
                }
                var destination = Path.GetFullPath(entry.FullName, temporary);
                if (!IsWithin(temporary, destination))
                {
                    throw new SollangException($"registry archive entry escapes its package: {entry.FullName}");
                }
                var portablePath = entry.FullName.TrimEnd('/');
                if (portablePath.Length == 0 || !portablePaths.Add(portablePath))
                {
                    throw new SollangException(
                        $"registry archive contains an empty or case-colliding path: {entry.FullName}");
                }
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaxExpandedBytes || entry.Length > MaxArchiveBytes)
                {
                    throw new SollangException($"registry archive expands beyond its size limit: {archivePath}");
                }
                if (entry.FullName.EndsWith('/', StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
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

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
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
        throw new SollangException($"timed out waiting for registry package cache lock: {path}");
    }

    private sealed record RegistryIndex(string Package, IReadOnlyList<RegistryVersion> Versions);

    private sealed record RegistryVersion(SemanticVersion Version, string Checksum, bool Yanked);

    private sealed class RegistryIndexParser(IReadOnlyList<Token> tokens, string sourcePath)
    {
        private int _index;

        public static RegistryIndex Parse(string source, string sourcePath) =>
            new RegistryIndexParser(new Lexer(source).Lex(), sourcePath).Parse();

        private RegistryIndex Parse()
        {
            SkipSeparators();
            ExpectIdentifier("registry");
            Expect(TokenKind.LeftBrace, "'{'");
            string? package = null;
            List<RegistryVersion>? versions = null;
            SkipSeparators();
            while (!Check(TokenKind.RightBrace))
            {
                var field = Expect(TokenKind.Identifier, "registry field");
                Expect(TokenKind.Colon, "':'");
                switch (field.Text)
                {
                    case "package" when package is null:
                        package = Expect(TokenKind.String, "package string").Text;
                        break;
                    case "versions" when versions is null:
                        versions = ParseVersions();
                        break;
                    case "package" or "versions":
                        throw Error(field, $"duplicate registry field '{field.Text}'");
                    default:
                        throw Error(field, $"unknown registry field '{field.Text}'");
                }
                RequireSeparator();
                SkipSeparators();
            }
            Expect(TokenKind.RightBrace, "'}'");
            SkipSeparators();
            Expect(TokenKind.End, "end of file");
            if (package is null || versions is null || versions.Count == 0)
            {
                throw new SollangException($"registry index requires package and non-empty versions: {sourcePath}");
            }
            return new RegistryIndex(package, versions);
        }

        private List<RegistryVersion> ParseVersions()
        {
            Expect(TokenKind.LeftBracket, "'['");
            var versions = new List<RegistryVersion>();
            SkipSeparators();
            while (!Check(TokenKind.RightBracket))
            {
                Expect(TokenKind.LeftBrace, "'{'");
                string? versionText = null;
                string? checksum = null;
                bool? yanked = null;
                SkipSeparators();
                while (!Check(TokenKind.RightBrace))
                {
                    var field = Expect(TokenKind.Identifier, "version field");
                    Expect(TokenKind.Colon, "':'");
                    switch (field.Text)
                    {
                        case "version" when versionText is null:
                            versionText = Expect(TokenKind.String, "version string").Text;
                            break;
                        case "checksum" when checksum is null:
                            checksum = Expect(TokenKind.String, "checksum string").Text;
                            break;
                        case "yanked" when yanked is null:
                            var value = Expect(TokenKind.Identifier, "true or false");
                            yanked = value.Text switch
                            {
                                "true" => true,
                                "false" => false,
                                _ => throw Error(value, "yanked must be true or false")
                            };
                            break;
                        case "version" or "checksum" or "yanked":
                            throw Error(field, $"duplicate version field '{field.Text}'");
                        default:
                            throw Error(field, $"unknown version field '{field.Text}'");
                    }
                    RequireSeparator();
                    SkipSeparators();
                }
                Expect(TokenKind.RightBrace, "'}'");
                if (versionText is null || checksum is null || yanked is null)
                {
                    throw new SollangException($"registry version requires version, checksum, and yanked: {sourcePath}");
                }
                if (!IsSha256(checksum))
                {
                    throw new SollangException($"invalid registry SHA-256 checksum in {sourcePath}: {checksum}");
                }
                var version = SemanticVersion.Parse(versionText, $"registry index {sourcePath}");
                if (versions.Any(candidate => candidate.Version.CompareTo(version) == 0))
                {
                    throw new SollangException($"duplicate registry version '{version}' in {sourcePath}");
                }
                versions.Add(new RegistryVersion(version, checksum, yanked.Value));
                RequireSeparator();
                SkipSeparators();
            }
            Expect(TokenKind.RightBracket, "']'");
            return versions;
        }

        private void ExpectIdentifier(string expected)
        {
            var token = Expect(TokenKind.Identifier, $"'{expected}'");
            if (!string.Equals(token.Text, expected, StringComparison.Ordinal))
            {
                throw Error(token, $"expected '{expected}'");
            }
        }

        private void RequireSeparator()
        {
            if (!Check(TokenKind.RightBrace)
                && !Check(TokenKind.RightBracket)
                && !Check(TokenKind.NewLine)
                && !Check(TokenKind.Comma))
            {
                throw Error(Peek(), "registry entries must be separated by a newline or comma");
            }
        }

        private void SkipSeparators()
        {
            while (Check(TokenKind.NewLine) || Check(TokenKind.Comma)) _index++;
        }

        private Token Expect(TokenKind kind, string expected)
        {
            var token = Peek();
            if (token.Kind != kind) throw Error(token, $"expected {expected}");
            _index++;
            return token;
        }

        private bool Check(TokenKind kind) => Peek().Kind == kind;

        private Token Peek() => tokens[Math.Min(_index, tokens.Count - 1)];

        private SollangException Error(Token token, string message) =>
            new($"{sourcePath}({token.Line},{token.Column}): {message}");
    }
}
