namespace smalllang.compiler.semantic.composite_types

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.semantic.type_resolve as typeResolve
import smalllang.compiler.semantic.types as types
import syntax.generated.smalllang as grammar

public struct CompositeType {
    sourceModule: Int
    typeAst: Int
    canonical: Int
    kind: Int
    elementOrigin: Int
    elementModule: Int
    elementSymbol: Int
    keyOrigin: Int
    keyModule: Int
    keySymbol: Int
    valueOrigin: Int
    valueModule: Int
    valueSymbol: Int
    lengthToken: Int
    status: Int
}

# Component origins match nominal_types: 0 local, 1 builtin, 3 generic.
public resolve sources: [Text; ~] -> [CompositeType; ~] {
    ["Unit", "Text", "Int", "Int8", "Int16", "Int32", "Int64", "Long", "UInt8", "UInt16", "UInt32", "UInt64", "Size", "UIntSize", "CodePoint", "Arena", "Arguments", "MappedBytes", "MutableMappedBytes", "Float", "Float32", "Float64", "Double", "Bool", ~] => builtinNames!
    [CompositeType; ~] => results!
    sources -> typeResolve.resolve => importedTypes!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        source -> symbols.collect => table!
        source -> types.canonicalize => typeUses!
        0 => typeIndex!
        typeIndex! < (typeUses! -> len) -> while {
            typeUses![typeIndex!] => typeUse
            (typeUse.kind >= 2 and typeUse.kind <= 6) -> if {
                [-1, -1, -1, ~] => componentTokens!
                nodes![typeUse.astNode] => typeNode
                0 => identifierOrdinal!
                typeNode.firstToken => tokenIndex!
                tokenIndex! < typeNode.firstToken + typeNode.tokenCount -> while {
                    tokens![tokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                        typeUse.kind == 5 -> if {
                            identifierOrdinal! == 0 -> if { tokenIndex! => componentTokens![1] }
                            identifierOrdinal! == 1 -> if { tokenIndex! => componentTokens![2] }
                        } else {
                            typeUse.kind == 6 -> if {
                                identifierOrdinal! == 1 -> if { tokenIndex! => componentTokens![0] }
                            } else {
                                identifierOrdinal! == 0 -> if { tokenIndex! => componentTokens![0] }
                            }
                        }
                        identifierOrdinal! + 1 => identifierOrdinal!
                    }
                    tokenIndex! + 1 => tokenIndex!
                }

                -1 => typeOwnerFunction!
                typeNode.parent => ownerAst!
                (ownerAst! >= 0 and typeOwnerFunction! < 0) -> while {
                    0 => ownerSymbolIndex!
                    ownerSymbolIndex! < (table! -> len) -> while {
                        table![ownerSymbolIndex!] => ownerCandidate
                        (ownerCandidate.kind == 7 and ownerCandidate.astNode == ownerAst!) -> if { ownerSymbolIndex! => typeOwnerFunction! }
                        ownerSymbolIndex! + 1 => ownerSymbolIndex!
                    }
                    typeOwnerFunction! < 0 -> if { nodes![ownerAst!].parent => ownerAst! }
                }

                [-1, -1, -1, ~] => componentOrigins!
                [-1, -1, -1, ~] => componentModules!
                [-1, -1, -1, ~] => componentSymbols!
                0 => status!
                0 => componentSlot!
                componentSlot! < 3 -> while {
                    componentTokens![componentSlot!] >= 0 -> if {
                        tokens![componentTokens![componentSlot!]] => componentName
                        -1 => builtinIndex!
                        0 => builtinSearch!
                        (builtinSearch! < (builtinNames! -> len) and builtinIndex! < 0) -> while {
                            builtinNames![builtinSearch!] => builtinName
                            componentName.span.length == (builtinName -> len) => equal!
                            UIntSize(0) => nameByte!
                            (equal! and nameByte! < componentName.span.length) -> while {
                                source -> byte(componentName.span.start + nameByte!) => leftByte
                                builtinName -> byte(nameByte!) => rightByte
                                leftByte != rightByte -> if { false => equal! }
                                nameByte! + UIntSize(1) => nameByte!
                            }
                            equal! -> if { builtinSearch! => builtinIndex! }
                            builtinSearch! + 1 => builtinSearch!
                        }
                        builtinIndex! >= 0 -> if {
                            1 => componentOrigins![componentSlot!]
                            -1 => componentModules![componentSlot!]
                            builtinIndex! => componentSymbols![componentSlot!]
                        } else {
                            -1 => componentSymbol!
                            0 => symbolIndex!
                            (symbolIndex! < (table! -> len) and componentSymbol! < 0) -> while {
                                table![symbolIndex!] => candidate
                                ((candidate.parent < 0 and (candidate.kind == 3 or candidate.kind == 4)) or (candidate.kind == 32 and candidate.parent == typeOwnerFunction!)) -> if {
                                    tokens![candidate.nameToken] => candidateName
                                    componentName.span.length == candidateName.span.length => equal!
                                    UIntSize(0) => nameByte!
                                    (equal! and nameByte! < componentName.span.length) -> while {
                                        source -> byte(componentName.span.start + nameByte!) => leftByte
                                        source -> byte(candidateName.span.start + nameByte!) => rightByte
                                        leftByte != rightByte -> if { false => equal! }
                                        nameByte! + UIntSize(1) => nameByte!
                                    }
                                    equal! -> if { symbolIndex! => componentSymbol! }
                                }
                                symbolIndex! + 1 => symbolIndex!
                            }
                            componentSymbol! >= 0 -> if {
                                table![componentSymbol!].kind == 32 -> if { 3 } else { 0 } => componentOrigin
                                componentOrigin => componentOrigins![componentSlot!]
                                sourceIndex! => componentModules![componentSlot!]
                                componentSymbol! => componentSymbols![componentSlot!]
                            } else {
                                2 => status!
                            }
                        }
                    }
                    componentSlot! + 1 => componentSlot!
                }
                (typeUse.kind != 5 and componentOrigins![0] < 0) -> if {
                    0 => importedTypeIndex!
                    importedTypeIndex! < (importedTypes! -> len) -> while {
                        importedTypes![importedTypeIndex!] => importedType
                        (importedType.sourceModule == sourceIndex! and importedType.typeAst == typeUse.astNode and importedType.status == 0) -> if {
                            2 => componentOrigins![0]
                            importedType.targetModule => componentModules![0]
                            importedType.targetSymbol => componentSymbols![0]
                            0 => status!
                        }
                        importedTypeIndex! + 1 => importedTypeIndex!
                    }
                }
                results! -> push(CompositeType {
                    sourceModule: sourceIndex!
                    typeAst: typeUse.astNode
                    canonical: typeUse.canonical
                    kind: typeUse.kind
                    elementOrigin: componentOrigins![0]
                    elementModule: componentModules![0]
                    elementSymbol: componentSymbols![0]
                    keyOrigin: componentOrigins![1]
                    keyModule: componentModules![1]
                    keySymbol: componentSymbols![1]
                    valueOrigin: componentOrigins![2]
                    valueModule: componentModules![2]
                    valueSymbol: componentSymbols![2]
                    lengthToken: typeUse.lengthToken
                    status: status!
                })
            }
            typeIndex! + 1 => typeIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    results!
}
