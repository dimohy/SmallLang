import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    ["struct Point {\nx: Int\n}\ntake value: {Point: Point} -> Int => 1\nmain { take({Point { x: 1 }: Point { x: 2 }}) }", ~] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 38 -> if {
            "local dictionary = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "local dictionary errors = $(errors! -> len)" -> println
}
