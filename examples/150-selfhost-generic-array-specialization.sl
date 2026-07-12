import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        copy<T> values: [T; ~] -> [T; ~] => values
        use: -> [Int; ~] => copy([1, 2, ~])
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 11 -> if {
            "array specialization = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "array specialization errors = $(errors! -> len)" -> println
}
