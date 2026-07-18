import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        import sys.file as file

        inspect source: file.SourceText -> UIntSize {
            source -> len
        }

        main {
            "ready" -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
