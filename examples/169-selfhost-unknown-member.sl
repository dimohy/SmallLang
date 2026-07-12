import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        struct Point {
            x: Int
        }
        get point: Point -> Int => point.y
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => member
        "unknown member = $(error.code),$(error.expectedSymbol),$member,$(error.span.start),$(error.span.length)" -> println
    }
}
