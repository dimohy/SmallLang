import smalllang.compiler.semantic.nominal_types as nominalTypes

main {
    [
        """
        namespace sample.math
        public struct Number { }
        """,
        """
        namespace app.main
        import sample.math as math
        struct Local {
            count: Int
        }
        struct Uses {
            local: Local
            remote: math.Number
            missing: Unknown
        }
        """,
        ~
    ] => sources!
    sources! -> nominalTypes.resolve => resolved!
    resolved! -> each item {
        item.sourceModule == 1 -> if {
            "nominal = $(item.origin),$(item.targetModule),$(item.targetSymbol),$(item.status)" -> println
        }
    }
}
