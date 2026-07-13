import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        calculate value: Int -> Unit {
            value + 1 => adjusted
            "result=$(adjusted + 2 * -3), nested=$(adjusted * 2), unary=$(-adjusted), literal=$(7), name=$(adjusted)" -> println
        }

        main {
            calculate(10)
            4 => offset
            "main=$(10 + offset * -2), name=$(offset)" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
