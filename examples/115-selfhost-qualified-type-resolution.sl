import smalllang.compiler.semantic.type_resolve as typeResolve

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
            value: math.Number
        }
        """,
        ~
    ] => sources!
    sources! -> typeResolve.resolve => results!
    results! -> each result {
        "type = $(result.sourceModule),$(result.canonical),$(result.targetModule),$(result.targetSymbol),$(result.status)" -> println
    }
}
