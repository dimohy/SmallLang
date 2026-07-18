import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer

main {
    [
        """
        emit source: Text -> Unit uses Console {
            source -> slice(UIntSize(1), UIntSize(2)) -> print
        }

        emitIndexed sources: [Text; ~] -> Unit uses Console {
            sources[0] -> slice(UIntSize(1), UIntSize(2)) -> print
        }

        main { }
        """,
        ~
    ] => sources!
    sources![0] -> ast.lower => astNodes!
    sources![0] -> lexer.lex => tokens!
    0 => astIndex!
    astNodes! -> each node {
        "" => payload!
        node.payloadToken >= 0 -> if {
            tokens![node.payloadToken] => token
            sources![0] -> slice(token.span.start, token.span.length) => payload!
        }
        "ast=$(astIndex!),kind=$(node.kind),parent=$(node.parent),payload=$(payload!)" -> println
        astIndex! + 1 => astIndex!
    }
    sources! -> typedIr.lower => ir!
    0 => index!
    ir! -> each node {
        "ir=$(index!),kind=$(node.kind),parent=$(node.parent),ast=$(node.astNode),symbol=$(node.symbol),opcode=$(node.opcode),op0=$(node.operand0),op1=$(node.operand1),next=$(node.nextOperand)" -> println
        index! + 1 => index!
    }
}
