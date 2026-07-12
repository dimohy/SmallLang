import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        wrong<T, E> value: T -> E => value
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "generic mismatch = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol),$expression" -> println
    }
}
