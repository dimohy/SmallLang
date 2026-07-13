using SmallLang.Compiler.Diagnostics;
using SmallLang.Compiler.Lexing;

namespace SmallLang.Compiler.Cli;

internal sealed record ProjectManifest(string Path, string Name, string RootSource)
{
    public const string FileName = "smalllang.project";

    public string Directory => System.IO.Path.GetDirectoryName(Path)
        ?? System.IO.Directory.GetCurrentDirectory();

    public static ProjectManifest Load(string pathOrDirectory)
    {
        var manifestPath = System.IO.Directory.Exists(pathOrDirectory)
            ? System.IO.Path.Combine(pathOrDirectory, FileName)
            : pathOrDirectory;
        manifestPath = System.IO.Path.GetFullPath(manifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new SmallLangException($"project manifest not found: {manifestPath}");
        }

        var source = File.ReadAllText(manifestPath);
        var parser = new ManifestParser(new Lexer(source).Lex(), manifestPath);
        var (name, root) = parser.Parse();
        ValidateName(name, manifestPath);
        var projectDirectory = System.IO.Path.GetDirectoryName(manifestPath)
            ?? System.IO.Directory.GetCurrentDirectory();
        if (System.IO.Path.IsPathRooted(root))
        {
            throw new SmallLangException($"project root must be relative to the manifest: {root}");
        }

        var rootSource = System.IO.Path.GetFullPath(root, projectDirectory);
        var relativeRoot = System.IO.Path.GetRelativePath(projectDirectory, rootSource);
        if (relativeRoot == ".."
            || relativeRoot.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeRoot.StartsWith(".." + System.IO.Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new SmallLangException($"project root escapes the manifest directory: {root}");
        }
        if (!string.Equals(System.IO.Path.GetExtension(rootSource), ".sl", StringComparison.OrdinalIgnoreCase))
        {
            throw new SmallLangException($"project root must be an .sl source file: {root}");
        }
        if (!File.Exists(rootSource))
        {
            throw new SmallLangException($"project root source not found: {rootSource}");
        }

        return new ProjectManifest(manifestPath, name, rootSource);
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

    private static void ValidateName(string name, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SmallLangException($"project name must not be empty in {manifestPath}");
        }
        if (name.Any(static character => character < ' ' || "<>:\"/\\|?*".Contains(character)))
        {
            throw new SmallLangException($"project name is not a portable file name: {name}");
        }
    }

    private sealed class ManifestParser(IReadOnlyList<Token> tokens, string path)
    {
        private int _index;

        public (string Name, string Root) Parse()
        {
            SkipNewLines();
            var project = Expect(TokenKind.Identifier, "'project'");
            if (!string.Equals(project.Text, "project", StringComparison.Ordinal))
            {
                throw Error(project, "project manifest must start with 'project'");
            }
            Expect(TokenKind.LeftBrace, "'{'");

            string? name = null;
            string? root = null;
            SkipSeparators();
            while (!Check(TokenKind.RightBrace))
            {
                var field = Expect(TokenKind.Identifier, "field name");
                Expect(TokenKind.Colon, "':'");
                var value = Expect(TokenKind.String, "string literal");
                switch (field.Text)
                {
                    case "name" when name is null:
                        name = value.Text;
                        break;
                    case "root" when root is null:
                        root = value.Text;
                        break;
                    case "name" or "root":
                        throw Error(field, $"duplicate project field '{field.Text}'");
                    default:
                        throw Error(field, $"unknown project field '{field.Text}'");
                }

                if (!Check(TokenKind.RightBrace)
                    && !Check(TokenKind.NewLine)
                    && !Check(TokenKind.Comma))
                {
                    throw Error(Peek(), "project fields must be separated by a newline or comma");
                }
                SkipSeparators();
            }

            Expect(TokenKind.RightBrace, "'}'");
            SkipNewLines();
            Expect(TokenKind.End, "end of file");
            if (name is null)
            {
                throw new SmallLangException($"project manifest is missing required field 'name': {path}");
            }
            if (root is null)
            {
                throw new SmallLangException($"project manifest is missing required field 'root': {path}");
            }
            return (name, root);
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

        private SmallLangException Error(Token token, string message) =>
            new($"{path}({token.Line},{token.Column}): {message}");
    }
}
