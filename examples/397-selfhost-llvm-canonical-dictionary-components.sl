import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        make: -> {UInt16: Int64} => {1: 9, 2: 5}

        lookup values: {UInt16: Int64} -> Int64 => values![UInt16(1)]

        main {
            make -> lookup => value
            "value = $value" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
