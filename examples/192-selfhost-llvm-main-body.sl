import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        identity value: Int -> Int => value
        compute: -> Int {
            identity(1) => value
            value
        }
        main {
            1 + 2 => code
            identity(code)
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
