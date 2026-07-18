import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        countValues values: [Int; ~] -> Int {
            values -> len => count
            0 => index!
            index! < count -> while {
                index! + 1 => index!
            }
            index!
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
