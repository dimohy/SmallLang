import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.type_diagnostics as typeDiagnostics

main {
    [
        """
        identity<T> value: T -> T => value
        main { }
        """,
        ~
    ] => sources!
    sources! -> nominalTypes.resolve => resolved!
    sources! -> typeDiagnostics.analyze => errors!
    resolved! -> each item {
        "generic type = $(item.origin),$(item.targetSymbol),$(item.status)" -> println
    }
    "generic errors = $(errors! -> len)" -> println
}
