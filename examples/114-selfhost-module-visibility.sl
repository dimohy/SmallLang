import smalllang.compiler.semantic.qualified as qualified

main {
    [
        """
        namespace private.model
        struct Hidden { }
        """,
        """
        namespace app.main
        import private.model as model
        main {
            model.Hidden
        }
        """,
        ~
    ] => sources!
    sources! -> qualified.resolve => results!
    results![0] => result
    "visibility = $(result.targetModule),$(result.targetSymbol),$(result.status)" -> println
}
