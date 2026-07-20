using System.Globalization;
using System.Text.RegularExpressions;
using Sollang.Compiler.Diagnostics;

namespace Sollang.Compiler.Cli;

internal readonly partial record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease,
    string? BuildMetadata) : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string text, string context)
    {
        var match = VersionPattern().Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            throw new SollangException($"invalid semantic version '{text}' in {context}; expected SemVer 2.0.0 such as 1.2.3");
        }

        return new SemanticVersion(
            major,
            minor,
            patch,
            EmptyToNull(match.Groups["pre"].Value),
            EmptyToNull(match.Groups["build"].Value));
    }

    public int CompareTo(SemanticVersion other)
    {
        var numeric = Major.CompareTo(other.Major);
        if (numeric == 0) numeric = Minor.CompareTo(other.Minor);
        if (numeric == 0) numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;

        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = CompareIdentifier(left[index], right[index]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString()
    {
        var value = $"{Major.ToString(CultureInfo.InvariantCulture)}.{Minor.ToString(CultureInfo.InvariantCulture)}.{Patch.ToString(CultureInfo.InvariantCulture)}";
        if (PreRelease is not null) value += "-" + PreRelease;
        if (BuildMetadata is not null) value += "+" + BuildMetadata;
        return value;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightNumeric = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        if (leftNumeric && rightNumeric) return leftNumber.CompareTo(rightNumber);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    [GeneratedRegex(
        @"^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex VersionPattern();
}

internal sealed record VersionRequirement(
    string Text,
    Func<SemanticVersion, bool> Accepts,
    bool AllowsPreRelease)
{
    public static VersionRequirement Any { get; } = new("*", static _ => true, AllowsPreRelease: false);

    public static VersionRequirement Parse(string text, string context)
    {
        text = text.Trim();
        if (text == "*") return Any;
        if (text.Length == 0)
        {
            throw new SollangException($"empty version requirement in {context}");
        }

        var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var predicates = terms.Select(term => ParseTerm(term, context)).ToArray();
        return new VersionRequirement(
            text,
            version => predicates.All(predicate => predicate(version)),
            terms.Any(static term => term.Contains('-', StringComparison.Ordinal)));
    }

    private static Func<SemanticVersion, bool> ParseTerm(string term, string context)
    {
        if (term.StartsWith('^'))
        {
            var lower = SemanticVersion.Parse(term[1..], context);
            return lower.Major > 0
                ? version => version.Major == lower.Major && version.CompareTo(lower) >= 0
                : lower.Minor > 0
                    ? version => version.Major == 0 && version.Minor == lower.Minor && version.CompareTo(lower) >= 0
                    : version => version.Major == 0 && version.Minor == 0 && version.Patch == lower.Patch && version.CompareTo(lower) >= 0;
        }
        if (term.StartsWith('~'))
        {
            var lower = SemanticVersion.Parse(term[1..], context);
            return version => version.Major == lower.Major
                && version.Minor == lower.Minor
                && version.CompareTo(lower) >= 0;
        }

        var (operation, versionText) = term switch
        {
            _ when term.StartsWith(">=", StringComparison.Ordinal) => (">=", term[2..]),
            _ when term.StartsWith("<=", StringComparison.Ordinal) => ("<=", term[2..]),
            _ when term.StartsWith('>') => (">", term[1..]),
            _ when term.StartsWith('<') => ("<", term[1..]),
            _ when term.StartsWith('=') => ("=", term[1..]),
            _ => ("=", term)
        };
        var expected = SemanticVersion.Parse(versionText, context);
        return operation switch
        {
            ">=" => version => version.CompareTo(expected) >= 0,
            "<=" => version => version.CompareTo(expected) <= 0,
            ">" => version => version.CompareTo(expected) > 0,
            "<" => version => version.CompareTo(expected) < 0,
            _ => version => version.CompareTo(expected) == 0
        };
    }
}
