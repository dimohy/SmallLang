namespace Sollang.Compiler.Semantics;

internal sealed record SemanticFunctionReuse(
    string Identity,
    string ModuleName,
    IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyDictionary<string, string> CapturedBindings);

internal sealed record SemanticSpecializationReuse(
    string Identity,
    string? TemplateIdentity,
    string Name,
    string ModuleName,
    BoundFunctionKind Kind,
    string? InputName,
    string? InputType,
    BoundFunctionInputOwnership InputOwnership,
    string ReturnType,
    string? BlockInputName,
    string? BlockInputType,
    string? BlockResultType,
    string? SpecializedType,
    string? SpecializedSecondaryType,
    string? SpecializedTertiaryType,
    int? SpecializedValue,
    bool IsStandardLibrary,
    bool IsLocal,
    bool IsPublic,
    bool IsAsync);

internal sealed record SemanticReusePlan(
    byte[] DeclarationFingerprint,
    IReadOnlyDictionary<string, SemanticFunctionReuse> Functions,
    IReadOnlyDictionary<string, string> ResolvedCalls,
    IReadOnlyDictionary<string, SemanticSpecializationReuse> Specializations,
    string? MainModuleName,
    IReadOnlyDictionary<string, string>? MainBindings);
