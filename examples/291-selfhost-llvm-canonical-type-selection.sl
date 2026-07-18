import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.llvm.text as llvm

main {
    [
        """
        import model as model
        import sys.file as file

        struct Point {
            value: Int
        }

        makeArray: -> [Int; ~] => [1, 2, ~]

        isPositive point: Point -> Bool => point.value > 0

        sourceLength source: file.SourceText -> UIntSize => source -> len

        main {
            Point { value: 7 } => point
            [Point; ~] => points!
            points! -> each item {
                item.value => observed
            }
            points! -> parallel item {
                item -> isPositive
            } => parallelFlags!
            parallelFlags! -> each flag {
                not flag => inverted
            }
            [model.Plain { value: 9 }, ~] => plains!
            plains![0] => plain
            makeArray => values!
            UIntSize(1) => size!
        }
        """,
        """
        namespace model

        public struct Plain {
            value: Int
        }
        """,
        """
        namespace sys.file

        public struct SourceText {
            token: UInt64
        }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    false => wroteArray!
    false => wroteStruct!
    false => wrotePlainImportedStruct!
    false => wroteLocalNominalArray!
    false => wroteEachNominalMember!
    false => wroteParallelBoolArray!
    0 => eachNominalMemberCount!
    false => wroteSourceText!
    false => wroteSize!
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
        (not wroteStruct! and node.typeId >= 0 and node.typeKind == 1 and node.typeModule >= 0 and node.typeFlags % 2 == 1) -> if {
            node => canonicalStruct!
            99 => canonicalStruct!.typeOrigin
            "struct=" -> print
            canonicalStruct! -> llvm.writeType
            "" -> println
            true => wroteStruct!
        }
        (not wrotePlainImportedStruct! and node.typeId >= 0 and node.typeKind == 1 and node.typeModule == 1) -> if {
            node => canonicalPlainImportedStruct!
            99 => canonicalPlainImportedStruct!.typeOrigin
            "plain-imported-struct=" -> print
            canonicalPlainImportedStruct! -> llvm.writeType
            "" -> println
            true => wrotePlainImportedStruct!
        }
        (not wroteLocalNominalArray! and node.typeId >= 0 and node.typeKind == 3 and node.typeModule == 0 and node.typeSymbol == 0) -> if {
            "local-nominal-array=" -> print
            node -> llvm.writeType
            "" -> println
            true => wroteLocalNominalArray!
        }
        (not wroteEachNominalMember! and node.kind == 13 and node.typeId >= 0 and node.typeKind == 1 and node.typeSymbol == 2) -> if {
            "each-nominal-member=" -> print
            node -> llvm.writeType
            "" -> println
            true => wroteEachNominalMember!
        }
        (node.kind == 13 and node.typeId >= 0 and node.typeKind == 1 and node.typeSymbol == 2) -> if { eachNominalMemberCount! + 1 => eachNominalMemberCount! }
        (not wroteParallelBoolArray! and node.typeId >= 0 and node.typeKind == 3 and node.typeSymbol == 23) -> if {
            "parallel-bool-array=" -> print
            node -> llvm.writeType
            "" -> println
            true => wroteParallelBoolArray!
        }
        (not wroteSize! and node.typeSymbol == 13) -> if {
            "size=" -> print
            node -> llvm.writeType
            "" -> println
            true => wroteSize!
        }
        (not wroteSourceText! and node.typeId >= 0 and node.typeKind == 1 and node.typeOrigin == 1 and node.typeSymbol == 24) -> if {
            "source-text=" -> print
            node -> llvm.writeType
            "" -> println
            true => wroteSourceText!
        }
    }
    "each-nominal-member-count=$(eachNominalMemberCount!)" -> println
}
