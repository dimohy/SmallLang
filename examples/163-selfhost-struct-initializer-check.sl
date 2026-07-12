import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        struct Point {
            x: Int
        }
        main {
            Point { x: true }
            Point { y: 1 }
        }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => text
        "initializer error = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$text,$(error.span.start),$(error.span.length)" -> println
    }
}
