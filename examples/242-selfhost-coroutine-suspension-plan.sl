import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        child value: Int -> async Int {
            value + 1
        }

        parent value: Int -> async Int {
            value * 2 => base
            value -> child => firstTask
            firstTask -> await => first
            first -> child => secondTask
            secondTask -> await => second
            base + second
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typedIr.suspensions => points!
    sources! -> typedIr.frameSlots => slots!
    "suspensions=$(points! -> len),states=$(points![0].state)/$(points![1].state),slots=$(slots! -> len),slotStates=$(slots![0].state)/$(slots![1].state)" -> println
}
