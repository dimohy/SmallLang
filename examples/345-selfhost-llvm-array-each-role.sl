import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        sum: -> Int {
            [1, 2, ~] => values!
            0 => total!
            values! -> each value {
                total! + value => total!
            }
            total!
        }

        main {
            sum
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
