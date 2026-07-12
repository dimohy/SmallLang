import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        struct Point {
            x: Int
            y: Int
        }
        main { Point { x: 1 } }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => literal
        "missing field = $(error.code),$(error.expectedSymbol),$literal,$(error.span.start),$(error.span.length)" -> println
    }
}
