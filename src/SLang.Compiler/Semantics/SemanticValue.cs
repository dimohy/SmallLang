using System.Globalization;

namespace SLang.Compiler.Semantics;

internal abstract record SemanticValue
{
    public abstract string ToDisplayString();
}

internal sealed record TextValue(string Text) : SemanticValue
{
    public override string ToDisplayString() => Text;
}

internal sealed record IntegerValue(long Value) : SemanticValue
{
    public override string ToDisplayString() => Value.ToString(CultureInfo.InvariantCulture);
}
