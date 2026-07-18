import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.resolve as resolve
import smalllang.compiler.semantic.symbols as symbols

main {
    """
    identity value: move Int -> Int {
        value
    }

    outer: -> Int {
        readLater: -> Int {
            later => copied
            copied
        }
        7 => later
        readLater
    }

    main { }
    """ => source
    source -> resolve.resolve => names!
    source -> symbols.collect => table!
    source -> lexer.lex => tokens!
    names! -> each name {
        table![name.symbol] => symbol
        tokens![name.nameToken] => referenceToken
        tokens![symbol.nameToken] => declarationToken
        source -> slice(referenceToken.span.start, referenceToken.span.length) => reference
        source -> slice(declarationToken.span.start, declarationToken.span.length) => declaration
        "resolved = $reference,$declaration,$(symbol.kind),$(symbol.parent),$(symbol.flags)" -> println
    }
}
