import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        namespace sample.math
        public double value: Int -> Int => value + value
        """,
        """
        namespace app.main
        import sample.math as math
        calculate: -> Int => math.double(3)
        main { }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    ir! -> each node {
        node.kind == 6 -> if {
            ir![node.operand0] => argument
            "call ir = $(node.sourceModule),$(node.targetModule),$(node.symbol),$(node.typeOrigin),$(node.typeSymbol),$(argument.kind),$(argument.typeSymbol)" -> println
        }
    }
}
