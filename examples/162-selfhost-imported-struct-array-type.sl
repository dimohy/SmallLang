import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    ["namespace sample.shapes\npublic struct Point {\nx: Int\n}", "namespace app.main\nimport sample.shapes as shapes\ntake values: [shapes.Point; ~] -> Int => 1\nmain { take([shapes.Point { x: 1 }, ~]) }", ~] => sources!
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
