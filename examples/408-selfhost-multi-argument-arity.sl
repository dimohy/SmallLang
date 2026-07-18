import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        weighted value: Int, scale: Int, offset: Int -> Int => value * scale + offset
        main { weighted(7, 3) }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        "arity = $(error.code),$(error.expectedOrigin),$(error.actualOrigin)" -> println
    }
}
