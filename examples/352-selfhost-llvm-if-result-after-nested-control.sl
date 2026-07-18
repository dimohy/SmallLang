import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        decide value: Int -> Bool {
            value >= 0 -> if {
                0 => marker!
                value == 1 -> if {
                    1 => marker!
                } else {
                    2 => marker!
                }
                marker! > 0
            } else {
                false
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
