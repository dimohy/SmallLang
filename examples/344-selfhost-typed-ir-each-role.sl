import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        sum: -> Int {
            [1, 2, ~] => values!
            0 => total!
            values! -> each value {
                total! + value => total!
            }
            total!
        }

        main {
            sum
        }
        """,
        ~
    ] => sources!
    sources! -> typedIr.lower => ir!
    0 => index!
    ir! -> each node {
        "ir = $(index!),$(node.kind),$(node.parent),$(node.astNode),$(node.symbol),$(node.opcode),$(node.operand0),$(node.operand1),$(node.nextOperand),$(node.flags)" -> println
        index! + 1 => index!
    }
}
