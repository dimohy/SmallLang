import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            true -> if {
                11 => repeated
                "$repeated" -> print
            }
            true -> if {
                22 => repeated
                "$repeated" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
