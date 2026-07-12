import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        namespace sample.shapes
        public struct Point {
            x: Int
            y: Int
        }
        """,
        """
        namespace app.main
        import sample.shapes as shapes
        main { shapes.Point { x: 1 } }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => literal
        "imported missing field = $(error.code),$(error.expectedSymbol),$literal,$(error.span.fileId),$(error.span.start),$(error.span.length)" -> println
    }
}
