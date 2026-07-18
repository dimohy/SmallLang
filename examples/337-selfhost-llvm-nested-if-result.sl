import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        decide value: Int -> Bool {
            (value == 13 or value == 14) -> if {
                true
            } else {
                value >= 0 -> if {
                    value == 3
                } else {
                    false
                }
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
