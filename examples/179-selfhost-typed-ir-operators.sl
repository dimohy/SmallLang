import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer as lexer

main {
    [
        """
        calculate: -> Int => 1 + 2 * 3
        negate: -> Int => -4
        main { }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    sources![0] -> lexer.lex => tokens!
    ir! -> each node {
        "-" => payload!
        node.payloadToken >= 0 -> if {
            tokens![node.payloadToken] => token
            sources![0] -> slice(token.span.start, token.span.length) => payload!
        }
        "operator ir = $(node.kind),$(node.parent),$(node.typeSymbol),$(payload!),$(node.operand0),$(node.operand1)" -> println
    }
}
