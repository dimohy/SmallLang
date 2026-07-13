import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        child value: Int -> async Int {
            value + 1
        }

        accumulate count: Int -> async Int {
            0 => index!
            0 => total!
            index! < count -> while {
                index! -> child => firstTask
                index! + 10 -> child => secondTask
                firstTask -> await => first
                secondTask -> await => second
                index! + 1 => index!
                index! == 2 -> if continue
                total! + first + second => total!
                index! == 3 -> if {
                    break
                }
            }
            total!
        }

        main { }
        """,
        ~
    ] => sources!

    sources! -> typedIr.suspensions => points!
    sources! -> typedIr.frameSlots => slots!
    sources! -> typedIr.lower => ir!
    0 => stateTotal!
    points! -> each point {
        stateTotal! + point.state => stateTotal!
    }
    0 => loopEdges!
    0 => targetedEdges!
    0 => edgeOpcodes!
    ir! -> each node {
        (node.kind == 21 or node.kind == 22) -> if {
            loopEdges! + 1 => loopEdges!
            edgeOpcodes! + node.opcode => edgeOpcodes!
            (node.operand0 >= 0 and ir![node.operand0].kind == 20) -> if {
                targetedEdges! + 1 => targetedEdges!
            }
        }
    }
    "loopSuspensions=$(points! -> len),stateTotal=$(stateTotal!),slots=$(slots! -> len),edges=$(loopEdges!),targeted=$(targetedEdges!),opcodes=$(edgeOpcodes!)" -> println
}
