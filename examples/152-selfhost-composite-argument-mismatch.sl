import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        take values: [Text; ~] -> Int => 1
        main { take([1, 2, ~]) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "composite argument = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
