import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        make: -> [UInt16; ~] => [1, 2, ~]

        first values: [UInt16; ~] -> UInt16 => values![0]

        main {
            make -> first => value
            Int(value) => widened
            "array value = $widened" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
