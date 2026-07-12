import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        take value: {Int: Text} -> Int => 1
        main { take({1: 2}) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => expression
        "dictionary argument = $(error.code),$(error.expectedModule),$(error.expectedSymbol),$(error.actualModule),$(error.actualSymbol),$expression,$(error.span.start),$(error.span.length)" -> println
    }
}
