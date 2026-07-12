namespace smalllang.compiler.semantic.types

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import syntax.generated.smalllang as grammar

public struct TypeUse {
    astNode: Int
    canonical: Int
    kind: Int
    flags: Int
    elementCanonical: Int
    keyCanonical: Int
    valueCanonical: Int
    lengthToken: Int
}

# Kinds: 1 named, 2 slice, 3 dynamic array, 4 fixed array,
# 5 dictionary, 6 box. Equivalent token sequences ignore trivia.
public canonicalize source: Text -> [TypeUse; ~] {
    source -> ast.lower => nodes!
    source -> lexer.lex => tokens!
    [TypeUse; ~] => uses!
    [Int; ~] => representatives!
    nodes! -> len => astCount
    0 => astIndex!

    astIndex! < astCount -> while {
        nodes![astIndex!] => node
        node.kind == 12 -> if {
            node.firstToken => firstSignificant!
            true => findingFirst!
            (firstSignificant! < node.firstToken + node.tokenCount and findingFirst!) -> while {
                tokens![firstSignificant!].kind == grammar.triviaIdWhitespace -> if {
                    firstSignificant! + 1 => firstSignificant!
                } else {
                    tokens![firstSignificant!].kind == grammar.triviaIdComment -> if {
                        firstSignificant! + 1 => firstSignificant!
                    } else {
                        false => findingFirst!
                    }
                }
            }

            1 => typeKind!
            -1 => elementToken!
            -1 => keyToken!
            -1 => valueToken!
            -1 => lengthToken!
            tokens![firstSignificant!].kind == grammar.tokenIdLeftBracket -> if {
                2 => typeKind!
                false => hasSemicolon!
                false => hasTilde!
                firstSignificant! => shapeToken!
                shapeToken! < node.firstToken + node.tokenCount -> while {
                    tokens![shapeToken!].kind == grammar.tokenIdSemicolon -> if { true => hasSemicolon! }
                    tokens![shapeToken!].kind == grammar.tokenIdTilde -> if { true => hasTilde! }
                    (elementToken! < 0 and tokens![shapeToken!].kind == grammar.tokenIdIdentifier) -> if {
                        shapeToken! => elementToken!
                    }
                    hasSemicolon! -> if {
                        (tokens![shapeToken!].kind == grammar.tokenIdIdentifier or tokens![shapeToken!].kind == grammar.tokenIdNumber) -> if {
                            shapeToken! != elementToken! -> if { shapeToken! => lengthToken! }
                        }
                    }
                    shapeToken! + 1 => shapeToken!
                }
                hasSemicolon! -> if {
                    hasTilde! -> if { 3 => typeKind! } else { 4 => typeKind! }
                }
            } else {
                tokens![firstSignificant!].kind == grammar.tokenIdLeftBrace -> if {
                    5 => typeKind!
                    false => afterTypeColon!
                    firstSignificant! => dictionaryToken!
                    dictionaryToken! < node.firstToken + node.tokenCount -> while {
                        tokens![dictionaryToken!].kind == grammar.tokenIdColon -> if {
                            true => afterTypeColon!
                        } else {
                            tokens![dictionaryToken!].kind == grammar.tokenIdIdentifier -> if {
                                afterTypeColon! -> if {
                                    valueToken! < 0 -> if { dictionaryToken! => valueToken! }
                                } else {
                                    keyToken! < 0 -> if { dictionaryToken! => keyToken! }
                                }
                            }
                        }
                        dictionaryToken! + 1 => dictionaryToken!
                    }
                } else {
                    tokens![firstSignificant!] => firstTypeToken
                    firstTypeToken.kind == grammar.tokenIdIdentifier -> if {
                        firstTypeToken.span.length == UIntSize(3) -> if {
                            source -> byte(firstTypeToken.span.start) => boxByte0
                            source -> byte(firstTypeToken.span.start + UIntSize(1)) => boxByte1
                            source -> byte(firstTypeToken.span.start + UIntSize(2)) => boxByte2
                            (boxByte0 == UInt8(98) and boxByte1 == UInt8(111) and boxByte2 == UInt8(120)) -> if {
                                6 => typeKind!
                                firstSignificant! + 1 => boxElementToken!
                                true => findingBoxElement!
                                (boxElementToken! < node.firstToken + node.tokenCount and findingBoxElement!) -> while {
                                    tokens![boxElementToken!].kind == grammar.tokenIdIdentifier -> if {
                                        boxElementToken! => elementToken!
                                        false => findingBoxElement!
                                    } else {
                                        boxElementToken! + 1 => boxElementToken!
                                    }
                                }
                            }
                        }
                    }
                }
            }

            -1 => canonical!
            0 => representativeIndex!
            (representativeIndex! < (representatives! -> len) and canonical! < 0) -> while {
                nodes![representatives![representativeIndex!]] => representative
                node.firstToken => leftCursor!
                node.firstToken + node.tokenCount => leftEnd
                representative.firstToken => rightCursor!
                representative.firstToken + representative.tokenCount => rightEnd
                true => equal!
                false => comparedAll!
                (equal! and not comparedAll!) -> while {
                    true => skipLeft!
                    (leftCursor! < leftEnd and skipLeft!) -> while {
                        tokens![leftCursor!].kind == grammar.triviaIdWhitespace -> if {
                            leftCursor! + 1 => leftCursor!
                        } else {
                            tokens![leftCursor!].kind == grammar.triviaIdComment -> if {
                                leftCursor! + 1 => leftCursor!
                            } else {
                                false => skipLeft!
                            }
                        }
                    }
                    true => skipRight!
                    (rightCursor! < rightEnd and skipRight!) -> while {
                        tokens![rightCursor!].kind == grammar.triviaIdWhitespace -> if {
                            rightCursor! + 1 => rightCursor!
                        } else {
                            tokens![rightCursor!].kind == grammar.triviaIdComment -> if {
                                rightCursor! + 1 => rightCursor!
                            } else {
                                false => skipRight!
                            }
                        }
                    }
                    (leftCursor! >= leftEnd and rightCursor! >= rightEnd) -> if {
                        true => comparedAll!
                    } else {
                        (leftCursor! >= leftEnd or rightCursor! >= rightEnd) -> if {
                            false => equal!
                        } else {
                            tokens![leftCursor!] => leftToken
                            tokens![rightCursor!] => rightToken
                            (leftToken.kind != rightToken.kind or leftToken.span.length != rightToken.span.length) -> if {
                                false => equal!
                            } else {
                                UIntSize(0) => typeByte!
                                (equal! and typeByte! < leftToken.span.length) -> while {
                                    source -> byte(leftToken.span.start + typeByte!) => leftByte
                                    source -> byte(rightToken.span.start + typeByte!) => rightByte
                                    leftByte != rightByte -> if { false => equal! }
                                    typeByte! + UIntSize(1) => typeByte!
                                }
                            }
                            leftCursor! + 1 => leftCursor!
                            rightCursor! + 1 => rightCursor!
                        }
                    }
                }
                equal! -> if {
                    representativeIndex! => canonical!
                }
                representativeIndex! + 1 => representativeIndex!
            }
            canonical! < 0 -> if {
                representatives! -> len => canonical!
                representatives! -> push(astIndex!)
            }
            TypeUse {
                astNode: astIndex!
                canonical: canonical!
                kind: typeKind!
                flags: 0
                elementCanonical: elementToken!
                keyCanonical: keyToken!
                valueCanonical: valueToken!
                lengthToken: lengthToken!
            } => use
            uses! -> push(use)
        }
        astIndex! + 1 => astIndex!
    }

    # Intern nominal component names and replace temporary token indexes in
    # element/key/value fields with canonical type ids.
    [Int; ~] => componentTokens!
    [Int; ~] => componentCanonicals!
    uses! -> len => useCount
    0 => seedUseIndex!
    seedUseIndex! < useCount -> while {
        uses![seedUseIndex!] => seedUse
        seedUse.kind == 1 -> if {
            nodes![seedUse.astNode] => seedAst
            seedAst.firstToken => seedToken!
            true => findingSeed!
            (seedToken! < seedAst.firstToken + seedAst.tokenCount and findingSeed!) -> while {
                tokens![seedToken!].kind == grammar.tokenIdIdentifier -> if {
                    componentTokens! -> push(seedToken!)
                    componentCanonicals! -> push(seedUse.canonical)
                    false => findingSeed!
                } else {
                    seedToken! + 1 => seedToken!
                }
            }
        }
        seedUseIndex! + 1 => seedUseIndex!
    }
    representatives! -> len => nextCanonical!
    0 => linkUseIndex!
    linkUseIndex! < useCount -> while {
        uses![linkUseIndex!] => linkedUse!
        0 => componentSlot!
        componentSlot! < 3 -> while {
            -1 => componentToken!
            componentSlot! == 0 -> if { linkedUse!.elementCanonical => componentToken! }
            componentSlot! == 1 -> if { linkedUse!.keyCanonical => componentToken! }
            componentSlot! == 2 -> if { linkedUse!.valueCanonical => componentToken! }
            componentToken! >= 0 -> if {
                -1 => componentCanonical!
                0 => knownComponentIndex!
                (knownComponentIndex! < (componentTokens! -> len) and componentCanonical! < 0) -> while {
                    tokens![componentToken!] => componentName
                    tokens![componentTokens![knownComponentIndex!]] => knownName
                    componentName.span.length == knownName.span.length => componentEqual!
                    UIntSize(0) => componentByte!
                    (componentEqual! and componentByte! < componentName.span.length) -> while {
                        source -> byte(componentName.span.start + componentByte!) => componentLeftByte
                        source -> byte(knownName.span.start + componentByte!) => componentRightByte
                        componentLeftByte != componentRightByte -> if { false => componentEqual! }
                        componentByte! + UIntSize(1) => componentByte!
                    }
                    componentEqual! -> if {
                        componentCanonicals![knownComponentIndex!] => componentCanonical!
                    }
                    knownComponentIndex! + 1 => knownComponentIndex!
                }
                componentCanonical! < 0 -> if {
                    nextCanonical! => componentCanonical!
                    nextCanonical! + 1 => nextCanonical!
                    componentTokens! -> push(componentToken!)
                    componentCanonicals! -> push(componentCanonical!)
                }
                componentSlot! == 0 -> if { componentCanonical! => linkedUse!.elementCanonical }
                componentSlot! == 1 -> if { componentCanonical! => linkedUse!.keyCanonical }
                componentSlot! == 2 -> if { componentCanonical! => linkedUse!.valueCanonical }
            }
            componentSlot! + 1 => componentSlot!
        }
        linkedUse! => uses![linkUseIndex!]
        linkUseIndex! + 1 => linkUseIndex!
    }

    uses!
}
