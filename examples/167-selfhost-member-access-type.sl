import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        struct Point {
            x: Int
        }
        get point: Point -> Int => point.x
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 36 -> if {
            "member type = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "member errors = $(errors! -> len)" -> println
}
