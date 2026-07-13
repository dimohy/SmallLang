import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        choose first: Bool -> Int {
            first -> if {
                false -> if {
                    1
                } else {
                    2
                }
            } else {
                3
            }
        }

        main {
            true -> if {
                false -> if {
                    "wrong-inner" -> println
                } else {
                    "nested" -> println
                }
            } else {
                "wrong-outer" -> println
            }
            choose(true) => first
            choose(false) => second
            "values=$first,$second" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
