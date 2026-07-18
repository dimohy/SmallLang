import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        emit source: Text -> Unit uses Console {
            source -> slice(UIntSize(1), UIntSize(2)) -> println
        }

        main {
            "abcd" -> emit
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
