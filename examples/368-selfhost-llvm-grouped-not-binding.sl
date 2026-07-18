import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            not (false or false) => ready
            ready -> if {
                "grouped-not-ok" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
