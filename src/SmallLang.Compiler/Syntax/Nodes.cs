namespace SmallLang.Compiler.Syntax;

internal sealed record SmallLangProgram(
    IReadOnlyList<FunctionDeclaration> Functions,
    IReadOnlyList<Statement> Statements);

internal sealed record FunctionDeclaration(
    string Name,
    string? InputType,
    string ReturnType,
    Expression Body,
    int Line,
    int Column);

internal abstract record Statement;

internal sealed record BindingStatement(string Name, Expression Value, int Line, int Column) : Statement;

internal sealed record EachStatement(
    string ItemName,
    Expression Start,
    Expression End,
    IReadOnlyList<Statement> Body,
    int Line,
    int Column)
    : Statement;

internal sealed record ExpressionStatement(Expression Expression) : Statement;

internal abstract record Expression(int Line, int Column);

internal sealed record StringExpression(IReadOnlyList<StringSegment> Segments, int Line, int Column)
    : Expression(Line, Column);

internal sealed record NumberExpression(string Text, int Line, int Column) : Expression(Line, Column);

internal sealed record NameExpression(string Name, int Line, int Column) : Expression(Line, Column);

internal sealed record AddExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record MultiplyExpression(Expression Left, Expression Right, int Line, int Column)
    : Expression(Line, Column);

internal sealed record FlowExpression(
    Expression Source,
    IReadOnlyList<IReadOnlyList<string>> Targets,
    int Line,
    int Column)
    : Expression(Line, Column);

internal sealed record CallExpression(
    IReadOnlyList<string> Path,
    IReadOnlyList<Expression> Arguments,
    int Line,
    int Column)
    : Expression(Line, Column);

internal abstract record StringSegment;

internal sealed record TextSegment(string Text) : StringSegment;

internal sealed record InterpolationSegment(IReadOnlyList<string> Path) : StringSegment;
