import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main {
            0 => outer!
            outer! < 2 -> while {
                outer! + 1 => outer!
                0 => inner!
                inner! < 4 -> while {
                    inner! + 1 => inner!
                    inner! == 2 -> if {
                        "skip" -> println
                        continue
                    }
                    inner! == 3 -> if {
                        "stop" -> println
                        break
                    }
                    "body" -> println
                }
                "outer" -> println
            }
            "done" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
