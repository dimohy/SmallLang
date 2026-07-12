import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.diagnostics as diagnostics
import smalllang.compiler.semantic.symbols as symbols

main {
    "struct Point { x: Int x: Int } struct Point { y: Int } main { }" => source
    source -> symbols.collect => table!
    source -> diagnostics.analyze => errors!
    source -> lexer.lex => tokens!
    errors! -> each error {
        table![error.symbol] => duplicate
        tokens![duplicate.nameToken] => nameToken
        source -> slice(nameToken.span.start, nameToken.span.length) => name
        "duplicate = $(error.code),$name,$(error.symbol),$(error.previousSymbol),$(error.span.start),$(error.span.length)" -> println
    }
}
