import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            10 + 5 - 3 == 12 -> if {
                "mixed-additive" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
