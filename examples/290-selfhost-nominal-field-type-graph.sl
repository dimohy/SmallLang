import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.semantic.type_ids as typeIds

main {
    [
        """
        namespace sample.generic

        public keep<T> value: T -> T => value
        """,
        """
        namespace app.main
        import sample.generic as generic

        struct Envelope {
            payload: Result<[Int; ~], Text>
        }

        pause value: Int -> async Int {
            value
        }

        retain value: Envelope -> async Envelope {
            generic.keep(value) => retained
            1 -> pause => pending
            pending -> await => ignored
            retained
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typeIds.resolve => semantic
    sources! -> typedIr.lower => ir!
    sources! -> typedIr.frameSlots => slots!
    0 => validFields!
    semantic.fields -> each field {
        (field.status == 0 and field.sourceModule == 1 and field.ownerSymbol >= 0 and field.fieldType >= 0) -> if {
            validFields! + 1 => validFields!
        }
    }
    0 => ownedCalls!
    0 => ownedBindings!
    ir! -> each node {
        (node.kind == 6 and node.typeId >= 0 and node.typeFlags == 3) -> if { ownedCalls! + 1 => ownedCalls! }
        (node.kind == 17 and node.typeId >= 0 and node.typeFlags == 3) -> if { ownedBindings! + 1 => ownedBindings! }
    }
    0 => ownedFrames!
    slots! -> each slot {
        (slot.typeId >= 0 and slot.typeFlags == 3 and (slot.flags / 2) % 2 == 1) -> if {
            ownedFrames! + 1 => ownedFrames!
        }
    }
    "nominal fields=$(validFields!), owned calls=$(ownedCalls!), bindings=$(ownedBindings!), frames=$(ownedFrames!)" -> println
}
