import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        double value: Int -> Int => value + value
        use: -> Int => double(2)
        bad: -> Text => double(2)
        main { }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => call
        "call return mismatch = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$call,$(error.span.start),$(error.span.length)" -> println
    }
}
