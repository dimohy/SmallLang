import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        namespace sample.text
        public echo value: Text -> Text => value
        """,
        """
        namespace app.main
        import sample.text as text
        main { text.echo(1) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => argument
        "imported argument = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$argument,$(error.span.start),$(error.span.length)" -> println
    }
}
