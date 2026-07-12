import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        namespace private.math
        double value: Int -> Int => value + value
        """,
        """
        namespace app.main
        import private.math as math
        main { math.double(1) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => call
        "private call diagnostic = $(error.code),$call,$(error.span.start),$(error.span.length)" -> println
    }
}
