import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        run: -> Int {
            pick index: Int -> Int {
                values![index]
            }
            [4, 5, ~] => values!
            pick(1)
        }

        main {
            run
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
