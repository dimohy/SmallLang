import smalllang.compiler.semantic.type_diagnostics as typeDiagnostics

main {
    [
        """
        namespace sample.math
        public struct Number { }
        """,
        """
        namespace app.main
        import sample.math as math
        struct Holder {
            value: math.Missing
        }
        """,
        ~
    ] => sources!
    sources! -> typeDiagnostics.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => typeName
        "type diagnostic = $(error.code),$typeName,$(error.span.fileId),$(error.span.start),$(error.span.length),$(error.targetSymbol)" -> println
    }
}
