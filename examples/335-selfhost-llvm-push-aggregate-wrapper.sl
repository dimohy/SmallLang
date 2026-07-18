import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Counts {
            count: Int
        }

        collect enabled: Bool -> Int {
            [Counts; ~] => items!
            "abc" => value
            enabled -> if {
                items! -> push(Counts { count: value -> len })
            }
            0
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
