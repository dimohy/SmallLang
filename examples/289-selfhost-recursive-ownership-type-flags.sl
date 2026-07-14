import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        namespace sample.generic

        public keep<T> value: T -> T => value
        """,
        """
        namespace app.main
        import sample.generic as generic

        pause value: Int -> async Int {
            value
        }

        retain value: Result<[Int; ~], Text> -> async Result<[Int; ~], Text> {
            generic.keep(value) => retained
            1 -> pause => pending
            pending -> await => ignored
            retained
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typedIr.lower => ir!
    sources! -> typedIr.frameSlots => slots!
    0 => ownedCalls!
    0 => ownedBindings!
    ir! -> each node {
        (node.kind == 6 and node.typeId >= 0 and node.typeFlags == 3) -> if {
            ownedCalls! + 1 => ownedCalls!
        }
        (node.kind == 17 and node.typeId >= 0 and node.typeFlags == 3) -> if {
            ownedBindings! + 1 => ownedBindings!
        }
    }
    0 => ownedFrames!
    slots! -> each slot {
        (slot.typeId >= 0 and slot.typeFlags == 3 and (slot.flags / 2) % 2 == 1) -> if {
            ownedFrames! + 1 => ownedFrames!
        }
    }
    "recursive owned calls=$(ownedCalls!), bindings=$(ownedBindings!), frames=$(ownedFrames!)" -> println
}
