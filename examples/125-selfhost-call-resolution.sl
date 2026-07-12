import smalllang.compiler.semantic.calls as calls

main {
    "double value: Int -> Int => value + value\nmain { double(2) }" => source
    source -> calls.resolve => resolved!
    resolved! -> each call {
        "call = $(call.functionSymbol),$(call.status)" -> println
    }
}
