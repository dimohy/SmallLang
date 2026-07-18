import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        countValues values: [Int; ~] -> Int {
            count: -> Int {
                0 => index!
                index! < (values -> len) -> while {
                    index! + 1 => index!
                }
                index!
            }
            count
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
