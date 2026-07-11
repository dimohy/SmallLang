using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed record BoundProgram(
    TypeDefinitionTable Types,
    IReadOnlyDictionary<string, BoundTraitDefinition> Traits,
    IReadOnlyDictionary<string, BoundFunction> Functions,
    IReadOnlyDictionary<object, BoundFunction> ResolvedGenericCalls,
    IReadOnlyList<Statement> MainStatements,
    IReadOnlyDictionary<string, BoundType> MainBindings,
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
    string? GenericTraitBound = null,
    TypeId? SpecializedType = null,
    bool IsValueGeneric = false,
    int? SpecializedValue = null,
    bool HasValueGenericFixedArrayInput = false);

internal sealed record BoundTraitMethod(
    string Name,
    BoundFunctionInputOwnership SelfOwnership,
    TypeId ReturnType,
    int Line,
    int Column);

internal sealed record BoundTraitDefinition(
    string Name,
    IReadOnlyList<BoundTraitMethod> Methods,
    int Line,
    int Column);

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
    RuntimeNowMillis
}

internal enum TypeId
{
    Unit,
    Text,
    Int,
    Bool,
    IntSlice,
    StaticIntArray,
    DynamicIntArray,
    IntDictionaryView,
    IntDictionary,
    GenericParameter = 512,
    FirstUserDefined = 1024
}

internal sealed record BoundStructField(string Name, TypeId Type, int Index, int Line, int Column);

internal sealed record BoundStructDefinition(
    TypeId Id,
    string Name,
    IReadOnlyList<BoundStructField> Fields,
    int Line,
    int Column)
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
    int Column);

internal sealed record BoundBoxDefinition(TypeId Id, TypeId ElementType, int Size, int Alignment);

internal sealed class TypeDefinitionTable
{
    private readonly IReadOnlyDictionary<string, TypeId> _names;
    private readonly IReadOnlyDictionary<TypeId, BoundStructDefinition> _structs;
    private readonly IReadOnlyDictionary<TypeId, BoundEnumDefinition> _enums;
    private readonly IReadOnlyDictionary<TypeId, BoundBoxDefinition> _boxes;

    public TypeDefinitionTable(
        IReadOnlyDictionary<string, TypeId> names,
        IReadOnlyDictionary<TypeId, BoundStructDefinition> structs,
        IReadOnlyDictionary<TypeId, BoundEnumDefinition> enums,
        IReadOnlyDictionary<TypeId, BoundBoxDefinition> boxes)
    {
        _names = names;
        _structs = structs;
        _enums = enums;
        _boxes = boxes;
    }

    public IReadOnlyCollection<BoundStructDefinition> Structs => _structs.Values.ToArray();

    public IReadOnlyCollection<BoundEnumDefinition> Enums => _enums.Values.ToArray();

    public IReadOnlyCollection<BoundBoxDefinition> Boxes => _boxes.Values.ToArray();

    public bool TryResolve(string name, out TypeId type) => _names.TryGetValue(name, out type);

    public bool IsStruct(TypeId type) => _structs.ContainsKey(type);

    public bool IsEnum(TypeId type) => _enums.ContainsKey(type);

    public bool IsBox(TypeId type) => _boxes.ContainsKey(type);

    public bool ContainsOwnedStorage(TypeId type)
    {
        return ContainsOwnedStorage(type, new HashSet<TypeId>());
    }

    private bool ContainsOwnedStorage(TypeId type, HashSet<TypeId> visiting)
    {
        if (type is TypeId.DynamicIntArray or TypeId.IntDictionary || IsBox(type))
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
}
