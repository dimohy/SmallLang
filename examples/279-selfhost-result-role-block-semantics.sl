import smalllang.compiler.ast
import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer
import smalllang.compiler.semantic.calls
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols
import smalllang.compiler.semantic.type_check as typeCheck

main {
    """
    build seed: Int -> Int block field: Int {
        seed -> yield
        seed + 1
    }

    main {
        10 -> build field {
            field + 1 => local
            local -> println
        } => built
        built + 2
    }
    """ => roleSource

    roleSource -> ast.lower => nodes!
    roleSource -> lexer.lex => tokens!
    roleSource -> symbols.collect => table!
    roleSource -> calls.resolve => resolvedCalls!
    roleSource -> resolution.resolve => resolvedNames!
    [roleSource, ~] => sources!
    sources! -> expressionTypes.infer => inferred!
    sources! -> typeCheck.analyze => typeErrors!
    sources! -> typedIr.lower => ir!

    false => astOk!
    false => symbolOk!
    false => callOk!
    false => typeOk!
    false => referenceOk!
    false => blockTypeOk!
    (typeErrors! -> len) == 0 => typeCheckOk!
    -1 => roleReferenceAst!
    -1 => blockReferenceAst!
    false => irCallOk!
    false => irBindingOk!
    false => irBodyOk!

    nodes! -> each node {
        node.kind == 48 -> if {
            tokens![node.payloadToken].span.length == UIntSize(5) and tokens![node.secondaryToken].span.length == UIntSize(5) -> if {
                true => astOk!
            }
        }
    }
    table! -> each symbol {
        (symbol.kind == 9 and nodes![symbol.astNode].kind == 48 and tokens![symbol.nameToken].span.length == UIntSize(5)) -> if {
            true => symbolOk!
        }
    }
    resolvedCalls! -> each call {
        (nodes![call.callAst].kind == 48 and call.status == 0) -> if {
            true => callOk!
        }
    }
    resolvedNames! -> each resolved {
        table![resolved.symbol] => resolvedSymbol
        (resolvedSymbol.kind == 9 and nodes![resolvedSymbol.astNode].kind == 48) -> if {
            resolved.astNode => roleReferenceAst!
        }
        (resolvedSymbol.kind == 35 and nodes![resolvedSymbol.astNode].kind == 48) -> if {
            resolved.astNode => blockReferenceAst!
        }
    }
    inferred! -> each item {
        (nodes![item.astNode].kind == 48 and item.origin == 1 and item.targetSymbol == 2) -> if {
            true => typeOk!
        }
        (item.astNode == roleReferenceAst! and item.origin == 1 and item.targetSymbol == 2) -> if {
            true => referenceOk!
        }
        (item.astNode == blockReferenceAst! and item.origin == 1 and item.targetSymbol == 2) -> if {
            true => blockTypeOk!
        }
    }
    ir! -> each node {
        (node.kind == 6 and nodes![node.astNode].kind == 48 and node.typeOrigin == 1 and node.typeSymbol == 2) -> if {
            true => irCallOk!
        }
        (node.kind == 17 and nodes![node.astNode].kind == 48 and node.typeOrigin == 1 and node.typeSymbol == 2) -> if {
            true => irBindingOk!
        }
        (node.kind == 6 and nodes![node.astNode].kind != 48 and node.parent >= 0 and ir![node.parent].kind == 6 and nodes![ir![node.parent].astNode].kind == 48) -> if {
            true => irBodyOk!
        }
    }

    (astOk! and symbolOk! and callOk! and typeOk! and referenceOk! and blockTypeOk! and typeCheckOk! and irCallOk! and irBindingOk! and irBodyOk!) -> if {
        "role semantics = valid"
    } else {
        "role semantics = invalid"
    } -> println
}
