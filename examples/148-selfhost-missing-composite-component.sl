import smalllang.compiler.semantic.type_diagnostics as typeDiagnostics

main {
    [
        """
        take values: [Unknown; ~] -> Int => 1
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeDiagnostics.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => typeName
        "composite diagnostic = $(error.code),$typeName,$(error.span.start),$(error.span.length)" -> println
    }
}
