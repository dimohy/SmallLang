import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Counts {
            count: Int
        }

        measure values: [Int; ~] -> Counts {
            Counts {
                count: Int(values -> len)
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
