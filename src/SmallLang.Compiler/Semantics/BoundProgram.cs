using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed record BoundProgram(
    TypeDefinitionTable Types,
    IReadOnlyDictionary<string, BoundTraitDefinition> Traits,
    IReadOnlyDictionary<string, BoundFunction> Functions,
    IReadOnlyDictionary<object, BoundFunction> ResolvedGenericCalls,
    IReadOnlyList<Statement> MainStatements,
    IReadOnlyDictionary<string, BoundType> MainBindings,
    IReadOnlyDictionary<BoundFunction, IReadOnlyDictionary<string, BoundType>> FunctionBindings,
    StackFramePlan MainStackFrame,
    IReadOnlyDictionary<BoundFunction, StackFramePlan> FunctionStackFrames);

internal sealed record BoundFunction(
    string Name,
    string? InputName,
    BoundType? InputType,
    BoundFunctionInputOwnership InputOwnership,
    BoundType ReturnType,
    string? BlockInputName,
    BoundType? BlockInputType,
    IReadOnlyDictionary<string, BoundFunction> LocalFunctions,
    Expression? Body,
    IReadOnlyList<Statement> BlockBody,
    int Line,
    int Column,
    BoundFunctionKind Kind,
    bool IsStandardLibrary,
    bool IsLocal,
    string? TraitName = null,
    string? GenericParameterName = null,
    string? SecondaryGenericParameterName = null,
    string? GenericTraitBound = null,
    string? GenericAssociatedTypeName = null,
    TypeId? GenericAssociatedTypeConstraint = null,
    IReadOnlyDictionary<string, TypeId>? ImplAssociatedTypes = null,
    TypeId? SpecializedType = null,
    TypeId? SpecializedSecondaryType = null,
    bool IsValueGeneric = false,
    int? SpecializedValue = null,
    bool HasValueGenericFixedArrayInput = false,
    string ModuleName = "",
    bool IsPublic = false,
    bool IsAsync = false);

internal sealed record BoundTraitMethod(
    string Name,
    BoundFunctionInputOwnership SelfOwnership,
    TypeId? ReturnType,
    string? ReturnAssociatedTypeName,
    int Line,
    int Column);

internal sealed record BoundTraitAssociatedType(string Name, int Line, int Column);

internal sealed record BoundTraitDefinition(
    string Name,
    IReadOnlyList<BoundTraitAssociatedType> AssociatedTypes,
    IReadOnlyList<BoundTraitMethod> Methods,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false);

internal enum BoundFunctionInputOwnership
{
    Default,
    Move,
    MutableBorrow
}

internal enum BoundFunctionKind
{
    User,
    UserBlock,
    RuntimePrint,
    RuntimePrintLine,
    RuntimeReadInt,
    RuntimeSeedRandom,
    RuntimeRandomBelow,
    RuntimeOpenIntWriter,
    RuntimeWriteInt,
    RuntimeCloseIntWriter,
    RuntimeOpenIntReader,
    RuntimeClosestInt,
    RuntimeCloseIntReader,
    RuntimeNowMillis,
    RuntimeSleep,
    RuntimeArguments,
    RuntimeEnvironment,
    RuntimeRunProcess,
    RuntimeWriteScalar,
    RuntimeReadScalar
}

internal enum TypeId
{
    Unit,
    Text,
    Int,
    Bool,
    Int8,
    Int16,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Size,
    UIntSize,
    CodePoint,
    Arguments,
    Arena,
    MappedBytes,
    MutableMappedBytes,
    Float32,
    Float64,
    IntSlice,
    StaticIntArray,
    StaticTextArray,
    DynamicIntArray,
    IntDictionaryView,
    IntDictionary,
    TaskInt,
    Duration,
    BoxDuration,
    GenericParameter = 512,
    SecondaryGenericParameter = 513,
    FirstUserDefined = 1024
}

internal sealed record BoundStructField(string Name, TypeId Type, int Index, int Line, int Column);

internal sealed record BoundStructDefinition(
    TypeId Id,
    string Name,
    IReadOnlyList<BoundStructField> Fields,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false,
    string? DeclaringTypeName = null)
{
    public BoundStructField GetField(string name)
    {
        return Fields.FirstOrDefault(field => field.Name == name)
            ?? throw new KeyNotFoundException($"struct '{Name}' has no field '{name}'");
    }
}

internal sealed record BoundEnumVariant(string Name, TypeId? PayloadType, int Tag, int Line, int Column);

internal sealed record BoundEnumDefinition(
    TypeId Id,
    string Name,
    IReadOnlyList<BoundEnumVariant> Variants,
    int PayloadWords,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false);

internal sealed record BoundBoxDefinition(TypeId Id, TypeId ElementType, int Size, int Alignment);

internal sealed record BoundStaticArrayDefinition(
    TypeId Id,
    TypeId ElementType,
    int ElementSize,
    int ElementAlignment);

internal sealed record BoundDynamicArrayDefinition(
    TypeId Id,
    TypeId ElementType,
    int ElementSize,
    int ElementAlignment);

internal sealed record BoundDictionaryDefinition(
    TypeId Id,
    TypeId KeyType,
    TypeId ValueType,
    int KeySize,
    int KeyAlignment,
    int ValueOffset,
    int ValueSize,
    int ValueAlignment,
    int EntryStride);

internal sealed class TypeDefinitionTable
{
    private readonly Dictionary<string, TypeId> _names;
    private readonly Dictionary<TypeId, BoundStructDefinition> _structs;
    private readonly Dictionary<TypeId, BoundEnumDefinition> _enums;
    private readonly Dictionary<TypeId, BoundBoxDefinition> _boxes;
    private readonly Dictionary<TypeId, BoundStaticArrayDefinition> _staticArrays = [];
    private readonly Dictionary<TypeId, TypeId> _staticArraysByElement = [];
    private readonly Dictionary<TypeId, BoundDynamicArrayDefinition> _dynamicArrays = [];
    private readonly Dictionary<TypeId, TypeId> _dynamicArraysByElement = [];
    private readonly Dictionary<TypeId, BoundDictionaryDefinition> _dictionaries = [];
    private readonly Dictionary<(TypeId Key, TypeId Value), TypeId> _dictionariesByTypes = [];
    private readonly Dictionary<TypeId, TypeId> _optionsByValue = [];
    private readonly Dictionary<(TypeId Ok, TypeId Error), TypeId> _resultsByTypes = [];
    private readonly Dictionary<TypeId, TypeId> _optionValues = [];
    private readonly Dictionary<TypeId, (TypeId Ok, TypeId Error)> _resultTypes = [];
    private readonly Dictionary<TypeId, TypeId> _tasksByValue = [];
    private readonly Dictionary<TypeId, TypeId> _taskValues = [];
    private int _nextParametricTypeId;
    private readonly int _pointerSize;

    public TypeDefinitionTable(
        IReadOnlyDictionary<string, TypeId> names,
        IReadOnlyDictionary<TypeId, BoundStructDefinition> structs,
        IReadOnlyDictionary<TypeId, BoundEnumDefinition> enums,
        IReadOnlyDictionary<TypeId, BoundBoxDefinition> boxes,
        int pointerSize)
    {
        _names = new Dictionary<string, TypeId>(names, StringComparer.Ordinal);
        _structs = new Dictionary<TypeId, BoundStructDefinition>(structs);
        _enums = new Dictionary<TypeId, BoundEnumDefinition>(enums);
        _boxes = new Dictionary<TypeId, BoundBoxDefinition>(boxes);
        _pointerSize = pointerSize;
        _tasksByValue.Add(TypeId.Int, TypeId.TaskInt);
        _taskValues.Add(TypeId.TaskInt, TypeId.Int);
        _nextParametricTypeId = _names.Values
            .Concat(_boxes.Keys)
            .Select(static type => (int)type)
            .DefaultIfEmpty((int)TypeId.FirstUserDefined)
            .Max() + 1;
    }

    public IReadOnlyCollection<BoundStructDefinition> Structs => _structs.Values.ToArray();

    public IReadOnlyCollection<BoundEnumDefinition> Enums => _enums.Values.ToArray();

    public IReadOnlyCollection<BoundBoxDefinition> Boxes => _boxes.Values.ToArray();

    public IReadOnlyCollection<BoundStaticArrayDefinition> StaticArrays => _staticArrays.Values.ToArray();

    public IReadOnlyCollection<BoundDynamicArrayDefinition> DynamicArrays => _dynamicArrays.Values.ToArray();

    public IReadOnlyCollection<BoundDictionaryDefinition> Dictionaries => _dictionaries.Values.ToArray();

    public bool TryResolve(string name, out TypeId type) => _names.TryGetValue(name, out type);

    public void AddAlias(string name, TypeId type) => _names.TryAdd(name, type);

    public bool IsStruct(TypeId type) => _structs.ContainsKey(type);

    public bool IsEnum(TypeId type) => _enums.ContainsKey(type);

    public bool IsBox(TypeId type) => _boxes.ContainsKey(type);

    public bool IsStaticArray(TypeId type) => _staticArrays.ContainsKey(type);

    public bool IsDynamicArray(TypeId type) => _dynamicArrays.ContainsKey(type);

    public bool IsDictionary(TypeId type) => _dictionaries.ContainsKey(type);

    public TypeId GetOrAddStaticArray(TypeId elementType)
    {
        if (_staticArraysByElement.TryGetValue(elementType, out var existing))
        {
            return existing;
        }

        var id = (TypeId)_nextParametricTypeId++;
        var size = InlineSizeOf(elementType);
        var alignment = Math.Min(Math.Max(size, 1), 8);
        _staticArrays.Add(id, new BoundStaticArrayDefinition(id, elementType, size, alignment));
        _staticArraysByElement.Add(elementType, id);
        return id;
    }

    public bool TryGetStaticArrayForElement(TypeId elementType, out TypeId arrayType) =>
        _staticArraysByElement.TryGetValue(elementType, out arrayType);

    public TypeId GetOrAddDynamicArray(TypeId elementType)
    {
        if (_dynamicArraysByElement.TryGetValue(elementType, out var existing))
        {
            return existing;
        }

        var id = (TypeId)_nextParametricTypeId++;
        var size = InlineSizeOf(elementType);
        var alignment = Math.Min(Math.Max(size, 1), 8);
        _dynamicArrays.Add(id, new BoundDynamicArrayDefinition(id, elementType, size, alignment));
        _dynamicArraysByElement.Add(elementType, id);
        return id;
    }

    public void RegisterDynamicArray(TypeId id, TypeId elementType)
    {
        if (_dynamicArraysByElement.TryGetValue(elementType, out var existing))
        {
            if (existing != id)
            {
                throw new InvalidOperationException($"dynamic array element type '{(int)elementType}' already has type id '{(int)existing}'");
            }
            return;
        }

        var size = InlineSizeOf(elementType);
        var alignment = Math.Min(Math.Max(size, 1), 8);
        _dynamicArrays.Add(id, new BoundDynamicArrayDefinition(id, elementType, size, alignment));
        _dynamicArraysByElement.Add(elementType, id);
        _nextParametricTypeId = Math.Max(_nextParametricTypeId, (int)id + 1);
    }

    public bool TryGetDynamicArrayForElement(TypeId elementType, out TypeId arrayType) =>
        _dynamicArraysByElement.TryGetValue(elementType, out arrayType);

    public TypeId GetOrAddDictionary(TypeId keyType, TypeId valueType)
    {
        if (_dictionariesByTypes.TryGetValue((keyType, valueType), out var existing))
        {
            return existing;
        }

        var keySize = InlineSizeOf(keyType);
        var keyAlignment = Math.Min(Math.Max(keySize, 1), 8);
        var valueSize = InlineSizeOf(valueType);
        var valueAlignment = Math.Min(Math.Max(valueSize, 1), 8);
        var valueOffset = AlignUp(keySize, valueAlignment);
        var entryAlignment = Math.Max(keyAlignment, valueAlignment);
        var stride = AlignUp(checked(valueOffset + valueSize), entryAlignment);
        var id = (TypeId)_nextParametricTypeId++;
        _dictionaries.Add(id, new BoundDictionaryDefinition(
            id, keyType, valueType, keySize, keyAlignment,
            valueOffset, valueSize, valueAlignment, stride));
        _dictionariesByTypes.Add((keyType, valueType), id);
        return id;
    }

    public bool TryGetDictionaryForTypes(TypeId keyType, TypeId valueType, out TypeId dictionaryType) =>
        _dictionariesByTypes.TryGetValue((keyType, valueType), out dictionaryType);

    public BoundDictionaryDefinition GetDictionary(TypeId type) =>
        _dictionaries.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not a dictionary");

    public TypeId GetOrAddOption(TypeId valueType, string displayName)
    {
        if (_optionsByValue.TryGetValue(valueType, out var existing))
        {
            return existing;
        }
        var id = (TypeId)_nextParametricTypeId++;
        var payloadWords = (InlineSizeOf(valueType) + 7) / 8;
        _enums.Add(id, new BoundEnumDefinition(id, displayName, [
            new BoundEnumVariant("None", null, 0, 0, 0),
            new BoundEnumVariant("Some", valueType, 1, 0, 0)
        ], payloadWords, 0, 0, ModuleName: "", IsPublic: true));
        _optionsByValue.Add(valueType, id);
        _optionValues.Add(id, valueType);
        return id;
    }

    public TypeId GetOrAddResult(TypeId okType, TypeId errorType, string displayName)
    {
        if (_resultsByTypes.TryGetValue((okType, errorType), out var existing))
        {
            return existing;
        }
        var id = (TypeId)_nextParametricTypeId++;
        var payloadWords = (Math.Max(InlineSizeOf(okType), InlineSizeOf(errorType)) + 7) / 8;
        _enums.Add(id, new BoundEnumDefinition(id, displayName, [
            new BoundEnumVariant("Ok", okType, 0, 0, 0),
            new BoundEnumVariant("Err", errorType, 1, 0, 0)
        ], payloadWords, 0, 0, ModuleName: "", IsPublic: true));
        _resultsByTypes.Add((okType, errorType), id);
        _resultTypes.Add(id, (okType, errorType));
        return id;
    }

    public bool TryGetOptionValue(TypeId type, out TypeId valueType) =>
        _optionValues.TryGetValue(type, out valueType);

    public bool TryGetResultTypes(TypeId type, out (TypeId Ok, TypeId Error) types) =>
        _resultTypes.TryGetValue(type, out types);

    public TypeId GetOrAddTask(TypeId valueType)
    {
        if (_tasksByValue.TryGetValue(valueType, out var existing))
        {
            return existing;
        }

        var id = (TypeId)_nextParametricTypeId++;
        _tasksByValue.Add(valueType, id);
        _taskValues.Add(id, valueType);
        return id;
    }

    public bool IsTask(TypeId type) => _taskValues.ContainsKey(type);

    public bool TryGetTaskValue(TypeId type, out TypeId valueType) =>
        _taskValues.TryGetValue(type, out valueType);

    public bool ContainsOwnedStorage(TypeId type)
    {
        return ContainsOwnedStorage(type, new HashSet<TypeId>());
    }

    private bool ContainsOwnedStorage(TypeId type, HashSet<TypeId> visiting)
    {
        if (type is TypeId.DynamicIntArray or TypeId.IntDictionary or TypeId.Arena
            or TypeId.MappedBytes or TypeId.MutableMappedBytes
            || IsTask(type) || IsBox(type) || IsStaticArray(type) || IsDynamicArray(type) || IsDictionary(type))
        {
            return true;
        }
        if (!visiting.Add(type))
        {
            return false;
        }

        try
        {
            if (_structs.TryGetValue(type, out var structure))
            {
                return structure.Fields.Any(field => ContainsOwnedStorage(field.Type, visiting));
            }
            if (_enums.TryGetValue(type, out var enumeration))
            {
                return enumeration.Variants.Any(variant => variant.PayloadType is { } payload
                    && ContainsOwnedStorage(payload, visiting));
            }

            return false;
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    public BoundStructDefinition GetStruct(TypeId type)
    {
        return _structs.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not a struct");
    }

    public BoundEnumDefinition GetEnum(TypeId type)
    {
        return _enums.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not an enum");
    }

    public BoundBoxDefinition GetBox(TypeId type)
    {
        return _boxes.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not a box");
    }

    public BoundStaticArrayDefinition GetStaticArray(TypeId type)
    {
        return _staticArrays.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not a static array");
    }

    public BoundDynamicArrayDefinition GetDynamicArray(TypeId type)
    {
        return _dynamicArrays.TryGetValue(type, out var definition)
            ? definition
            : throw new KeyNotFoundException($"type id '{(int)type}' is not a dynamic array");
    }

    public int InlineSizeOf(TypeId type)
    {
        if (type is TypeId.DynamicIntArray or TypeId.IntDictionary)
        {
            return 24;
        }
        if (IsDynamicArray(type) || IsDictionary(type))
        {
            return 24;
        }
        if (_boxes.ContainsKey(type))
        {
            return 8;
        }
        if (_structs.TryGetValue(type, out var structure))
        {
            var offset = 0;
            var maxAlignment = 1;
            foreach (var field in structure.Fields)
            {
                var size = InlineSizeOf(field.Type);
                var alignment = Math.Min(Math.Max(size, 1), 8);
                offset = AlignUp(offset, alignment);
                offset += size;
                maxAlignment = Math.Max(maxAlignment, alignment);
            }
            return AlignUp(offset, maxAlignment);
        }
        if (_enums.TryGetValue(type, out var enumeration))
        {
            return 8 + enumeration.PayloadWords * 8;
        }
        return type switch
        {
            TypeId.Unit => 0,
            TypeId.Bool => 1,
            TypeId.Int8 or TypeId.UInt8 => 1,
            TypeId.Int16 or TypeId.UInt16 => 2,
            TypeId.Int or TypeId.UInt32 or TypeId.Float32 => 4,
            TypeId.CodePoint => 4,
            TypeId.Int64 or TypeId.UInt64 or TypeId.Float64 => 8,
            TypeId.Size or TypeId.UIntSize => _pointerSize,
            TypeId.Text => 16,
            TypeId.Arguments => 8,
            TypeId.Arena => 24,
            TypeId.MappedBytes or TypeId.MutableMappedBytes => 40,
            _ when IsTask(type) => 16,
            TypeId.GenericParameter or TypeId.SecondaryGenericParameter => 8,
            _ => throw new InvalidOperationException($"type {type} has no inline size")
        };
    }

    private static int AlignUp(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
