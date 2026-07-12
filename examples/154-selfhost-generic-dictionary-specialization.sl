import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        copy<K, V> value: {K: V} -> {K: V} => value
        use: -> {Int: Int} => copy({1: 2, 3: 4})
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => errors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 11 -> if {
            "dictionary specialization = $(item.origin),$(item.targetModule),$(item.targetSymbol)" -> println
        }
    }
    "dictionary specialization errors = $(errors! -> len)" -> println
}
