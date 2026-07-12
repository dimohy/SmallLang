import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        copy<T> values: [T; ~] -> [T; ~] => values
        bad: -> [Text; ~] => copy([1, 2, ~])
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "composite mismatch = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
