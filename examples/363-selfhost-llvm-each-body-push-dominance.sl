import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        copyCount values: [Int; ~] -> UIntSize {
            [Int; ~] => copied!
            values -> each value {
                copied! -> push(value)
            }
            "done" -> println
            copied! -> len
        }

        main {
            [1, 2, 3, ~] -> copyCount => count
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
