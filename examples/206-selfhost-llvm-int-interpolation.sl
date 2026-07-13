import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            -2147483648 => minimum
            42 => answer
            "$minimum/$answer/$minimum" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
