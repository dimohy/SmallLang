using Sollang.Compiler.Diagnostics;
using Sollang.Compiler.Lexing;

namespace Sollang.Compiler.Cli;

internal sealed record ProjectManifest(
    string Path,
    string Name,
    SemanticVersion Version,
    IReadOnlyDictionary<string, string> Products,
    IReadOnlyDictionary<string, ProjectDependency> Dependencies)
{
    public const string FileName = "sollang.project";

    public string Directory => System.IO.Path.GetDirectoryName(Path)
        ?? System.IO.Directory.GetCurrentDirectory();

    public ProjectProduct SelectProduct(string? requestedName)
    {
        if (requestedName is not null)
        {
            if (!Products.TryGetValue(requestedName, out var selectedRoot))
            {
                throw new SollangException(
                    $"project '{Name}' has no product '{requestedName}'; available products: "
                    + string.Join(", ", Products.Keys.Order(StringComparer.Ordinal)));
            }
            return new ProjectProduct(requestedName, selectedRoot);
        }

        if (Products.Count != 1)
        {
            throw new SollangException(
                $"project '{Name}' has multiple products; select one with --product: "
                + string.Join(", ", Products.Keys.Order(StringComparer.Ordinal)));
        }

        var only = Products.Single();
        return new ProjectProduct(only.Key, only.Value);
    }

    public static ProjectManifest Load(string pathOrDirectory)
    {
        var manifestPath = System.IO.Directory.Exists(pathOrDirectory)
            ? System.IO.Path.Combine(pathOrDirectory, FileName)
            : pathOrDirectory;
        manifestPath = System.IO.Path.GetFullPath(manifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new SollangException($"project manifest not found: {manifestPath}");
        }

        var source = File.ReadAllText(manifestPath);
        var parser = new ManifestParser(new Lexer(source).Lex(), manifestPath);
        var parsed = parser.Parse();
        ValidateProjectName(parsed.Name, manifestPath);

        if (parsed.Root is not null && parsed.Products is not null)
        {
            throw new SollangException(
                $"project manifest cannot declare both 'root' and 'products': {manifestPath}");
        }
        if (parsed.Root is null && parsed.Products is null)
        {
            throw new SollangException(
                $"project manifest requires either 'root' or 'products': {manifestPath}");
        }

        var projectDirectory = System.IO.Path.GetDirectoryName(manifestPath)
            ?? System.IO.Directory.GetCurrentDirectory();
        IReadOnlyDictionary<string, string> products;
        if (parsed.Root is not null)
        {
            products = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [parsed.Name] = ResolveRoot(parsed.Root, projectDirectory, manifestPath)
            };
        }
        else
        {
            if (parsed.Products!.Count == 0)
            {
                throw new SollangException($"project 'products' must not be empty: {manifestPath}");
            }
            products = parsed.Products.ToDictionary(
                pair => ValidateEntryName(pair.Key, "product", manifestPath),
                pair => ResolveRoot(pair.Value, projectDirectory, manifestPath),
                StringComparer.Ordinal);
        }

        var version = SemanticVersion.Parse(parsed.Version, $"project manifest {manifestPath}");
        var dependencies = (parsed.Dependencies ?? new Dictionary<string, ParsedDependency>(StringComparer.Ordinal))
            .ToDictionary(
                pair => ValidateEntryName(pair.Key, "dependency", manifestPath),
                pair => new ProjectDependency(
                    pair.Value.Path is not null
                        ? new PathProjectDependencySource(
                            ResolveDependencyPath(pair.Value.Path, projectDirectory, manifestPath))
                        : pair.Value.Git is not null
                            ? new GitProjectDependencySource(
                                ValidateGitLocation(pair.Value.Git, projectDirectory, manifestPath),
                                ValidateGitRevision(pair.Value.Revision!, manifestPath))
                            : new RegistryProjectDependencySource(
                                ValidateRegistryLocation(pair.Value.Registry!, projectDirectory, manifestPath)),
                    pair.Value.Version is null
                        ? VersionRequirement.Any
                        : VersionRequirement.Parse(
                            pair.Value.Version,
                            $"dependency '{pair.Key}' in {manifestPath}")),
                StringComparer.Ordinal);
        return new ProjectManifest(manifestPath, parsed.Name, version, products, dependencies);
    }

    public static string? FindFrom(string startDirectory)
    {
        for (var current = new DirectoryInfo(System.IO.Path.GetFullPath(startDirectory));
             current is not null;
             current = current.Parent)
        {
            var candidate = System.IO.Path.Combine(current.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string ResolveRoot(string root, string projectDirectory, string manifestPath)
    {
        if (System.IO.Path.IsPathRooted(root))
        {
            throw new SollangException($"project root must be relative to the manifest: {root}");
        }

        var rootSource = System.IO.Path.GetFullPath(root, projectDirectory);
        var relativeRoot = System.IO.Path.GetRelativePath(projectDirectory, rootSource);
        if (relativeRoot == ".."
            || relativeRoot.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeRoot.StartsWith(".." + System.IO.Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new SollangException($"project root escapes the manifest directory: {root}");
        }
        if (!string.Equals(System.IO.Path.GetExtension(rootSource), ".slg", StringComparison.OrdinalIgnoreCase))
        {
            throw new SollangException($"project root must be an .slg source file: {root}");
        }
        if (!File.Exists(rootSource))
        {
            throw new SollangException($"project root source not found: {rootSource}");
        }
        return rootSource;
    }

    private static string ResolveDependencyPath(string path, string projectDirectory, string manifestPath)
    {
        if (System.IO.Path.IsPathRooted(path))
        {
            throw new SollangException(
                $"dependency path must be relative to the manifest in {manifestPath}: {path}");
        }
        return System.IO.Path.GetFullPath(path, projectDirectory);
    }

    private static string ValidateGitLocation(string location, string projectDirectory, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new SollangException($"dependency git location must not be empty in {manifestPath}");
        }
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("https" or "ssh" or "file"))
            {
                throw new SollangException($"dependency git URL must use https, ssh, or file in {manifestPath}: {location}");
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new SollangException($"dependency git URL must not contain credentials in {manifestPath}");
            }
            return uri.AbsoluteUri;
        }
        return System.IO.Path.GetFullPath(location, projectDirectory);
    }

    private static string ValidateGitRevision(string revision, string manifestPath)
    {
        if (revision.Length is not (40 or 64)
            || revision.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new SollangException(
                $"dependency git revision must be a full 40- or 64-digit commit hash in {manifestPath}: {revision}");
        }
        return revision.ToLowerInvariant();
    }

    private static string ValidateRegistryLocation(string location, string projectDirectory, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            throw new SollangException($"dependency registry location must not be empty in {manifestPath}");
        }
        Uri uri;
        if (Uri.TryCreate(location, UriKind.Absolute, out var absolute))
        {
            uri = absolute;
        }
        else
        {
            uri = new Uri(System.IO.Path.GetFullPath(location, projectDirectory));
        }
        if (uri.Scheme is not ("https" or "file"))
        {
            throw new SollangException(
                $"dependency registry URL must use https or file in {manifestPath}: {location}");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new SollangException(
                $"dependency registry URL must not contain credentials, a query, or a fragment in {manifestPath}");
        }
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static void ValidateProjectName(string name, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SollangException($"project name must not be empty in {manifestPath}");
        }
        if (name.Any(static character => character < ' ' || "<>:\"/\\|?*".Contains(character)))
        {
            throw new SollangException($"project name is not a portable file name: {name}");
        }
    }

    private static string ValidateEntryName(string name, string kind, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SollangException($"{kind} name must not be empty in {manifestPath}");
        }
        if (!IsIdentifierStart(name[0]) || name.Skip(1).Any(static character => !IsIdentifierPart(character)))
        {
            throw new SollangException(
                $"{kind} name must be an import-safe identifier in {manifestPath}: {name}");
        }
        return name;
    }

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);

    private sealed class ManifestParser(IReadOnlyList<Token> tokens, string path)
    {
        private int _index;

        public ParsedManifest Parse()
        {
            SkipNewLines();
            var project = Expect(TokenKind.Identifier, "'project'");
            if (!string.Equals(project.Text, "project", StringComparison.Ordinal))
            {
                throw Error(project, "project manifest must start with 'project'");
            }
            Expect(TokenKind.LeftBrace, "'{'");

            string? name = null;
            string? version = null;
            string? root = null;
            Dictionary<string, string>? products = null;
            Dictionary<string, ParsedDependency>? dependencies = null;
            SkipSeparators();
            while (!Check(TokenKind.RightBrace))
            {
                var field = Expect(TokenKind.Identifier, "field name");
                Expect(TokenKind.Colon, "':'");
                switch (field.Text)
                {
                    case "name" when name is null:
                        name = Expect(TokenKind.String, "string literal").Text;
                        break;
                    case "version" when version is null:
                        version = Expect(TokenKind.String, "string literal").Text;
                        break;
                    case "root" when root is null:
                        root = Expect(TokenKind.String, "string literal").Text;
                        break;
                    case "products" when products is null:
                        products = ParseStringMap("products");
                        break;
                    case "dependencies" when dependencies is null:
                        dependencies = ParseDependencyMap();
                        break;
                    case "name" or "version" or "root" or "products" or "dependencies":
                        throw Error(field, $"duplicate project field '{field.Text}'");
                    default:
                        throw Error(field, $"unknown project field '{field.Text}'");
                }

                RequireSeparator("project fields");
                SkipSeparators();
            }

            Expect(TokenKind.RightBrace, "'}'");
            SkipNewLines();
            Expect(TokenKind.End, "end of file");
            if (name is null)
            {
                throw new SollangException($"project manifest is missing required field 'name': {path}");
            }
            if (version is null)
            {
                throw new SollangException($"project manifest is missing required field 'version': {path}");
            }
            return new ParsedManifest(name, version, root, products, dependencies);
        }

        private Dictionary<string, ParsedDependency> ParseDependencyMap()
        {
            Expect(TokenKind.LeftBrace, "'{'");
            var values = new Dictionary<string, ParsedDependency>(StringComparer.Ordinal);
            SkipSeparators();
            while (!Check(TokenKind.RightBrace))
            {
                var key = Expect(TokenKind.Identifier, "dependencies entry name");
                Expect(TokenKind.Colon, "':'");
                ParsedDependency value;
                if (Check(TokenKind.String))
                {
                    value = new ParsedDependency(Expect(TokenKind.String, "string literal").Text, null, null, null, null);
                }
                else
                {
                    Expect(TokenKind.LeftBrace, "'{' or path string");
                    string? dependencyPath = null;
                    string? dependencyGit = null;
                    string? dependencyRegistry = null;
                    string? dependencyRevision = null;
                    string? dependencyVersion = null;
                    SkipSeparators();
                    while (!Check(TokenKind.RightBrace))
                    {
                        var field = Expect(TokenKind.Identifier, "dependency field name");
                        Expect(TokenKind.Colon, "':'");
                        switch (field.Text)
                        {
                            case "path" when dependencyPath is null:
                                dependencyPath = Expect(TokenKind.String, "string literal").Text;
                                break;
                            case "git" when dependencyGit is null:
                                dependencyGit = Expect(TokenKind.String, "string literal").Text;
                                break;
                            case "registry" when dependencyRegistry is null:
                                dependencyRegistry = Expect(TokenKind.String, "string literal").Text;
                                break;
                            case "rev" when dependencyRevision is null:
                                dependencyRevision = Expect(TokenKind.String, "string literal").Text;
                                break;
                            case "version" when dependencyVersion is null:
                                dependencyVersion = Expect(TokenKind.String, "string literal").Text;
                                break;
                            case "path" or "git" or "registry" or "rev" or "version":
                                throw Error(field, $"duplicate dependency field '{field.Text}'");
                            default:
                                throw Error(field, $"unknown dependency field '{field.Text}'");
                        }
                        RequireSeparator("dependency fields");
                        SkipSeparators();
                    }
                    Expect(TokenKind.RightBrace, "'}'");
                    var sourceCount = (dependencyPath is null ? 0 : 1)
                        + (dependencyGit is null ? 0 : 1)
                        + (dependencyRegistry is null ? 0 : 1);
                    if (sourceCount != 1)
                    {
                        throw Error(key, $"dependency '{key.Text}' must declare exactly one of 'path', 'git', or 'registry'");
                    }
                    if (dependencyGit is not null && dependencyRevision is null)
                    {
                        throw Error(key, $"git dependency '{key.Text}' is missing required field 'rev'");
                    }
                    if (dependencyGit is null && dependencyRevision is not null)
                    {
                        throw Error(key, $"non-git dependency '{key.Text}' cannot declare 'rev'");
                    }
                    value = new ParsedDependency(
                        dependencyPath,
                        dependencyGit,
                        dependencyRegistry,
                        dependencyRevision,
                        dependencyVersion);
                }
                if (!values.TryAdd(key.Text, value))
                {
                    throw Error(key, $"duplicate dependencies entry '{key.Text}'");
                }
                RequireSeparator("dependencies entries");
                SkipSeparators();
            }
            Expect(TokenKind.RightBrace, "'}'");
            return values;
        }

        private Dictionary<string, string> ParseStringMap(string fieldName)
        {
            Expect(TokenKind.LeftBrace, "'{'");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            SkipSeparators();
            while (!Check(TokenKind.RightBrace))
            {
                var key = Expect(TokenKind.Identifier, $"{fieldName} entry name");
                Expect(TokenKind.Colon, "':'");
                var value = Expect(TokenKind.String, "string literal");
                if (!values.TryAdd(key.Text, value.Text))
                {
                    throw Error(key, $"duplicate {fieldName} entry '{key.Text}'");
                }
                RequireSeparator($"{fieldName} entries");
                SkipSeparators();
            }
            Expect(TokenKind.RightBrace, "'}'");
            return values;
        }

        private void RequireSeparator(string description)
        {
            if (!Check(TokenKind.RightBrace)
                && !Check(TokenKind.NewLine)
                && !Check(TokenKind.Comma))
            {
                throw Error(Peek(), $"{description} must be separated by a newline or comma");
            }
        }

        private void SkipSeparators()
        {
            while (Check(TokenKind.NewLine) || Check(TokenKind.Comma))
            {
                _index++;
            }
        }

        private void SkipNewLines()
        {
            while (Check(TokenKind.NewLine))
            {
                _index++;
            }
        }

        private Token Expect(TokenKind kind, string expected)
        {
            var token = Peek();
            if (token.Kind != kind)
            {
                throw Error(token, $"expected {expected}");
            }
            _index++;
            return token;
        }

        private bool Check(TokenKind kind) => Peek().Kind == kind;

        private Token Peek() => tokens[Math.Min(_index, tokens.Count - 1)];

        private SollangException Error(Token token, string message) =>
            new($"{path}({token.Line},{token.Column}): {message}");
    }

    private sealed record ParsedManifest(
        string Name,
        string Version,
        string? Root,
        Dictionary<string, string>? Products,
        Dictionary<string, ParsedDependency>? Dependencies);

    private sealed record ParsedDependency(
        string? Path,
        string? Git,
        string? Registry,
        string? Revision,
        string? Version);
}

internal sealed record ProjectProduct(string Name, string RootSource);

internal sealed record ProjectDependency(ProjectDependencySource Source, VersionRequirement Version);

internal abstract record ProjectDependencySource;

internal sealed record PathProjectDependencySource(string Path) : ProjectDependencySource;

internal sealed record GitProjectDependencySource(string Location, string Revision) : ProjectDependencySource;

internal sealed record RegistryProjectDependencySource(string Location) : ProjectDependencySource;
