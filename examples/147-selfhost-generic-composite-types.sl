import smalllang.compiler.semantic.composite_types as compositeTypes

main {
    [
        """
        copy<T> values: [T; ~] -> [T; ~] => values
        map<T> value: {Text: T} -> {Text: T} => value
        main { }
        """,
        ~
    ] => sources!
    sources! -> compositeTypes.resolve => resolved!
    resolved! -> each item {
        "composite = $(item.kind),$(item.elementOrigin),$(item.elementSymbol),$(item.keyOrigin),$(item.keySymbol),$(item.valueOrigin),$(item.valueSymbol),$(item.status)" -> println
    }
}
