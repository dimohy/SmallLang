using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sollang.Compiler.Semantics;

namespace Sollang.Compiler.Cli;

internal sealed record IncrementalSemanticCacheProbe(
    string Status,
    SemanticReusePlan? ReusePlan,
    IReadOnlyList<string> Functions,
    IReadOnlyList<KeyValuePair<string, string>> Calls);

internal sealed class IncrementalSemanticCache
{
    private const ulong Magic = 6002245291165495635;
    private const ulong Schema = 4;
    private const int DigestLength = 32;
    private const int HeaderWords = 11;
    private const int MaximumRecords = 1_000_000;
    private const int MaximumIdentityBytes = 1024 * 1024;
    private const long MaximumArtifactBytes = 1024L * 1024 * 1024;

    private readonly IncrementalCacheLocation _location;
    private readonly byte[] _declarationFingerprint;
    private readonly string[] _functions;
    private readonly KeyValuePair<string, string>[] _calls;
    private readonly KeyValuePair<string, byte[]>[] _modules;
    private readonly SemanticFunctionReuse[] _reusableFunctions;
    private readonly SemanticSpecializationReuse[] _specializations;
    private readonly string? _mainModuleName;
    private readonly IReadOnlyDictionary<string, string>? _mainBindings;

    private IncrementalSemanticCache(
        IncrementalCacheLocation location,
        byte[] declarationFingerprint,
        string[] functions,
        KeyValuePair<string, string>[] calls,
        KeyValuePair<string, byte[]>[] modules,
        SemanticFunctionReuse[] reusableFunctions,
        SemanticSpecializationReuse[] specializations,
        string? mainModuleName,
        IReadOnlyDictionary<string, string>? mainBindings,
        string status,
        int mappedFunctions,
        int mappedCalls,
        int reusedFunctions,
        int totalReusableFunctions,
        bool reusedMainSemantics)
    {
        _location = location;
        _declarationFingerprint = declarationFingerprint;
        _functions = functions;
        _calls = calls;
        _modules = modules;
        _reusableFunctions = reusableFunctions;
        _specializations = specializations;
        _mainModuleName = mainModuleName;
        _mainBindings = mainBindings;
        Status = status;
        MappedFunctions = mappedFunctions;
        MappedCalls = mappedCalls;
        ReusedFunctions = reusedFunctions;
        TotalReusableFunctions = totalReusableFunctions;
        ReusedMainSemantics = reusedMainSemantics;
    }

    public string Status { get; }
    public int MappedFunctions { get; }
    public int TotalFunctions => _functions.Length;
    public int MappedCalls { get; }
    public int TotalCalls => _calls.Length;
    public int ReusedFunctions { get; }
    public int TotalReusableFunctions { get; }
    public bool ReusedMainSemantics { get; }
    public string Path => _location.SemanticPath;

    public static IncrementalSemanticCacheProbe Probe(
        IncrementalCacheLocation location,
        LoadedCompilation compilation)
    {
        if (!File.Exists(location.SemanticPath))
            return EmptyProbe("cold");

        try
        {
            var old = Read(location);
            var currentModules = BuildModuleHashes(compilation).ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            var oldModules = old.Modules.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            var exactModules = currentModules
                .Where(pair => oldModules.TryGetValue(pair.Key, out var oldHash)
                    && CryptographicOperations.FixedTimeEquals(pair.Value, oldHash))
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            var reusable = old.ReusableFunctions
                .Where(function => exactModules.Contains(function.ModuleName))
                .ToDictionary(static function => function.Identity, StringComparer.Ordinal);
            var specializations = old.Specializations
                .Where(specialization => exactModules.Contains(specialization.ModuleName))
                .ToDictionary(static specialization => specialization.Identity, StringComparer.Ordinal);
            var mainBindings = old.MainModuleName is not null
                && exactModules.Contains(old.MainModuleName)
                    ? old.MainBindings
                    : null;
            return new IncrementalSemanticCacheProbe(
                "loaded",
                new SemanticReusePlan(
                    old.DeclarationFingerprint,
                    reusable,
                    old.Calls.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
                    specializations,
                    mainBindings is null ? null : old.MainModuleName,
                    mainBindings),
                old.Functions,
                old.Calls);
        }
        catch (SemanticCacheMissException error)
        {
            return EmptyProbe("miss: " + error.Message);
        }
        catch (Exception error) when (error is IOException
                                      or InvalidDataException
                                      or DecoderFallbackException
                                      or CryptographicException)
        {
            return EmptyProbe("rejected: " + error.Message);
        }
    }

    public static IncrementalSemanticCache Create(
        IncrementalCacheLocation location,
        LoadedCompilation compilation,
        BoundProgram program,
        IncrementalSemanticCacheProbe probe)
    {
        var functions = program.StableFunctionIdentities.Values
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var calls = program.StableCallSiteIdentities
            .Select(pair => new KeyValuePair<string, string>(
                pair.Value,
                program.StableFunctionIdentities[program.ResolvedGenericCalls[pair.Key]]))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        EnsureUniquePairs(calls, "semantic call-site identity collision");
        var modules = BuildModuleHashes(compilation);
        var reusableFunctions = BuildReusableFunctions(program);
        var specializations = BuildSpecializations(program);
        var (mainModuleName, mainBindings) = BuildReusableMain(compilation, program);

        var oldFunctions = probe.Functions.ToHashSet(StringComparer.Ordinal);
        var oldCalls = probe.Calls.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var mappedFunctions = functions.Count(oldFunctions.Contains);
        var mappedCalls = calls.Count(pair =>
            oldCalls.TryGetValue(pair.Key, out var target)
            && StringComparer.Ordinal.Equals(target, pair.Value));
        return new IncrementalSemanticCache(
            location,
            program.StableDeclarationFingerprint,
            functions,
            calls,
            modules,
            reusableFunctions,
            specializations,
            mainModuleName,
            mainBindings,
            probe.Status,
            mappedFunctions,
            mappedCalls,
            program.ReusedSemanticFunctions,
            program.TotalSemanticFunctions,
            program.ReusedMainSemantics);
    }

    public void Publish()
    {
        var directory = System.IO.Path.GetDirectoryName(_location.SemanticPath)
            ?? throw new InvalidOperationException("semantic cache path has no directory");
        Directory.CreateDirectory(directory);
        var temporaryPath = _location.SemanticPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            using (var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                WriteUInt64(stream, checksum, Magic);
                WriteUInt64(stream, checksum, Schema);
                WriteUInt64(stream, checksum, _location.CompilerHash);
                WriteUInt64(stream, checksum, _location.TargetHash);
                WriteUInt64(stream, checksum, _location.ConfigurationHash);
                WriteUInt64(stream, checksum, checked((ulong)_functions.Length));
                WriteUInt64(stream, checksum, checked((ulong)_calls.Length));
                WriteUInt64(stream, checksum, checked((ulong)_modules.Length));
                WriteUInt64(stream, checksum, checked((ulong)_reusableFunctions.Length));
                WriteUInt64(stream, checksum, checked((ulong)_specializations.Length));
                WriteUInt64(stream, checksum, _mainBindings is null ? 0UL : 1UL);
                WriteDigest(stream, checksum, _declarationFingerprint);
                if (_mainBindings is not null)
                {
                    WriteString(stream, checksum, _mainModuleName!);
                    WriteBindings(stream, checksum, _mainBindings);
                }
                foreach (var function in _functions)
                    WriteString(stream, checksum, function);
                foreach (var call in _calls)
                {
                    WriteString(stream, checksum, call.Key);
                    WriteString(stream, checksum, call.Value);
                }
                foreach (var module in _modules)
                {
                    WriteString(stream, checksum, module.Key);
                    WriteDigest(stream, checksum, module.Value);
                }
                foreach (var function in _reusableFunctions)
                {
                    WriteString(stream, checksum, function.Identity);
                    WriteString(stream, checksum, function.ModuleName);
                    WriteBindings(stream, checksum, function.Bindings);
                    WriteBindings(stream, checksum, function.CapturedBindings);
                }
                foreach (var specialization in _specializations)
                    WriteSpecialization(stream, checksum, specialization);
                stream.Write(checksum.GetHashAndReset());
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _location.SemanticPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static IncrementalSemanticCacheProbe EmptyProbe(string status) =>
        new(status, null, [], []);

    private static SemanticFunctionReuse[] BuildReusableFunctions(BoundProgram program)
    {
        var seen = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        return EnumerateFunctions(program.Functions.Values, seen)
            .Where(static function => function.Kind is BoundFunctionKind.User or BoundFunctionKind.UserBlock)
            .Where(program.FunctionBindings.ContainsKey)
            .Select(function => new
            {
                Function = function,
                Identity = program.StableFunctionIdentities[function]
            })
            .Select(item => new SemanticFunctionReuse(
                item.Identity,
                item.Function.ModuleName,
                StableBindings(program.Types, program.FunctionBindings[item.Function]),
                program.FunctionCapturedBindings.TryGetValue(item.Function, out var captured)
                    ? StableBindings(program.Types, captured)
                    : new Dictionary<string, string>(StringComparer.Ordinal)))
            .OrderBy(static function => function.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<BoundFunction> EnumerateFunctions(
        IEnumerable<BoundFunction> functions,
        ISet<BoundFunction> seen)
    {
        foreach (var function in functions)
        {
            if (!seen.Add(function))
                continue;
            yield return function;
            foreach (var local in EnumerateFunctions(function.LocalFunctions.Values, seen))
                yield return local;
        }
    }

    private static (string? ModuleName, IReadOnlyDictionary<string, string>? Bindings) BuildReusableMain(
        LoadedCompilation compilation,
        BoundProgram program)
    {
        var executable = compilation.Sources.SingleOrDefault(static source =>
            source.Program.Statements.Count > 0);
        return executable is null
            ? (null, null)
            : (executable.ModuleName, StableBindings(program.Types, program.MainBindings));
    }

    private static SemanticSpecializationReuse[] BuildSpecializations(BoundProgram program)
    {
        var seenFunctions = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        var distinctFunctions = program.Functions.Values.Where(seenFunctions.Add).ToArray();
        var templates = distinctFunctions
            .Where(static function => function.GenericParameterName is not null
                && function.SpecializedType is null
                && function.SpecializedValue is null)
            .ToArray();
        var seenTargets = new HashSet<BoundFunction>(ReferenceEqualityComparer.Instance);
        return program.ResolvedGenericCalls.Values
            .Where(seenTargets.Add)
            .Where(target => !IsDeclaredUnspecializedTarget(target, distinctFunctions))
            .Select(target =>
            {
                var template = templates
                    .Where(candidate => StringComparer.Ordinal.Equals(candidate.ModuleName, target.ModuleName)
                        && candidate.Kind == target.Kind
                        && target.Name.StartsWith(candidate.Name + "$", StringComparison.Ordinal))
                    .OrderByDescending(static candidate => candidate.Name.Length)
                    .FirstOrDefault();
                return new SemanticSpecializationReuse(
                    program.StableFunctionIdentities[target],
                    template is null ? null : program.StableFunctionIdentities[template],
                    target.Name,
                    target.ModuleName,
                    target.Kind,
                    target.InputName,
                    StableType(program, target.InputType),
                    target.InputOwnership,
                    SemanticStableIdentity.Type(program.Types, target.ReturnType),
                    target.BlockInputName,
                    StableType(program, target.BlockInputType),
                    StableType(program, target.BlockResultType),
                    StableType(program, target.SpecializedType),
                    StableType(program, target.SpecializedSecondaryType),
                    StableType(program, target.SpecializedTertiaryType),
                    target.SpecializedValue,
                    target.IsStandardLibrary,
                    target.IsLocal,
                    target.IsPublic,
                    target.IsAsync);
            })
            .OrderBy(static specialization => specialization.Identity, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsDeclaredUnspecializedTarget(
        BoundFunction target,
        IReadOnlyList<BoundFunction> declaredFunctions) =>
        target.SpecializedType is null
        && target.SpecializedValue is null
        && declaredFunctions.Contains(target, ReferenceEqualityComparer.Instance);

    private static string? StableType(BoundProgram program, TypeId? type) =>
        type is null ? null : SemanticStableIdentity.Type(program.Types, type.Value);

    private static IReadOnlyDictionary<string, string> StableBindings(
        TypeDefinitionTable types,
        IReadOnlyDictionary<string, BoundType> bindings) =>
        bindings.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(
            static pair => pair.Key,
            pair => SemanticStableIdentity.Type(types, pair.Value),
            StringComparer.Ordinal);

    private static KeyValuePair<string, byte[]>[] BuildModuleHashes(LoadedCompilation compilation)
    {
        return compilation.Sources
            .GroupBy(static source => source.ModuleName, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, byte[]>(group.Key, HashModule(group)))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static byte[] HashModule(IEnumerable<CompilationSource> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var source in sources.OrderBy(static source => source.Path, StringComparer.Ordinal))
        {
            AppendHashUInt64(hash, checked((ulong)Encoding.UTF8.GetByteCount(source.Path)));
            hash.AppendData(Encoding.UTF8.GetBytes(source.Path));
            AppendHashUInt64(hash, checked((ulong)source.SourceBytes.Length));
            hash.AppendData(source.SourceBytes);
        }
        return hash.GetHashAndReset();
    }

    private static DecodedSemanticCache Read(IncrementalCacheLocation location)
    {
        var length = new FileInfo(location.SemanticPath).Length;
        if (length < HeaderWords * sizeof(ulong) + DigestLength * 2
            || length > MaximumArtifactBytes)
            throw new InvalidDataException("semantic generation length is invalid");

        using var stream = new FileStream(
            location.SemanticPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (ReadUInt64(stream, checksum) != Magic || ReadUInt64(stream, checksum) != Schema)
            throw new InvalidDataException("semantic generation magic or schema mismatch");
        if (ReadUInt64(stream, checksum) != location.CompilerHash
            || ReadUInt64(stream, checksum) != location.TargetHash
            || ReadUInt64(stream, checksum) != location.ConfigurationHash)
            throw new SemanticCacheMissException("compiler, target, or configuration changed");

        var functionCount = CheckedCount(ReadUInt64(stream, checksum));
        var callCount = CheckedCount(ReadUInt64(stream, checksum));
        var moduleCount = CheckedCount(ReadUInt64(stream, checksum));
        var reusableCount = CheckedCount(ReadUInt64(stream, checksum));
        var specializationCount = CheckedCount(ReadUInt64(stream, checksum));
        var hasMain = ReadUInt64(stream, checksum) switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException("semantic main presence is invalid")
        };
        var declarationFingerprint = ReadDigest(stream, checksum);
        var mainModuleName = hasMain ? ReadString(stream, checksum) : null;
        var mainBindings = hasMain ? ReadBindings(stream, checksum) : null;
        var functions = ReadCanonicalStrings(stream, checksum, functionCount, "function identities");
        var calls = ReadCanonicalPairs(stream, checksum, callCount, "call-site identities");
        var modules = new KeyValuePair<string, byte[]>[moduleCount];
        string? previous = null;
        for (var index = 0; index < moduleCount; index++)
        {
            var name = ReadString(stream, checksum);
            RequireIncreasing(previous, name, "module identities");
            modules[index] = new KeyValuePair<string, byte[]>(name, ReadDigest(stream, checksum));
            previous = name;
        }
        var reusable = new SemanticFunctionReuse[reusableCount];
        previous = null;
        for (var index = 0; index < reusableCount; index++)
        {
            var identity = ReadString(stream, checksum);
            RequireIncreasing(previous, identity, "reusable function identities");
            var module = ReadString(stream, checksum);
            var bindings = ReadBindings(stream, checksum);
            var captured = ReadBindings(stream, checksum);
            reusable[index] = new SemanticFunctionReuse(identity, module, bindings, captured);
            previous = identity;
        }
        var specializations = new SemanticSpecializationReuse[specializationCount];
        previous = null;
        for (var index = 0; index < specializationCount; index++)
        {
            var specialization = ReadSpecialization(stream, checksum);
            RequireIncreasing(previous, specialization.Identity, "specialization identities");
            specializations[index] = specialization;
            previous = specialization.Identity;
        }
        if (stream.Position != length - DigestLength)
            throw new InvalidDataException("semantic generation declared lengths do not reach the checksum");
        Span<byte> declared = stackalloc byte[DigestLength];
        stream.ReadExactly(declared);
        if (!CryptographicOperations.FixedTimeEquals(checksum.GetHashAndReset(), declared))
            throw new InvalidDataException("semantic generation checksum mismatch");
        return new DecodedSemanticCache(
            declarationFingerprint,
            functions,
            calls,
            modules,
            reusable,
            specializations,
            mainModuleName,
            mainBindings);
    }

    private static string[] ReadCanonicalStrings(
        Stream stream,
        IncrementalHash checksum,
        int count,
        string description)
    {
        var result = new string[count];
        string? previous = null;
        for (var index = 0; index < count; index++)
        {
            var value = ReadString(stream, checksum);
            RequireIncreasing(previous, value, description);
            result[index] = value;
            previous = value;
        }
        return result;
    }

    private static KeyValuePair<string, string>[] ReadCanonicalPairs(
        Stream stream,
        IncrementalHash checksum,
        int count,
        string description)
    {
        var result = new KeyValuePair<string, string>[count];
        string? previous = null;
        for (var index = 0; index < count; index++)
        {
            var key = ReadString(stream, checksum);
            RequireIncreasing(previous, key, description);
            result[index] = new KeyValuePair<string, string>(key, ReadString(stream, checksum));
            previous = key;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ReadBindings(
        Stream stream,
        IncrementalHash checksum)
    {
        var pairs = ReadCanonicalPairs(
            stream, checksum, CheckedCount(ReadUInt64(stream, checksum)), "binding names");
        return pairs.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
    }

    private static void WriteSpecialization(
        Stream stream,
        IncrementalHash checksum,
        SemanticSpecializationReuse specialization)
    {
        WriteString(stream, checksum, specialization.Identity);
        WriteOptionalString(stream, checksum, specialization.TemplateIdentity);
        WriteString(stream, checksum, specialization.Name);
        WriteString(stream, checksum, specialization.ModuleName);
        WriteUInt64(stream, checksum, checked((ulong)specialization.Kind));
        WriteOptionalString(stream, checksum, specialization.InputName);
        WriteOptionalString(stream, checksum, specialization.InputType);
        WriteUInt64(stream, checksum, checked((ulong)specialization.InputOwnership));
        WriteString(stream, checksum, specialization.ReturnType);
        WriteOptionalString(stream, checksum, specialization.BlockInputName);
        WriteOptionalString(stream, checksum, specialization.BlockInputType);
        WriteOptionalString(stream, checksum, specialization.BlockResultType);
        WriteOptionalString(stream, checksum, specialization.SpecializedType);
        WriteOptionalString(stream, checksum, specialization.SpecializedSecondaryType);
        WriteOptionalString(stream, checksum, specialization.SpecializedTertiaryType);
        WriteOptionalString(
            stream,
            checksum,
            specialization.SpecializedValue?.ToString(CultureInfo.InvariantCulture));
        ulong flags = 0;
        if (specialization.IsStandardLibrary) flags |= 1;
        if (specialization.IsLocal) flags |= 2;
        if (specialization.IsPublic) flags |= 4;
        if (specialization.IsAsync) flags |= 8;
        WriteUInt64(stream, checksum, flags);
    }

    private static SemanticSpecializationReuse ReadSpecialization(
        Stream stream,
        IncrementalHash checksum)
    {
        var identity = ReadString(stream, checksum);
        var templateIdentity = ReadOptionalString(stream, checksum);
        var name = ReadString(stream, checksum);
        var moduleName = ReadString(stream, checksum);
        var kindValue = ReadUInt64(stream, checksum);
        if (kindValue > int.MaxValue
            || !Enum.IsDefined(typeof(BoundFunctionKind), (int)kindValue))
            throw new InvalidDataException("semantic specialization function kind is invalid");
        var inputName = ReadOptionalString(stream, checksum);
        var inputType = ReadOptionalString(stream, checksum);
        var ownershipValue = ReadUInt64(stream, checksum);
        if (ownershipValue > int.MaxValue
            || !Enum.IsDefined(typeof(BoundFunctionInputOwnership), (int)ownershipValue))
            throw new InvalidDataException("semantic specialization input ownership is invalid");
        var returnType = ReadString(stream, checksum);
        var blockInputName = ReadOptionalString(stream, checksum);
        var blockInputType = ReadOptionalString(stream, checksum);
        var blockResultType = ReadOptionalString(stream, checksum);
        var specializedType = ReadOptionalString(stream, checksum);
        var specializedSecondaryType = ReadOptionalString(stream, checksum);
        var specializedTertiaryType = ReadOptionalString(stream, checksum);
        var valueText = ReadOptionalString(stream, checksum);
        int? specializedValue = null;
        if (valueText is not null)
        {
            if (!int.TryParse(valueText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || !StringComparer.Ordinal.Equals(valueText, value.ToString(CultureInfo.InvariantCulture)))
                throw new InvalidDataException("semantic specialization value is invalid");
            specializedValue = value;
        }
        var flags = ReadUInt64(stream, checksum);
        if (flags > 15)
            throw new InvalidDataException("semantic specialization flags are invalid");
        return new SemanticSpecializationReuse(
            identity,
            templateIdentity,
            name,
            moduleName,
            (BoundFunctionKind)kindValue,
            inputName,
            inputType,
            (BoundFunctionInputOwnership)ownershipValue,
            returnType,
            blockInputName,
            blockInputType,
            blockResultType,
            specializedType,
            specializedSecondaryType,
            specializedTertiaryType,
            specializedValue,
            (flags & 1) != 0,
            (flags & 2) != 0,
            (flags & 4) != 0,
            (flags & 8) != 0);
    }

    private static void WriteOptionalString(
        Stream stream,
        IncrementalHash checksum,
        string? value)
    {
        WriteUInt64(stream, checksum, value is null ? 0UL : 1UL);
        if (value is not null)
            WriteString(stream, checksum, value);
    }

    private static string? ReadOptionalString(Stream stream, IncrementalHash checksum) =>
        ReadUInt64(stream, checksum) switch
        {
            0 => null,
            1 => ReadString(stream, checksum),
            _ => throw new InvalidDataException("semantic optional string presence is invalid")
        };

    private static void WriteBindings(
        Stream stream,
        IncrementalHash checksum,
        IReadOnlyDictionary<string, string> bindings)
    {
        WriteUInt64(stream, checksum, checked((ulong)bindings.Count));
        foreach (var binding in bindings.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            WriteString(stream, checksum, binding.Key);
            WriteString(stream, checksum, binding.Value);
        }
    }

    private static void EnsureUniquePairs(
        IReadOnlyList<KeyValuePair<string, string>> pairs,
        string message)
    {
        for (var index = 1; index < pairs.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(pairs[index - 1].Key, pairs[index].Key))
                throw new InvalidOperationException($"{message}: {pairs[index].Key}");
        }
    }

    private static void RequireIncreasing(string? previous, string value, string description)
    {
        if (previous is not null && StringComparer.Ordinal.Compare(previous, value) >= 0)
            throw new InvalidDataException($"semantic {description} are not canonical");
    }

    private static int CheckedCount(ulong value)
    {
        if (value > MaximumRecords)
            throw new InvalidDataException("semantic generation record count is invalid");
        return (int)value;
    }

    private static void WriteString(Stream stream, IncrementalHash checksum, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumIdentityBytes)
            throw new InvalidDataException("semantic identity is too large");
        WriteUInt64(stream, checksum, checked((ulong)bytes.Length));
        stream.Write(bytes);
        checksum.AppendData(bytes);
    }

    private static string ReadString(Stream stream, IncrementalHash checksum)
    {
        var length = ReadUInt64(stream, checksum);
        if (length > MaximumIdentityBytes)
            throw new InvalidDataException("semantic identity length is invalid");
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        checksum.AppendData(bytes);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void WriteDigest(Stream stream, IncrementalHash checksum, byte[] digest)
    {
        if (digest.Length != DigestLength)
            throw new InvalidDataException("semantic digest length is invalid");
        stream.Write(digest);
        checksum.AppendData(digest);
    }

    private static byte[] ReadDigest(Stream stream, IncrementalHash checksum)
    {
        var digest = new byte[DigestLength];
        stream.ReadExactly(digest);
        checksum.AppendData(digest);
        return digest;
    }

    private static void WriteUInt64(Stream stream, IncrementalHash checksum, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
        checksum.AppendData(bytes);
    }

    private static ulong ReadUInt64(Stream stream, IncrementalHash checksum)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        stream.ReadExactly(bytes);
        checksum.AppendData(bytes);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    private static void AppendHashUInt64(IncrementalHash hash, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private sealed record DecodedSemanticCache(
        byte[] DeclarationFingerprint,
        IReadOnlyList<string> Functions,
        IReadOnlyList<KeyValuePair<string, string>> Calls,
        IReadOnlyList<KeyValuePair<string, byte[]>> Modules,
        IReadOnlyList<SemanticFunctionReuse> ReusableFunctions,
        IReadOnlyList<SemanticSpecializationReuse> Specializations,
        string? MainModuleName,
        IReadOnlyDictionary<string, string>? MainBindings);

    private sealed class SemanticCacheMissException(string message) : Exception(message);
}
