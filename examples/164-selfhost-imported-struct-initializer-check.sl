import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        namespace sample.shapes
        public struct Point {
            x: Int
        }
        """,
        """
        namespace app.main
        import sample.shapes as shapes
        main { shapes.Point { x: true } }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => text
        "imported initializer = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$text,$(error.span.fileId),$(error.span.start),$(error.span.length)" -> println
    }
}
