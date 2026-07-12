namespace smalllang.compiler.semantic.calls

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.qualified as qualified
import smalllang.compiler.semantic.symbols as symbols
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

# Status 0 is a resolved local function and 2 is an unresolved call target.
public resolve source: Text -> [CallResolution; ~] {
    source -> ast.lower => nodes!
    source -> lexer.lex => tokens!
    source -> symbols.collect => table!
    [CallResolution; ~] => resolved!
    0 => astIndex!
    astIndex! < (nodes! -> len) -> while {
        nodes![astIndex!] => node
        node.kind == 11 -> if {
            -1 => callNameToken!
            node.firstToken => tokenIndex!
            (tokenIndex! < node.firstToken + node.tokenCount and callNameToken! < 0) -> while {
                tokens![tokenIndex!].kind == grammar.tokenIdIdentifier -> if { tokenIndex! => callNameToken! }
                tokenIndex! + 1 => tokenIndex!
            }
            -1 => functionSymbol!
            0 => symbolIndex!
            (symbolIndex! < (table! -> len) and functionSymbol! < 0) -> while {
                table![symbolIndex!] => candidate
                (candidate.kind == 7 and candidate.parent < 0) -> if {
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
                    equal! -> if { symbolIndex! => functionSymbol! }
                }
                symbolIndex! + 1 => symbolIndex!
            }
            resolved! -> push(CallResolution {
                callAst: astIndex!
                functionSymbol: functionSymbol!
                status: functionSymbol! >= 0 -> if { 0 } else { 2 }
            })
        }
        astIndex! + 1 => astIndex!
    }
    resolved!
}

# Origins: 0 local function, 1 imported function. Imported statuses preserve
# qualified lookup: 0 public, 2 missing/non-function, 3 non-public.
public resolveModules sources: [Text; ~] -> [ModuleCallResolution; ~] {
    sources -> modules.identities => identities!
    sources -> qualified.resolve => qualifiedResults!
    [ModuleCallResolution; ~] => results!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        source -> symbols.collect => table!
        [CallResolution; ~] => localCalls!
        0 => callAstIndex!
        callAstIndex! < (nodes! -> len) -> while {
            nodes![callAstIndex!] => callNode
            callNode.kind == 11 -> if {
                -1 => callNameToken!
                callNode.firstToken => callTokenIndex!
                (callTokenIndex! < callNode.firstToken + callNode.tokenCount and callNameToken! < 0) -> while {
                    tokens![callTokenIndex!].kind == grammar.tokenIdIdentifier -> if { callTokenIndex! => callNameToken! }
                    callTokenIndex! + 1 => callTokenIndex!
                }
                -1 => localFunctionSymbol!
                0 => localSymbolIndex!
                (localSymbolIndex! < (table! -> len) and localFunctionSymbol! < 0) -> while {
                    table![localSymbolIndex!] => localCandidate
                    (localCandidate.kind == 7 and localCandidate.parent < 0) -> if {
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
                        localEqual! -> if { localSymbolIndex! => localFunctionSymbol! }
                    }
                    localSymbolIndex! + 1 => localSymbolIndex!
                }
                localCalls! -> push(CallResolution {
                    callAst: callAstIndex!
                    functionSymbol: localFunctionSymbol!
                    status: localFunctionSymbol! >= 0 -> if { 0 } else { 2 }
                })
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
                results! -> push(ModuleCallResolution {
                    sourceModule: sourceIndex!
                    callAst: local.callAst
                    origin: 0
                    targetModule: sourceIndex!
                    targetSourceModule: sourceIndex!
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
