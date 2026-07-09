using SmallLang.Compiler.Syntax;

namespace SmallLang.Compiler.Semantics;

internal sealed record BoundProgram(
    IReadOnlyDictionary<string, BoundFunction> Functions,
    IReadOnlyList<Statement> MainStatements,
    IReadOnlyDictionary<string, BoundType> MainBindings);

internal sealed record BoundFunction(
    string Name,
    string? InputName,
    BoundType? InputType,
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
    bool IsLocal);

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

internal enum BoundType
{
    Unit,
    Text,
    Int,
    Bool,
    StaticIntArray,
    DynamicIntArray,
    IntDictionary
}
