import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        choose value: Int -> Int {
            [1, 2, 3, ~] => values
            value < 0 -> if {
                10 -> return
            }
            1
        }

        main {
            choose(-1) => negative
            "$(negative)" -> println
            choose(1) => positive
            "$(positive)" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
