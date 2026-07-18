import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        weighted value: Int, scale: Int, offset: Int -> Int => value * scale + offset
        main { 7 -> weighted("bad", 2) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        sources![error.sourceModule] -> slice(error.span.start, error.span.length) => argument
        "argument mismatch = $(error.code),$(error.expectedSymbol),$(error.actualSymbol),$argument" -> println
    }
}
