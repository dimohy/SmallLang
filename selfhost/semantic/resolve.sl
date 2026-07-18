namespace smalllang.compiler.semantic.resolve

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.syntax as syntax

public struct ResolvedName {
    astNode: Int
    symbol: Int
    nameToken: Int
}

public struct ResolutionRequest {
    source: Text
    nodes: [ast.AstNode; ~]
    symbols: [symbols.Symbol; ~]
    tokens: [syntax.SyntaxToken; ~]
}

public resolvePrepared request: ResolutionRequest -> [ResolvedName; ~] {
    [ResolvedName; ~] => resolved!
    request.nodes -> len => astCount
    request.symbols -> len => symbolCount
    0 => nameAstIndex!

    nameAstIndex! < astCount -> while {
        request.nodes[nameAstIndex!] => nameAst
        nameAst.kind == 15 -> if {
            -1 => lexicalScope!
            nameAst.parent => ownerAst!
            (ownerAst! >= 0 and lexicalScope! < 0) -> while {
                0 => ownerSymbolIndex!
                ownerSymbolIndex! < symbolCount -> while {
                    request.symbols[ownerSymbolIndex!] => ownerCandidate
                    (ownerCandidate.kind != 35 and ownerCandidate.astNode == ownerAst!) -> if {
                        ownerSymbolIndex! => lexicalScope!
                        symbolCount => ownerSymbolIndex!
                    } else {
                        ownerSymbolIndex! + 1 => ownerSymbolIndex!
                    }
                }
                lexicalScope! < 0 -> if {
                    request.nodes[ownerAst!].parent => ownerAst!
                }
            }

            -1 => resolvedSymbol!
            lexicalScope! => searchScope!
            false => scopesDone!
            (resolvedSymbol! < 0 and not scopesDone!) -> while {
                -1 => nearestSymbol!
                UIntSize(0) => nearestStart!
                -1 => bindingOwnerAst!
                nameAst.parent => referenceOwnerAst!
                (referenceOwnerAst! >= 0 and bindingOwnerAst! < 0) -> while {
                    request.nodes[referenceOwnerAst!].kind == 9 -> if {
                        referenceOwnerAst! => bindingOwnerAst!
                    } else {
                        request.nodes[referenceOwnerAst!].parent => referenceOwnerAst!
                    }
                }
                0 => candidateSymbolIndex!
                candidateSymbolIndex! < symbolCount -> while {
                    request.symbols[candidateSymbolIndex!] => candidate
                    (candidate.kind != 48 and candidate.parent == searchScope!) -> if {
                        request.tokens[nameAst.payloadToken] => referenceName
                        request.tokens[candidate.nameToken] => candidateName
                        referenceName.span.length == candidateName.span.length => namesEqual!
                        UIntSize(0) => nameByte!
                        (namesEqual! and nameByte! < referenceName.span.length) -> while {
                            request.source -> byte(referenceName.span.start + nameByte!) => referenceByte
                            request.source -> byte(candidateName.span.start + nameByte!) => candidateByte
                            referenceByte != candidateByte -> if { false => namesEqual! }
                            nameByte! + UIntSize(1) => nameByte!
                        }
                        namesEqual! -> if {
                            request.nodes[candidate.astNode].start => candidateStart
                            candidate.kind == 35 => candidateVisible!
                            candidate.kind != 35 -> if {
                                -1 => candidateBlockAst!
                                candidate.astNode => candidateOwnerAst!
                                (candidateOwnerAst! >= 0 and candidateBlockAst! < 0) -> while {
                                    request.nodes[candidateOwnerAst!].kind == 43 -> if {
                                        candidateOwnerAst! => candidateBlockAst!
                                    } else {
                                        request.nodes[candidateOwnerAst!].parent => candidateOwnerAst!
                                    }
                                }
                                candidateBlockAst! < 0 => candidateVisible!
                                nameAst.parent => referenceBlockSearch!
                                referenceBlockSearch! >= 0 -> while {
                                    referenceBlockSearch! == candidateBlockAst! -> if { true => candidateVisible! }
                                    request.nodes[referenceBlockSearch!].parent => referenceBlockSearch!
                                }
                            }
                            # A nested function is declared before the enclosing
                            # function's executable bindings, but runs only after
                            # those bindings have initialized its closure slots.
                            # Permit that outer-function capture without making
                            # ordinary same-scope forward references visible.
                            false => forwardFunctionCapture!
                            (candidate.kind == 9 and lexicalScope! >= 0 and searchScope! >= 0 and lexicalScope! != searchScope!) -> if {
                                lexicalScope! => referenceFunctionScope!
                                false => referenceFunctionFound!
                                (referenceFunctionScope! >= 0 and not referenceFunctionFound!) -> while {
                                    request.symbols[referenceFunctionScope!] => referenceOwnerCandidate
                                    request.nodes[referenceOwnerCandidate.astNode] => referenceOwnerAst
                                    (referenceOwnerAst.kind == 7 or referenceOwnerAst.kind == 29 or referenceOwnerAst.kind == 31) -> if {
                                        true => referenceFunctionFound!
                                    } else {
                                        referenceOwnerCandidate.parent => referenceFunctionScope!
                                    }
                                }
                                request.symbols[searchScope!] => candidateOwner
                                request.nodes[candidateOwner.astNode] => candidateOwnerAst
                                (referenceFunctionFound! and referenceFunctionScope! != searchScope! and (candidateOwnerAst.kind == 7 or candidateOwnerAst.kind == 29 or candidateOwnerAst.kind == 31)) -> if {
                                    true => forwardFunctionCapture!
                                }
                            }
                            ((candidateVisible! or forwardFunctionCapture!) and (candidate.kind == 35 or candidateStart < nameAst.start or forwardFunctionCapture!) and candidate.astNode != bindingOwnerAst!) -> if {
                                (nearestSymbol! < 0 or candidateStart >= nearestStart!) -> if {
                                    candidateSymbolIndex! => nearestSymbol!
                                    candidateStart => nearestStart!
                                }
                            }
                        }
                    }
                    candidateSymbolIndex! + 1 => candidateSymbolIndex!
                }
                nearestSymbol! >= 0 -> if {
                    nearestSymbol! => resolvedSymbol!
                }
                resolvedSymbol! < 0 -> if {
                    searchScope! >= 0 -> if {
                        request.symbols[searchScope!].parent => searchScope!
                    } else {
                        true => scopesDone!
                    }
                }
            }

            resolvedSymbol! >= 0 -> if {
                ResolvedName {
                    astNode: nameAstIndex!
                    symbol: resolvedSymbol!
                    nameToken: nameAst.payloadToken
                } => resolution
                resolved! -> push(resolution)
            }
        }
        nameAstIndex! + 1 => nameAstIndex!
    }

    resolved!
}

public resolve source: Text -> [ResolvedName; ~] {
    source -> ast.lower => nodes!
    nodes! -> symbols.collectPrepared => table!
    source -> lexer.lex => tokens!
    ResolutionRequest { source: source, nodes: nodes!, symbols: table!, tokens: tokens! } => request!
    request! -> resolvePrepared => resolved!
    resolved!
}
