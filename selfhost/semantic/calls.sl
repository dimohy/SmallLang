namespace smalllang.compiler.semantic.calls

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.analysis as analysis
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.qualified as qualified
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

public struct CallResolution {
    callAst: Int
    functionSymbol: Int
    status: Int
}

public struct ModuleCallResolution {
    sourceModule: Int
    callAst: Int
    origin: Int
    targetModule: Int
    targetSourceModule: Int
    functionSymbol: Int
    status: Int
}

public struct ModuleCallRequest {
    sources: [Text; ~]
    modules: [modules.ModuleIdentity; ~]
    qualified: [qualified.QualifiedResolution; ~]
}

struct TokenNameRequest {
    source: Text
    token: syntax.SyntaxToken
    expected: Text
}

struct RuntimeNameRequest {
    source: Text
    token: syntax.SyntaxToken
}

tokenNameIs request: TokenNameRequest -> Bool {
    request.token.span.length == (request.expected -> len) => equal!
    UIntSize(0) => index!
    (equal! and index! < request.token.span.length) -> while {
        (request.source -> byte(request.token.span.start + index!)) != (request.expected -> byte(index!)) -> if { false => equal! }
        index! + UIntSize(1) => index!
    }
    equal!
}

# Negative symbols are stable built-in aliases used before stdlib-qualified
# resolution. -101/-102 are retained for typed-IR compatibility.
runtimeFunctionSymbol request: RuntimeNameRequest -> Int {
    TokenNameRequest { source: request.source, token: request.token, expected: "print" } -> tokenNameIs -> if { -101 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "println" } -> tokenNameIs -> if { -102 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "readInt" } -> tokenNameIs -> if { -103 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "seedRandom" } -> tokenNameIs -> if { -104 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "randomBelow" } -> tokenNameIs -> if { -105 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "openIntWriter" } -> tokenNameIs -> if { -106 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "writeInt" } -> tokenNameIs -> if { -107 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "closeIntWriter" } -> tokenNameIs -> if { -108 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "openIntReader" } -> tokenNameIs -> if { -109 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "closestInt" } -> tokenNameIs -> if { -110 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "closeIntReader" } -> tokenNameIs -> if { -111 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "nowMillis" } -> tokenNameIs -> if { -112 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "sleep" } -> tokenNameIs -> if { -113 } else {
    TokenNameRequest { source: request.source, token: request.token, expected: "flush" } -> tokenNameIs -> if { -114 } else { -1 }
    }
    }
    }
    }
    }
    }
    }
    }
    }
    }
    }
    }
    }
}

# Status 0 is a resolved local function and 2 is an unresolved call target.
public resolve source: Text -> [CallResolution; ~] {
    source -> ast.lower => nodes!
    source -> lexer.lex => tokens!
    nodes! -> symbols.collectPrepared => table!
    [CallResolution; ~] => resolved!
    0 => astIndex!
    astIndex! < (nodes! -> len) -> while {
        nodes![astIndex!] => node
        false => hasControlTarget!
        false => hasFlowCallTarget!
        false => hasQualifiedFlowTarget!
        0 => controlTargetSearch!
        controlTargetSearch! < (nodes! -> len) -> while {
            (nodes![controlTargetSearch!].parent == astIndex! and (nodes![controlTargetSearch!].kind == 42 or nodes![controlTargetSearch!].kind == 44)) -> if { true => hasControlTarget! }
            controlTargetSearch! + 1 => controlTargetSearch!
        }
        node.firstToken => flowTokenIndex!
        (node.kind == 10 and flowTokenIndex! < node.firstToken + node.tokenCount) -> while {
            tokens![flowTokenIndex!].kind == grammar.tokenIdArrow -> if { true => hasFlowCallTarget! }
            tokens![flowTokenIndex!].kind == grammar.tokenIdDot -> if { true => hasQualifiedFlowTarget! }
            flowTokenIndex! + 1 => flowTokenIndex!
        }
        ((node.kind == 10 and hasFlowCallTarget! and not hasControlTarget!) or (node.kind == 11 and (node.cstRuleId == grammar.ruleIdCallExpression or node.cstRuleId == grammar.ruleIdTypeApplicationExpression)) or node.kind == 15 or node.kind == 48) -> if {
            -1 => callNameToken!
            node.kind == 48 -> if { node.payloadToken => callNameToken! }
            node.firstToken => tokenIndex!
            false => inFlowTypeArgument!
            false => inFlowCallArguments!
            (tokenIndex! < node.firstToken + node.tokenCount and node.kind != 48 and (node.kind == 10 or callNameToken! < 0)) -> while {
                tokens![tokenIndex!].kind == grammar.tokenIdLess -> if { true => inFlowTypeArgument! }
                tokens![tokenIndex!].kind == grammar.tokenIdGreater -> if { false => inFlowTypeArgument! }
                tokens![tokenIndex!].kind == grammar.tokenIdLeftParen -> if { true => inFlowCallArguments! }
                tokens![tokenIndex!].kind == grammar.tokenIdRightParen -> if { false => inFlowCallArguments! }
                (tokens![tokenIndex!].kind == grammar.tokenIdIdentifier and not inFlowTypeArgument! and not inFlowCallArguments!) -> if { tokenIndex! => callNameToken! }
                tokenIndex! + 1 => tokenIndex!
            }
            callNameToken! >= 0 -> if {
            -1 => functionSymbol!
            node.parent => callerAst!
            -1 => callerFunctionAst!
            (callerAst! >= 0 and callerFunctionAst! < 0) -> while {
                nodes![callerAst!] => callerNode
                callerNode.kind == 7 -> if { callerAst! => callerFunctionAst! } else { callerNode.parent => callerAst! }
            }
            -1 => callerFunctionSymbol!
            0 => callerSymbolSearch!
            (callerSymbolSearch! < (table! -> len) and callerFunctionSymbol! < 0) -> while {
                (table![callerSymbolSearch!].kind == 7 and table![callerSymbolSearch!].astNode == callerFunctionAst!) -> if { callerSymbolSearch! => callerFunctionSymbol! }
                callerSymbolSearch! + 1 => callerSymbolSearch!
            }
            -1 => localOwnerSymbol!
            callerFunctionSymbol! >= 0 -> if {
                table![callerFunctionSymbol!].parent >= 0 -> if { table![callerFunctionSymbol!].parent => localOwnerSymbol! } else { callerFunctionSymbol! => localOwnerSymbol! }
            }
            0 => symbolIndex!
            (symbolIndex! < (table! -> len) and functionSymbol! < 0) -> while {
                table![symbolIndex!] => candidate
                (candidate.kind == 7 and (candidate.parent < 0 or candidate.parent == localOwnerSymbol!)) -> if {
                    tokens![callNameToken!] => callName
                    tokens![candidate.nameToken] => functionName
                    callName.span.length == functionName.span.length => equal!
                    UIntSize(0) => nameByte!
                    (equal! and nameByte! < callName.span.length) -> while {
                        source -> byte(callName.span.start + nameByte!) => callByte
                        source -> byte(functionName.span.start + nameByte!) => functionByte
                        callByte != functionByte -> if { false => equal! }
                        nameByte! + UIntSize(1) => nameByte!
                    }
                    (equal! and (node.kind == 10 or node.kind == 11 or node.kind == 48 or candidate.secondaryTypeNode < 0)) -> if { symbolIndex! => functionSymbol! }
                }
                symbolIndex! + 1 => symbolIndex!
            }
            ((node.kind == 10 and hasQualifiedFlowTarget!) or node.kind == 11 or node.kind == 48 or functionSymbol! >= 0) -> if {
                resolved! -> push(CallResolution {
                    callAst: astIndex!
                    functionSymbol: functionSymbol!
                    status: functionSymbol! >= 0 -> if { 0 } else { 2 }
                })
            }
            }
        }
        astIndex! + 1 => astIndex!
    }
    resolved!
}

# Origins: 0 local function, 1 imported function. Imported statuses preserve
# qualified lookup: 0 public, 2 missing/non-function, 3 non-public.
public resolveModulesPrepared request: move ModuleCallRequest -> [ModuleCallResolution; ~] {
    [modules.ModuleIdentity; ~] => identities!
    request.modules -> each identity { identities! -> push(identity) }
    [qualified.QualifiedResolution; ~] => qualifiedResults!
    request.qualified -> each qualifiedResult { qualifiedResults! -> push(qualifiedResult) }
    request.sources => sources
    [ModuleCallResolution; ~] => results!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        nodes! -> symbols.collectPrepared => table!
        [CallResolution; ~] => localCalls!
        0 => callAstIndex!
        callAstIndex! < (nodes! -> len) -> while {
            nodes![callAstIndex!] => callNode
            false => moduleHasControlTarget!
            false => moduleHasFlowCallTarget!
            false => moduleHasQualifiedFlowTarget!
            0 => moduleControlTargetSearch!
            moduleControlTargetSearch! < (nodes! -> len) -> while {
                (nodes![moduleControlTargetSearch!].parent == callAstIndex! and (nodes![moduleControlTargetSearch!].kind == 42 or nodes![moduleControlTargetSearch!].kind == 44)) -> if { true => moduleHasControlTarget! }
                moduleControlTargetSearch! + 1 => moduleControlTargetSearch!
            }
            callNode.firstToken => moduleFlowTokenIndex!
            (callNode.kind == 10 and moduleFlowTokenIndex! < callNode.firstToken + callNode.tokenCount) -> while {
                tokens![moduleFlowTokenIndex!].kind == grammar.tokenIdArrow -> if { true => moduleHasFlowCallTarget! }
                tokens![moduleFlowTokenIndex!].kind == grammar.tokenIdDot -> if { true => moduleHasQualifiedFlowTarget! }
                moduleFlowTokenIndex! + 1 => moduleFlowTokenIndex!
            }
            ((callNode.kind == 10 and moduleHasFlowCallTarget! and not moduleHasControlTarget!) or (callNode.kind == 11 and (callNode.cstRuleId == grammar.ruleIdCallExpression or callNode.cstRuleId == grammar.ruleIdTypeApplicationExpression)) or callNode.kind == 15 or callNode.kind == 48) -> if {
                -1 => callNameToken!
                callNode.kind == 48 -> if { callNode.payloadToken => callNameToken! }
                callNode.firstToken => callTokenIndex!
                false => moduleInFlowTypeArgument!
                false => moduleInFlowCallArguments!
                (callTokenIndex! < callNode.firstToken + callNode.tokenCount and callNode.kind != 48 and (callNode.kind == 10 or callNameToken! < 0)) -> while {
                    tokens![callTokenIndex!].kind == grammar.tokenIdLess -> if { true => moduleInFlowTypeArgument! }
                    tokens![callTokenIndex!].kind == grammar.tokenIdGreater -> if { false => moduleInFlowTypeArgument! }
                    tokens![callTokenIndex!].kind == grammar.tokenIdLeftParen -> if { true => moduleInFlowCallArguments! }
                    tokens![callTokenIndex!].kind == grammar.tokenIdRightParen -> if { false => moduleInFlowCallArguments! }
                    (tokens![callTokenIndex!].kind == grammar.tokenIdIdentifier and not moduleInFlowTypeArgument! and not moduleInFlowCallArguments!) -> if { callTokenIndex! => callNameToken! }
                    callTokenIndex! + 1 => callTokenIndex!
                }
                callNameToken! >= 0 -> if {
                -1 => localFunctionSymbol!
                callNode.parent => callerAst!
                -1 => callerFunctionAst!
                (callerAst! >= 0 and callerFunctionAst! < 0) -> while {
                    nodes![callerAst!] => callerNode
                    callerNode.kind == 7 -> if { callerAst! => callerFunctionAst! } else { callerNode.parent => callerAst! }
                }
                -1 => callerFunctionSymbol!
                0 => callerSymbolSearch!
                (callerSymbolSearch! < (table! -> len) and callerFunctionSymbol! < 0) -> while {
                    (table![callerSymbolSearch!].kind == 7 and table![callerSymbolSearch!].astNode == callerFunctionAst!) -> if { callerSymbolSearch! => callerFunctionSymbol! }
                    callerSymbolSearch! + 1 => callerSymbolSearch!
                }
                -1 => localOwnerSymbol!
                callerFunctionSymbol! >= 0 -> if {
                    table![callerFunctionSymbol!].parent >= 0 -> if { table![callerFunctionSymbol!].parent => localOwnerSymbol! } else { callerFunctionSymbol! => localOwnerSymbol! }
                }
                0 => localSymbolIndex!
                (localSymbolIndex! < (table! -> len) and localFunctionSymbol! < 0) -> while {
                    table![localSymbolIndex!] => localCandidate
                    (localCandidate.kind == 7 and (localCandidate.parent < 0 or localCandidate.parent == localOwnerSymbol!)) -> if {
                        tokens![callNameToken!] => callName
                        tokens![localCandidate.nameToken] => functionName
                        callName.span.length == functionName.span.length => localEqual!
                        UIntSize(0) => localNameByte!
                        (localEqual! and localNameByte! < callName.span.length) -> while {
                            source -> byte(callName.span.start + localNameByte!) => callByte
                            source -> byte(functionName.span.start + localNameByte!) => functionByte
                            callByte != functionByte -> if { false => localEqual! }
                            localNameByte! + UIntSize(1) => localNameByte!
                        }
                        (localEqual! and (callNode.kind == 10 or callNode.kind == 11 or callNode.kind == 48 or localCandidate.secondaryTypeNode < 0)) -> if { localSymbolIndex! => localFunctionSymbol! }
                    }
                    localSymbolIndex! + 1 => localSymbolIndex!
                }
                ((callNode.kind == 10 or callNode.kind == 11 or callNode.kind == 15) and localFunctionSymbol! < 0) -> if {
                    RuntimeNameRequest { source: source, token: tokens![callNameToken!] } -> runtimeFunctionSymbol => runtimeSymbol
                    runtimeSymbol < -1 -> if { runtimeSymbol => localFunctionSymbol! }
                }
                ((callNode.kind == 10 and moduleHasQualifiedFlowTarget!) or callNode.kind == 11 or callNode.kind == 48 or localFunctionSymbol! != -1) -> if {
                    localCalls! -> push(CallResolution {
                        callAst: callAstIndex!
                        functionSymbol: localFunctionSymbol!
                        status: localFunctionSymbol! != -1 -> if { 0 } else { 2 }
                    })
                }
                }
            }
            callAstIndex! + 1 => callAstIndex!
        }
        0 => localIndex!
        localIndex! < (localCalls! -> len) -> while {
            localCalls![localIndex!] => local
            nodes![local.callAst] => localCallNode
            localCallNode.firstToken + localCallNode.tokenCount => callLeftParenToken!
            localCallNode.firstToken => callScanToken!
            callScanToken! < localCallNode.firstToken + localCallNode.tokenCount -> while {
                tokens![callScanToken!].kind == grammar.tokenIdLeftParen -> if {
                    callScanToken! => callLeftParenToken!
                    localCallNode.firstToken + localCallNode.tokenCount => callScanToken!
                } else {
                    callScanToken! + 1 => callScanToken!
                }
            }
            -1 => importedIndex!
            0 => qualifiedIndex!
            qualifiedIndex! < (qualifiedResults! -> len) -> while {
                qualifiedResults![qualifiedIndex!] => candidate
                candidate.sourceModule == sourceIndex! -> if {
                    candidate.pathAst => ancestor!
                    0 => callPathDistance!
                    false => belongsToCall!
                    (ancestor! >= 0 and not belongsToCall!) -> while {
                        ancestor! == local.callAst -> if {
                            true => belongsToCall!
                        } else {
                            nodes![ancestor!].parent => ancestor!
                            callPathDistance! + 1 => callPathDistance!
                        }
                    }
                    nodes![candidate.pathAst] => candidatePath
                    (belongsToCall! and candidatePath.firstToken + candidatePath.tokenCount <= callLeftParenToken!) -> if { qualifiedIndex! => importedIndex! }
                }
                qualifiedIndex! + 1 => qualifiedIndex!
            }
            importedIndex! >= 0 -> if {
                qualifiedResults![importedIndex!] => imported
                identities![imported.targetModule].sourceIndex => targetSourceModule
                imported.status => importedStatus!
                imported.status == 0 -> if {
                    sources[targetSourceModule] -> symbols.collect => targetSymbols!
                    targetSymbols![imported.targetSymbol].kind != 7 -> if { 2 => importedStatus! }
                }
                results! -> push(ModuleCallResolution {
                    sourceModule: sourceIndex!
                    callAst: local.callAst
                    origin: 1
                    targetModule: imported.targetModule
                    targetSourceModule: targetSourceModule
                    functionSymbol: imported.targetSymbol
                    status: importedStatus!
                })
            } else {
                local.functionSymbol < -1 => runtimeCall
                results! -> push(ModuleCallResolution {
                    sourceModule: sourceIndex!
                    callAst: local.callAst
                    origin: runtimeCall -> if { 2 } else { 0 }
                    targetModule: sourceIndex!
                    targetSourceModule: runtimeCall -> if { -1 } else { sourceIndex! }
                    functionSymbol: local.functionSymbol
                    status: local.status
                })
            }
            localIndex! + 1 => localIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    results!
}

public resolveModules sources: [Text; ~] -> [ModuleCallResolution; ~] {
    sources -> modules.identities => identities!
    sources -> qualified.resolve => qualifiedResults!
    [Text; ~] => preparedSources!
    sources -> each preparedSource { preparedSources! -> push(preparedSource) }
    ModuleCallRequest {
        sources: preparedSources!
        modules: identities!
        qualified: qualifiedResults!
    } => request!
    request! -> resolveModulesPrepared => results!
    results!
}

public resolveModulesAnalyzed package: analysis.PackageAnalysis -> [ModuleCallResolution; ~] {
    package -> modules.identitiesAnalyzed => identities!
    package -> qualified.resolveAnalyzed => qualifiedResults!
    [ModuleCallResolution; ~] => results!
    0 => sourceIndex!
    sourceIndex! < (package.sources -> len) -> while {
        package.sources[sourceIndex!] => source
        package.ranges[sourceIndex!] => sourceRange
        [CallResolution; ~] => localCalls!
        0 => callAstIndex!
        callAstIndex! < sourceRange.astCount -> while {
            package.nodes[sourceRange.astStart + callAstIndex!] => callNode
            false => moduleHasControlTarget!
            false => moduleHasFlowCallTarget!
            false => moduleHasQualifiedFlowTarget!
            0 => moduleControlTargetSearch!
            moduleControlTargetSearch! < sourceRange.astCount -> while {
                (package.nodes[sourceRange.astStart + moduleControlTargetSearch!].parent == callAstIndex! and (package.nodes[sourceRange.astStart + moduleControlTargetSearch!].kind == 42 or package.nodes[sourceRange.astStart + moduleControlTargetSearch!].kind == 44)) -> if { true => moduleHasControlTarget! }
                moduleControlTargetSearch! + 1 => moduleControlTargetSearch!
            }
            callNode.firstToken => moduleFlowTokenIndex!
            (callNode.kind == 10 and moduleFlowTokenIndex! < callNode.firstToken + callNode.tokenCount) -> while {
                package.tokens[sourceRange.tokenStart + moduleFlowTokenIndex!].kind == grammar.tokenIdArrow -> if { true => moduleHasFlowCallTarget! }
                package.tokens[sourceRange.tokenStart + moduleFlowTokenIndex!].kind == grammar.tokenIdDot -> if { true => moduleHasQualifiedFlowTarget! }
                moduleFlowTokenIndex! + 1 => moduleFlowTokenIndex!
            }
            ((callNode.kind == 10 and moduleHasFlowCallTarget! and not moduleHasControlTarget!) or (callNode.kind == 11 and (callNode.cstRuleId == grammar.ruleIdCallExpression or callNode.cstRuleId == grammar.ruleIdTypeApplicationExpression)) or callNode.kind == 15 or callNode.kind == 48) -> if {
                -1 => callNameToken!
                callNode.kind == 48 -> if { callNode.payloadToken => callNameToken! }
                callNode.firstToken => callTokenIndex!
                false => moduleInFlowTypeArgument!
                false => moduleInFlowCallArguments!
                (callTokenIndex! < callNode.firstToken + callNode.tokenCount and callNode.kind != 48 and (callNode.kind == 10 or callNameToken! < 0)) -> while {
                    package.tokens[sourceRange.tokenStart + callTokenIndex!].kind == grammar.tokenIdLess -> if { true => moduleInFlowTypeArgument! }
                    package.tokens[sourceRange.tokenStart + callTokenIndex!].kind == grammar.tokenIdGreater -> if { false => moduleInFlowTypeArgument! }
                    package.tokens[sourceRange.tokenStart + callTokenIndex!].kind == grammar.tokenIdLeftParen -> if { true => moduleInFlowCallArguments! }
                    package.tokens[sourceRange.tokenStart + callTokenIndex!].kind == grammar.tokenIdRightParen -> if { false => moduleInFlowCallArguments! }
                    (package.tokens[sourceRange.tokenStart + callTokenIndex!].kind == grammar.tokenIdIdentifier and not moduleInFlowTypeArgument! and not moduleInFlowCallArguments!) -> if { callTokenIndex! => callNameToken! }
                    callTokenIndex! + 1 => callTokenIndex!
                }
                callNameToken! >= 0 -> if {
                -1 => localFunctionSymbol!
                callNode.parent => callerAst!
                -1 => callerFunctionAst!
                (callerAst! >= 0 and callerFunctionAst! < 0) -> while {
                    package.nodes[sourceRange.astStart + callerAst!] => callerNode
                    callerNode.kind == 7 -> if { callerAst! => callerFunctionAst! } else { callerNode.parent => callerAst! }
                }
                -1 => callerFunctionSymbol!
                0 => callerSymbolSearch!
                (callerSymbolSearch! < sourceRange.symbolCount and callerFunctionSymbol! < 0) -> while {
                    package.symbols[sourceRange.symbolStart + callerSymbolSearch!] => callerCandidate
                    (callerCandidate.kind == 7 and callerCandidate.astNode == callerFunctionAst!) -> if { callerSymbolSearch! => callerFunctionSymbol! }
                    callerSymbolSearch! + 1 => callerSymbolSearch!
                }
                -1 => localOwnerSymbol!
                callerFunctionSymbol! >= 0 -> if {
                    package.symbols[sourceRange.symbolStart + callerFunctionSymbol!] => callerFunction
                    callerFunction.parent >= 0 -> if { callerFunction.parent => localOwnerSymbol! } else { callerFunctionSymbol! => localOwnerSymbol! }
                }
                0 => localSymbolIndex!
                (localSymbolIndex! < sourceRange.symbolCount and localFunctionSymbol! < 0) -> while {
                    package.symbols[sourceRange.symbolStart + localSymbolIndex!] => localCandidate
                    (localCandidate.kind == 7 and (localCandidate.parent < 0 or localCandidate.parent == localOwnerSymbol!)) -> if {
                        package.tokens[sourceRange.tokenStart + callNameToken!] => callName
                        package.tokens[sourceRange.tokenStart + localCandidate.nameToken] => functionName
                        callName.span.length == functionName.span.length => localEqual!
                        UIntSize(0) => localNameByte!
                        (localEqual! and localNameByte! < callName.span.length) -> while {
                            source -> byte(callName.span.start + localNameByte!) => callByte
                            source -> byte(functionName.span.start + localNameByte!) => functionByte
                            callByte != functionByte -> if { false => localEqual! }
                            localNameByte! + UIntSize(1) => localNameByte!
                        }
                        (localEqual! and (callNode.kind == 10 or callNode.kind == 11 or callNode.kind == 48 or localCandidate.secondaryTypeNode < 0)) -> if { localSymbolIndex! => localFunctionSymbol! }
                    }
                    localSymbolIndex! + 1 => localSymbolIndex!
                }
                ((callNode.kind == 10 or callNode.kind == 11 or callNode.kind == 15) and localFunctionSymbol! < 0) -> if {
                    RuntimeNameRequest { source: source, token: package.tokens[sourceRange.tokenStart + callNameToken!] } -> runtimeFunctionSymbol => runtimeSymbol
                    runtimeSymbol < -1 -> if { runtimeSymbol => localFunctionSymbol! }
                }
                ((callNode.kind == 10 and moduleHasQualifiedFlowTarget!) or callNode.kind == 11 or callNode.kind == 48 or localFunctionSymbol! != -1) -> if {
                    localCalls! -> push(CallResolution {
                        callAst: callAstIndex!
                        functionSymbol: localFunctionSymbol!
                        status: localFunctionSymbol! != -1 -> if { 0 } else { 2 }
                    })
                }
                }
            }
            callAstIndex! + 1 => callAstIndex!
        }
        0 => localIndex!
        localIndex! < (localCalls! -> len) -> while {
            localCalls![localIndex!] => local
            package.nodes[sourceRange.astStart + local.callAst] => localCallNode
            localCallNode.firstToken + localCallNode.tokenCount => callLeftParenToken!
            localCallNode.firstToken => callScanToken!
            callScanToken! < localCallNode.firstToken + localCallNode.tokenCount -> while {
                package.tokens[sourceRange.tokenStart + callScanToken!].kind == grammar.tokenIdLeftParen -> if {
                    callScanToken! => callLeftParenToken!
                    localCallNode.firstToken + localCallNode.tokenCount => callScanToken!
                } else {
                    callScanToken! + 1 => callScanToken!
                }
            }
            -1 => importedIndex!
            0 => qualifiedIndex!
            qualifiedIndex! < (qualifiedResults! -> len) -> while {
                qualifiedResults![qualifiedIndex!] => candidate
                candidate.sourceModule == sourceIndex! -> if {
                    candidate.pathAst => ancestor!
                    0 => callPathDistance!
                    false => belongsToCall!
                    (ancestor! >= 0 and not belongsToCall!) -> while {
                        ancestor! == local.callAst -> if {
                            true => belongsToCall!
                        } else {
                            package.nodes[sourceRange.astStart + ancestor!].parent => ancestor!
                            callPathDistance! + 1 => callPathDistance!
                        }
                    }
                    package.nodes[sourceRange.astStart + candidate.pathAst] => candidatePath
                    (belongsToCall! and candidatePath.firstToken + candidatePath.tokenCount <= callLeftParenToken!) -> if { qualifiedIndex! => importedIndex! }
                }
                qualifiedIndex! + 1 => qualifiedIndex!
            }
            importedIndex! >= 0 -> if {
                qualifiedResults![importedIndex!] => imported
                identities![imported.targetModule].sourceIndex => targetSourceModule
                imported.status => importedStatus!
                imported.status == 0 -> if {
                    package.ranges[targetSourceModule] => targetRange
                    package.symbols[targetRange.symbolStart + imported.targetSymbol].kind != 7 -> if { 2 => importedStatus! }
                }
                results! -> push(ModuleCallResolution {
                    sourceModule: sourceIndex!
                    callAst: local.callAst
                    origin: 1
                    targetModule: imported.targetModule
                    targetSourceModule: targetSourceModule
                    functionSymbol: imported.targetSymbol
                    status: importedStatus!
                })
            } else {
                local.functionSymbol < -1 => runtimeCall
                results! -> push(ModuleCallResolution {
                    sourceModule: sourceIndex!
                    callAst: local.callAst
                    origin: runtimeCall -> if { 2 } else { 0 }
                    targetModule: sourceIndex!
                    targetSourceModule: runtimeCall -> if { -1 } else { sourceIndex! }
                    functionSymbol: local.functionSymbol
                    status: local.status
                })
            }
            localIndex! + 1 => localIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    results!
}
