import smalllang.compiler.semantic.calls as calls

main {
    [
        """
        namespace private.math
        double value: Int -> Int => value + value
        """,
        """
        namespace app.main
        import private.math as math
        main { math.double(2) }
        """,
        ~
    ] => sources!
    sources! -> calls.resolveModules => resolved!
    resolved! -> each call {
        call.sourceModule == 1 -> if {
            "private call = $(call.origin),$(call.targetSourceModule),$(call.functionSymbol),$(call.status)" -> println
        }
    }
}
