import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        sum: -> Int => 1 + 2
        bad: -> Text => 1 + 2
        less: -> Bool => 1 < 2
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "operator mismatch = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
