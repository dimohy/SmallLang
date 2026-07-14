import smalllang.compiler.semantic.qualified

main {
    [
        """
        namespace sample.math
        public struct Number { }
        """,
        """
        namespace app.main
        import sample.math
        main {
            math.Number
        }
        """,
        ~
    ] => sources!
    sources! -> qualified.resolve => results!
    results! -> each result {
        "qualified = $(result.sourceModule),$(result.targetModule),$(result.targetSymbol),$(result.status)" -> println
    }
}
