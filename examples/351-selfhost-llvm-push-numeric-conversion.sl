import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        append index: UIntSize -> Int {
            [Int; ~] => values!
            values! -> push(index)
            values![0]
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
