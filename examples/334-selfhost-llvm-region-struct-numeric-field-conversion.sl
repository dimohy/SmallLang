import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Counts {
            count: Int
        }

        inspect flag: Bool -> Int {
            "abc" => value
            Counts { count: 0 } => counts!
            flag -> if {
                value -> len => counts!.count
                Counts { count: value -> len }
                "converted" -> println
            }
            0
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
