import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            UIntSize(7) => wide
            true -> if {
                1 + wide => total
                "value=$(total)" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
