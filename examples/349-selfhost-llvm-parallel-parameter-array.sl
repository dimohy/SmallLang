import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        incrementOne value: Int -> Int {
            value + 1
        }

        increment values: [Int; ~] -> [Int; ~] {
            values -> parallel value {
                value -> incrementOne
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
