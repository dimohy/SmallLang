import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.ast as ast

main {
    [
        """
        double value: Int -> Int => value + value
        main {
            double(2) => result
            result + 1
        }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        (nodes![item.astNode].kind == 15 or nodes![item.astNode].kind == 20) -> if {
            nodes![item.astNode].start >= UIntSize(60) -> if {
                "call binding type = $(nodes![item.astNode].kind),$(item.targetSymbol)" -> println
            }
        }
    }
}
