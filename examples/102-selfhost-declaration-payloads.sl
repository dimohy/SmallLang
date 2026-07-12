import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer

main {
    "public struct Point { x: Int y: Int } public enum Maybe { None Some(Int) } public trait Show { show: self -> Int } impl Show for Point { show: self -> Int => 1 } main { }" => source
    source -> ast.lower => nodes!
    source -> lexer.lex => tokens!
    nodes! -> len => count
    0 => index!
    index! < count -> while {
        nodes![index!] => node
        false => emit!
        (node.kind >= 3 and node.kind <= 7) -> if { true => emit! }
        (node.kind >= 26 and node.kind <= 31) -> if { true => emit! }
        emit! -> if {
            tokens![node.payloadToken] => payload
            source -> slice(payload.span.start, payload.span.length) => name
            "declaration = $(node.kind),$(node.parent),$name,$(node.flags)" -> println
        }
        index! + 1 => index!
    }
}
