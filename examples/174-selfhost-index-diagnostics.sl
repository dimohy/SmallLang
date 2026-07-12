import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        main {
            7 => value
            value![0]
            [1, 2, ~] => values
            values![true]
        }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        (error.code == 15 or error.code == 16) -> if {
            "index error = $(error.code),$(error.expectedOrigin),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualSymbol)" -> println
        }
    }
}
