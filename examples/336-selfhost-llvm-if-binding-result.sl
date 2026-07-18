import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        choose flag: Bool -> Int {
            0 => first!
            0 => second!
            flag -> if {
                1 => first!
                2 => second!
            } else {
                3 => first!
                4 => second!
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
