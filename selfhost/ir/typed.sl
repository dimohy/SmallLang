namespace smalllang.compiler.ir.typed

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

# Stable, flat typed IR. Indexes are relocatable array offsets so later LLVM
# lowering can consume the table without allocating an object graph.
# Kinds: 0 function, 1 return, 2 Text constant, 3 Int constant,
# 4 Bool constant, 5 name, 6 call, 7 unary, 8 binary, 9 other expression,
# 10 parameter, 11 entry point, 12 struct literal, 13 member access,
# 14 array literal, 15 index access, 16 dictionary literal, 17 binding,
# 18 structured if, 19 control-flow region, 20 structured while,
# 21 loop exit, 22 guarded loop exit (opcode 0 break, 1 continue;
# operand0 targets the while, guarded operand1 is the Bool condition),
# 23 explicit return (operand0 is the optional returned value).
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

# A move event is a consuming call whose argument is a resolved local owner.
# Cleanup lowering uses this side table instead of overloading value/type flags.
public struct MoveEvent {
    siteIr: Int
    sourceModule: Int
    symbol: Int
    regionIr: Int
    memberIr: Int
}

# A suspension point belongs to one async function and receives a stable,
# one-based resume state in source order. awaitIr is -1 until the corresponding
# flow target becomes a first-class typed-IR node; awaitAst remains stable now.
public struct CoroutineSuspendPoint {
    functionIr: Int
    sourceModule: Int
    awaitAst: Int
    awaitIr: Int
    state: Int
}

# One frame slot records a binding that is defined before a suspension and
# referenced after it inside the same async function. Parameters stay in the
# function context and therefore do not appear in this side table.
public struct CoroutineFrameSlot {
    functionIr: Int
    state: Int
    sourceModule: Int
    symbol: Int
    typeOrigin: Int
    typeModule: Int
    typeSymbol: Int
}

public lower sources: [Text; ~] -> [TypedIrNode; ~] {
    sources -> expressionTypes.infer => inferred!
    sources -> nominalTypes.resolve => nominal!
    sources -> compositeTypes.resolve => composite!
    sources -> calls.resolveModules => resolvedCalls!
    sources -> modules.identities => moduleIdentities!
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
                UIntSize(0) => resultTopLevelStart!
                0 => typeSearch!
                typeSearch! < (inferred! -> len) -> while {
                    inferred![typeSearch!] => candidateType
                    candidateType.sourceModule == sourceIndex! -> if {
                        nodes![candidateType.astNode].parent => ancestor!
                        candidateType.astNode => functionChildAst!
                        1 => distance!
                        false => belongsToFunction!
                        (ancestor! >= 0 and not belongsToFunction!) -> while {
                            ancestor! == function.astNode -> if { true => belongsToFunction! } else {
                                ancestor! => functionChildAst!
                                nodes![ancestor!].parent => ancestor!
                                distance! + 1 => distance!
                            }
                        }
                        belongsToFunction! -> if {
                            nodes![functionChildAst!].start => candidateTopLevelStart
                            (resultTypeIndex! < 0 or candidateTopLevelStart > resultTopLevelStart! or (candidateTopLevelStart == resultTopLevelStart! and distance! < resultDistance!)) -> if {
                                typeSearch! => resultTypeIndex!
                                distance! => resultDistance!
                                candidateTopLevelStart => resultTopLevelStart!
                            }
                        }
                    }
                    typeSearch! + 1 => typeSearch!
                }
                resultTypeIndex! >= 0 -> if {
                    inferred![resultTypeIndex!] => resultType
                    resultType.origin => functionResultOrigin!
                    resultType.targetModule => functionResultModule!
                    resultType.targetSymbol => functionResultSymbol!
                    function.typeNode => declaredResultAst!
                    function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode => declaredResultAst! }
                    0 => declaredResultNominalSearch!
                    declaredResultNominalSearch! < (nominal! -> len) -> while {
                        nominal![declaredResultNominalSearch!] => declaredResultNominal
                        (declaredResultNominal.sourceModule == sourceIndex! and declaredResultNominal.typeAst == declaredResultAst!) -> if {
                            declaredResultNominal.origin => functionResultOrigin!
                            declaredResultNominal.targetModule => functionResultModule!
                            declaredResultNominal.targetSymbol => functionResultSymbol!
                        }
                        declaredResultNominalSearch! + 1 => declaredResultNominalSearch!
                    }
                    0 => declaredResultCompositeSearch!
                    declaredResultCompositeSearch! < (composite! -> len) -> while {
                        composite![declaredResultCompositeSearch!] => declaredResultComposite
                        (declaredResultComposite.sourceModule == sourceIndex! and declaredResultComposite.typeAst == declaredResultAst!) -> if {
                            10 + declaredResultComposite.kind => functionResultOrigin!
                            declaredResultComposite.elementModule => functionResultModule!
                            declaredResultComposite.elementSymbol => functionResultSymbol!
                        }
                        declaredResultCompositeSearch! + 1 => declaredResultCompositeSearch!
                    }
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
                        typeOrigin: functionResultOrigin!
                        typeModule: functionResultModule!
                        typeSymbol: functionResultSymbol!
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
                        typeOrigin: functionResultOrigin!
                        typeModule: functionResultModule!
                        typeSymbol: functionResultSymbol!
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
                        nodes![expressionAstIndex!] => expression
                        expression.parent => expressionAncestor!
                        false => expressionBelongsToFunction!
                        (expressionAncestor! >= 0 and not expressionBelongsToFunction!) -> while {
                            expressionAncestor! == function.astNode -> if { true => expressionBelongsToFunction! } else {
                                nodes![expressionAncestor!].parent => expressionAncestor!
                            }
                        }
                        -1 => expressionTypeIndex!
                        0 => expressionTypeSearch!
                        expressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![expressionTypeSearch!] => candidateExpressionType
                            (candidateExpressionType.sourceModule == sourceIndex! and candidateExpressionType.astNode == expressionAstIndex!) -> if { expressionTypeSearch! => expressionTypeIndex! }
                            expressionTypeSearch! + 1 => expressionTypeSearch!
                        }
                        expressionBelongsToFunction! -> if {
                            (expression.kind == 42 or expression.kind == 43 or expression.kind == 44 or expression.kind == 45 or expression.kind == 46 or expression.kind == 47) -> if {
                                results! -> len => controlIr
                                controlIr => astToIr![expressionAstIndex!]
                                18 => controlKind!
                                expression.kind == 43 -> if { 19 => controlKind! }
                                expression.kind == 44 -> if { 20 => controlKind! }
                                expression.kind == 45 -> if { 21 => controlKind! }
                                expression.kind == 46 -> if { 22 => controlKind! }
                                expression.kind == 47 -> if { 23 => controlKind! }
                                -1 => controlOpcode!
                                expression.kind == 45 -> if {
                                    0 => controlOpcode!
                                    (source -> byte(expression.start)) == UInt8(99) -> if { 1 => controlOpcode! }
                                }
                                expression.kind == 46 -> if { expression.operatorKind => controlOpcode! }
                                1 => controlTypeOrigin!
                                -1 => controlTypeModule!
                                0 => controlTypeSymbol!
                                expressionTypeIndex! >= 0 -> if {
                                    inferred![expressionTypeIndex!] => inferredControlType
                                    inferredControlType.origin => controlTypeOrigin!
                                    inferredControlType.targetModule => controlTypeModule!
                                    inferredControlType.targetSymbol => controlTypeSymbol!
                                }
                                results! -> push(TypedIrNode {
                                    kind: controlKind!
                                    parent: returnIr
                                    sourceModule: sourceIndex!
                                    astNode: expressionAstIndex!
                                    symbol: -1
                                    targetModule: sourceIndex!
                                    typeOrigin: controlTypeOrigin!
                                    typeModule: controlTypeModule!
                                    typeSymbol: controlTypeSymbol!
                                    payloadToken: expression.payloadToken
                                    opcode: controlOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: expression.flags
                                })
                            } else {
                                expressionTypeIndex! >= 0 -> if {
                                inferred![expressionTypeIndex!] => expressionType
                                9 => expressionKind!
                                expression.kind == 13 -> if { 2 => expressionKind! }
                                expression.kind == 14 -> if { 3 => expressionKind! }
                                expression.kind == 15 -> if {
                                    5 => expressionKind!
                                    (expressionType.origin == 1 and expressionType.targetSymbol == 23) -> if {
                                        true => expressionIsBoolLiteral!
                                        0 => expressionBoolNameSearch!
                                        expressionBoolNameSearch! < (resolvedNames! -> len) -> while {
                                            resolvedNames![expressionBoolNameSearch!].astNode == expressionAstIndex! -> if { false => expressionIsBoolLiteral! }
                                            expressionBoolNameSearch! + 1 => expressionBoolNameSearch!
                                        }
                                        expressionIsBoolLiteral! -> if { 4 => expressionKind! }
                                    }
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
                                expression.flags => expressionFlags!
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
                                            (resolvedCall.functionSymbol >= 0 and resolvedCall.targetSourceModule >= 0) -> if {
                                                sources[resolvedCall.targetSourceModule] -> symbols.collect => expressionTargetTable!
                                                ((expressionTargetTable![resolvedCall.functionSymbol].flags / 8) % 2 == 1 and (expressionFlags! / 8) % 2 == 0) -> if {
                                                    expressionFlags! + 8 => expressionFlags!
                                                }
                                            }
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
                                    flags: expressionFlags!
                                })
                                }
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
                        (operatorIr!.kind == 6 or operatorIr!.kind == 7 or operatorIr!.kind == 8 or operatorIr!.kind == 13 or operatorIr!.kind == 15 or operatorIr!.kind == 17 or operatorIr!.kind == 22 or operatorIr!.kind == 23) -> if {
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
                            operatorIr!.kind == 22 -> if {
                                -1 => operatorIr!.operand0
                                firstOperand! => operatorIr!.operand1
                            }
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

                    expressionIrStart => controlIrIndex!
                    controlIrIndex! < expressionIrEnd -> while {
                        results![controlIrIndex!] => control!
                        control!.kind == 19 -> if {
                            -1 => firstRegionChild!
                            -1 => lastRegionChild!
                            UIntSize(0) => firstRegionChildStart!
                            UIntSize(0) => lastRegionChildStart!
                            expressionIrStart => regionChildSearch!
                            regionChildSearch! < expressionIrEnd -> while {
                                results![regionChildSearch!].parent == controlIrIndex! -> if {
                                    nodes![results![regionChildSearch!].astNode].start => regionChildStart
                                    (firstRegionChild! < 0 or regionChildStart < firstRegionChildStart!) -> if {
                                        regionChildSearch! => firstRegionChild!
                                        regionChildStart => firstRegionChildStart!
                                    }
                                    (lastRegionChild! < 0 or regionChildStart > lastRegionChildStart!) -> if {
                                        regionChildSearch! => lastRegionChild!
                                        regionChildStart => lastRegionChildStart!
                                    }
                                }
                                regionChildSearch! + 1 => regionChildSearch!
                            }
                            firstRegionChild! => control!.operand0
                            (lastRegionChild! >= 0 and results![lastRegionChild!].kind == 9) -> while {
                                -1 => nestedRegionResult!
                                UIntSize(0) => nestedRegionResultStart!
                                expressionIrStart => nestedResultSearch!
                                nestedResultSearch! < expressionIrEnd -> while {
                                    results![nestedResultSearch!].parent == lastRegionChild! -> if {
                                        nodes![results![nestedResultSearch!].astNode].start => nestedResultStart
                                        (nestedRegionResult! < 0 or nestedResultStart > nestedRegionResultStart!) -> if {
                                            nestedResultSearch! => nestedRegionResult!
                                            nestedResultStart => nestedRegionResultStart!
                                        }
                                    }
                                    nestedResultSearch! + 1 => nestedResultSearch!
                                }
                                nestedRegionResult! >= 0 -> if { nestedRegionResult! => lastRegionChild! } else { -1 => lastRegionChild! }
                            }
                            lastRegionChild! => control!.operand1
                            control! => results![controlIrIndex!]
                        }
                        (control!.kind == 18 or control!.kind == 20) -> if {
                            nodes![control!.astNode].parent => controlFlowAst
                            -1 => conditionIr!
                            UIntSize(0) => conditionStart!
                            expressionIrStart => conditionSearch!
                            conditionSearch! < expressionIrEnd -> while {
                                results![conditionSearch!] => conditionCandidate
                                (nodes![conditionCandidate.astNode].parent == controlFlowAst and nodes![conditionCandidate.astNode].start < nodes![control!.astNode].start) -> if {
                                    nodes![conditionCandidate.astNode].start => candidateStart
                                    (conditionIr! < 0 or candidateStart > conditionStart!) -> if {
                                        conditionSearch! => conditionIr!
                                        candidateStart => conditionStart!
                                    }
                                }
                                conditionSearch! + 1 => conditionSearch!
                            }
                            -1 => thenRegion!
                            -1 => elseRegion!
                            expressionIrStart => regionSearch!
                            regionSearch! < expressionIrEnd -> while {
                                results![regionSearch!] => regionCandidate
                                (regionCandidate.kind == 19 and regionCandidate.parent == controlIrIndex!) -> if {
                                    thenRegion! < 0 -> if { regionSearch! => thenRegion! } else { regionSearch! => elseRegion! }
                                }
                                regionSearch! + 1 => regionSearch!
                            }
                            (conditionIr! < 0 and control!.parent >= expressionIrStart and results![control!.parent].kind != 19) -> if {
                                control!.parent => enclosingConditionIr!
                                results![enclosingConditionIr!].parent => control!.parent
                                enclosingConditionIr! => conditionIr!
                            }
                            conditionIr! => control!.operand0
                            thenRegion! => control!.operand1
                            control!.kind == 18 -> if { elseRegion! => control!.nextOperand } else { -1 => control!.nextOperand }
                            control! => results![controlIrIndex!]
                            (control!.kind == 20 and conditionIr! >= 0) -> if {
                                results![conditionIr!] => loopCondition!
                                controlIrIndex! => loopCondition!.parent
                                loopCondition! => results![conditionIr!]
                            }
                        }
                        controlIrIndex! + 1 => controlIrIndex!
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
                    astToIr![resultType.astNode] => returnOperandIr!
                    (returnOperandIr! >= 0 and results![returnOperandIr!].kind == 9) -> if {
                        expressionIrStart => returnControlSearch!
                        returnControlSearch! < expressionIrEnd -> while {
                            ((results![returnControlSearch!].kind == 18 or results![returnControlSearch!].kind == 20) and results![returnControlSearch!].parent == returnOperandIr!) -> if { returnControlSearch! => returnOperandIr! }
                            returnControlSearch! + 1 => returnControlSearch!
                        }
                    }
                    returnOperandIr! => returnNode!.operand0
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
                        (belongsToEntry! and (entryDistance! < entryResultDistance! or (entryDistance! == entryResultDistance! and (entryResultTypeIndex! < 0 or nodes![entryCandidateType.astNode].start > nodes![inferred![entryResultTypeIndex!].astNode].start)))) -> if {
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
                        nodes![entryExpressionAst!] => entryExpression
                        entryExpression.parent => entryExpressionAncestor!
                        false => entryExpressionBelongs!
                        (entryExpressionAncestor! >= 0 and not entryExpressionBelongs!) -> while {
                            entryExpressionAncestor! == entryAstIndex! -> if { true => entryExpressionBelongs! } else { nodes![entryExpressionAncestor!].parent => entryExpressionAncestor! }
                        }
                        -1 => entryExpressionTypeIndex!
                        0 => entryExpressionTypeSearch!
                        entryExpressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![entryExpressionTypeSearch!] => entryExpressionTypeCandidate
                            (entryExpressionTypeCandidate.sourceModule == sourceIndex! and entryExpressionTypeCandidate.astNode == entryExpressionAst!) -> if { entryExpressionTypeSearch! => entryExpressionTypeIndex! }
                            entryExpressionTypeSearch! + 1 => entryExpressionTypeSearch!
                        }
                        entryExpressionBelongs! -> if {
                            (entryExpression.kind == 42 or entryExpression.kind == 43 or entryExpression.kind == 44 or entryExpression.kind == 45 or entryExpression.kind == 46 or entryExpression.kind == 47) -> if {
                                results! -> len => entryControlIr
                                entryControlIr => entryAstToIr![entryExpressionAst!]
                                18 => entryControlKind!
                                entryExpression.kind == 43 -> if { 19 => entryControlKind! }
                                entryExpression.kind == 44 -> if { 20 => entryControlKind! }
                                entryExpression.kind == 45 -> if { 21 => entryControlKind! }
                                entryExpression.kind == 46 -> if { 22 => entryControlKind! }
                                entryExpression.kind == 47 -> if { 23 => entryControlKind! }
                                -1 => entryControlOpcode!
                                entryExpression.kind == 45 -> if {
                                    0 => entryControlOpcode!
                                    (source -> byte(entryExpression.start)) == UInt8(99) -> if { 1 => entryControlOpcode! }
                                }
                                entryExpression.kind == 46 -> if { entryExpression.operatorKind => entryControlOpcode! }
                                1 => entryControlTypeOrigin!
                                -1 => entryControlTypeModule!
                                0 => entryControlTypeSymbol!
                                entryExpressionTypeIndex! >= 0 -> if {
                                    inferred![entryExpressionTypeIndex!] => inferredEntryControlType
                                    inferredEntryControlType.origin => entryControlTypeOrigin!
                                    inferredEntryControlType.targetModule => entryControlTypeModule!
                                    inferredEntryControlType.targetSymbol => entryControlTypeSymbol!
                                }
                                results! -> push(TypedIrNode {
                                    kind: entryControlKind!
                                    parent: entryIr!
                                    sourceModule: sourceIndex!
                                    astNode: entryExpressionAst!
                                    symbol: -1
                                    targetModule: sourceIndex!
                                    typeOrigin: entryControlTypeOrigin!
                                    typeModule: entryControlTypeModule!
                                    typeSymbol: entryControlTypeSymbol!
                                    payloadToken: entryExpression.payloadToken
                                    opcode: entryControlOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: entryExpression.flags
                                })
                            } else {
                                entryExpressionTypeIndex! >= 0 -> if {
                                inferred![entryExpressionTypeIndex!] => entryExpressionType
                                9 => entryExpressionKind!
                                entryExpression.kind == 13 -> if { 2 => entryExpressionKind! }
                                entryExpression.kind == 14 -> if { 3 => entryExpressionKind! }
                                entryExpression.kind == 15 -> if {
                                    5 => entryExpressionKind!
                                    (entryExpressionType.origin == 1 and entryExpressionType.targetSymbol == 23) -> if {
                                        true => entryExpressionIsBoolLiteral!
                                        0 => entryBoolNameSearch!
                                        entryBoolNameSearch! < (resolvedNames! -> len) -> while {
                                            resolvedNames![entryBoolNameSearch!].astNode == entryExpressionAst! -> if { false => entryExpressionIsBoolLiteral! }
                                            entryBoolNameSearch! + 1 => entryBoolNameSearch!
                                        }
                                        entryExpressionIsBoolLiteral! -> if { 4 => entryExpressionKind! }
                                    }
                                }
                                entryExpression.kind == 11 -> if { 6 => entryExpressionKind! }
                                entryExpression.kind == 22 -> if { 7 => entryExpressionKind! }
                                (entryExpression.kind >= 18 and entryExpression.kind <= 21) -> if { 8 => entryExpressionKind! }
                                (entryExpression.kind == 24 or entryExpression.kind == 25) -> if { 8 => entryExpressionKind! }
                                entryExpression.kind == 39 -> if { 12 => entryExpressionKind! }
                                entryExpression.kind == 36 -> if { 13 => entryExpressionKind! }
                                entryExpression.kind == 37 -> if { 14 => entryExpressionKind! }
                                entryExpression.kind == 38 -> if { 16 => entryExpressionKind! }
                                entryExpression.kind == 41 -> if { 15 => entryExpressionKind! }
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
                                entryExpression.flags => entryExpressionFlags!
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
                                            (entryResolvedCall.functionSymbol >= 0 and entryResolvedCall.targetSourceModule >= 0) -> if {
                                                sources[entryResolvedCall.targetSourceModule] -> symbols.collect => entryExpressionTargetTable!
                                                ((entryExpressionTargetTable![entryResolvedCall.functionSymbol].flags / 8) % 2 == 1 and (entryExpressionFlags! / 8) % 2 == 0) -> if {
                                                    entryExpressionFlags! + 8 => entryExpressionFlags!
                                                }
                                            }
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
                                    flags: entryExpressionFlags!
                                })
                                }
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
                        (entryOperator!.kind == 6 or entryOperator!.kind == 7 or entryOperator!.kind == 8 or entryOperator!.kind == 17 or entryOperator!.kind == 22 or entryOperator!.kind == 23) -> if {
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
                            entryOperator!.kind == 22 -> if {
                                -1 => entryOperator!.operand0
                                entryFirstOperand! => entryOperator!.operand1
                            }
                            entryOperator! => results![entryOperandIr!]
                        }
                        entryOperandIr! + 1 => entryOperandIr!
                    }
                    entryExpressionStart => entrySiblingIr!
                    entrySiblingIr! < entryExpressionEnd -> while {
                        results![entrySiblingIr!] => entrySibling!
                        -1 => entryNextSibling!
                        UIntSize(0) => entryNextSiblingStart!
                        entryExpressionStart => entrySiblingSearch!
                        entrySiblingSearch! < entryExpressionEnd -> while {
                            results![entrySiblingSearch!] => entrySiblingCandidate
                            (entrySiblingCandidate.parent == entrySibling!.parent and nodes![entrySiblingCandidate.astNode].start > nodes![entrySibling!.astNode].start) -> if {
                                nodes![entrySiblingCandidate.astNode].start => entrySiblingCandidateStart
                                (entryNextSibling! < 0 or entrySiblingCandidateStart < entryNextSiblingStart!) -> if {
                                    entrySiblingSearch! => entryNextSibling!
                                    entrySiblingCandidateStart => entryNextSiblingStart!
                                }
                            }
                            entrySiblingSearch! + 1 => entrySiblingSearch!
                        }
                        entryNextSibling! => entrySibling!.nextOperand
                        entrySibling! => results![entrySiblingIr!]
                        entrySiblingIr! + 1 => entrySiblingIr!
                    }
                    entryExpressionStart => entryControlIrIndex!
                    entryControlIrIndex! < entryExpressionEnd -> while {
                        results![entryControlIrIndex!] => entryControl!
                        entryControl!.kind == 19 -> if {
                            -1 => entryFirstRegionChild!
                            -1 => entryLastRegionChild!
                            UIntSize(0) => entryFirstRegionChildStart!
                            UIntSize(0) => entryLastRegionChildStart!
                            entryExpressionStart => entryRegionChildSearch!
                            entryRegionChildSearch! < entryExpressionEnd -> while {
                                results![entryRegionChildSearch!].parent == entryControlIrIndex! -> if {
                                    nodes![results![entryRegionChildSearch!].astNode].start => entryRegionChildStart
                                    (entryFirstRegionChild! < 0 or entryRegionChildStart < entryFirstRegionChildStart!) -> if {
                                        entryRegionChildSearch! => entryFirstRegionChild!
                                        entryRegionChildStart => entryFirstRegionChildStart!
                                    }
                                    (entryLastRegionChild! < 0 or entryRegionChildStart > entryLastRegionChildStart!) -> if {
                                        entryRegionChildSearch! => entryLastRegionChild!
                                        entryRegionChildStart => entryLastRegionChildStart!
                                    }
                                }
                                entryRegionChildSearch! + 1 => entryRegionChildSearch!
                            }
                            entryFirstRegionChild! => entryControl!.operand0
                            (entryLastRegionChild! >= 0 and results![entryLastRegionChild!].kind == 9) -> while {
                                -1 => entryNestedRegionResult!
                                UIntSize(0) => entryNestedRegionResultStart!
                                entryExpressionStart => entryNestedResultSearch!
                                entryNestedResultSearch! < entryExpressionEnd -> while {
                                    results![entryNestedResultSearch!].parent == entryLastRegionChild! -> if {
                                        nodes![results![entryNestedResultSearch!].astNode].start => entryNestedResultStart
                                        (entryNestedRegionResult! < 0 or entryNestedResultStart > entryNestedRegionResultStart!) -> if {
                                            entryNestedResultSearch! => entryNestedRegionResult!
                                            entryNestedResultStart => entryNestedRegionResultStart!
                                        }
                                    }
                                    entryNestedResultSearch! + 1 => entryNestedResultSearch!
                                }
                                entryNestedRegionResult! >= 0 -> if { entryNestedRegionResult! => entryLastRegionChild! } else { -1 => entryLastRegionChild! }
                            }
                            entryLastRegionChild! => entryControl!.operand1
                            entryControl! => results![entryControlIrIndex!]
                        }
                        (entryControl!.kind == 18 or entryControl!.kind == 20) -> if {
                            nodes![entryControl!.astNode].parent => entryControlFlowAst
                            -1 => entryConditionIr!
                            UIntSize(0) => entryConditionStart!
                            entryExpressionStart => entryConditionSearch!
                            entryConditionSearch! < entryExpressionEnd -> while {
                                results![entryConditionSearch!] => entryConditionCandidate
                                (nodes![entryConditionCandidate.astNode].parent == entryControlFlowAst and nodes![entryConditionCandidate.astNode].start < nodes![entryControl!.astNode].start) -> if {
                                    nodes![entryConditionCandidate.astNode].start => entryCandidateStart
                                    (entryConditionIr! < 0 or entryCandidateStart > entryConditionStart!) -> if {
                                        entryConditionSearch! => entryConditionIr!
                                        entryCandidateStart => entryConditionStart!
                                    }
                                }
                                entryConditionSearch! + 1 => entryConditionSearch!
                            }
                            -1 => entryThenRegion!
                            -1 => entryElseRegion!
                            entryExpressionStart => entryRegionSearch!
                            entryRegionSearch! < entryExpressionEnd -> while {
                                results![entryRegionSearch!] => entryRegionCandidate
                                (entryRegionCandidate.kind == 19 and entryRegionCandidate.parent == entryControlIrIndex!) -> if {
                                    entryThenRegion! < 0 -> if { entryRegionSearch! => entryThenRegion! } else { entryRegionSearch! => entryElseRegion! }
                                }
                                entryRegionSearch! + 1 => entryRegionSearch!
                            }
                            (entryConditionIr! < 0 and entryControl!.parent >= entryExpressionStart and results![entryControl!.parent].kind != 19) -> if {
                                entryControl!.parent => entryEnclosingConditionIr!
                                results![entryEnclosingConditionIr!].parent => entryControl!.parent
                                entryEnclosingConditionIr! => entryConditionIr!
                            }
                            entryConditionIr! => entryControl!.operand0
                            entryThenRegion! => entryControl!.operand1
                            entryControl!.kind == 18 -> if { entryElseRegion! => entryControl!.nextOperand } else { -1 => entryControl!.nextOperand }
                            entryControl! => results![entryControlIrIndex!]
                            (entryControl!.kind == 20 and entryConditionIr! >= 0) -> if {
                                results![entryConditionIr!] => entryLoopCondition!
                                entryControlIrIndex! => entryLoopCondition!.parent
                                entryLoopCondition! => results![entryConditionIr!]
                            }
                        }
                        entryControlIrIndex! + 1 => entryControlIrIndex!
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
                    entryExpressionStart => entryAggregateIrIndex!
                    entryAggregateIrIndex! < entryExpressionEnd -> while {
                        results![entryAggregateIrIndex!] => entryAggregate!
                        (entryAggregate!.kind == 12 or entryAggregate!.kind == 14 or entryAggregate!.kind == 16) -> if {
                            -1 => entryFirstFieldOperand!
                            entryExpressionStart => entryFieldOperandSearch!
                            entryFieldOperandSearch! < entryExpressionEnd -> while {
                                results![entryFieldOperandSearch!].parent == entryAggregateIrIndex! -> if {
                                    (entryFirstFieldOperand! < 0 or nodes![results![entryFieldOperandSearch!].astNode].start < nodes![results![entryFirstFieldOperand!].astNode].start) -> if { entryFieldOperandSearch! => entryFirstFieldOperand! }
                                }
                                entryFieldOperandSearch! + 1 => entryFieldOperandSearch!
                            }
                            entryFirstFieldOperand! => entryAggregate!.operand0
                            entryAggregate! => results![entryAggregateIrIndex!]
                        }
                        entryAggregateIrIndex! + 1 => entryAggregateIrIndex!
                    }
                    results![entryIr!] => entryNode!
                    entryAstToIr![inferred![entryResultTypeIndex!].astNode] => entryResultIr!
                    (entryResultIr! >= 0 and results![entryResultIr!].kind == 9) -> if {
                        entryExpressionStart => entryResultControlSearch!
                        entryResultControlSearch! < entryExpressionEnd -> while {
                            ((results![entryResultControlSearch!].kind == 18 or results![entryResultControlSearch!].kind == 20) and results![entryResultControlSearch!].parent == entryResultIr!) -> if { entryResultControlSearch! => entryResultIr! }
                            entryResultControlSearch! + 1 => entryResultControlSearch!
                        }
                    }
                    entryResultIr! => entryNode!.operand0
                    entryNode! => results![entryIr!]
                }
            }
            entryAstIndex! + 1 => entryAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    0 => ownedBindingIndex!
    ownedBindingIndex! < (results! -> len) -> while {
        results![ownedBindingIndex!] => ownedBinding!
        (ownedBinding!.kind == 17 and (ownedBinding!.typeOrigin == 13 or ownedBinding!.typeOrigin == 15)) -> if {
            -1 => ownedBindingValue!
            0 => ownedBindingValueSearch!
            ownedBindingValueSearch! < (results! -> len) -> while {
                results![ownedBindingValueSearch!] => ownedValueCandidate
                ((ownedBinding!.typeOrigin == 13 and ownedValueCandidate.kind == 14) or (ownedBinding!.typeOrigin == 15 and ownedValueCandidate.kind == 16)) -> if {
                    ownedValueCandidate.parent => ownedValueAncestor!
                    false => ownedValueBelongs!
                    (ownedValueAncestor! >= 0 and not ownedValueBelongs!) -> while {
                        ownedValueAncestor! == ownedBindingIndex! -> if { true => ownedValueBelongs! } else { results![ownedValueAncestor!].parent => ownedValueAncestor! }
                    }
                    (ownedValueBelongs! and ownedBindingValue! < 0) -> if { ownedBindingValueSearch! => ownedBindingValue! }
                }
                ownedBindingValueSearch! + 1 => ownedBindingValueSearch!
            }
            ownedBindingValue! >= 0 -> if {
                ownedBindingValue! => ownedBinding!.operand0
                ownedBinding! => results![ownedBindingIndex!]
            }
        }
        ownedBindingIndex! + 1 => ownedBindingIndex!
    }
    0 => memberIndex!
    memberIndex! < (results! -> len) -> while {
        results![memberIndex!] => member!
        (member!.kind == 13 and member!.operand0 >= 0) -> if {
            results![member!.operand0] => memberBase
            memberBase.typeOrigin => memberCurrentOrigin!
            memberBase.typeModule => memberCurrentModule!
            memberBase.typeSymbol => memberCurrentSymbol!
            sources[member!.sourceModule] -> ast.lower => memberNodes!
            sources[member!.sourceModule] -> lexer.lex => memberTokens!
            memberNodes![member!.astNode] => memberAst
            0 => memberIdentifierOrdinal!
            memberAst.firstToken => memberTokenIndex!
            memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                memberTokens![memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                    memberIdentifierOrdinal! > 0 -> if {
                        memberCurrentModule! => memberOwnerSource!
                        memberCurrentOrigin! == 2 -> if { moduleIdentities![memberCurrentModule!].sourceIndex => memberOwnerSource! }
                        sources[memberOwnerSource!] -> symbols.collect => memberOwnerTable!
                        sources[memberOwnerSource!] -> lexer.lex => memberOwnerTokens!
                        0 => memberFieldOrdinal!
                        0 => memberFieldIndex!
                        memberFieldIndex! < (memberOwnerTable! -> len) -> while {
                            memberOwnerTable![memberFieldIndex!] => memberField
                            (memberField.kind == 26 and memberField.parent == memberCurrentSymbol!) -> if {
                                memberTokens![memberTokenIndex!] => memberName
                                memberOwnerTokens![memberField.nameToken] => memberFieldName
                                memberName.span.length == memberFieldName.span.length => memberEqual!
                                UIntSize(0) => memberNameByte!
                                (memberEqual! and memberNameByte! < memberName.span.length) -> while {
                                    sources[member!.sourceModule] -> byte(memberName.span.start + memberNameByte!) => memberByte
                                    sources[memberOwnerSource!] -> byte(memberFieldName.span.start + memberNameByte!) => memberFieldByte
                                    memberByte != memberFieldByte -> if { false => memberEqual! }
                                    memberNameByte! + UIntSize(1) => memberNameByte!
                                }
                                memberEqual! -> if {
                                    memberFieldOrdinal! => member!.symbol
                                    memberOwnerSource! => member!.targetModule
                                    -1 => memberFieldTypeIndex!
                                    0 => memberFieldTypeSearch!
                                    memberFieldTypeSearch! < (nominal! -> len) -> while {
                                        (nominal![memberFieldTypeSearch!].sourceModule == memberOwnerSource! and nominal![memberFieldTypeSearch!].typeAst == memberField.typeNode) -> if { memberFieldTypeSearch! => memberFieldTypeIndex! }
                                        memberFieldTypeSearch! + 1 => memberFieldTypeSearch!
                                    }
                                    memberFieldTypeIndex! >= 0 -> if {
                                        nominal![memberFieldTypeIndex!] => memberFieldType
                                        memberFieldType.origin => memberCurrentOrigin!
                                        memberFieldType.targetModule => memberCurrentModule!
                                        memberFieldType.targetSymbol => memberCurrentSymbol!
                                    } else {
                                        0 => memberCompositeSearch!
                                        memberCompositeSearch! < (composite! -> len) -> while {
                                            composite![memberCompositeSearch!] => memberCompositeType
                                            (memberCompositeType.sourceModule == memberOwnerSource! and memberCompositeType.typeAst == memberField.typeNode) -> if {
                                                10 + memberCompositeType.kind => memberCurrentOrigin!
                                                memberCompositeType.kind == 5 -> if {
                                                    memberCompositeType.keySymbol => memberCurrentModule!
                                                    memberCompositeType.valueSymbol => memberCurrentSymbol!
                                                } else {
                                                    memberCompositeType.elementModule => memberCurrentModule!
                                                    memberCompositeType.elementSymbol => memberCurrentSymbol!
                                                }
                                            }
                                            memberCompositeSearch! + 1 => memberCompositeSearch!
                                        }
                                    }
                                    memberCurrentOrigin! => member!.typeOrigin
                                    memberCurrentModule! => member!.typeModule
                                    memberCurrentSymbol! => member!.typeSymbol
                                }
                                memberFieldOrdinal! + 1 => memberFieldOrdinal!
                            }
                            memberFieldIndex! + 1 => memberFieldIndex!
                        }
                    }
                    memberIdentifierOrdinal! + 1 => memberIdentifierOrdinal!
                }
                memberTokenIndex! + 1 => memberTokenIndex!
            }
            member! => results![memberIndex!]
        }
        memberIndex! + 1 => memberIndex!
    }
    0 => loopExitIndex!
    loopExitIndex! < (results! -> len) -> while {
        results![loopExitIndex!] => loopExit!
        (loopExit!.kind == 21 or loopExit!.kind == 22) -> if {
            loopExit!.parent => targetLoop!
            (targetLoop! >= 0 and results![targetLoop!].kind != 20) -> while {
                results![targetLoop!].parent => targetLoop!
            }
            targetLoop! => loopExit!.operand0
            loopExit! => results![loopExitIndex!]
        }
        loopExitIndex! + 1 => loopExitIndex!
    }
    results!
}

public movesFrom ir: [TypedIrNode; ~] -> [MoveEvent; ~] {
    [MoveEvent; ~] => events!
    0 => siteIndex!
    siteIndex! < (ir -> len) -> while {
        ir[siteIndex!] => site
        -1 => movedValueIr!
        (site.kind == 6 and site.symbol >= 0 and site.operand0 >= 0) -> if {
            false => consumes!
            0 => functionIndex!
            functionIndex! < (ir -> len) -> while {
                ir[functionIndex!] => function
                (function.kind == 0 and function.sourceModule == site.targetModule and function.symbol == site.symbol and function.operand1 >= 0) -> if {
                    ir[function.operand1] => parameter
                    parameter.flags % 2 == 1 -> if { true => consumes! }
                }
                functionIndex! + 1 => functionIndex!
            }
            consumes! -> if { site.operand0 => movedValueIr! }
        }
        (site.kind == 17 and site.operand0 >= 0 and ir[site.operand0].kind == 13 and (ir[site.operand0].typeOrigin == 13 or ir[site.operand0].typeOrigin == 15 or ir[site.operand0].typeOrigin == 0 or ir[site.operand0].typeOrigin == 2)) -> if {
            site.operand0 => movedValueIr!
        }
        movedValueIr! >= 0 -> if {
            movedValueIr! => moveRootIr!
            -1 => moveMemberIr!
            ir[moveRootIr!].kind == 13 -> if { moveRootIr! => moveMemberIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 13) -> while { ir[moveRootIr!].operand0 => moveRootIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 5 and ir[moveRootIr!].symbol >= 0) -> if {
                site.parent => moveRegion!
                (moveRegion! >= 0 and ir[moveRegion!].kind != 1 and ir[moveRegion!].kind != 19 and ir[moveRegion!].kind != 20) -> while {
                    ir[moveRegion!].parent => moveRegion!
                }
                events! -> push(MoveEvent {
                    siteIr: siteIndex!
                    sourceModule: ir[moveRootIr!].sourceModule
                    symbol: ir[moveRootIr!].symbol
                    regionIr: moveRegion!
                    memberIr: moveMemberIr!
                })
            }
        }
        siteIndex! + 1 => siteIndex!
    }
    events!
}

public moves sources: [Text; ~] -> [MoveEvent; ~] {
    sources -> lower -> movesFrom
}

public suspensions sources: [Text; ~] -> [CoroutineSuspendPoint; ~] {
    sources -> lower => ir!
    [CoroutineSuspendPoint; ~] => points!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => astNodes!
        source -> lexer.lex => tokens!
        0 => awaitAstIndex!
        awaitAstIndex! < (astNodes! -> len) -> while {
            astNodes![awaitAstIndex!] => awaitAst
            false => isAwait!
            (awaitAst.kind == 16 and awaitAst.parent >= 0 and astNodes![awaitAst.parent].kind == 10 and awaitAst.payloadToken >= 0) -> if {
                tokens![awaitAst.payloadToken] => awaitToken
                awaitToken.span.length == UIntSize(5) -> if {
                    source -> byte(awaitToken.span.start) => awaitByte0
                    source -> byte(awaitToken.span.start + UIntSize(1)) => awaitByte1
                    source -> byte(awaitToken.span.start + UIntSize(2)) => awaitByte2
                    source -> byte(awaitToken.span.start + UIntSize(3)) => awaitByte3
                    source -> byte(awaitToken.span.start + UIntSize(4)) => awaitByte4
                    (awaitByte0 == UInt8(97) and awaitByte1 == UInt8(119) and awaitByte2 == UInt8(97) and awaitByte3 == UInt8(105) and awaitByte4 == UInt8(116)) -> if {
                        true => isAwait!
                    }
                }
            }
            isAwait! -> if {
                awaitAst.parent => functionAstIndex!
                (functionAstIndex! >= 0 and astNodes![functionAstIndex!].kind != 7) -> while {
                    astNodes![functionAstIndex!].parent => functionAstIndex!
                }
                (functionAstIndex! >= 0 and (astNodes![functionAstIndex!].flags / 8) % 2 == 1) -> if {
                    -1 => functionIr!
                    -1 => awaitIr!
                    0 => irIndex!
                    irIndex! < (ir! -> len) -> while {
                        ir![irIndex!] => node
                        (node.sourceModule == sourceIndex! and node.astNode == functionAstIndex! and node.kind == 0) -> if {
                            irIndex! => functionIr!
                        }
                        (node.sourceModule == sourceIndex! and node.astNode == awaitAstIndex!) -> if {
                            irIndex! => awaitIr!
                        }
                        irIndex! + 1 => irIndex!
                    }
                    functionIr! >= 0 -> if {
                        1 => state!
                        0 => pointSearch!
                        pointSearch! < (points! -> len) -> while {
                            points![pointSearch!].functionIr == functionIr! -> if { state! + 1 => state! }
                            pointSearch! + 1 => pointSearch!
                        }
                        points! -> push(CoroutineSuspendPoint {
                            functionIr: functionIr!
                            sourceModule: sourceIndex!
                            awaitAst: awaitAstIndex!
                            awaitIr: awaitIr!
                            state: state!
                        })
                    }
                }
            }
            awaitAstIndex! + 1 => awaitAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    points!
}

public frameSlots sources: [Text; ~] -> [CoroutineFrameSlot; ~] {
    sources -> lower => ir!
    sources -> suspensions => points!
    [CoroutineFrameSlot; ~] => slots!
    0 => pointIndex!
    pointIndex! < (points! -> len) -> while {
        points![pointIndex!] => point
        sources[point.sourceModule] -> ast.lower => astNodes!
        ir![point.functionIr].astNode => functionAst
        astNodes![point.awaitAst].start => awaitStart
        point.awaitAst => awaitBindingAst!
        (awaitBindingAst! >= 0 and astNodes![awaitBindingAst!].kind != 9) -> while {
            astNodes![awaitBindingAst!].parent => awaitBindingAst!
        }
        0 => definitionIndex!
        definitionIndex! < (ir! -> len) -> while {
            ir![definitionIndex!] => definition
            false => belongsToFunction!
            definition.astNode => definitionAncestor!
            (definitionAncestor! >= 0 and not belongsToFunction!) -> while {
                definitionAncestor! == functionAst -> if { true => belongsToFunction! } else {
                    astNodes![definitionAncestor!].parent => definitionAncestor!
                }
            }
            (definition.kind == 17 and definition.sourceModule == point.sourceModule and definition.symbol >= 0 and definition.astNode != awaitBindingAst! and belongsToFunction! and astNodes![definition.astNode].start < awaitStart) -> if {
                false => usedAfterAwait!
                0 => useIndex!
                (useIndex! < (ir! -> len) and not usedAfterAwait!) -> while {
                    ir![useIndex!] => use
                    (use.kind == 5 and use.sourceModule == point.sourceModule and use.symbol == definition.symbol and use.astNode >= 0 and astNodes![use.astNode].start > awaitStart) -> if {
                        false => useBelongsToFunction!
                        use.astNode => useAncestor!
                        (useAncestor! >= 0 and not useBelongsToFunction!) -> while {
                            useAncestor! == functionAst -> if { true => useBelongsToFunction! } else {
                                astNodes![useAncestor!].parent => useAncestor!
                            }
                        }
                        useBelongsToFunction! -> if { true => usedAfterAwait! }
                    }
                    useIndex! + 1 => useIndex!
                }
                usedAfterAwait! -> if {
                    slots! -> push(CoroutineFrameSlot {
                        functionIr: point.functionIr
                        state: point.state
                        sourceModule: point.sourceModule
                        symbol: definition.symbol
                        typeOrigin: definition.typeOrigin
                        typeModule: definition.typeModule
                        typeSymbol: definition.typeSymbol
                    })
                }
            }
            definitionIndex! + 1 => definitionIndex!
        }
        pointIndex! + 1 => pointIndex!
    }
    slots!
}
