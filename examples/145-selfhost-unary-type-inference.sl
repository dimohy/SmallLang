import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    ["negate: -> Int => -1\ninvert: -> Bool => not false\nmain { }", ~] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 22 -> if {
            "unary type = $(nodes![item.astNode].operatorKind),$(item.targetSymbol)" -> println
        }
    }
    "unary errors = $(errors! -> len)" -> println
}
