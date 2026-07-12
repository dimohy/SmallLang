import smalllang.compiler.semantic.calls as calls

main {
    [
        """
        namespace sample.math
        public double value: Int -> Int => value + value
        """,
        """
        namespace app.main
        import sample.math as math
        main { math.double(2) }
        """,
        ~
    ] => sources!
    sources! -> calls.resolveModules => resolved!
    resolved! -> each call {
        call.sourceModule == 1 -> if {
            "imported call = $(call.origin),$(call.targetSourceModule),$(call.functionSymbol),$(call.status)" -> println
        }
    }
}
