import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        ok: -> Int => 1
        bad: -> Text => 2
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "return mismatch = $(error.code),$(error.expectedSymbol),$(error.actualBuiltin),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
