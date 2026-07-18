import smalllang.compiler.ast as ast
import smalllang.compiler.ir.typed as typedIr

main {
    [
        """
        main {
            not (false or false) => ready
            ready -> if {
                "grouped-not-ok" -> println
            }
        }
        """,
        ~
    ] => sources!
    sources![0] -> ast.lower => astNodes!
    0 => astIndex!
    astNodes! -> each node {
        "grouped not ast = $(astIndex!),$(node.kind),$(node.parent),$(node.operatorKind),$(node.start),$(node.length)" -> println
        astIndex! + 1 => astIndex!
    }
    sources! -> typedIr.lower => ir!
    0 => index!
    ir! -> each node {
        "grouped not ir = $(index!),$(node.kind),$(node.parent),$(node.astNode),$(node.opcode),$(node.operand0),$(node.operand1)" -> println
        index! + 1 => index!
    }
}
