namespace SmallLang.Compiler.Syntax;

internal sealed record SmallLangProgram(
    IReadOnlyList<string> NamespacePath,
    IReadOnlyList<ImportDeclaration> Imports,
    IReadOnlyList<StructDeclaration> Structs,
    IReadOnlyList<EnumDeclaration> Enums,
    IReadOnlyList<TraitDeclaration> Traits,
    IReadOnlyList<FunctionDeclaration> Functions,
    IReadOnlyList<Statement> Statements);

internal sealed record ImportDeclaration(
    IReadOnlyList<string> Path,
    string Alias);

internal sealed record StructDeclaration(
    string Name,
    IReadOnlyList<StructFieldDeclaration> Fields,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false);

internal sealed record StructFieldDeclaration(string Name, string TypeName, int Line, int Column);

internal sealed record EnumDeclaration(
    string Name,
    IReadOnlyList<EnumVariantDeclaration> Variants,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false);

internal sealed record EnumVariantDeclaration(string Name, string? PayloadType, int Line, int Column);

internal sealed record TraitDeclaration(
    string Name,
    IReadOnlyList<TraitAssociatedTypeDeclaration> AssociatedTypes,
    IReadOnlyList<TraitMethodDeclaration> Methods,
    int Line,
    int Column,
    string ModuleName = "",
    bool IsPublic = false);

internal sealed record TraitAssociatedTypeDeclaration(string Name, int Line, int Column);

internal sealed record TraitMethodDeclaration(
    string Name,
    FunctionInputOwnership SelfOwnership,
    string ReturnType,
    int Line,
    int Column);

internal sealed record FunctionDeclaration(
    string Name,
    string? InputName,
    string? InputType,
    FunctionInputOwnership InputOwnership,
    string ReturnType,
    string? BlockInputName,
    string? BlockInputType,
    IReadOnlyList<FunctionDeclaration> LocalFunctions,
    Expression? Body,
    IReadOnlyList<Statement> BlockBody,
    int Line,
    int Column,
    bool IsIntrinsic,
    bool IsStandardLibrary,
    string? TraitName = null,
    string? GenericParameterName = null,
    string? GenericTraitBound = null,
    string? GenericAssociatedTypeName = null,
    string? GenericAssociatedTypeConstraint = null,
    IReadOnlyDictionary<string, string>? ImplAssociatedTypes = null,
    bool IsValueGeneric = false,
    bool HasValueGenericFixedArrayInput = false,
    string ModuleName = "",
    bool IsPublic = false);

internal enum FunctionInputOwnership
{
    Default,
    Move,
    MutableBorrow
}

internal abstract record Statement;

internal sealed record BindingStatement(string Name, Expression Value, int Line, int Column, bool IsMutable) : Statement;

internal sealed record IndexAssignmentStatement(string Name, Expression Index, Expression Value, int Line, int Column)
    : Statement;

internal sealed record FieldAssignmentStatement(
    string Name,
    string FieldName,
    Expression Value,
    int Line,
    int Column)
    : Statement;

internal sealed record BlockFunctionCallStatement(
    Expression Source,
    IReadOnlyList<string> Target,
    string ItemName,
    IReadOnlyList<Statement> Body,
    int Line,
    int Column,
    bool UsesDefaultItemName)
    : Statement;

internal sealed record ExpressionStatement(Expression Expression) : Statement;

internal abstract record Expression(int Line, int Column);

internal sealed record StringExpression(IReadOnlyList<StringSegment> Segments, int Line, int Column)
    : Expression(Line, Column);

internal sealed record NumberExpression(string Text, int Line, int Column) : Expression(Line, Column);

internal sealed record BoolExpression(bool Value, int Line, int Column) : Expression(Line, Column);

internal sealed record NameExpression(string Name, int Line, int Column) : Expression(Line, Column);

internal sealed record AddExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record SubtractExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record MultiplyExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record DivideExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record ModuloExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record NegateExpression(Expression Value, int Line, int Column)
    : Expression(Line, Column);

internal sealed record CompareExpression(
    Expression Left,
    ComparisonOperator Operator,
    Expression Right,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record AndExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record OrExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record NotExpression(Expression Value, int Line, int Column)
    : Expression(Line, Column);

internal sealed record RangeExpression(Expression Start, Expression End, int Line, int Column)
    : Expression(Line, Column);

internal sealed record FoldExpression(
    Expression Source,
    Expression Initial,
    string AccumulatorName,
    string ItemName,
    BlockBody Body,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record IfExpression(
    Expression Condition,
    BlockBody Then,
    BlockBody? Else,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record WhenExpression(
    Expression? Subject,
    IReadOnlyList<WhenArm> Arms,
    BlockBody Else,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record FlowExpression(
    Expression Source,
    IReadOnlyList<FlowTarget> Targets,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record FlowTarget(
    IReadOnlyList<string> Path,
    IReadOnlyList<Expression> Arguments,
    bool UsesCallSyntax,
    int? CompileTimeValueArgument,
    int Line,
    int Column);

internal sealed record CallExpression(
    IReadOnlyList<string> Path,
    IReadOnlyList<Expression> Arguments,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record ArrayLiteralExpression(
    IReadOnlyList<Expression> Elements,
    bool IsDynamic,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record ArrayRepeatExpression(
    Expression Value,
    int? Count,
    string? CountParameterName,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record TypedEmptyArrayExpression(string ElementType, int? CapacityHint, int Line, int Column)
    : Expression(Line, Column);

internal sealed record DictionaryLiteralExpression(
    IReadOnlyList<DictionaryEntryExpression> Entries,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record TypedEmptyDictionaryExpression(string KeyType, string ValueType, int? CapacityHint, int Line, int Column)
    : Expression(Line, Column);

internal sealed record DictionaryEntryExpression(Expression Key, Expression Value);

internal sealed record IndexExpression(Expression Source, Expression Index, int Line, int Column)
    : Expression(Line, Column);

internal sealed record StructLiteralExpression(
    string TypeName,
    IReadOnlyList<StructFieldInitializer> Fields,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record StructFieldInitializer(string Name, Expression Value, int Line, int Column);

internal sealed record FieldAccessExpression(Expression Source, string FieldName, int Line, int Column)
    : Expression(Line, Column);

internal sealed record BoxExpression(Expression Value, int Line, int Column)
    : Expression(Line, Column);

internal sealed record EnumPatternExpression(
    string TypeName,
    string VariantName,
    string? BindingName,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record EnumMatchExpression(
    Expression Subject,
    IReadOnlyList<WhenArm> Arms,
    BlockBody? Else,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record BlockBody(IReadOnlyList<Statement> Statements, Expression? Value, int Line, int Column);

internal sealed record WhenArm(Expression Condition, BlockBody Body, int Line, int Column);

internal sealed record SubjectCompareExpression(
    ComparisonOperator Operator,
    Expression Right,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record SubjectRangeExpression(
    Expression Start,
    Expression End,
    int Line,
    int Column)
    : Expression(Line, Column);

internal enum ComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual
}

internal abstract record StringSegment;

internal sealed record TextSegment(string Text) : StringSegment;

internal sealed record InterpolationSegment(Expression Expression) : StringSegment;
