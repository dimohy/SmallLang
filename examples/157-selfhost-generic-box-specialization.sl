import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        wrap<T> value: box T -> box T => value
        use: -> box Int => wrap(box 1)
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        (nodes![item.astNode].kind == 23 or nodes![item.astNode].kind == 11) -> if {
            "box specialization = $(nodes![item.astNode].kind),$(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "box specialization errors = $(errors! -> len)" -> println
}
