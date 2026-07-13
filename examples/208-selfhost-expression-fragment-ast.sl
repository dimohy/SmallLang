import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer

main {
    """
    value + 2 * -3
    """ => source
    source -> ast.lowerExpression => nodes!
    source -> lexer.lex => tokens!
    nodes! -> each node {
        "-" => payload!
        node.payloadToken >= 0 -> if {
            tokens![node.payloadToken] => token
            source -> slice(token.span.start, token.span.length) => payload!
        }
        "fragment = $(node.kind),$(node.parent),$(node.operatorKind),$(payload!),$(node.start),$(node.length)" -> println
    }
}
