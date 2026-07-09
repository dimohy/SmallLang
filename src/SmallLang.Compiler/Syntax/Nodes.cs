namespace SmallLang.Compiler.Syntax;

internal sealed record SmallLangProgram(
    IReadOnlyList<FunctionDeclaration> Functions,
    IReadOnlyList<Statement> Statements);

internal sealed record FunctionDeclaration(
    string Name,
    string? InputName,
    string? InputType,
    string ReturnType,
    string? BlockInputName,
    string? BlockInputType,
    IReadOnlyList<FunctionDeclaration> LocalFunctions,
    Expression? Body,
    IReadOnlyList<Statement> BlockBody,
    int Line,
    int Column,
    bool IsIntrinsic,
    bool IsStandardLibrary);

internal abstract record Statement;

internal sealed record BindingStatement(string Name, Expression Value, int Line, int Column, bool IsMutable) : Statement;

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

internal sealed record ArrayRepeatExpression(Expression Value, int Count, int Line, int Column)
    : Expression(Line, Column);

internal sealed record DictionaryLiteralExpression(
    IReadOnlyList<DictionaryEntryExpression> Entries,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record DictionaryEntryExpression(Expression Key, Expression Value);

internal sealed record IndexExpression(Expression Source, Expression Index, int Line, int Column)
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
