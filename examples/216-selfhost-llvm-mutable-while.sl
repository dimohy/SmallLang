import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        run limit: Int -> Unit {
            0 => index!
            index! < limit -> while {
                index! + 1 => index!
            }
            "function-done" -> println
        }

        main {
            run(3)
            0 => current!
            current! < 2 -> while {
                current! + 1 => current!
            }
            "main-done" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
