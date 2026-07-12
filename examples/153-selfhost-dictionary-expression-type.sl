import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.ast as ast

main {
    [
        """
        main { {1: 2, 3: 4} }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 38 -> if {
            "dictionary expression = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
}
