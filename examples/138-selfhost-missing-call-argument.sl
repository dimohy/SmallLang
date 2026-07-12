import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        take value: Int -> Int => value
        main { take() }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => call
        "arity error = $(error.code),$call,$(error.span.start),$(error.span.length)" -> println
    }
}
