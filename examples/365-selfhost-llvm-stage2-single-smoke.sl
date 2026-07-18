import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            "stage2-single-ok" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
