import smalllang.compiler.semantic.type_resolve as typeResolve

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
    sources! -> typeResolve.resolve => results!
    results! -> each result {
        "private type = $(result.targetModule),$(result.targetSymbol),$(result.status)" -> println
    }
}
