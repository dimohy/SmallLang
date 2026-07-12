import smalllang.compiler.semantic.type_diagnostics as typeDiagnostics

main {
    [
        """
        struct Holder {
            value: Unknown
        }
        """,
        ~
    ] => sources!
    sources! -> typeDiagnostics.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => typeName
        "local type diagnostic = $(error.code),$typeName,$(error.span.fileId),$(error.span.start),$(error.span.length)" -> println
    }
}
