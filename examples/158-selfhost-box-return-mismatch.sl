import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        wrap<T> value: box T -> box T => value
        bad: -> box Text => wrap(box 1)
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "box mismatch = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
