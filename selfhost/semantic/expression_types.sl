namespace smalllang.compiler.semantic.expression_types

import smalllang.compiler.ast as ast
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols

public struct ExpressionType {
    sourceModule: Int
    astNode: Int
    origin: Int
    targetModule: Int
    targetSymbol: Int
}

# Bottom-up expression inference over the flat AST. Builtin ids use the stable
# nominal table: Text 1, Int 2, Bool 23.
public infer sources: [Text; ~] -> [ExpressionType; ~] {
    sources -> nominalTypes.resolve => nominal!
    [ExpressionType; ~] => inferred!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> symbols.collect => table!
        source -> resolution.resolve => resolvedNames!
        source -> calls.resolve => resolvedCalls!
        0 => astIndex!
        astIndex! < (nodes! -> len) -> while {
            nodes![astIndex!] => node
            node.kind == 13 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 1 })
            }
            node.kind == 14 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 2 })
            }
            node.kind == 15 -> if {
                -1 => resolvedNameIndex!
                0 => nameSearch!
                nameSearch! < (resolvedNames! -> len) -> while {
                    resolvedNames![nameSearch!].astNode == astIndex! -> if { nameSearch! => resolvedNameIndex! }
                    nameSearch! + 1 => nameSearch!
                }
                resolvedNameIndex! >= 0 -> if {
                    table![resolvedNames![resolvedNameIndex!].symbol] => valueSymbol
                    -1 => nominalIndex!
                    0 => typeSearch!
                    typeSearch! < (nominal! -> len) -> while {
                        nominal![typeSearch!] => candidateType
                        (candidateType.sourceModule == sourceIndex! and candidateType.typeAst == valueSymbol.typeNode) -> if {
                            typeSearch! => nominalIndex!
                        }
                        typeSearch! + 1 => typeSearch!
                    }
                    nominalIndex! >= 0 -> if {
                        nominal![nominalIndex!] => valueType
                        inferred! -> push(ExpressionType {
                            sourceModule: sourceIndex!
                            astNode: astIndex!
                            origin: valueType.origin
                            targetModule: valueType.targetModule
                            targetSymbol: valueType.targetSymbol
                        })
                    }
                }
            }
            astIndex! + 1 => astIndex!
        }

        0 => callIndex!
        callIndex! < (resolvedCalls! -> len) -> while {
            resolvedCalls![callIndex!] => call
            call.status == 0 -> if {
                table![call.functionSymbol] => function
                function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode } else { function.typeNode } => returnTypeAst
                -1 => returnNominalIndex!
                0 => returnSearch!
                returnSearch! < (nominal! -> len) -> while {
                    nominal![returnSearch!] => candidateReturn
                    (candidateReturn.sourceModule == sourceIndex! and candidateReturn.typeAst == returnTypeAst) -> if {
                        returnSearch! => returnNominalIndex!
                    }
                    returnSearch! + 1 => returnSearch!
                }
                returnNominalIndex! >= 0 -> if {
                    nominal![returnNominalIndex!] => returnType
                    inferred! -> push(ExpressionType {
                        sourceModule: sourceIndex!
                        astNode: call.callAst
                        origin: returnType.origin
                        targetModule: returnType.targetModule
                        targetSymbol: returnType.targetSymbol
                    })
                }
            }
            callIndex! + 1 => callIndex!
        }

        true => changed!
        changed! -> while {
            false => changed!
            0 => operatorIndex!
            operatorIndex! < (nodes! -> len) -> while {
                nodes![operatorIndex!] => operator
                (operator.kind >= 18 and operator.kind <= 25) -> if {
                    false => alreadyInferred!
                    0 => existingIndex!
                    existingIndex! < (inferred! -> len) -> while {
                        inferred![existingIndex!] => existing
                        (existing.sourceModule == sourceIndex! and existing.astNode == operatorIndex!) -> if { true => alreadyInferred! }
                        existingIndex! + 1 => existingIndex!
                    }
                    not alreadyInferred! -> if {
                        -1 => firstChild!
                        -1 => secondChild!
                        0 => childSearch!
                        childSearch! < (inferred! -> len) -> while {
                            inferred![childSearch!] => child
                            (child.sourceModule == sourceIndex! and nodes![child.astNode].parent == operatorIndex!) -> if {
                                firstChild! < 0 -> if { childSearch! => firstChild! } else { childSearch! => secondChild! }
                            }
                            childSearch! + 1 => childSearch!
                        }
                        false => canInfer!
                        -1 => resultBuiltin!
                        (operator.kind == 18 or operator.kind == 19) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == right.origin and left.targetModule == right.targetModule and left.targetSymbol == right.targetSymbol) -> if {
                                    true => canInfer!
                                    23 => resultBuiltin!
                                }
                            }
                        }
                        (operator.kind == 20 or operator.kind == 21) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == 1 and left.targetSymbol == 2 and right.origin == 1 and right.targetSymbol == 2) -> if {
                                    true => canInfer!
                                    2 => resultBuiltin!
                                }
                            }
                        }
                        (operator.kind == 24 or operator.kind == 25) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == 1 and left.targetSymbol == 23 and right.origin == 1 and right.targetSymbol == 23) -> if {
                                    true => canInfer!
                                    23 => resultBuiltin!
                                }
                            }
                        }
                        canInfer! -> if {
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: operatorIndex!
                                origin: 1
                                targetModule: -1
                                targetSymbol: resultBuiltin!
                            })
                            true => changed!
                        }
                    }
                }
                operatorIndex! + 1 => operatorIndex!
            }
        }
        sourceIndex! + 1 => sourceIndex!
    }
    inferred!
}
