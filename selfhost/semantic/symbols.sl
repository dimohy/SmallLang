namespace smalllang.compiler.semantic.symbols

import smalllang.compiler.ast as ast
import sys.file as file

# Symbol and AST indexes are stable offsets into owned arrays. This keeps the
# semantic graph relocatable and avoids one heap allocation per declaration.
public struct Symbol {
    kind: Int
    parent: Int
    astNode: Int
    nameToken: Int
    typeNode: Int
    secondaryTypeNode: Int
    blockNameToken: Int
    blockTypeNode: Int
    blockResultTypeNode: Int
    flags: Int
}

public collectPrepared nodes: [ast.AstNode; ~] -> [Symbol; ~] {
    [Symbol; ~] => symbols!
    [Int; ~] => astToSymbol!
    nodes -> len => astCount
    0 => astMapIndex!
    astMapIndex! < astCount -> while {
        astToSymbol! -> push(-1)
        astMapIndex! + 1 => astMapIndex!
    }
    0 => astIndex!

    astIndex! < astCount -> while {
        nodes[astIndex!] => node
        false => isSymbol!
        node.kind >= 3 -> if {
            node.kind <= 7 -> if { true => isSymbol! }
        }
        node.kind == 9 -> if { true => isSymbol! }
        node.kind == 48 -> if { true => isSymbol! }
        (node.kind == 50 or node.kind == 51) -> if { true => isSymbol! }
        node.kind == 58 -> if { true => isSymbol! }
        node.kind == 60 -> if { true => isSymbol! }
        node.kind >= 26 -> if {
            node.kind <= 34 -> if { true => isSymbol! }
        }
        isSymbol! -> if {
            -1 => parentSymbol!
            node.parent => parentAst!
            (parentAst! >= 0 and parentSymbol! < 0) -> while {
                astToSymbol![parentAst!] => mappedParent
                mappedParent >= 0 -> if {
                    mappedParent => parentSymbol!
                } else {
                    nodes[parentAst!].parent => parentAst!
                }
            }
            Symbol {
                kind: node.kind == 60 -> if { 35 } else { node.kind }
                parent: parentSymbol!
                astNode: astIndex!
                nameToken: node.payloadToken
                typeNode: -1
                secondaryTypeNode: -1
                blockNameToken: node.tertiaryToken
                blockTypeNode: -1
                blockResultTypeNode: -1
                flags: node.flags
            } => symbol
            symbols! -> len => symbolIndex
            symbols! -> push(symbol)
            symbolIndex => astToSymbol![astIndex!]
        }
        astIndex! + 1 => astIndex!
    }

    # Attach each type node to its nearest symbol ancestor. The nearest rule
    # prevents a field or method type from being mistaken for its owner type.
    0 => typeAstIndex!
    typeAstIndex! < astCount -> while {
        nodes[typeAstIndex!] => typeAst
        (typeAst.kind == 12 and (typeAst.parent < 0 or nodes[typeAst.parent].kind != 12)) -> if {
            typeAst.parent => typeParentAst!
            -1 => typeOwnerSymbol!
            (typeParentAst! >= 0 and typeOwnerSymbol! < 0) -> while {
                astToSymbol![typeParentAst!] => mappedOwner
                mappedOwner >= 0 -> if {
                    mappedOwner => typeOwnerSymbol!
                } else {
                    nodes[typeParentAst!].parent => typeParentAst!
                }
            }
            typeOwnerSymbol! >= 0 -> if {
                symbols![typeOwnerSymbol!] => owner!
                nodes[owner!.astNode] => ownerAst
                owner!.kind == 7 and ownerAst.tertiaryToken >= 0 -> if {
                    owner!.typeNode < 0 -> if {
                        typeAstIndex! => owner!.typeNode
                    } else {
                        ownerAst.secondaryToken >= 0 -> if {
                            owner!.secondaryTypeNode < 0 -> if {
                                typeAstIndex! => owner!.secondaryTypeNode
                            } else {
                                owner!.blockTypeNode < 0 -> if {
                                    typeAstIndex! => owner!.blockTypeNode
                                } else {
                                    owner!.blockResultTypeNode < 0 -> if { typeAstIndex! => owner!.blockResultTypeNode }
                                }
                            }
                        } else {
                            owner!.blockTypeNode < 0 -> if {
                                typeAstIndex! => owner!.blockTypeNode
                            } else {
                                owner!.blockResultTypeNode < 0 -> if { typeAstIndex! => owner!.blockResultTypeNode }
                            }
                        }
                    }
                } else {
                    owner!.kind == 31 and ownerAst.tertiaryToken >= 0 -> if {
                        owner!.typeNode < 0 -> if {
                            typeAstIndex! => owner!.typeNode
                        } else {
                            owner!.blockTypeNode < 0 -> if {
                                typeAstIndex! => owner!.blockTypeNode
                            } else {
                                owner!.blockResultTypeNode < 0 -> if { typeAstIndex! => owner!.blockResultTypeNode }
                            }
                        }
                    } else {
                        owner!.typeNode < 0 -> if {
                            typeAstIndex! => owner!.typeNode
                        } else {
                            owner!.secondaryTypeNode < 0 -> if {
                                typeAstIndex! => owner!.secondaryTypeNode
                            }
                        }
                    }
                }
                owner! => symbols![typeOwnerSymbol!]
            }
        }
        typeAstIndex! + 1 => typeAstIndex!
    }

    # A multi-name generic clause has one AST node but one lexical symbol per
    # parameter. The compact bootstrap AST currently stores up to three names.
    symbols! -> len => beforeSecondaryGenerics
    0 => genericSymbolIndex!
    genericSymbolIndex! < beforeSecondaryGenerics -> while {
        symbols![genericSymbolIndex!] => genericSymbol
        genericSymbol.kind == 32 -> if {
            nodes[genericSymbol.astNode].secondaryToken >= 0 -> if {
                symbols! -> push(Symbol {
                    kind: 32
                    parent: genericSymbol.parent
                    astNode: genericSymbol.astNode
                    nameToken: nodes[genericSymbol.astNode].secondaryToken
                    typeNode: -1
                    secondaryTypeNode: -1
                    blockNameToken: -1
                    blockTypeNode: -1
                    blockResultTypeNode: -1
                    flags: 0
                })
            }
            nodes[genericSymbol.astNode].tertiaryToken >= 0 -> if {
                symbols! -> push(Symbol {
                    kind: 32
                    parent: genericSymbol.parent
                    astNode: genericSymbol.astNode
                    nameToken: nodes[genericSymbol.astNode].tertiaryToken
                    typeNode: -1
                    secondaryTypeNode: -1
                    blockNameToken: -1
                    blockTypeNode: -1
                    blockResultTypeNode: -1
                    flags: 0
                })
            }
        }
        genericSymbolIndex! + 1 => genericSymbolIndex!
    }

    # Parameters and method self values are synthetic lexical symbols owned by
    # their declaration symbol; they reuse the declaration AST index.
    symbols! -> len => declaredSymbolCount
    0 => declarationSymbolIndex!
    declarationSymbolIndex! < declaredSymbolCount -> while {
        symbols![declarationSymbolIndex!] => declarationSymbol
        nodes[declarationSymbol.astNode] => declarationAst
        declarationAst.secondaryToken >= 0 -> if {
            ((declarationSymbol.kind == 7 and declarationSymbol.secondaryTypeNode >= 0) or declarationSymbol.kind == 29 or declarationSymbol.kind == 31) -> if {
                -1 => parameterTypeNode!
                declarationSymbol.kind == 7 -> if {
                    declarationSymbol.typeNode => parameterTypeNode!
                }
                Symbol {
                    kind: 35
                    parent: declarationSymbolIndex!
                    astNode: declarationSymbol.astNode
                    nameToken: declarationAst.secondaryToken
                    typeNode: parameterTypeNode!
                    secondaryTypeNode: -1
                    blockNameToken: -1
                    blockTypeNode: -1
                    blockResultTypeNode: -1
                    flags: declarationSymbol.flags
                } => parameterSymbol
                symbols! -> push(parameterSymbol)
            }
        }
        ((declarationSymbol.kind == 7 or declarationSymbol.kind == 31) and declarationSymbol.blockNameToken >= 0 and declarationSymbol.blockTypeNode >= 0) -> if {
            symbols! -> push(Symbol {
                kind: 35
                parent: declarationSymbolIndex!
                astNode: declarationSymbol.astNode
                nameToken: declarationSymbol.blockNameToken
                typeNode: declarationSymbol.blockTypeNode
                secondaryTypeNode: -1
                blockNameToken: -1
                blockTypeNode: -1
                blockResultTypeNode: -1
                flags: 0
            })
        }
        declarationSymbolIndex! + 1 => declarationSymbolIndex!
    }

    # Each enum arm is a lexical scope. A payload name belongs only to that
    # arm and is synthesized from the pattern's secondary token.
    symbols! -> len => enumArmScopeCount
    0 => enumArmScopeIndex!
    enumArmScopeIndex! < enumArmScopeCount -> while {
        symbols![enumArmScopeIndex!] => enumArmScope
        enumArmScope.kind == 58 -> if {
            0 => enumPatternAstIndex!
            enumPatternAstIndex! < astCount -> while {
                nodes[enumPatternAstIndex!] => enumPatternAst
                (enumPatternAst.kind == 59 and enumPatternAst.parent == enumArmScope.astNode and enumPatternAst.secondaryToken >= 0) -> if {
                    symbols! -> push(Symbol {
                        kind: 35
                        parent: enumArmScopeIndex!
                        astNode: enumPatternAstIndex!
                        nameToken: enumPatternAst.secondaryToken
                        typeNode: -1
                        secondaryTypeNode: -1
                        blockNameToken: -1
                        blockTypeNode: -1
                        blockResultTypeNode: -1
                        flags: 0
                    })
                }
                enumPatternAstIndex! + 1 => enumPatternAstIndex!
            }
        }
        enumArmScopeIndex! + 1 => enumArmScopeIndex!
    }

    # A role call is a lexical scope. Its block item is visible only inside the
    # caller body, while the trailing result binding belongs to the outer scope.
    symbols! -> len => roleScopeCount
    0 => roleScopeIndex!
    roleScopeIndex! < roleScopeCount -> while {
        symbols![roleScopeIndex!] => roleScope
        roleScope.kind == 48 -> if {
            nodes[roleScope.astNode] => roleAst
            roleAst.secondaryToken >= 0 -> if {
                symbols! -> push(Symbol {
                    kind: 9
                    parent: roleScope.parent
                    astNode: roleScope.astNode
                    nameToken: roleAst.secondaryToken
                    typeNode: -1
                    secondaryTypeNode: -1
                    blockNameToken: -1
                    blockTypeNode: -1
                    blockResultTypeNode: -1
                    flags: roleAst.flags
                })
            }
            roleAst.tertiaryToken >= 0 -> if {
                symbols! -> push(Symbol {
                    kind: 35
                    parent: roleScopeIndex!
                    astNode: roleScope.astNode
                    nameToken: roleAst.tertiaryToken
                    typeNode: -1
                    secondaryTypeNode: -1
                    blockNameToken: -1
                    blockTypeNode: -1
                    blockResultTypeNode: -1
                    flags: 0
                })
            }
        }
        roleScopeIndex! + 1 => roleScopeIndex!
    }

    symbols!
}

public collect source: Text -> [Symbol; ~] {
    source -> ast.lower => nodes!
    nodes! -> collectPrepared => symbols!
    symbols!
}

public collectSource source: file.SourceText -> [Symbol; ~] {
    source -> ast.lowerSource => nodes!
    nodes! -> collectPrepared => symbols!
    symbols!
}
