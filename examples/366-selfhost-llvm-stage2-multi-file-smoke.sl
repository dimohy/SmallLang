import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        namespace stage2.smoke

        public message: -> Text => "stage2-multi-ok"
        """,
        """
        import stage2.smoke as smoke

        main {
            smoke.message -> println
        }
        """,
        ~
    ] => sources!
    sources! -> llvm.emit
}
