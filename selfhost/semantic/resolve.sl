namespace smalllang.compiler.semantic.resolve

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols

public struct ResolvedName {
    astNode: Int
    symbol: Int
    nameToken: Int
}

public resolve source: Text -> [ResolvedName; ~] {
    source -> ast.lower => nodes!
    source -> symbols.collect => table!
    source -> lexer.lex => tokens!
    [ResolvedName; ~] => resolved!
    nodes! -> len => astCount
    table! -> len => symbolCount
    0 => nameAstIndex!

    nameAstIndex! < astCount -> while {
        nodes![nameAstIndex!] => nameAst
        nameAst.kind == 15 -> if {
            -1 => lexicalScope!
            nameAst.parent => ownerAst!
            (ownerAst! >= 0 and lexicalScope! < 0) -> while {
                0 => ownerSymbolIndex!
                ownerSymbolIndex! < symbolCount -> while {
                    table![ownerSymbolIndex!] => ownerCandidate
                    (ownerCandidate.kind != 35 and ownerCandidate.astNode == ownerAst!) -> if {
                        ownerSymbolIndex! => lexicalScope!
                        symbolCount => ownerSymbolIndex!
                    } else {
                        ownerSymbolIndex! + 1 => ownerSymbolIndex!
                    }
                }
                lexicalScope! < 0 -> if {
                    nodes![ownerAst!].parent => ownerAst!
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
                    nodes![referenceOwnerAst!].kind == 9 -> if {
                        referenceOwnerAst! => bindingOwnerAst!
                    } else {
                        nodes![referenceOwnerAst!].parent => referenceOwnerAst!
                    }
                }
                0 => candidateSymbolIndex!
                candidateSymbolIndex! < symbolCount -> while {
                    table![candidateSymbolIndex!] => candidate
                    candidate.parent == searchScope! -> if {
                        tokens![nameAst.payloadToken] => referenceName
                        tokens![candidate.nameToken] => candidateName
                        referenceName.span.length == candidateName.span.length => namesEqual!
                        UIntSize(0) => nameByte!
                        (namesEqual! and nameByte! < referenceName.span.length) -> while {
                            source -> byte(referenceName.span.start + nameByte!) => referenceByte
                            source -> byte(candidateName.span.start + nameByte!) => candidateByte
                            referenceByte != candidateByte -> if { false => namesEqual! }
                            nameByte! + UIntSize(1) => nameByte!
                        }
                        namesEqual! -> if {
                            nodes![candidate.astNode].start => candidateStart
                            ((candidate.kind == 35 or candidateStart < nameAst.start) and candidate.astNode != bindingOwnerAst!) -> if {
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
                        table![searchScope!].parent => searchScope!
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
