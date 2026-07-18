import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        weighted value: Int, scale: Int, offset: Int -> Int {
            value * scale + offset
        }

        adjusted value: Int -> Int {
            value -> weighted(2, 5)
        }

        guarded value: Int -> Int => value > 0 -> if {
            value -> weighted(3, 1)
        } else {
            0
        }

        main {
            7 -> weighted(3, 2) => flowed
            weighted(7, 4, 1) => direct
            6 -> adjusted => nested
            5 -> guarded => region
            "flowed=$flowed, direct=$direct, nested=$nested, region=$region" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
