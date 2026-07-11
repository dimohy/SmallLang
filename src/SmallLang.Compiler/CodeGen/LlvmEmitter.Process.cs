using SmallLang.Compiler.Semantics;
using SmallLang.Compiler.Syntax;
using SmallLang.Compiler.Diagnostics;

namespace SmallLang.Compiler.CodeGen;

internal sealed partial class LlvmEmitter
{
    private RuntimeArguments EmitRuntimeArgumentsIntrinsic()
    {
        if (!_platform.SupportsProcessArguments)
        {
            throw new SmallLangException("process arguments are unavailable on the current target");
        }
        var length = NextTemp("argument_count");
        EmitCall(length, "i64", "smalllang_argument_count", "");
        return new RuntimeArguments(length);
    }

    private RuntimeText EmitArgumentLoad(RuntimeArguments arguments, Expression indexExpression)
    {
        var index = EmitMapInteger(indexExpression, BoundType.UIntSize, "argument_index");
        var inBounds = NextTemp("argument_in_bounds");
        EmitCompare(inBounds, "ult", "i64", index, arguments.LengthName);
        EmitTrapUnless(inBounds, "argument_bounds");
        return EmitArgumentLoad(index);
    }

    private RuntimeText EmitArgumentLoad(string index)
    {
        var value = NextTemp("argument");
        EmitCall(value, "%smalllang.text", "smalllang_argument", $"i64 {index}");
        var pointer = NextTemp("argument_ptr");
        EmitAssign(pointer, $"extractvalue %smalllang.text {value}, 0");
        var length = NextTemp("argument_len");
        EmitAssign(length, $"extractvalue %smalllang.text {value}, 1");
        return new RuntimeText(pointer, length);
    }
}
