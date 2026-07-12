import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        main { missing(1) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => call
        "unresolved call = $(error.code),$call,$(error.span.start),$(error.span.length)" -> println
    }
}
