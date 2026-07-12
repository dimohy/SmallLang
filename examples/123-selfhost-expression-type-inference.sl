import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.ast as ast

main {
    [
        """
        sum: -> Int => 1 + 2
        less: -> Bool => 1 < 2
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].parent >= 0 -> if {
            nodes![nodes![item.astNode].parent].kind == 10 -> if {
                "expression type = $(nodes![item.astNode].kind),$(item.targetSymbol)" -> println
            }
        }
    }
}
