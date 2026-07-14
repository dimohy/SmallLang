import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        matches value: Int -> Bool => value == 2 or value == 5 or value == 10 or value == 14 or value == 19 or value == 20
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
