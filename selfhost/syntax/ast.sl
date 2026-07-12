namespace smalllang.compiler.ast

import smalllang.compiler.cst as cst
import smalllang.compiler.lexer as lexer
import syntax.generated.smalllang as grammar

# The bootstrap AST is flat and index-addressed like the green CST. Later
# lowering slices will add declaration/expression payload tables without
# introducing per-node heap allocation.
public struct AstNode {
    kind: Int
    parent: Int
    cstRuleId: Int
    operatorKind: Int
    payloadToken: Int
    secondaryToken: Int
    flags: Int
    firstToken: Int
    tokenCount: Int
    start: UIntSize
    length: UIntSize
}

# Kinds: 0 source, 1 namespace, 2 import, 3 struct, 4 enum, 5 trait,
# 6 impl, 7 function, 8 main, 9 binding, 10 flow, 11 call, 12 type,
# 13 string, 14 number, 15 name, 16 path, 17 function signature,
# 18 equality, 19 comparison, 20 additive, 21 multiplicative, 22 unary,
# 23 box, 24 logical-or, 25 logical-and, 26 struct field, 27 enum variant,
# 28 trait associated type, 29 trait method, 30 impl associated type,
# 31 method, 32 generic parameter, 33 generic where constraint,
# 34 associated-type equality, 36 member access, 37 array expression,
# 38 dictionary expression, 39 struct literal, 40 struct field initializer.
# Keyword operator codes use the same
# -(keywordIndex + 1) representation as syntax diagnostics.
public lower source: Text -> [AstNode; ~] {
    classify rule: Int -> Int => when {
        rule == grammar.ruleIdSourceFile => 0
        rule == grammar.ruleIdNamespaceDeclaration => 1
        rule == grammar.ruleIdImportDeclaration => 2
        rule == grammar.ruleIdStructDeclaration => 3
        rule == grammar.ruleIdEnumDeclaration => 4
        rule == grammar.ruleIdTraitDeclaration => 5
        rule == grammar.ruleIdImplDeclaration => 6
        rule == grammar.ruleIdFunctionDeclaration => 7
        rule == grammar.ruleIdMainBlock => 8
        rule == grammar.ruleIdBindingStatement => 9
        rule == grammar.ruleIdFlowExpression => 10
        rule == grammar.ruleIdFlowTargetCall => 11
        rule == grammar.ruleIdCallExpression => 11
        rule == grammar.ruleIdTypeAnnotation => 12
        rule == grammar.ruleIdStringExpression => 13
        rule == grammar.ruleIdNumberExpression => 14
        rule == grammar.ruleIdNameExpression => 15
        rule == grammar.ruleIdPath => 16
        rule == grammar.ruleIdFunctionSignature => 17
        rule == grammar.ruleIdEqualityExpression => 18
        rule == grammar.ruleIdComparisonExpression => 19
        rule == grammar.ruleIdAdditiveExpression => 20
        rule == grammar.ruleIdMultiplicativeExpression => 21
        rule == grammar.ruleIdUnaryExpression => 22
        rule == grammar.ruleIdBoxExpression => 23
        rule == grammar.ruleIdLogicalOrExpression => 24
        rule == grammar.ruleIdLogicalAndExpression => 25
        rule == grammar.ruleIdStructFieldDeclaration => 26
        rule == grammar.ruleIdEnumVariantDeclaration => 27
        rule == grammar.ruleIdTraitAssociatedTypeDeclaration => 28
        rule == grammar.ruleIdTraitMethodDeclaration => 29
        rule == grammar.ruleIdImplAssociatedTypeBinding => 30
        rule == grammar.ruleIdMethodDeclaration => 31
        rule == grammar.ruleIdGenericParameterClause => 32
        rule == grammar.ruleIdGenericWhereClause => 33
        rule == grammar.ruleIdAssociatedTypeEqualityConstraint => 34
        rule == grammar.ruleIdPostfixExpression => 36
        rule == grammar.ruleIdArrayExpression => 37
        rule == grammar.ruleIdDictionaryExpression => 38
        rule == grammar.ruleIdStructLiteralExpression => 39
        rule == grammar.ruleIdStructFieldInitializer => 40
        else => -1
    }
    source -> cst.build => green!
    source -> lexer.lex => tokens!
    [AstNode; ~] => ast!
    [Int; ~] => cstToAst!
    0 => cstIndex!
    0 - 1 => missingNode

    green! -> len => greenCount
    cstIndex! < greenCount -> while {
        green![cstIndex!] => node
        node.ruleId -> classify => astKind!
        -1 => operatorKind!
        -1 => operatorPayloadToken!
        node.firstToken => operatorTokenIndex!
        node.firstToken + node.tokenCount => operatorTokenEnd
        operatorTokenIndex! < operatorTokenEnd -> while {
            tokens![operatorTokenIndex!].kind => candidateOperator
            tokens![operatorTokenIndex!] => candidateToken
            astKind! == 18 and (candidateOperator == grammar.tokenIdEqualEqual or candidateOperator == grammar.tokenIdBangEqual) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            astKind! == 19 and (candidateOperator == grammar.tokenIdLessEqual or candidateOperator == grammar.tokenIdGreaterEqual or candidateOperator == grammar.tokenIdLess or candidateOperator == grammar.tokenIdGreater) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            astKind! == 20 and (candidateOperator == grammar.tokenIdPlus or candidateOperator == grammar.tokenIdMinus) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            astKind! == 21 and (candidateOperator == grammar.tokenIdStar or candidateOperator == grammar.tokenIdSlash or candidateOperator == grammar.tokenIdPercent) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            astKind! == 22 and (candidateOperator == grammar.tokenIdMinus or candidateOperator == grammar.tokenIdBang) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            (astKind! == 36 and candidateOperator == grammar.tokenIdDot) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            (candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(2)) -> if {
                source -> byte(candidateToken.span.start) => shortKeywordByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => shortKeywordByte1
                (astKind! == 24 and shortKeywordByte0 == UInt8(111) and shortKeywordByte1 == UInt8(114)) -> if {
                    -24 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            (candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(3)) -> if {
                source -> byte(candidateToken.span.start) => longKeywordByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => longKeywordByte1
                source -> byte(candidateToken.span.start + UIntSize(2)) => longKeywordByte2
                (astKind! == 25 and longKeywordByte0 == UInt8(97) and longKeywordByte1 == UInt8(110) and longKeywordByte2 == UInt8(100)) -> if {
                    -25 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
                (astKind! == 22 and longKeywordByte0 == UInt8(110) and longKeywordByte1 == UInt8(111) and longKeywordByte2 == UInt8(116)) -> if {
                    -26 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
                (astKind! == 22 and longKeywordByte0 == UInt8(98) and longKeywordByte1 == UInt8(111) and longKeywordByte2 == UInt8(120)) -> if {
                    23 => astKind!
                    -27 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            operatorTokenIndex! + 1 => operatorTokenIndex!
        }
        operatorPayloadToken! < 0 -> if {
            astKind! >= 18 -> if {
                astKind! <= 22 -> if {
                    -1 => astKind!
                }
            }
            astKind! == 24 -> if {
                -1 => astKind!
            }
            astKind! == 25 -> if {
                -1 => astKind!
            }
            astKind! == 36 -> if {
                -1 => astKind!
            }
        }
        astKind! == 23 -> if {
            -27 => operatorKind!
            node.firstToken => operatorPayloadToken!
        }
        cstToAst! -> push(missingNode)
        astKind! >= 0 -> if {
            -1 => astParent!
            node.parent => parentCst!
            (parentCst! >= 0 and astParent! < 0) -> while {
                cstToAst![parentCst!] => mappedParent
                mappedParent >= 0 -> if {
                    mappedParent => astParent!
                } else {
                    green![parentCst!].parent => parentCst!
                }
            }
            node.firstToken => astFirstToken!
            node.tokenCount => astTokenCount!
            (astTokenCount! > 0 and (tokens![astFirstToken!].kind == grammar.triviaIdWhitespace or tokens![astFirstToken!].kind == grammar.triviaIdComment)) -> while {
                astFirstToken! + 1 => astFirstToken!
                astTokenCount! - 1 => astTokenCount!
            }
            (astTokenCount! > 0 and (tokens![astFirstToken! + astTokenCount! - 1].kind == grammar.triviaIdWhitespace or tokens![astFirstToken! + astTokenCount! - 1].kind == grammar.triviaIdComment)) -> while {
                astTokenCount! - 1 => astTokenCount!
            }
            node.start => astStart!
            UIntSize(0) => astLength!
            astTokenCount! > 0 -> if {
                tokens![astFirstToken!].span.start => astStart!
                tokens![astFirstToken! + astTokenCount! - 1] => astLastToken
                astLastToken.span.start + astLastToken.span.length - astStart! => astLength!
            }
            astFirstToken! => payloadToken!
            operatorPayloadToken! >= 0 -> if {
                operatorPayloadToken! => payloadToken!
            }
            AstNode {
                kind: astKind!
                parent: astParent!
                cstRuleId: node.ruleId
                operatorKind: operatorKind!
                payloadToken: payloadToken!
                secondaryToken: -1
                flags: 0
                firstToken: astFirstToken!
                tokenCount: astTokenCount!
                start: astStart!
                length: astLength!
            } => astNode
            ast! -> len => astIndex
            ast! -> push(astNode)
            astIndex => cstToAst![cstIndex!]
        }
        cstIndex! + 1 => cstIndex!
    }

    # Resolve declaration payloads after every semantic parent/child index is
    # known. Path children provide qualified names; direct declarations scan
    # only their header token range.
    ast! -> len => astCount
    0 => declarationIndex!
    declarationIndex! < astCount -> while {
        ast![declarationIndex!] => declaration!
        (declaration!.kind == 1 or declaration!.kind == 2 or declaration!.kind == 6 or declaration!.kind == 7) -> if {
            false => pathPayloadFound!
            0 => childIndex!
            childIndex! < astCount -> while {
                ast![childIndex!] => child
                not pathPayloadFound! -> if {
                    (child.parent == declarationIndex! and child.kind == 16) -> if {
                        child.payloadToken => declaration!.payloadToken
                        true => pathPayloadFound!
                    }
                }
                childIndex! + 1 => childIndex!
            }
        }
        (declaration!.kind == 3 or declaration!.kind == 4 or declaration!.kind == 5) -> if {
            declaration!.firstToken => headerToken!
            declaration!.firstToken + declaration!.tokenCount => headerEnd
            headerToken! < headerEnd -> while {
                tokens![headerToken!].kind == grammar.tokenIdLeftBrace -> if {
                    headerEnd => headerToken!
                } else {
                    tokens![headerToken!].kind == grammar.tokenIdIdentifier -> if {
                        headerToken! => declaration!.payloadToken
                    }
                    headerToken! + 1 => headerToken!
                }
            }
        }
        (declaration!.kind == 28 or declaration!.kind == 30) -> if {
            declaration!.firstToken => associatedToken!
            declaration!.firstToken + declaration!.tokenCount => associatedEnd!
            associatedToken! < associatedEnd! -> while {
                tokens![associatedToken!].kind == grammar.tokenIdEqual -> if {
                    associatedEnd! => associatedToken!
                } else {
                    tokens![associatedToken!].kind == grammar.tokenIdIdentifier -> if {
                        associatedToken! => declaration!.payloadToken
                    }
                    associatedToken! + 1 => associatedToken!
                }
            }
        }
        (declaration!.kind == 7 or declaration!.kind == 29 or declaration!.kind == 31) -> if {
            declaration!.firstToken => signatureToken!
            declaration!.firstToken + declaration!.tokenCount => signatureEnd
            false => afterColon!
            signatureToken! < signatureEnd -> while {
                tokens![signatureToken!].kind == grammar.tokenIdColon -> if {
                    true => afterColon!
                    signatureToken! + 1 => signatureToken!
                } else {
                    tokens![signatureToken!].kind == grammar.tokenIdArrow -> if {
                        signatureEnd => signatureToken!
                    } else {
                        tokens![signatureToken!].kind == grammar.tokenIdIdentifier -> if {
                            afterColon! -> if {
                                tokens![signatureToken!] => modifierToken
                                false => ownershipKeyword!
                                modifierToken.span.length == UIntSize(4) -> if {
                                    source -> byte(modifierToken.span.start) => moveByte0
                                    source -> byte(modifierToken.span.start + UIntSize(1)) => moveByte1
                                    source -> byte(modifierToken.span.start + UIntSize(2)) => moveByte2
                                    source -> byte(modifierToken.span.start + UIntSize(3)) => moveByte3
                                    (moveByte0 == UInt8(109) and moveByte1 == UInt8(111) and moveByte2 == UInt8(118) and moveByte3 == UInt8(101)) -> if {
                                        1 => declaration!.flags
                                        true => ownershipKeyword!
                                    }
                                }
                                modifierToken.span.length == UIntSize(3) -> if {
                                    source -> byte(modifierToken.span.start) => mutByte0
                                    source -> byte(modifierToken.span.start + UIntSize(1)) => mutByte1
                                    source -> byte(modifierToken.span.start + UIntSize(2)) => mutByte2
                                    (mutByte0 == UInt8(109) and mutByte1 == UInt8(117) and mutByte2 == UInt8(116)) -> if {
                                        2 => declaration!.flags
                                        true => ownershipKeyword!
                                    }
                                }
                                (not ownershipKeyword! and modifierToken.span.length == UIntSize(4)) -> if {
                                    source -> byte(modifierToken.span.start) => selfByte0
                                    source -> byte(modifierToken.span.start + UIntSize(1)) => selfByte1
                                    source -> byte(modifierToken.span.start + UIntSize(2)) => selfByte2
                                    source -> byte(modifierToken.span.start + UIntSize(3)) => selfByte3
                                    (selfByte0 == UInt8(115) and selfByte1 == UInt8(101) and selfByte2 == UInt8(108) and selfByte3 == UInt8(102)) -> if {
                                        signatureToken! => declaration!.secondaryToken
                                    }
                                }
                            } else {
                                signatureToken! != declaration!.payloadToken -> if {
                                    signatureToken! => declaration!.secondaryToken
                                }
                            }
                        }
                        signatureToken! + 1 => signatureToken!
                    }
                }
            }
        }
        declaration!.kind == 9 -> if {
            declaration!.firstToken => bindingToken!
            declaration!.firstToken + declaration!.tokenCount => bindingEnd!
            false => afterFatArrow!
            bindingToken! < bindingEnd! -> while {
                tokens![bindingToken!].kind == grammar.tokenIdFatArrow -> if {
                    true => afterFatArrow!
                    bindingToken! + 1 => bindingToken!
                } else {
                    afterFatArrow! -> if {
                        tokens![bindingToken!].kind == grammar.tokenIdIdentifier -> if {
                            bindingToken! => declaration!.payloadToken
                            bindingEnd! => bindingToken!
                        } else {
                            bindingToken! + 1 => bindingToken!
                        }
                    } else {
                        bindingToken! + 1 => bindingToken!
                    }
                }
            }
        }
        declaration!.kind == 32 -> if {
            -1 => firstGenericToken!
            -1 => secondGenericToken!
            false => afterGenericComma!
            declaration!.firstToken => genericToken!
            genericToken! < declaration!.firstToken + declaration!.tokenCount -> while {
                tokens![genericToken!].kind == grammar.tokenIdComma -> if {
                    true => afterGenericComma!
                } else {
                    tokens![genericToken!].kind == grammar.tokenIdIdentifier -> if {
                        (not afterGenericComma! and firstGenericToken! < 0) -> if {
                            genericToken! => firstGenericToken!
                        }
                        (afterGenericComma! and secondGenericToken! < 0) -> if {
                            genericToken! => secondGenericToken!
                        }
                    }
                }
                genericToken! + 1 => genericToken!
            }
            firstGenericToken! => declaration!.payloadToken
            secondGenericToken! => declaration!.secondaryToken
        }
        (declaration!.kind >= 3 and declaration!.kind <= 7) -> if {
            declaration!.firstToken => visibilityToken!
            true => findingVisibility!
            (visibilityToken! < declaration!.firstToken + declaration!.tokenCount and findingVisibility!) -> while {
                tokens![visibilityToken!].kind == grammar.triviaIdWhitespace -> if {
                    visibilityToken! + 1 => visibilityToken!
                } else {
                    tokens![visibilityToken!].kind == grammar.triviaIdComment -> if {
                        visibilityToken! + 1 => visibilityToken!
                    } else {
                        false => findingVisibility!
                    }
                }
            }
            tokens![visibilityToken!] => visibilityName
            (visibilityName.kind == grammar.tokenIdIdentifier and visibilityName.span.length == UIntSize(6)) -> if {
                source -> byte(visibilityName.span.start) => publicByte0
                source -> byte(visibilityName.span.start + UIntSize(1)) => publicByte1
                source -> byte(visibilityName.span.start + UIntSize(2)) => publicByte2
                source -> byte(visibilityName.span.start + UIntSize(3)) => publicByte3
                source -> byte(visibilityName.span.start + UIntSize(4)) => publicByte4
                source -> byte(visibilityName.span.start + UIntSize(5)) => publicByte5
                (publicByte0 == UInt8(112) and publicByte1 == UInt8(117) and publicByte2 == UInt8(98) and publicByte3 == UInt8(108) and publicByte4 == UInt8(105) and publicByte5 == UInt8(99)) -> if {
                    declaration!.flags + 4 => declaration!.flags
                }
            }
        }
        declaration! => ast![declarationIndex!]
        declarationIndex! + 1 => declarationIndex!
    }

    ast!
}
