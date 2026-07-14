namespace smalllang.compiler.semantic.effects

import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

public struct FunctionEffect {
    sourceModule: Int
    functionSymbol: Int
    mask: Int
}

# Codes: 1 missing caller effect, 2 unknown effect, 3 duplicate effect.
public struct EffectDiagnostic {
    code: Int
    sourceModule: Int
    functionSymbol: Int
    targetSourceModule: Int
    targetFunctionSymbol: Int
    effectMask: Int
    astNode: Int
    span: syntax.SourceSpan
}

public struct EffectAnalysis {
    functions: [FunctionEffect; ~]
    diagnostics: [EffectDiagnostic; ~]
}

struct TokenTextRequest {
    source: Text
    token: syntax.SyntaxToken
    expected: Text
}

struct EffectMaskRequest {
    source: Text
    token: syntax.SyntaxToken
}

struct HasEffectRequest {
    mask: Int
    effect: Int
}

# Closed capability bits. Async is deliberately not an effect bit: it models
# suspension, while these bits model authority over the outside world.
public effectConsole: -> Int => 1
public effectFile: -> Int => 2
public effectClock: -> Int => 4
public effectRandom: -> Int => 8
public effectProcess: -> Int => 16
public effectEnvironment: -> Int => 32

tokenTextIs request: TokenTextRequest -> Bool {
    request.token.span.length == (request.expected -> len) => equal!
    UIntSize(0) => index!
    (equal! and index! < request.token.span.length) -> while {
        (request.source -> byte(request.token.span.start + index!)) != (request.expected -> byte(index!)) -> if {
            false => equal!
        }
        index! + UIntSize(1) => index!
    }
    equal!
}

effectMask request: EffectMaskRequest -> Int {
    TokenTextRequest { source: request.source, token: request.token, expected: "Console" } -> tokenTextIs -> if { 1 } else {
    TokenTextRequest { source: request.source, token: request.token, expected: "File" } -> tokenTextIs -> if { 2 } else {
    TokenTextRequest { source: request.source, token: request.token, expected: "Clock" } -> tokenTextIs -> if { 4 } else {
    TokenTextRequest { source: request.source, token: request.token, expected: "Random" } -> tokenTextIs -> if { 8 } else {
    TokenTextRequest { source: request.source, token: request.token, expected: "Process" } -> tokenTextIs -> if { 16 } else {
    TokenTextRequest { source: request.source, token: request.token, expected: "Environment" } -> tokenTextIs -> if { 32 } else { 0 }
    }
    }
    }
    }
    }
}

hasEffect request: HasEffectRequest -> Bool {
    (request.mask / request.effect) % 2 == 1
}

public analyze sources: [Text; ~] -> EffectAnalysis {
    sources -> semanticContext.prepare => prepared
    prepared -> analyzeContext
}

public analyzeContext prepared: semanticContext.CompilationContext -> EffectAnalysis {
    [FunctionEffect; ~] => functions!
    [EffectDiagnostic; ~] => diagnostics!
    [1, 2, 4, 8, 16, 32] => effectBits

    # First build one source-qualified effect fact for every function symbol.
    0 => sourceIndex!
    sourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[sourceIndex!] => source
        prepared.ranges[sourceIndex!] => sourceRange
        0 => symbolIndex!
        symbolIndex! < sourceRange.symbolCount -> while {
            prepared.symbols[sourceRange.symbolStart + symbolIndex!] => symbol
            symbol.kind == 7 -> if {
                prepared.nodes[sourceRange.astStart + symbol.astNode] => functionNode
                -1 => signatureAst!
                0 => childIndex!
                (childIndex! < sourceRange.astCount and signatureAst! < 0) -> while {
                    prepared.nodes[sourceRange.astStart + childIndex!] => child
                    (child.parent == symbol.astNode and child.kind == 17) -> if { childIndex! => signatureAst! }
                    childIndex! + 1 => childIndex!
                }

                0 => mask!
                false => afterUses!
                signatureAst! >= 0 -> if {
                    prepared.nodes[sourceRange.astStart + signatureAst!] => signature
                    signature.firstToken => tokenIndex!
                    tokenIndex! < signature.firstToken + signature.tokenCount -> while {
                        prepared.tokens[sourceRange.tokenStart + tokenIndex!] => token
                        TokenTextRequest { source: source, token: token, expected: "uses" } -> tokenTextIs -> if {
                            true => afterUses!
                        } else {
                            (afterUses! and token.kind == grammar.tokenIdIdentifier) -> if {
                                EffectMaskRequest { source: source, token: token } -> effectMask => effect!
                                effect! == 0 -> if {
                                    diagnostics! -> push(EffectDiagnostic {
                                        code: 2
                                        sourceModule: sourceIndex!
                                        functionSymbol: symbolIndex!
                                        targetSourceModule: -1
                                        targetFunctionSymbol: -1
                                        effectMask: 0
                                        astNode: symbol.astNode
                                        span: token.span
                                    })
                                } else {
                                    HasEffectRequest { mask: mask!, effect: effect! } -> hasEffect -> if {
                                        diagnostics! -> push(EffectDiagnostic {
                                            code: 3
                                            sourceModule: sourceIndex!
                                            functionSymbol: symbolIndex!
                                            targetSourceModule: -1
                                            targetFunctionSymbol: -1
                                            effectMask: effect!
                                            astNode: symbol.astNode
                                            span: token.span
                                        })
                                    } else {
                                        mask! + effect! => mask!
                                    }
                                }
                            }
                        }
                        tokenIndex! + 1 => tokenIndex!
                    }
                }
                functions! -> push(FunctionEffect {
                    sourceModule: sourceIndex!
                    functionSymbol: symbolIndex!
                    mask: mask!
                })
            }
            symbolIndex! + 1 => symbolIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }

    # Then validate every prepared resolved call against its lexical caller.
    0 => callIndex!
    callIndex! < (prepared.calls -> len) -> while {
        prepared.calls[callIndex!] => call
        call.status == 0 -> if {
            prepared.ranges[call.sourceModule] => sourceRange
            prepared.nodes[sourceRange.astStart + call.callAst] => callNode
            callNode.parent => ancestor!
            -1 => callerAst!
            (ancestor! >= 0 and callerAst! < 0) -> while {
                prepared.nodes[sourceRange.astStart + ancestor!] => ancestorNode
                ancestorNode.kind == 7 -> if {
                    ancestor! => callerAst!
                } else {
                    ancestorNode.kind == 8 -> if {
                        -2 => callerAst!
                    } else {
                        ancestorNode.parent => ancestor!
                    }
                }
            }

            # Main is the unrestricted root capability boundary.
            callerAst! >= 0 -> if {
                -1 => callerSymbol!
                0 => callerSymbolSearch!
                (callerSymbolSearch! < sourceRange.symbolCount and callerSymbol! < 0) -> while {
                    prepared.symbols[sourceRange.symbolStart + callerSymbolSearch!] => callerCandidate
                    (callerCandidate.kind == 7 and callerCandidate.astNode == callerAst!) -> if {
                        callerSymbolSearch! => callerSymbol!
                    }
                    callerSymbolSearch! + 1 => callerSymbolSearch!
                }

                0 => callerMask!
                0 => callerFactIndex!
                callerFactIndex! < (functions! -> len) -> while {
                    functions![callerFactIndex!] => callerFact
                    (callerFact.sourceModule == call.sourceModule and callerFact.functionSymbol == callerSymbol!) -> if {
                        callerFact.mask => callerMask!
                    }
                    callerFactIndex! + 1 => callerFactIndex!
                }

                0 => requiredMask!
                (call.functionSymbol == -101 or call.functionSymbol == -102 or call.functionSymbol == -103) -> if {
                    1 => requiredMask!
                } else {
                (call.functionSymbol == -104 or call.functionSymbol == -105) -> if {
                    8 => requiredMask!
                } else {
                (call.functionSymbol <= -106 and call.functionSymbol >= -111) -> if {
                    2 => requiredMask!
                } else {
                (call.functionSymbol == -112 or call.functionSymbol == -113) -> if {
                    4 => requiredMask!
                } else {
                call.functionSymbol == -114 -> if {
                    2 => requiredMask!
                } else {
                    0 => targetFactIndex!
                    targetFactIndex! < (functions! -> len) -> while {
                        functions![targetFactIndex!] => targetFact
                        (targetFact.sourceModule == call.targetSourceModule and targetFact.functionSymbol == call.functionSymbol) -> if {
                            targetFact.mask => requiredMask!
                        }
                        targetFactIndex! + 1 => targetFactIndex!
                    }
                }
                }
                }
                }
                }

                0 => effectBitIndex!
                effectBitIndex! < 6 -> while {
                    effectBits[effectBitIndex!] => requiredEffect
                    (HasEffectRequest { mask: requiredMask!, effect: requiredEffect } -> hasEffect) and not (HasEffectRequest { mask: callerMask!, effect: requiredEffect } -> hasEffect) -> if {
                        diagnostics! -> push(EffectDiagnostic {
                            code: 1
                            sourceModule: call.sourceModule
                            functionSymbol: callerSymbol!
                            targetSourceModule: call.targetSourceModule
                            targetFunctionSymbol: call.functionSymbol
                            effectMask: requiredEffect
                            astNode: call.callAst
                            span: syntax.SourceSpan {
                                fileId: call.sourceModule
                                start: callNode.start
                                length: callNode.length
                            }
                        })
                    }
                    effectBitIndex! + 1 => effectBitIndex!
                }
            }
        }
        callIndex! + 1 => callIndex!
    }

    # Memory-map construction is syntax rather than a function call, so derive
    # it directly from the prepared AST. Mapped-view flush uses runtime alias
    # -114 above and therefore follows the ordinary resolved-call path.
    0 => mapSourceIndex!
    mapSourceIndex! < (prepared.sources -> len) -> while {
        prepared.ranges[mapSourceIndex!] => mapSourceRange
        0 => mapAstIndex!
        mapAstIndex! < mapSourceRange.astCount -> while {
            prepared.nodes[mapSourceRange.astStart + mapAstIndex!] => mapNode
            mapNode.kind == 49 -> if {
                mapNode.parent => mapAncestor!
                -1 => mapCallerAst!
                (mapAncestor! >= 0 and mapCallerAst! < 0) -> while {
                    prepared.nodes[mapSourceRange.astStart + mapAncestor!] => mapAncestorNode
                    mapAncestorNode.kind == 7 -> if {
                        mapAncestor! => mapCallerAst!
                    } else {
                        mapAncestorNode.kind == 8 -> if {
                            -2 => mapCallerAst!
                        } else {
                            mapAncestorNode.parent => mapAncestor!
                        }
                    }
                }
                mapCallerAst! >= 0 -> if {
                    -1 => mapCallerSymbol!
                    0 => mapSymbolSearch!
                    (mapSymbolSearch! < mapSourceRange.symbolCount and mapCallerSymbol! < 0) -> while {
                        prepared.symbols[mapSourceRange.symbolStart + mapSymbolSearch!] => mapCandidate
                        (mapCandidate.kind == 7 and mapCandidate.astNode == mapCallerAst!) -> if {
                            mapSymbolSearch! => mapCallerSymbol!
                        }
                        mapSymbolSearch! + 1 => mapSymbolSearch!
                    }
                    0 => mapCallerMask!
                    0 => mapFactSearch!
                    mapFactSearch! < (functions! -> len) -> while {
                        functions![mapFactSearch!] => mapFact
                        (mapFact.sourceModule == mapSourceIndex! and mapFact.functionSymbol == mapCallerSymbol!) -> if {
                            mapFact.mask => mapCallerMask!
                        }
                        mapFactSearch! + 1 => mapFactSearch!
                    }
                    not (HasEffectRequest { mask: mapCallerMask!, effect: 2 } -> hasEffect) -> if {
                        diagnostics! -> push(EffectDiagnostic {
                            code: 1
                            sourceModule: mapSourceIndex!
                            functionSymbol: mapCallerSymbol!
                            targetSourceModule: -1
                            targetFunctionSymbol: -115
                            effectMask: 2
                            astNode: mapAstIndex!
                            span: syntax.SourceSpan {
                                fileId: mapSourceIndex!
                                start: mapNode.start
                                length: mapNode.length
                            }
                        })
                    }
                }
            }
            mapAstIndex! + 1 => mapAstIndex!
        }
        mapSourceIndex! + 1 => mapSourceIndex!
    }

    EffectAnalysis { functions: functions!, diagnostics: diagnostics! } => result!
    result!
}
