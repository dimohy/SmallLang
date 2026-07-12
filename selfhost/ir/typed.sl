namespace smalllang.compiler.ir.typed

import smalllang.compiler.ast as ast
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols

# Stable, flat typed IR. Indexes are relocatable array offsets so later LLVM
# lowering can consume the table without allocating an object graph.
# Kinds: 0 function, 1 return, 2 Text constant, 3 Int constant,
# 4 Bool constant, 5 name, 6 call, 7 unary, 8 binary, 9 other expression,
# 10 parameter, 11 entry point.
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
    flags: Int
}

public lower sources: [Text; ~] -> [TypedIrNode; ~] {
    sources -> expressionTypes.infer => inferred!
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
                    results! -> len => functionIr!
                    functionIr! + 1 => returnIr
                    -1 => parameterIr!
                    (parameterSymbol! >= 0 and parameterTypeIndex! >= 0) -> if { returnIr + 1 => parameterIr! }
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
                        flags: 0
                    })
                    parameterIr! >= 0 -> if {
                        inferred![parameterTypeIndex!] => parameterType
                        table![parameterSymbol!] => parameter
                        results! -> push(TypedIrNode {
                            kind: 10
                            parent: functionIr!
                            sourceModule: sourceIndex!
                            astNode: function.astNode
                            symbol: parameterSymbol!
                            targetModule: sourceIndex!
                            typeOrigin: parameterType.origin
                            typeModule: parameterType.targetModule
                            typeSymbol: parameterType.targetSymbol
                            payloadToken: parameter.nameToken
                            opcode: -1
                            operand0: -1
                            operand1: -1
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
                        (operatorIr!.kind == 6 or operatorIr!.kind == 7 or operatorIr!.kind == 8) -> if {
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
                            (operatorIr!.kind == 6 or operatorIr!.kind == 8) -> if { secondOperand! => operatorIr!.operand1 }
                            operatorIr! => results![operandIrIndex!]
                        }
                        operandIrIndex! + 1 => operandIrIndex!
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
                    flags: 0
                })
            }
            entryAstIndex! + 1 => entryAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    results!
}
