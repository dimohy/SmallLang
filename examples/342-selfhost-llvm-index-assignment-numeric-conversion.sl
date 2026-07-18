import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            [1, 2, ~] => values!
            UIntSize(9) => wide
            wide => values![0]
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
