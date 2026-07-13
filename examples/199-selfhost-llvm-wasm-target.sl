import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        main { }
        """,
        ~
    ] => sources!
    sources! -> llvm.emitWasm
}
