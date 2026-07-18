import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        emit sources: [Text; ~] -> Unit uses Console {
            emitOne index: Int -> Unit uses Console {
                true -> if {
                    sources[index] -> slice(UIntSize(1), UIntSize(1)) -> print
                }
            }
            0 -> emitOne
            "" -> println
        }

        main {
            ["abcd", ~] -> emit
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
