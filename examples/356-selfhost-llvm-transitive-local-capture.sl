import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        calculate base: Int -> Int {
            addBase value: Int -> Int {
                value + base
            }

            forward value: Int -> Int {
                value -> addBase
            }

            5 -> forward
        }

        main {
            7 -> calculate => result
            result == 12 -> if {
                "transitive-capture" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
