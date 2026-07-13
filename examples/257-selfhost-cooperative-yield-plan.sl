import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        child value: Int -> async Int {
            value + 1
        }

        compute count: Int -> async Int {
            0 => index!
            0 => total!
            index! < count -> while {
                index! + 1 => index!
                yield
                index! -> child => pending
                pending -> await => next
                total! + next => total!
            }
            total!
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typedIr.suspensions => points!
    sources! -> typedIr.frameSlots => slots!
    0 => stateTotal!
    0 => yields!
    0 => awaits!
    points! -> each point {
        stateTotal! + point.state => stateTotal!
        point.kind == 1 -> if { yields! + 1 => yields! } else {
            awaits! + 1 => awaits!
        }
    }
    "suspensions=$(points! -> len),states=$(stateTotal!),yields=$(yields!),awaits=$(awaits!),slots=$(slots! -> len)" -> println
}
