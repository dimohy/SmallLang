import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        check values: [Int; ~] -> Unit uses Console {
            (false and values[0] == 1) -> if { "unexpected" -> println }
        }

        main {
            [Int; ~] => values!
            values! -> check
            "short-circuit" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
