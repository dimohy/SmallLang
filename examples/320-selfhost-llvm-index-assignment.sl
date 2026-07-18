import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            [1, 2, ~] => values!
            UIntSize(0) => index
            9 => values![index]
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
