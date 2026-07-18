import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Input {
            values: [Int; ~]
            indices: [Int; ~]
        }

        pick input: Input -> Int {
            UIntSize(0) => zero
            true -> if {
                input.values[input.indices[zero]]
            } else {
                0
            }
        }

        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
