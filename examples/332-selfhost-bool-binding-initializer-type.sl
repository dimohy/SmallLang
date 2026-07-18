import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        struct Pair {
            value: Int
        }

        choose flag: Bool -> Pair {
            flag -> if { Pair { value: 1 } } else { Pair { value: 2 } }
        }

        main {
            1 == 1 => equal!
            UIntSize(0) => index!
            (equal! and index! < UIntSize(1)) -> while {
                false => equal!
                index! + UIntSize(1) => index!
            }
            equal! -> if { "ok" -> println }
            "abc" => text
            (text -> len) == UIntSize(3) => lengthEqual!
            lengthEqual! -> if { "length" -> println }
        }
        """,
        ~
    ] -> typedIr.lower => ir!
    false => wroteStructReturn!
    ir! -> each node {
        node.kind == 17 -> if { "binding=$(node.symbol),type=$(node.typeSymbol)" -> println }
        (not wroteStructReturn! and node.kind == 1 and node.typeOrigin == 0) -> if {
            "return-struct=$(node.typeOrigin),$(node.typeModule),$(node.typeSymbol),operandKind=$(ir![node.operand0].kind)" -> println
            true => wroteStructReturn!
        }
    }
}
