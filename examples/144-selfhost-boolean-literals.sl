import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.diagnostics as diagnostics
import smalllang.compiler.semantic.type_check as typeCheck
import smalllang.compiler.ast as ast

main {
    [
        """
        check: -> Bool => true and false
        main { }
        """,
        ~
    ] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources![0] -> diagnostics.analyze => errors!
    sources! -> typeCheck.analyze => typeErrors!
    sources![0] -> ast.lower => nodes!
    inferred! -> each item {
        nodes![item.astNode].kind == 25 -> if {
            "boolean type = $(item.targetSymbol)" -> println
        }
    }
    "boolean errors = $(errors! -> len)" -> println
    "boolean type errors = $(typeErrors! -> len)" -> println
}
