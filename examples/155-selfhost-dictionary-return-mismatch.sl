import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        copy<K, V> value: {K: V} -> {K: V} => value
        bad: -> {Int: Text} => copy({1: 2})
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "dictionary mismatch = $(error.code),$(error.expectedModule),$(error.expectedSymbol),$(error.actualModule),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
