namespace smalllang.compiler.semantic.user_effects

import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

public struct UserEffectSignature {
    sourceModule: Int
    effectSymbol: Int
    nameToken: Int
    flags: Int
}

public struct UserEffectOperation {
    sourceModule: Int
    effectSymbol: Int
    operationSymbol: Int
    nameToken: Int
    inputTypeNode: Int
    returnTypeNode: Int
}

public struct UserEffectRequirement {
    sourceModule: Int
    functionSymbol: Int
    effectSourceModule: Int
    effectSymbol: Int
    astNode: Int
    status: Int
}

public struct UserEffectCall {
    sourceModule: Int
    functionSymbol: Int
    effectSourceModule: Int
    effectSymbol: Int
    operationSymbol: Int
    astNode: Int
    status: Int
}

# Codes: 1 duplicate operation, 2 unknown effect, 3 private imported effect,
# 4 operation requires the effect in uses, 5 ambiguous operation.
public struct UserEffectDiagnostic {
    code: Int
    sourceModule: Int
    functionSymbol: Int
    effectSourceModule: Int
    effectSymbol: Int
    operationSymbol: Int
    astNode: Int
    span: syntax.SourceSpan
}

public struct UserEffectAnalysis {
    signatures: [UserEffectSignature; ~]
    operations: [UserEffectOperation; ~]
    requirements: [UserEffectRequirement; ~]
    calls: [UserEffectCall; ~]
    diagnostics: [UserEffectDiagnostic; ~]
}

struct TokenPairRequest {
    leftSource: Text
    left: syntax.SyntaxToken
    rightSource: Text
    right: syntax.SyntaxToken
}

tokenEqual request: TokenPairRequest -> Bool {
    request.left.span.length == request.right.span.length => equal!
    UIntSize(0) => index!
    (equal! and index! < request.left.span.length) -> while {
        (request.leftSource -> byte(request.left.span.start + index!)) != (request.rightSource -> byte(request.right.span.start + index!)) -> if {
            false => equal!
        }
        index! + UIntSize(1) => index!
    }
    equal!
}

public analyze sources: [Text; ~] -> UserEffectAnalysis {
    sources -> semanticContext.prepare => prepared
    prepared -> analyzeContext
}

public analyzeContext prepared: semanticContext.CompilationContext -> UserEffectAnalysis {
    [UserEffectSignature; ~] => signatures!
    [UserEffectOperation; ~] => operations!
    [UserEffectRequirement; ~] => requirements!
    [UserEffectCall; ~] => calls!
    [UserEffectDiagnostic; ~] => diagnostics!

    # Declarations and their typed operation signatures are ordinary symbols.
    0 => sourceIndex!
    sourceIndex! < (prepared.sources -> len) -> while {
        prepared.ranges[sourceIndex!] => sourceRange
        0 => symbolIndex!
        symbolIndex! < sourceRange.symbolCount -> while {
            prepared.symbols[sourceRange.symbolStart + symbolIndex!] => symbol
            symbol.kind == 50 -> if {
                signatures! -> push(UserEffectSignature {
                    sourceModule: sourceIndex!
                    effectSymbol: symbolIndex!
                    nameToken: symbol.nameToken
                    flags: symbol.flags
                })
            }
            symbol.kind == 51 -> if {
                symbol.typeNode => inputTypeNode!
                symbol.secondaryTypeNode => returnTypeNode!
                symbol.secondaryTypeNode < 0 -> if {
                    -1 => inputTypeNode!
                    symbol.typeNode => returnTypeNode!
                }
                operations! -> push(UserEffectOperation {
                    sourceModule: sourceIndex!
                    effectSymbol: symbol.parent
                    operationSymbol: symbolIndex!
                    nameToken: symbol.nameToken
                    inputTypeNode: inputTypeNode!
                    returnTypeNode: returnTypeNode!
                })
            }
            symbolIndex! + 1 => symbolIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }

    # Duplicate operation names are rejected within one effect signature.
    0 => leftOperationIndex!
    leftOperationIndex! < (operations! -> len) -> while {
        operations![leftOperationIndex!] => leftOperation
        leftOperationIndex! + 1 => rightOperationIndex!
        rightOperationIndex! < (operations! -> len) -> while {
            operations![rightOperationIndex!] => rightOperation
            (leftOperation.sourceModule == rightOperation.sourceModule and leftOperation.effectSymbol == rightOperation.effectSymbol) -> if {
                TokenPairRequest {
                    leftSource: prepared.sources[leftOperation.sourceModule]
                    left: prepared.tokens[prepared.ranges[leftOperation.sourceModule].tokenStart + leftOperation.nameToken]
                    rightSource: prepared.sources[rightOperation.sourceModule]
                    right: prepared.tokens[prepared.ranges[rightOperation.sourceModule].tokenStart + rightOperation.nameToken]
                } -> tokenEqual -> if {
                    prepared.symbols[prepared.ranges[rightOperation.sourceModule].symbolStart + rightOperation.operationSymbol] => duplicateSymbol
                    prepared.nodes[prepared.ranges[rightOperation.sourceModule].astStart + duplicateSymbol.astNode] => duplicateNode
                    diagnostics! -> push(UserEffectDiagnostic {
                        code: 1
                        sourceModule: rightOperation.sourceModule
                        functionSymbol: -1
                        effectSourceModule: rightOperation.sourceModule
                        effectSymbol: rightOperation.effectSymbol
                        operationSymbol: rightOperation.operationSymbol
                        astNode: duplicateSymbol.astNode
                        span: syntax.SourceSpan { fileId: rightOperation.sourceModule, start: duplicateNode.start, length: duplicateNode.length }
                    })
                }
            }
            rightOperationIndex! + 1 => rightOperationIndex!
        }
        leftOperationIndex! + 1 => leftOperationIndex!
    }

    # Resolve every user effect reference in a function uses clause. Local
    # effects use lexical names; imported effects reuse qualified module facts.
    0 => requirementSourceIndex!
    requirementSourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[requirementSourceIndex!] => requirementSource
        prepared.ranges[requirementSourceIndex!] => requirementRange
        0 => requirementAstIndex!
        requirementAstIndex! < requirementRange.astCount -> while {
            prepared.nodes[requirementRange.astStart + requirementAstIndex!] => requirementNode
            requirementNode.kind == 52 -> if {
                requirementNode.parent => requirementAncestor!
                -1 => requirementFunctionAst!
                (requirementAncestor! >= 0 and requirementFunctionAst! < 0) -> while {
                    prepared.nodes[requirementRange.astStart + requirementAncestor!] => ancestorNode
                    ancestorNode.kind == 7 -> if { requirementAncestor! => requirementFunctionAst! } else { ancestorNode.parent => requirementAncestor! }
                }
                -1 => requirementFunctionSymbol!
                0 => functionSearch!
                (functionSearch! < requirementRange.symbolCount and requirementFunctionSymbol! < 0) -> while {
                    prepared.symbols[requirementRange.symbolStart + functionSearch!] => functionCandidate
                    (functionCandidate.kind == 7 and functionCandidate.astNode == requirementFunctionAst!) -> if { functionSearch! => requirementFunctionSymbol! }
                    functionSearch! + 1 => functionSearch!
                }

                -1 => referenceNameToken!
                requirementNode.firstToken => referenceTokenIndex!
                referenceTokenIndex! < requirementNode.firstToken + requirementNode.tokenCount -> while {
                    prepared.tokens[requirementRange.tokenStart + referenceTokenIndex!].kind == grammar.tokenIdIdentifier -> if { referenceTokenIndex! => referenceNameToken! }
                    referenceTokenIndex! + 1 => referenceTokenIndex!
                }
                -1 => effectSourceModule!
                -1 => effectSymbol!
                2 => requirementStatus!
                0 => localSignatureIndex!
                localSignatureIndex! < (signatures! -> len) -> while {
                    signatures![localSignatureIndex!] => signature
                    signature.sourceModule == requirementSourceIndex! -> if {
                        TokenPairRequest {
                            leftSource: requirementSource
                            left: prepared.tokens[requirementRange.tokenStart + referenceNameToken!]
                            rightSource: prepared.sources[signature.sourceModule]
                            right: prepared.tokens[prepared.ranges[signature.sourceModule].tokenStart + signature.nameToken]
                        } -> tokenEqual -> if {
                            signature.sourceModule => effectSourceModule!
                            signature.effectSymbol => effectSymbol!
                            0 => requirementStatus!
                        }
                    }
                    localSignatureIndex! + 1 => localSignatureIndex!
                }
                0 => qualifiedIndex!
                qualifiedIndex! < (prepared.qualified -> len) -> while {
                    prepared.qualified[qualifiedIndex!] => qualifiedEffect
                    (qualifiedEffect.sourceModule == requirementSourceIndex! and effectSymbol! < 0) -> if {
                        qualifiedEffect.pathAst => qualifiedAncestor!
                        false => belongsToReference!
                        (qualifiedAncestor! >= 0 and not belongsToReference!) -> while {
                            qualifiedAncestor! == requirementAstIndex! -> if { true => belongsToReference! } else {
                                prepared.nodes[requirementRange.astStart + qualifiedAncestor!].parent => qualifiedAncestor!
                            }
                        }
                        belongsToReference! -> if {
                            prepared.modules[qualifiedEffect.targetModule].sourceIndex => targetSourceModule
                            prepared.ranges[targetSourceModule] => targetRange
                            (qualifiedEffect.targetSymbol >= 0 and prepared.symbols[targetRange.symbolStart + qualifiedEffect.targetSymbol].kind == 50) -> if {
                                targetSourceModule => effectSourceModule!
                                qualifiedEffect.targetSymbol => effectSymbol!
                                qualifiedEffect.status => requirementStatus!
                            }
                        }
                    }
                    qualifiedIndex! + 1 => qualifiedIndex!
                }
                requirements! -> push(UserEffectRequirement {
                    sourceModule: requirementSourceIndex!
                    functionSymbol: requirementFunctionSymbol!
                    effectSourceModule: effectSourceModule!
                    effectSymbol: effectSymbol!
                    astNode: requirementAstIndex!
                    status: requirementStatus!
                })
                requirementStatus! != 0 -> if {
                    diagnostics! -> push(UserEffectDiagnostic {
                        code: requirementStatus! == 3 -> if { 3 } else { 2 }
                        sourceModule: requirementSourceIndex!
                        functionSymbol: requirementFunctionSymbol!
                        effectSourceModule: effectSourceModule!
                        effectSymbol: effectSymbol!
                        operationSymbol: -1
                        astNode: requirementAstIndex!
                        span: syntax.SourceSpan { fileId: requirementSourceIndex!, start: requirementNode.start, length: requirementNode.length }
                    })
                }
            }
            requirementAstIndex! + 1 => requirementAstIndex!
        }
        requirementSourceIndex! + 1 => requirementSourceIndex!
    }

    # Resolve bare operation calls against the caller's declared user effects.
    # Ordinary resolved functions win, so an operation never steals a lexical
    # function call with the same name.
    0 => callSourceIndex!
    callSourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[callSourceIndex!] => callSource
        prepared.ranges[callSourceIndex!] => callRange
        0 => callAstIndex!
        callAstIndex! < callRange.astCount -> while {
            prepared.nodes[callRange.astStart + callAstIndex!] => callNode
            false => flowArrow!
            callNode.firstToken => arrowSearch!
            arrowSearch! < callNode.firstToken + callNode.tokenCount -> while {
                prepared.tokens[callRange.tokenStart + arrowSearch!].kind == grammar.tokenIdArrow -> if { true => flowArrow! }
                arrowSearch! + 1 => arrowSearch!
            }
            (callNode.kind == 11 or (callNode.kind == 10 and flowArrow!)) -> if {
                false => ordinaryResolved!
                0 => preparedCallIndex!
                preparedCallIndex! < (prepared.calls -> len) -> while {
                    prepared.calls[preparedCallIndex!] => preparedCall
                    (preparedCall.sourceModule == callSourceIndex! and preparedCall.callAst == callAstIndex! and preparedCall.status == 0) -> if { true => ordinaryResolved! }
                    preparedCallIndex! + 1 => preparedCallIndex!
                }
                not ordinaryResolved! -> if {
                    callNode.parent => callAncestor!
                    -1 => callFunctionAst!
                    (callAncestor! >= 0 and callFunctionAst! < 0) -> while {
                        prepared.nodes[callRange.astStart + callAncestor!] => callAncestorNode
                        callAncestorNode.kind == 7 -> if { callAncestor! => callFunctionAst! } else { callAncestorNode.parent => callAncestor! }
                    }
                    -1 => callFunctionSymbol!
                    0 => callFunctionSearch!
                    (callFunctionSearch! < callRange.symbolCount and callFunctionSymbol! < 0) -> while {
                        prepared.symbols[callRange.symbolStart + callFunctionSearch!] => callFunctionCandidate
                        (callFunctionCandidate.kind == 7 and callFunctionCandidate.astNode == callFunctionAst!) -> if { callFunctionSearch! => callFunctionSymbol! }
                        callFunctionSearch! + 1 => callFunctionSearch!
                    }

                    -1 => callNameToken!
                    -1 => firstCallNameToken!
                    false => afterArrow!
                    false => insideArguments!
                    callNode.firstToken => callTokenIndex!
                    callTokenIndex! < callNode.firstToken + callNode.tokenCount -> while {
                        prepared.tokens[callRange.tokenStart + callTokenIndex!] => callToken
                        callToken.kind == grammar.tokenIdArrow -> if {
                            true => afterArrow!
                            -1 => callNameToken!
                            -1 => firstCallNameToken!
                        }
                        callToken.kind == grammar.tokenIdLeftParen -> if { true => insideArguments! }
                        callToken.kind == grammar.tokenIdRightParen -> if { false => insideArguments! }
                        (callToken.kind == grammar.tokenIdIdentifier and not insideArguments! and (callNode.kind == 11 or afterArrow!)) -> if {
                            firstCallNameToken! < 0 -> if { callTokenIndex! => firstCallNameToken! }
                            callTokenIndex! => callNameToken!
                        }
                        callTokenIndex! + 1 => callTokenIndex!
                    }
                    callNameToken! >= 0 -> if {
                        0 => matchCount!
                        -1 => matchedEffectSource!
                        -1 => matchedEffectSymbol!
                        -1 => matchedOperationSymbol!
                        0 => callerRequirementIndex!
                        callerRequirementIndex! < (requirements! -> len) -> while {
                            requirements![callerRequirementIndex!] => requirement
                            (requirement.sourceModule == callSourceIndex! and requirement.functionSymbol == callFunctionSymbol! and requirement.status == 0) -> if {
                                0 => operationSearch!
                                operationSearch! < (operations! -> len) -> while {
                                    operations![operationSearch!] => operation
                                    (operation.sourceModule == requirement.effectSourceModule and operation.effectSymbol == requirement.effectSymbol) -> if {
                                        TokenPairRequest {
                                            leftSource: callSource
                                            left: prepared.tokens[callRange.tokenStart + callNameToken!]
                                            rightSource: prepared.sources[operation.sourceModule]
                                            right: prepared.tokens[prepared.ranges[operation.sourceModule].tokenStart + operation.nameToken]
                                        } -> tokenEqual -> if {
                                            matchCount! + 1 => matchCount!
                                            operation.sourceModule => matchedEffectSource!
                                            operation.effectSymbol => matchedEffectSymbol!
                                            operation.operationSymbol => matchedOperationSymbol!
                                        }
                                    }
                                    operationSearch! + 1 => operationSearch!
                                }
                            }
                            callerRequirementIndex! + 1 => callerRequirementIndex!
                        }
                        matchCount! == 1 -> if {
                            calls! -> push(UserEffectCall {
                                sourceModule: callSourceIndex!
                                functionSymbol: callFunctionSymbol!
                                effectSourceModule: matchedEffectSource!
                                effectSymbol: matchedEffectSymbol!
                                operationSymbol: matchedOperationSymbol!
                                astNode: callAstIndex!
                                status: 0
                            })
                        }
                        matchCount! > 1 -> if {
                            diagnostics! -> push(UserEffectDiagnostic {
                                code: 5
                                sourceModule: callSourceIndex!
                                functionSymbol: callFunctionSymbol!
                                effectSourceModule: -1
                                effectSymbol: -1
                                operationSymbol: -1
                                astNode: callAstIndex!
                                span: syntax.SourceSpan { fileId: callSourceIndex!, start: callNode.start, length: callNode.length }
                            })
                        }
                        (matchCount! == 0 and firstCallNameToken! >= 0 and firstCallNameToken! != callNameToken!) -> if {
                            -1 => explicitEffectSymbol!
                            0 => explicitSignatureIndex!
                            explicitSignatureIndex! < (signatures! -> len) -> while {
                                signatures![explicitSignatureIndex!] => explicitSignature
                                explicitSignature.sourceModule == callSourceIndex! -> if {
                                    TokenPairRequest {
                                        leftSource: callSource
                                        left: prepared.tokens[callRange.tokenStart + firstCallNameToken!]
                                        rightSource: prepared.sources[explicitSignature.sourceModule]
                                        right: prepared.tokens[prepared.ranges[explicitSignature.sourceModule].tokenStart + explicitSignature.nameToken]
                                    } -> tokenEqual -> if { explicitSignature.effectSymbol => explicitEffectSymbol! }
                                }
                                explicitSignatureIndex! + 1 => explicitSignatureIndex!
                            }
                            -1 => explicitOperationSymbol!
                            0 => explicitOperationIndex!
                            explicitOperationIndex! < (operations! -> len) -> while {
                                operations![explicitOperationIndex!] => explicitOperation
                                (explicitOperation.sourceModule == callSourceIndex! and explicitOperation.effectSymbol == explicitEffectSymbol!) -> if {
                                    TokenPairRequest {
                                        leftSource: callSource
                                        left: prepared.tokens[callRange.tokenStart + callNameToken!]
                                        rightSource: prepared.sources[explicitOperation.sourceModule]
                                        right: prepared.tokens[prepared.ranges[explicitOperation.sourceModule].tokenStart + explicitOperation.nameToken]
                                    } -> tokenEqual -> if { explicitOperation.operationSymbol => explicitOperationSymbol! }
                                }
                                explicitOperationIndex! + 1 => explicitOperationIndex!
                            }
                            explicitOperationSymbol! >= 0 -> if {
                                calls! -> push(UserEffectCall {
                                    sourceModule: callSourceIndex!
                                    functionSymbol: callFunctionSymbol!
                                    effectSourceModule: callSourceIndex!
                                    effectSymbol: explicitEffectSymbol!
                                    operationSymbol: explicitOperationSymbol!
                                    astNode: callAstIndex!
                                    status: 2
                                })
                                diagnostics! -> push(UserEffectDiagnostic {
                                    code: 4
                                    sourceModule: callSourceIndex!
                                    functionSymbol: callFunctionSymbol!
                                    effectSourceModule: callSourceIndex!
                                    effectSymbol: explicitEffectSymbol!
                                    operationSymbol: explicitOperationSymbol!
                                    astNode: callAstIndex!
                                    span: syntax.SourceSpan { fileId: callSourceIndex!, start: callNode.start, length: callNode.length }
                                })
                            }
                        }
                    }
                }
            }
            callAstIndex! + 1 => callAstIndex!
        }
        callSourceIndex! + 1 => callSourceIndex!
    }

    UserEffectAnalysis {
        signatures: signatures!
        operations: operations!
        requirements: requirements!
        calls: calls!
        diagnostics: diagnostics!
    } => result!
    result!
}
