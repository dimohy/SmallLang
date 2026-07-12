import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        echo value: Text -> Text => value
        main { echo(1) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => argument
        "argument mismatch = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$argument,$(error.span.start),$(error.span.length)" -> println
    }
}
