import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    ["struct Point {\nx: Int\n}\ntake values: [Point; ~] -> Int => 1\nmain { take([Point { x: 1 }, Point { x: 2 }, ~]) }", ~] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        (nodes![item.astNode].kind == 39 or nodes![item.astNode].kind == 37) -> if {
            "local collection = $(nodes![item.astNode].kind),$(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "local collection errors = $(errors! -> len)" -> println
}
