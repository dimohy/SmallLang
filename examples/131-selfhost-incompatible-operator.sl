import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        main { 1 + (1 < 2) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "operator error = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
