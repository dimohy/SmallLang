import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        now: -> Int => 1
        main { now() }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => call
        "arity error = $(error.code),$call,$(error.span.start),$(error.span.length)" -> println
    }
}
