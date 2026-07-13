import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        child value: Int -> async Int {
            value + 1
        }

        parent value: Int -> async Int {
            value * 2 => base
            [1, 2, 3, ~] => values!
            value -> child => firstTask
            value -> child => secondTask
            firstTask -> await => first
            secondTask -> await => second
            base + first + second + (values! -> len)
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typedIr.suspensions => points!
    sources! -> typedIr.frameSlots => slots!
    0 => taskSlots!
    0 => ownedSlots!
    0 => slotIndex!
    slotIndex! < (slots! -> len) -> while {
        ((slots![slotIndex!].flags / 4) % 2 == 1) -> if { taskSlots! + 1 => taskSlots! }
        ((slots![slotIndex!].flags / 2) % 2 == 1) -> if { ownedSlots! + 1 => ownedSlots! }
        slotIndex! + 1 => slotIndex!
    }
    "suspensions=$(points! -> len),states=$(points![0].state)/$(points![1].state),slots=$(slots! -> len),taskSlots=$(taskSlots!),ownedSlots=$(ownedSlots!)" -> println
}
