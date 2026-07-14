import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        struct Point {
            value: Int
        }

        makeArray: -> [Int; ~] => [1, 2, ~]

        main {
            Point { value: 7 } => point
            makeArray => values!
        }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    false => wroteArray!
    false => wroteStruct!
    ir! -> each node {
        (not wroteArray! and node.typeId >= 0 and node.typeKind == 3) -> if {
            node => canonicalArray!
            99 => canonicalArray!.typeOrigin
            0 => canonicalArray!.typeSymbol
            "array=" -> print
            canonicalArray! -> llvm.writeType
            "" -> println
            true => wroteArray!
        }
        (not wroteStruct! and node.typeId >= 0 and node.typeKind == 1 and node.typeFlags % 2 == 1) -> if {
            node => canonicalStruct!
            99 => canonicalStruct!.typeOrigin
            "struct=" -> print
            canonicalStruct! -> llvm.writeType
            "" -> println
            true => wroteStruct!
        }
    }
}
