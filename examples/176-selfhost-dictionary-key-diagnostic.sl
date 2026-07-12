import smalllang.compiler.semantic.type_check as typeCheck

main {
    [
        """
        main {
            {1: true} => flags
            flags![true]
        }
        """,
        ~
    ] => sources!
    sources! -> typeCheck.analyze => errors!
    errors! -> each error {
        error.code == 16 -> if {
            "dictionary key error = $(error.expectedOrigin),$(error.expectedModule),$(error.expectedSymbol),$(error.actualOrigin),$(error.actualModule),$(error.actualSymbol)" -> println
        }
    }
}
