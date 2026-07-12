import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        namespace sample.text
        public echo value: Text -> Text => value
        """,
        """"
        namespace app.main
        import sample.text as text
        greeting: -> Text => text.echo("hello")
        main { }
        """",
        ~
    ] => sources!
    sources! -> llvm.emit
}
