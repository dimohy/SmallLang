import smalllang.compiler.semantic.type_diagnostics as typeDiagnostics

main {
    [
        """
        namespace private.model
        struct Hidden { }
        """,
        """
        namespace app.main
        import private.model as model
        struct Holder {
            value: model.Hidden
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
