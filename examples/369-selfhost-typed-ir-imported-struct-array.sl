import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        import sample.items as items

        inspect: -> Int {
            items.make => values!
            values![0].value
        }

        main { }
        """,
        """
        namespace sample.items

        public struct Item {
            value: Int
        }

        public make: -> [Item; ~] {
            [Item { value: 7 }, ~]
        }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    0 => index!
    ir! -> each node {
        (node.kind == 14 or node.kind == 16) -> if {
            "struct array ir = $(index!),$(node.kind),$(node.typeId),$(node.typeKind),$(node.typeOrigin),$(node.typeModule),$(node.typeSymbol),$(node.operand0)" -> println
        }
        index! + 1 => index!
    }
}
