import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Counts {
            count: Int
        }

        make values: [Int; ~] -> Counts => Counts {
            count: values -> len
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
