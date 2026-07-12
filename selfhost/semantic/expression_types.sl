namespace smalllang.compiler.semantic.expression_types

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

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
    sources -> calls.resolveModules => moduleCalls!
    [ExpressionType; ~] => inferred!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        source -> symbols.collect => table!
        source -> resolution.resolve => resolvedNames!
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
                tokens![node.payloadToken] => nameToken
                false => booleanLiteral!
                nameToken.span.length == UIntSize(4) -> if {
                    source -> byte(nameToken.span.start) => boolByte0
                    source -> byte(nameToken.span.start + UIntSize(1)) => boolByte1
                    source -> byte(nameToken.span.start + UIntSize(2)) => boolByte2
                    source -> byte(nameToken.span.start + UIntSize(3)) => boolByte3
                    (boolByte0 == UInt8(116) and boolByte1 == UInt8(114) and boolByte2 == UInt8(117) and boolByte3 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                nameToken.span.length == UIntSize(5) -> if {
                    source -> byte(nameToken.span.start) => falseByte0
                    source -> byte(nameToken.span.start + UIntSize(1)) => falseByte1
                    source -> byte(nameToken.span.start + UIntSize(2)) => falseByte2
                    source -> byte(nameToken.span.start + UIntSize(3)) => falseByte3
                    source -> byte(nameToken.span.start + UIntSize(4)) => falseByte4
                    (falseByte0 == UInt8(102) and falseByte1 == UInt8(97) and falseByte2 == UInt8(108) and falseByte3 == UInt8(115) and falseByte4 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                booleanLiteral! -> if {
                    inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 23 })
                }
                -1 => resolvedNameIndex!
                0 => nameSearch!
                nameSearch! < (resolvedNames! -> len) -> while {
                    resolvedNames![nameSearch!].astNode == astIndex! -> if { nameSearch! => resolvedNameIndex! }
                    nameSearch! + 1 => nameSearch!
                }
                (not booleanLiteral! and resolvedNameIndex! >= 0) -> if {
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

        true => changed!
        changed! -> while {
            false => changed!
            0 => callIndex!
            callIndex! < (moduleCalls! -> len) -> while {
                moduleCalls![callIndex!] => call
                (call.sourceModule == sourceIndex! and call.status == 0) -> if {
                    false => callInferred!
                    0 => callExistingIndex!
                    callExistingIndex! < (inferred! -> len) -> while {
                        inferred![callExistingIndex!] => callExisting
                        (callExisting.sourceModule == sourceIndex! and callExisting.astNode == call.callAst) -> if { true => callInferred! }
                        callExistingIndex! + 1 => callExistingIndex!
                    }
                    not callInferred! -> if {
                        sources[call.targetSourceModule] -> symbols.collect => targetTable!
                        targetTable![call.functionSymbol] => function
                        function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode } else { function.typeNode } => returnTypeAst
                        -1 => returnNominalIndex!
                        0 => returnSearch!
                        returnSearch! < (nominal! -> len) -> while {
                            nominal![returnSearch!] => candidateReturn
                            (candidateReturn.sourceModule == call.targetSourceModule and candidateReturn.typeAst == returnTypeAst) -> if {
                                returnSearch! => returnNominalIndex!
                            }
                            returnSearch! + 1 => returnSearch!
                        }
                        returnNominalIndex! >= 0 -> if {
                            nominal![returnNominalIndex!] => returnType
                            returnType.origin => resultOrigin!
                            returnType.targetModule => resultModule!
                            returnType.targetSymbol => resultSymbol!
                            returnType.origin != 3 => canInferCall!
                            returnType.origin == 3 -> if {
                                function.secondaryTypeNode >= 0 -> if {
                                    -1 => inputNominalIndex!
                                    0 => inputSearch!
                                    inputSearch! < (nominal! -> len) -> while {
                                        nominal![inputSearch!] => candidateInput
                                        (candidateInput.sourceModule == call.targetSourceModule and candidateInput.typeAst == function.typeNode) -> if {
                                            inputSearch! => inputNominalIndex!
                                        }
                                        inputSearch! + 1 => inputSearch!
                                    }
                                    inputNominalIndex! >= 0 -> if {
                                        nominal![inputNominalIndex!] => inputType
                                        (inputType.origin == 3 and inputType.targetSymbol == returnType.targetSymbol) -> if {
                                            -1 => argumentTypeIndex!
                                            1000000 => argumentDistance!
                                            0 => argumentSearch!
                                            argumentSearch! < (inferred! -> len) -> while {
                                                inferred![argumentSearch!] => argumentType
                                                argumentType.sourceModule == sourceIndex! -> if {
                                                    nodes![argumentType.astNode].parent => argumentAncestor!
                                                    1 => distance!
                                                    false => belongsToCall!
                                                    (argumentAncestor! >= 0 and not belongsToCall!) -> while {
                                                        argumentAncestor! == call.callAst -> if { true => belongsToCall! } else {
                                                            nodes![argumentAncestor!].parent => argumentAncestor!
                                                            distance! + 1 => distance!
                                                        }
                                                    }
                                                    (belongsToCall! and distance! < argumentDistance!) -> if {
                                                        argumentSearch! => argumentTypeIndex!
                                                        distance! => argumentDistance!
                                                    }
                                                }
                                                argumentSearch! + 1 => argumentSearch!
                                            }
                                            argumentTypeIndex! >= 0 -> if {
                                                inferred![argumentTypeIndex!] => specialized
                                                specialized.origin => resultOrigin!
                                                specialized.targetModule => resultModule!
                                                specialized.targetSymbol => resultSymbol!
                                                true => canInferCall!
                                            }
                                        }
                                    }
                                }
                            }
                            canInferCall! -> if {
                                inferred! -> push(ExpressionType {
                                    sourceModule: sourceIndex!
                                    astNode: call.callAst
                                    origin: resultOrigin!
                                    targetModule: resultModule!
                                    targetSymbol: resultSymbol!
                                })
                                true => changed!
                            }
                        }
                    }
                }
                callIndex! + 1 => callIndex!
            }
            0 => bindingSymbolIndex!
            bindingSymbolIndex! < (table! -> len) -> while {
                table![bindingSymbolIndex!] => bindingSymbol
                bindingSymbol.kind == 9 -> if {
                    -1 => bindingValueIndex!
                    1000000 => bindingDistance!
                    0 => valueSearch!
                    valueSearch! < (inferred! -> len) -> while {
                        inferred![valueSearch!] => valueType
                        valueType.sourceModule == sourceIndex! -> if {
                            nodes![valueType.astNode].parent => ancestor!
                            1 => distance!
                            false => belongsToBinding!
                            (ancestor! >= 0 and not belongsToBinding!) -> while {
                                ancestor! == bindingSymbol.astNode -> if {
                                    true => belongsToBinding!
                                } else {
                                    nodes![ancestor!].parent => ancestor!
                                    distance! + 1 => distance!
                                }
                            }
                            (belongsToBinding! and distance! < bindingDistance!) -> if {
                                valueSearch! => bindingValueIndex!
                                distance! => bindingDistance!
                            }
                        }
                        valueSearch! + 1 => valueSearch!
                    }
                    bindingValueIndex! >= 0 -> if {
                        inferred![bindingValueIndex!] => bindingType
                        0 => referenceIndex!
                        referenceIndex! < (resolvedNames! -> len) -> while {
                            resolvedNames![referenceIndex!] => reference
                            reference.symbol == bindingSymbolIndex! -> if {
                                false => referenceInferred!
                                0 => inferredSearch!
                                inferredSearch! < (inferred! -> len) -> while {
                                    inferred![inferredSearch!] => existing
                                    (existing.sourceModule == sourceIndex! and existing.astNode == reference.astNode) -> if {
                                        true => referenceInferred!
                                    }
                                    inferredSearch! + 1 => inferredSearch!
                                }
                                not referenceInferred! -> if {
                                    inferred! -> push(ExpressionType {
                                        sourceModule: sourceIndex!
                                        astNode: reference.astNode
                                        origin: bindingType.origin
                                        targetModule: bindingType.targetModule
                                        targetSymbol: bindingType.targetSymbol
                                    })
                                    true => changed!
                                }
                            }
                            referenceIndex! + 1 => referenceIndex!
                        }
                    }
                }
                bindingSymbolIndex! + 1 => bindingSymbolIndex!
            }
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
                        operator.kind == 22 -> if {
                            firstChild! >= 0 -> if {
                                inferred![firstChild!] => operand
                                (operator.operatorKind == -26 and operand.origin == 1 and operand.targetSymbol == 23) -> if {
                                    true => canInfer!
                                    23 => resultBuiltin!
                                }
                                (operator.operatorKind == grammar.tokenIdMinus and operand.origin == 1 and operand.targetSymbol == 2) -> if {
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
