namespace smalllang.compiler.ir.typed

import smalllang.compiler.ast as ast
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols

# Stable, flat typed IR. Indexes are relocatable array offsets so later LLVM
# lowering can consume the table without allocating an object graph.
# Kinds: 0 function, 1 return, 2 Text constant, 3 Int constant,
# 4 Bool constant, 5 name, 6 call, 7 unary, 8 binary, 9 other expression,
# 10 parameter, 11 entry point, 12 struct literal, 13 member access,
# 14 array literal, 15 index access, 16 dictionary literal, 17 binding.
public struct TypedIrNode {
    kind: Int
    parent: Int
    sourceModule: Int
    astNode: Int
    symbol: Int
    targetModule: Int
    typeOrigin: Int
    typeModule: Int
    typeSymbol: Int
    payloadToken: Int
    opcode: Int
    operand0: Int
    operand1: Int
    nextOperand: Int
    flags: Int
}

public lower sources: [Text; ~] -> [TypedIrNode; ~] {
    sources -> expressionTypes.infer => inferred!
    sources -> nominalTypes.resolve => nominal!
    sources -> compositeTypes.resolve => composite!
    sources -> calls.resolveModules => resolvedCalls!
    [TypedIrNode; ~] => results!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> symbols.collect => table!
        source -> resolution.resolve => resolvedNames!
        0 => symbolIndex!
        symbolIndex! < (table! -> len) -> while {
            table![symbolIndex!] => function
            function.kind == 7 -> if {
                -1 => resultTypeIndex!
                1000000 => resultDistance!
                0 => typeSearch!
                typeSearch! < (inferred! -> len) -> while {
                    inferred![typeSearch!] => candidateType
                    candidateType.sourceModule == sourceIndex! -> if {
                        nodes![candidateType.astNode].parent => ancestor!
                        1 => distance!
                        false => belongsToFunction!
                        (ancestor! >= 0 and not belongsToFunction!) -> while {
                            ancestor! == function.astNode -> if { true => belongsToFunction! } else {
                                nodes![ancestor!].parent => ancestor!
                                distance! + 1 => distance!
                            }
                        }
                        (belongsToFunction! and distance! < resultDistance!) -> if {
                            typeSearch! => resultTypeIndex!
                            distance! => resultDistance!
                        }
                    }
                    typeSearch! + 1 => typeSearch!
                }
                resultTypeIndex! >= 0 -> if {
                    inferred![resultTypeIndex!] => resultType
                    -1 => parameterSymbol!
                    -1 => parameterTypeIndex!
                    0 => parameterSearch!
                    parameterSearch! < (table! -> len) -> while {
                        table![parameterSearch!] => parameterCandidate
                        (parameterCandidate.kind == 35 and parameterCandidate.parent == symbolIndex!) -> if { parameterSearch! => parameterSymbol! }
                        parameterSearch! + 1 => parameterSearch!
                    }
                    parameterSymbol! >= 0 -> if {
                        0 => parameterNameSearch!
                        parameterNameSearch! < (resolvedNames! -> len) -> while {
                            resolvedNames![parameterNameSearch!] => parameterName
                            parameterName.symbol == parameterSymbol! -> if {
                                0 => parameterInferredSearch!
                                parameterInferredSearch! < (inferred! -> len) -> while {
                                    inferred![parameterInferredSearch!] => parameterInferred
                                    (parameterInferred.sourceModule == sourceIndex! and parameterInferred.astNode == parameterName.astNode) -> if { parameterInferredSearch! => parameterTypeIndex! }
                                    parameterInferredSearch! + 1 => parameterInferredSearch!
                                }
                            }
                            parameterNameSearch! + 1 => parameterNameSearch!
                        }
                    }
                    -1 => parameterOrigin!
                    -1 => parameterModule!
                    -1 => parameterTypeSymbol!
                    parameterTypeIndex! >= 0 -> if {
                        inferred![parameterTypeIndex!] => inferredParameterType
                        inferredParameterType.origin => parameterOrigin!
                        inferredParameterType.targetModule => parameterModule!
                        inferredParameterType.targetSymbol => parameterTypeSymbol!
                    }
                    (parameterSymbol! >= 0 and parameterOrigin! < 0) -> if {
                        table![parameterSymbol!] => declaredParameter
                        0 => declaredNominalSearch!
                        declaredNominalSearch! < (nominal! -> len) -> while {
                            nominal![declaredNominalSearch!] => declaredNominal
                            (declaredNominal.sourceModule == sourceIndex! and declaredNominal.typeAst == declaredParameter.typeNode) -> if {
                                declaredNominal.origin => parameterOrigin!
                                declaredNominal.targetModule => parameterModule!
                                declaredNominal.targetSymbol => parameterTypeSymbol!
                            }
                            declaredNominalSearch! + 1 => declaredNominalSearch!
                        }
                        parameterOrigin! < 0 -> if {
                            0 => declaredCompositeSearch!
                            declaredCompositeSearch! < (composite! -> len) -> while {
                                composite![declaredCompositeSearch!] => declaredComposite
                                (declaredComposite.sourceModule == sourceIndex! and declaredComposite.typeAst == declaredParameter.typeNode) -> if {
                                    10 + declaredComposite.kind => parameterOrigin!
                                    declaredComposite.elementModule => parameterModule!
                                    declaredComposite.elementSymbol => parameterTypeSymbol!
                                }
                                declaredCompositeSearch! + 1 => declaredCompositeSearch!
                            }
                        }
                    }
                    results! -> len => functionIr!
                    functionIr! + 1 => returnIr
                    -1 => parameterIr!
                    (parameterSymbol! >= 0 and parameterOrigin! >= 0) -> if { returnIr + 1 => parameterIr! }
                    results! -> push(TypedIrNode {
                        kind: 0
                        parent: -1
                        sourceModule: sourceIndex!
                        astNode: function.astNode
                        symbol: symbolIndex!
                        targetModule: sourceIndex!
                        typeOrigin: resultType.origin
                        typeModule: resultType.targetModule
                        typeSymbol: resultType.targetSymbol
                        payloadToken: function.nameToken
                        opcode: -1
                        operand0: returnIr
                        operand1: parameterIr!
                        nextOperand: -1
                        flags: function.flags
                    })
                    results! -> push(TypedIrNode {
                        kind: 1
                        parent: functionIr!
                        sourceModule: sourceIndex!
                        astNode: resultType.astNode
                        symbol: symbolIndex!
                        targetModule: -1
                        typeOrigin: resultType.origin
                        typeModule: resultType.targetModule
                        typeSymbol: resultType.targetSymbol
                        payloadToken: -1
                        opcode: -1
                        operand0: -1
                        operand1: -1
                        nextOperand: -1
                        flags: 0
                    })
                    parameterIr! >= 0 -> if {
                        table![parameterSymbol!] => parameter
                        results! -> push(TypedIrNode {
                            kind: 10
                            parent: functionIr!
                            sourceModule: sourceIndex!
                            astNode: function.astNode
                            symbol: parameterSymbol!
                            targetModule: sourceIndex!
                            typeOrigin: parameterOrigin!
                            typeModule: parameterModule!
                            typeSymbol: parameterTypeSymbol!
                            payloadToken: parameter.nameToken
                            opcode: -1
                            operand0: -1
                            operand1: -1
                            nextOperand: -1
                            flags: parameter.flags
                        })
                    }

                    [Int; ~] => astToIr!
                    0 => astMapIndex!
                    astMapIndex! < (nodes! -> len) -> while {
                        astToIr! -> push(-1)
                        astMapIndex! + 1 => astMapIndex!
                    }
                    results! -> len => expressionIrStart
                    0 => bindingAstIndex!
                    bindingAstIndex! < (nodes! -> len) -> while {
                        nodes![bindingAstIndex!] => bindingAst
                        bindingAst.kind == 9 -> if {
                            bindingAst.parent => bindingAncestor!
                            false => bindingBelongsToFunction!
                            (bindingAncestor! >= 0 and not bindingBelongsToFunction!) -> while {
                                bindingAncestor! == function.astNode -> if { true => bindingBelongsToFunction! } else { nodes![bindingAncestor!].parent => bindingAncestor! }
                            }
                            bindingBelongsToFunction! -> if {
                                -1 => bindingTypeIndex!
                                1000000 => bindingTypeDistance!
                                0 => bindingTypeSearch!
                                bindingTypeSearch! < (inferred! -> len) -> while {
                                    inferred![bindingTypeSearch!] => bindingTypeCandidate
                                    bindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                        nodes![bindingTypeCandidate.astNode].parent => bindingTypeAncestor!
                                        1 => bindingDistance!
                                        false => belongsToBinding!
                                        (bindingTypeAncestor! >= 0 and not belongsToBinding!) -> while {
                                            bindingTypeAncestor! == bindingAstIndex! -> if { true => belongsToBinding! } else {
                                                nodes![bindingTypeAncestor!].parent => bindingTypeAncestor!
                                                bindingDistance! + 1 => bindingDistance!
                                            }
                                        }
                                        (belongsToBinding! and bindingDistance! < bindingTypeDistance!) -> if {
                                            bindingTypeSearch! => bindingTypeIndex!
                                            bindingDistance! => bindingTypeDistance!
                                        }
                                    }
                                    bindingTypeSearch! + 1 => bindingTypeSearch!
                                }
                                bindingTypeIndex! >= 0 -> if {
                                    -1 => bindingSymbol!
                                    0 => bindingSymbolSearch!
                                    bindingSymbolSearch! < (table! -> len) -> while {
                                        table![bindingSymbolSearch!].astNode == bindingAstIndex! -> if { bindingSymbolSearch! => bindingSymbol! }
                                        bindingSymbolSearch! + 1 => bindingSymbolSearch!
                                    }
                                    inferred![bindingTypeIndex!] => bindingType
                                    results! -> len => bindingIr
                                    bindingIr => astToIr![bindingAstIndex!]
                                    results! -> push(TypedIrNode {
                                        kind: 17
                                        parent: returnIr
                                        sourceModule: sourceIndex!
                                        astNode: bindingAstIndex!
                                        symbol: bindingSymbol!
                                        targetModule: sourceIndex!
                                        typeOrigin: bindingType.origin
                                        typeModule: bindingType.targetModule
                                        typeSymbol: bindingType.targetSymbol
                                        payloadToken: bindingAst.payloadToken
                                        opcode: -1
                                        operand0: -1
                                        operand1: -1
                                        nextOperand: -1
                                        flags: bindingAst.flags
                                    })
                                }
                            }
                        }
                        bindingAstIndex! + 1 => bindingAstIndex!
                    }
                    0 => expressionAstIndex!
                    expressionAstIndex! < (nodes! -> len) -> while {
                        -1 => expressionTypeIndex!
                        0 => expressionTypeSearch!
                        expressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![expressionTypeSearch!] => candidateExpressionType
                            (candidateExpressionType.sourceModule == sourceIndex! and candidateExpressionType.astNode == expressionAstIndex!) -> if { expressionTypeSearch! => expressionTypeIndex! }
                            expressionTypeSearch! + 1 => expressionTypeSearch!
                        }
                        expressionTypeIndex! >= 0 -> if {
                            nodes![expressionAstIndex!].parent => expressionAncestor!
                            false => expressionBelongsToFunction!
                            (expressionAncestor! >= 0 and not expressionBelongsToFunction!) -> while {
                                expressionAncestor! == function.astNode -> if { true => expressionBelongsToFunction! } else {
                                    nodes![expressionAncestor!].parent => expressionAncestor!
                                }
                            }
                            expressionBelongsToFunction! -> if {
                                inferred![expressionTypeIndex!] => expressionType
                                nodes![expressionAstIndex!] => expression
                                9 => expressionKind!
                                expression.kind == 13 -> if { 2 => expressionKind! }
                                expression.kind == 14 -> if { 3 => expressionKind! }
                                expression.kind == 15 -> if {
                                    (expressionType.origin == 1 and expressionType.targetSymbol == 23) -> if { 4 => expressionKind! } else { 5 => expressionKind! }
                                }
                                expression.kind == 11 -> if { 6 => expressionKind! }
                                expression.kind == 22 -> if { 7 => expressionKind! }
                                (expression.kind >= 18 and expression.kind <= 21) -> if { 8 => expressionKind! }
                                (expression.kind == 24 or expression.kind == 25) -> if { 8 => expressionKind! }
                                expression.kind == 39 -> if { 12 => expressionKind! }
                                expression.kind == 36 -> if { 13 => expressionKind! }
                                expression.kind == 37 -> if { 14 => expressionKind! }
                                expression.kind == 38 -> if { 16 => expressionKind! }
                                expression.kind == 41 -> if { 15 => expressionKind! }
                                0 => propertyCallSearch!
                                propertyCallSearch! < (resolvedCalls! -> len) -> while {
                                    resolvedCalls![propertyCallSearch!] => propertyCall
                                    (propertyCall.sourceModule == sourceIndex! and propertyCall.callAst == expressionAstIndex! and propertyCall.status == 0) -> if { 6 => expressionKind! }
                                    propertyCallSearch! + 1 => propertyCallSearch!
                                }
                                results! -> len => expressionIr
                                expressionIr => astToIr![expressionAstIndex!]
                                -1 => expressionSymbol!
                                -1 => expressionTargetModule!
                                expressionKind! == 5 -> if {
                                    0 => nameResolutionSearch!
                                    nameResolutionSearch! < (resolvedNames! -> len) -> while {
                                        resolvedNames![nameResolutionSearch!] => resolvedName
                                        resolvedName.astNode == expressionAstIndex! -> if { resolvedName.symbol => expressionSymbol! }
                                        nameResolutionSearch! + 1 => nameResolutionSearch!
                                    }
                                }
                                expressionKind! == 6 -> if {
                                    0 => callSearch!
                                    callSearch! < (resolvedCalls! -> len) -> while {
                                        resolvedCalls![callSearch!] => resolvedCall
                                        (resolvedCall.sourceModule == sourceIndex! and resolvedCall.callAst == expressionAstIndex! and resolvedCall.status == 0) -> if {
                                            resolvedCall.functionSymbol => expressionSymbol!
                                            resolvedCall.targetSourceModule => expressionTargetModule!
                                        }
                                        callSearch! + 1 => callSearch!
                                    }
                                }
                                results! -> push(TypedIrNode {
                                    kind: expressionKind!
                                    parent: returnIr
                                    sourceModule: sourceIndex!
                                    astNode: expressionAstIndex!
                                    symbol: expressionSymbol!
                                    targetModule: expressionTargetModule!
                                    typeOrigin: expressionType.origin
                                    typeModule: expressionType.targetModule
                                    typeSymbol: expressionType.targetSymbol
                                    payloadToken: expression.payloadToken
                                    opcode: expression.operatorKind
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: expression.flags
                                })
                            }
                        }
                        expressionAstIndex! + 1 => expressionAstIndex!
                    }

                    results! -> len => expressionIrEnd
                    expressionIrStart => parentIrIndex!
                    parentIrIndex! < expressionIrEnd -> while {
                        results![parentIrIndex!] => expressionIrNode!
                        nodes![expressionIrNode!.astNode].parent => parentAst!
                        -1 => semanticParentIr!
                        (parentAst! >= 0 and parentAst! != function.astNode and semanticParentIr! < 0) -> while {
                            astToIr![parentAst!] >= 0 -> if { astToIr![parentAst!] => semanticParentIr! } else {
                                nodes![parentAst!].parent => parentAst!
                            }
                        }
                        semanticParentIr! >= 0 -> if { semanticParentIr! => expressionIrNode!.parent }
                        expressionIrNode! => results![parentIrIndex!]
                        parentIrIndex! + 1 => parentIrIndex!
                    }

                    expressionIrStart => operandIrIndex!
                    operandIrIndex! < expressionIrEnd -> while {
                        results![operandIrIndex!] => operatorIr!
                        (operatorIr!.kind == 6 or operatorIr!.kind == 7 or operatorIr!.kind == 8 or operatorIr!.kind == 13 or operatorIr!.kind == 15 or operatorIr!.kind == 17) -> if {
                            -1 => firstOperand!
                            -1 => secondOperand!
                            UIntSize(0) => firstStart!
                            0 => childIrIndex!
                            childIrIndex! < expressionIrEnd -> while {
                                results![childIrIndex!] => childIr
                                childIr.parent == operandIrIndex! -> if {
                                    nodes![childIr.astNode].start => childStart
                                    firstOperand! < 0 -> if {
                                        childIrIndex! => firstOperand!
                                        childStart => firstStart!
                                    } else {
                                        childStart < firstStart! -> if {
                                            firstOperand! => secondOperand!
                                            childIrIndex! => firstOperand!
                                            childStart => firstStart!
                                        } else {
                                            secondOperand! < 0 -> if { childIrIndex! => secondOperand! }
                                        }
                                    }
                                }
                                childIrIndex! + 1 => childIrIndex!
                            }
                            firstOperand! => operatorIr!.operand0
                            (operatorIr!.kind == 6 or operatorIr!.kind == 8 or operatorIr!.kind == 15) -> if { secondOperand! => operatorIr!.operand1 }
                            operatorIr! => results![operandIrIndex!]
                        }
                        operandIrIndex! + 1 => operandIrIndex!
                    }

                    expressionIrStart => siblingIrIndex!
                    siblingIrIndex! < expressionIrEnd -> while {
                        results![siblingIrIndex!] => sibling!
                        -1 => nextSibling!
                        UIntSize(0) => nextSiblingStart!
                        expressionIrStart => siblingSearch!
                        siblingSearch! < expressionIrEnd -> while {
                            results![siblingSearch!] => siblingCandidate
                            (siblingCandidate.parent == sibling!.parent and nodes![siblingCandidate.astNode].start > nodes![sibling!.astNode].start) -> if {
                                nodes![siblingCandidate.astNode].start => siblingCandidateStart
                                (nextSibling! < 0 or siblingCandidateStart < nextSiblingStart!) -> if {
                                    siblingSearch! => nextSibling!
                                    siblingCandidateStart => nextSiblingStart!
                                }
                            }
                            siblingSearch! + 1 => siblingSearch!
                        }
                        nextSibling! => sibling!.nextOperand
                        sibling! => results![siblingIrIndex!]
                        siblingIrIndex! + 1 => siblingIrIndex!
                    }

                    expressionIrStart => bindingNameIrIndex!
                    bindingNameIrIndex! < expressionIrEnd -> while {
                        results![bindingNameIrIndex!] => bindingName!
                        bindingName!.kind == 5 -> if {
                            expressionIrStart => bindingDefinitionSearch!
                            bindingDefinitionSearch! < expressionIrEnd -> while {
                                (results![bindingDefinitionSearch!].kind == 17 and results![bindingDefinitionSearch!].symbol == bindingName!.symbol) -> if {
                                    bindingDefinitionSearch! => bindingName!.operand0
                                }
                                bindingDefinitionSearch! + 1 => bindingDefinitionSearch!
                            }
                            bindingName! => results![bindingNameIrIndex!]
                        }
                        bindingNameIrIndex! + 1 => bindingNameIrIndex!
                    }

                    expressionIrStart => aggregateIrIndex!
                    aggregateIrIndex! < expressionIrEnd -> while {
                        results![aggregateIrIndex!] => aggregate!
                        (aggregate!.kind == 12 or aggregate!.kind == 14 or aggregate!.kind == 16) -> if {
                            -1 => firstFieldOperand!
                            expressionIrStart => fieldOperandSearch!
                            fieldOperandSearch! < expressionIrEnd -> while {
                                results![fieldOperandSearch!].parent == aggregateIrIndex! -> if {
                                    (firstFieldOperand! < 0 or nodes![results![fieldOperandSearch!].astNode].start < nodes![results![firstFieldOperand!].astNode].start) -> if { fieldOperandSearch! => firstFieldOperand! }
                                }
                                fieldOperandSearch! + 1 => fieldOperandSearch!
                            }
                            firstFieldOperand! => aggregate!.operand0
                            aggregate! => results![aggregateIrIndex!]
                        }
                        aggregateIrIndex! + 1 => aggregateIrIndex!
                    }

                    results![returnIr] => returnNode!
                    astToIr![resultType.astNode] => returnNode!.operand0
                    returnNode! => results![returnIr]
                }
            }
            symbolIndex! + 1 => symbolIndex!
        }
        0 => entryAstIndex!
        entryAstIndex! < (nodes! -> len) -> while {
            nodes![entryAstIndex!] => entryAst
            entryAst.kind == 8 -> if {
                -1 => entryResultTypeIndex!
                1000000 => entryResultDistance!
                0 => entryTypeSearch!
                entryTypeSearch! < (inferred! -> len) -> while {
                    inferred![entryTypeSearch!] => entryCandidateType
                    entryCandidateType.sourceModule == sourceIndex! -> if {
                        nodes![entryCandidateType.astNode].parent => entryAncestor!
                        1 => entryDistance!
                        false => belongsToEntry!
                        (entryAncestor! >= 0 and not belongsToEntry!) -> while {
                            entryAncestor! == entryAstIndex! -> if { true => belongsToEntry! } else {
                                nodes![entryAncestor!].parent => entryAncestor!
                                entryDistance! + 1 => entryDistance!
                            }
                        }
                        (belongsToEntry! and entryDistance! < entryResultDistance!) -> if {
                            entryTypeSearch! => entryResultTypeIndex!
                            entryDistance! => entryResultDistance!
                        }
                    }
                    entryTypeSearch! + 1 => entryTypeSearch!
                }
                results! -> len => entryIr!
                results! -> push(TypedIrNode {
                    kind: 11
                    parent: -1
                    sourceModule: sourceIndex!
                    astNode: entryAstIndex!
                    symbol: -1
                    targetModule: sourceIndex!
                    typeOrigin: 1
                    typeModule: -1
                    typeSymbol: 2
                    payloadToken: -1
                    opcode: -1
                    operand0: -1
                    operand1: -1
                    nextOperand: -1
                    flags: 0
                })
                entryResultTypeIndex! >= 0 -> if {
                    [Int; ~] => entryAstToIr!
                    0 => entryMapIndex!
                    entryMapIndex! < (nodes! -> len) -> while {
                        entryAstToIr! -> push(-1)
                        entryMapIndex! + 1 => entryMapIndex!
                    }
                    results! -> len => entryExpressionStart
                    0 => entryBindingAstIndex!
                    entryBindingAstIndex! < (nodes! -> len) -> while {
                        nodes![entryBindingAstIndex!] => entryBindingAst
                        entryBindingAst.kind == 9 -> if {
                            entryBindingAst.parent => entryBindingAncestor!
                            false => entryBindingBelongs!
                            (entryBindingAncestor! >= 0 and not entryBindingBelongs!) -> while {
                                entryBindingAncestor! == entryAstIndex! -> if { true => entryBindingBelongs! } else { nodes![entryBindingAncestor!].parent => entryBindingAncestor! }
                            }
                            entryBindingBelongs! -> if {
                                -1 => entryBindingTypeIndex!
                                1000000 => entryBindingTypeDistance!
                                0 => entryBindingTypeSearch!
                                entryBindingTypeSearch! < (inferred! -> len) -> while {
                                    inferred![entryBindingTypeSearch!] => entryBindingTypeCandidate
                                    entryBindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                        nodes![entryBindingTypeCandidate.astNode].parent => entryBindingTypeAncestor!
                                        1 => entryBindingDistance!
                                        false => entryBelongsToBinding!
                                        (entryBindingTypeAncestor! >= 0 and not entryBelongsToBinding!) -> while {
                                            entryBindingTypeAncestor! == entryBindingAstIndex! -> if { true => entryBelongsToBinding! } else {
                                                nodes![entryBindingTypeAncestor!].parent => entryBindingTypeAncestor!
                                                entryBindingDistance! + 1 => entryBindingDistance!
                                            }
                                        }
                                        (entryBelongsToBinding! and entryBindingDistance! < entryBindingTypeDistance!) -> if {
                                            entryBindingTypeSearch! => entryBindingTypeIndex!
                                            entryBindingDistance! => entryBindingTypeDistance!
                                        }
                                    }
                                    entryBindingTypeSearch! + 1 => entryBindingTypeSearch!
                                }
                                entryBindingTypeIndex! >= 0 -> if {
                                    -1 => entryBindingSymbol!
                                    0 => entryBindingSymbolSearch!
                                    entryBindingSymbolSearch! < (table! -> len) -> while {
                                        table![entryBindingSymbolSearch!].astNode == entryBindingAstIndex! -> if { entryBindingSymbolSearch! => entryBindingSymbol! }
                                        entryBindingSymbolSearch! + 1 => entryBindingSymbolSearch!
                                    }
                                    inferred![entryBindingTypeIndex!] => entryBindingType
                                    results! -> len => entryBindingIr
                                    entryBindingIr => entryAstToIr![entryBindingAstIndex!]
                                    results! -> push(TypedIrNode {
                                        kind: 17
                                        parent: entryIr!
                                        sourceModule: sourceIndex!
                                        astNode: entryBindingAstIndex!
                                        symbol: entryBindingSymbol!
                                        targetModule: sourceIndex!
                                        typeOrigin: entryBindingType.origin
                                        typeModule: entryBindingType.targetModule
                                        typeSymbol: entryBindingType.targetSymbol
                                        payloadToken: entryBindingAst.payloadToken
                                        opcode: -1
                                        operand0: -1
                                        operand1: -1
                                        nextOperand: -1
                                        flags: entryBindingAst.flags
                                    })
                                }
                            }
                        }
                        entryBindingAstIndex! + 1 => entryBindingAstIndex!
                    }
                    0 => entryExpressionAst!
                    entryExpressionAst! < (nodes! -> len) -> while {
                        -1 => entryExpressionTypeIndex!
                        0 => entryExpressionTypeSearch!
                        entryExpressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![entryExpressionTypeSearch!] => entryExpressionTypeCandidate
                            (entryExpressionTypeCandidate.sourceModule == sourceIndex! and entryExpressionTypeCandidate.astNode == entryExpressionAst!) -> if { entryExpressionTypeSearch! => entryExpressionTypeIndex! }
                            entryExpressionTypeSearch! + 1 => entryExpressionTypeSearch!
                        }
                        entryExpressionTypeIndex! >= 0 -> if {
                            nodes![entryExpressionAst!].parent => entryExpressionAncestor!
                            false => entryExpressionBelongs!
                            (entryExpressionAncestor! >= 0 and not entryExpressionBelongs!) -> while {
                                entryExpressionAncestor! == entryAstIndex! -> if { true => entryExpressionBelongs! } else { nodes![entryExpressionAncestor!].parent => entryExpressionAncestor! }
                            }
                            entryExpressionBelongs! -> if {
                                inferred![entryExpressionTypeIndex!] => entryExpressionType
                                nodes![entryExpressionAst!] => entryExpression
                                9 => entryExpressionKind!
                                entryExpression.kind == 13 -> if { 2 => entryExpressionKind! }
                                entryExpression.kind == 14 -> if { 3 => entryExpressionKind! }
                                entryExpression.kind == 15 -> if {
                                    (entryExpressionType.origin == 1 and entryExpressionType.targetSymbol == 23) -> if { 4 => entryExpressionKind! } else { 5 => entryExpressionKind! }
                                }
                                entryExpression.kind == 11 -> if { 6 => entryExpressionKind! }
                                entryExpression.kind == 22 -> if { 7 => entryExpressionKind! }
                                (entryExpression.kind >= 18 and entryExpression.kind <= 21) -> if { 8 => entryExpressionKind! }
                                (entryExpression.kind == 24 or entryExpression.kind == 25) -> if { 8 => entryExpressionKind! }
                                0 => entryPropertyCallSearch!
                                entryPropertyCallSearch! < (resolvedCalls! -> len) -> while {
                                    resolvedCalls![entryPropertyCallSearch!] => entryPropertyCall
                                    (entryPropertyCall.sourceModule == sourceIndex! and entryPropertyCall.callAst == entryExpressionAst! and entryPropertyCall.status == 0) -> if { 6 => entryExpressionKind! }
                                    entryPropertyCallSearch! + 1 => entryPropertyCallSearch!
                                }
                                results! -> len => entryExpressionIr
                                entryExpressionIr => entryAstToIr![entryExpressionAst!]
                                -1 => entryExpressionSymbol!
                                -1 => entryExpressionTargetModule!
                                entryExpressionKind! == 5 -> if {
                                    0 => entryNameSearch!
                                    entryNameSearch! < (resolvedNames! -> len) -> while {
                                        resolvedNames![entryNameSearch!] => entryResolvedName
                                        entryResolvedName.astNode == entryExpressionAst! -> if { entryResolvedName.symbol => entryExpressionSymbol! }
                                        entryNameSearch! + 1 => entryNameSearch!
                                    }
                                }
                                entryExpressionKind! == 6 -> if {
                                    0 => entryCallSearch!
                                    entryCallSearch! < (resolvedCalls! -> len) -> while {
                                        resolvedCalls![entryCallSearch!] => entryResolvedCall
                                        (entryResolvedCall.sourceModule == sourceIndex! and entryResolvedCall.callAst == entryExpressionAst! and entryResolvedCall.status == 0) -> if {
                                            entryResolvedCall.functionSymbol => entryExpressionSymbol!
                                            entryResolvedCall.targetSourceModule => entryExpressionTargetModule!
                                        }
                                        entryCallSearch! + 1 => entryCallSearch!
                                    }
                                }
                                results! -> push(TypedIrNode {
                                    kind: entryExpressionKind!
                                    parent: entryIr!
                                    sourceModule: sourceIndex!
                                    astNode: entryExpressionAst!
                                    symbol: entryExpressionSymbol!
                                    targetModule: entryExpressionTargetModule!
                                    typeOrigin: entryExpressionType.origin
                                    typeModule: entryExpressionType.targetModule
                                    typeSymbol: entryExpressionType.targetSymbol
                                    payloadToken: entryExpression.payloadToken
                                    opcode: entryExpression.operatorKind
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: entryExpression.flags
                                })
                            }
                        }
                        entryExpressionAst! + 1 => entryExpressionAst!
                    }
                    results! -> len => entryExpressionEnd
                    entryExpressionStart => entryParentIr!
                    entryParentIr! < entryExpressionEnd -> while {
                        results![entryParentIr!] => entryIrNode!
                        nodes![entryIrNode!.astNode].parent => entryParentAst!
                        -1 => entrySemanticParent!
                        (entryParentAst! >= 0 and entryParentAst! != entryAstIndex! and entrySemanticParent! < 0) -> while {
                            entryAstToIr![entryParentAst!] >= 0 -> if { entryAstToIr![entryParentAst!] => entrySemanticParent! } else { nodes![entryParentAst!].parent => entryParentAst! }
                        }
                        entrySemanticParent! >= 0 -> if { entrySemanticParent! => entryIrNode!.parent }
                        entryIrNode! => results![entryParentIr!]
                        entryParentIr! + 1 => entryParentIr!
                    }
                    entryExpressionStart => entryOperandIr!
                    entryOperandIr! < entryExpressionEnd -> while {
                        results![entryOperandIr!] => entryOperator!
                        (entryOperator!.kind == 6 or entryOperator!.kind == 7 or entryOperator!.kind == 8 or entryOperator!.kind == 17) -> if {
                            -1 => entryFirstOperand!
                            -1 => entrySecondOperand!
                            UIntSize(0) => entryFirstStart!
                            entryExpressionStart => entryChildIr!
                            entryChildIr! < entryExpressionEnd -> while {
                                results![entryChildIr!] => entryChild
                                entryChild.parent == entryOperandIr! -> if {
                                    nodes![entryChild.astNode].start => entryChildStart
                                    entryFirstOperand! < 0 -> if {
                                        entryChildIr! => entryFirstOperand!
                                        entryChildStart => entryFirstStart!
                                    } else {
                                        entryChildStart < entryFirstStart! -> if {
                                            entryFirstOperand! => entrySecondOperand!
                                            entryChildIr! => entryFirstOperand!
                                            entryChildStart => entryFirstStart!
                                        } else { entrySecondOperand! < 0 -> if { entryChildIr! => entrySecondOperand! } }
                                    }
                                }
                                entryChildIr! + 1 => entryChildIr!
                            }
                            entryFirstOperand! => entryOperator!.operand0
                            (entryOperator!.kind == 6 or entryOperator!.kind == 8) -> if { entrySecondOperand! => entryOperator!.operand1 }
                            entryOperator! => results![entryOperandIr!]
                        }
                        entryOperandIr! + 1 => entryOperandIr!
                    }
                    entryExpressionStart => entryBindingNameIr!
                    entryBindingNameIr! < entryExpressionEnd -> while {
                        results![entryBindingNameIr!] => entryBindingName!
                        entryBindingName!.kind == 5 -> if {
                            entryExpressionStart => entryBindingDefinitionSearch!
                            entryBindingDefinitionSearch! < entryExpressionEnd -> while {
                                (results![entryBindingDefinitionSearch!].kind == 17 and results![entryBindingDefinitionSearch!].symbol == entryBindingName!.symbol) -> if {
                                    entryBindingDefinitionSearch! => entryBindingName!.operand0
                                }
                                entryBindingDefinitionSearch! + 1 => entryBindingDefinitionSearch!
                            }
                            entryBindingName! => results![entryBindingNameIr!]
                        }
                        entryBindingNameIr! + 1 => entryBindingNameIr!
                    }
                    results![entryIr!] => entryNode!
                    entryAstToIr![inferred![entryResultTypeIndex!].astNode] => entryNode!.operand0
                    entryNode! => results![entryIr!]
                }
            }
            entryAstIndex! + 1 => entryAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    results!
}
