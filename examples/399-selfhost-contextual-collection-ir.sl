import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        makeArray: -> [UInt16; ~] => [1, 2, ~]
        makeDictionary: -> {UInt16: Int64} => {1: 9, 2: 5}
        main { }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    0 => index!
    ir! -> each node {
        (node.kind == 0 or node.kind == 1 or node.kind == 14 or node.kind == 16) -> if {
            "contextual ir = $(index!),$(node.kind),$(node.typeId),$(node.typeKind),$(node.typeOrigin),$(node.typeModule),$(node.typeSymbol),$(node.operand0)" -> println
        }
        index! + 1 => index!
    }
}
