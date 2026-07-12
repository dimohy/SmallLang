import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        namespace sample.shapes
        public struct Point {
            x: Int
        }
        """,
        """
        namespace app.main
        import sample.shapes as shapes
        get point: shapes.Point -> Int => point.x
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![1] -> ast.lower => nodes!
    inferred! -> each item {
        (item.sourceModule == 1 and nodes![item.astNode].kind == 36) -> if {
            "imported member type = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "imported member errors = $(errors! -> len)" -> println
}
