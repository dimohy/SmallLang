import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Pair {
            left: Int
            right: Int
        }
        make: -> Pair => Pair {
            left: 1
            right: 2
        }
        identity value: Pair -> Pair => value
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
