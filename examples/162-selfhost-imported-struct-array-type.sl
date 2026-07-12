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
        take values: [shapes.Point; ~] -> Int => 1
        main { take([shapes.Point { x: 1 }, ~]) }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![1] -> ast.lower => nodes!
    inferred! -> each item {
        (item.sourceModule == 1 and (nodes![item.astNode].kind == 39 or nodes![item.astNode].kind == 37)) -> if {
            "imported collection = $(nodes![item.astNode].kind),$(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "imported collection errors = $(errors! -> len)" -> println
}
