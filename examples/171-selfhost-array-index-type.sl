import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        main {
            [1, 2, ~] => values
            values![0]
        }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 41 -> if {
            "array index = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "array index errors = $(errors! -> len)" -> println
}
