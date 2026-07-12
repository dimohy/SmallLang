import smalllang.compiler.semantic.qualified as qualified

main {
    [
        """
        namespace sample.math
        public struct Number { }
        """,
        """
        namespace app.main
        import sample.math as math
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
