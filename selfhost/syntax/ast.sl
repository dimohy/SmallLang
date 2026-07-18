namespace smalllang.compiler.ast

import smalllang.compiler.cst as cst
import smalllang.compiler.lexer as lexer
import syntax.generated.smalllang as grammar
import sys.file as file

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
    tertiaryToken: Int
    flags: Int
    firstToken: Int
    tokenCount: Int
    start: UIntSize
    length: UIntSize
}

struct LowerRequest {
    source: Text
    startRule: Int
}

# Kinds: 0 source, 1 namespace, 2 import, 3 struct, 4 enum, 5 trait,
# 6 impl, 7 function, 8 main, 9 binding, 10 flow, 11 call, 12 type,
# 13 string, 14 number, 15 name, 16 path, 17 function signature,
# 18 equality, 19 comparison, 20 additive, 21 multiplicative, 22 unary,
# 23 box, 24 logical-or, 25 logical-and, 26 struct field, 27 enum variant,
# 28 trait associated type, 29 trait method, 30 impl associated type,
# 31 method, 32 generic parameter, 33 generic where constraint,
# 34 associated-type equality, 36 member access, 37 array expression,
# 38 dictionary expression, 39 struct literal, 40 struct field initializer,
# 41 index access, 42 if flow target, 43 control-flow region,
# 44 while flow target, 45 loop control statement,
# 46 guarded loop control statement, 47 explicit return statement,
# 48 result-producing block-function call, 49 memory-map expression,
# 50 effect declaration, 51 effect operation, 52 effect reference,
# 53 index assignment, 54 mutable struct member assignment.
# 55 generic enum constructor, 56 subject when target, 57 standalone when,
# 58 when arm, 59 enum pattern, 60 additional function input.
# Function flags: 1 move input, 2 mutable input, 4 public, 8 async.
# Keyword operator codes use the same
# -(keywordIndex + 1) representation as syntax diagnostics.
classifyRule rule: Int -> Int {
    -1 => kind!
    rule == grammar.ruleIdSourceFile -> if { 0 => kind! }
    rule == grammar.ruleIdNamespaceDeclaration -> if { 1 => kind! }
    rule == grammar.ruleIdImportDeclaration -> if { 2 => kind! }
    rule == grammar.ruleIdStructDeclaration -> if { 3 => kind! }
    rule == grammar.ruleIdEnumDeclaration -> if { 4 => kind! }
    rule == grammar.ruleIdTraitDeclaration -> if { 5 => kind! }
    rule == grammar.ruleIdImplDeclaration -> if { 6 => kind! }
    rule == grammar.ruleIdFunctionDeclaration -> if { 7 => kind! }
    rule == grammar.ruleIdMainBlock -> if { 8 => kind! }
    rule == grammar.ruleIdBindingStatement -> if { 9 => kind! }
    rule == grammar.ruleIdFlowExpression -> if { 10 => kind! }
    rule == grammar.ruleIdControlFlowExpression -> if { 10 => kind! }
    rule == grammar.ruleIdFlowTargetCall -> if { 11 => kind! }
    rule == grammar.ruleIdCallExpression -> if { 11 => kind! }
    rule == grammar.ruleIdTypeApplicationExpression -> if { 11 => kind! }
    rule == grammar.ruleIdTypeAnnotation -> if { 12 => kind! }
    rule == grammar.ruleIdStringExpression -> if { 13 => kind! }
    rule == grammar.ruleIdNumberExpression -> if { 14 => kind! }
    rule == grammar.ruleIdNameExpression -> if { 15 => kind! }
    rule == grammar.ruleIdPath -> if { 16 => kind! }
    rule == grammar.ruleIdFunctionSignature -> if { 17 => kind! }
    rule == grammar.ruleIdEqualityExpression -> if { 18 => kind! }
    rule == grammar.ruleIdComparisonExpression -> if { 19 => kind! }
    rule == grammar.ruleIdAdditiveExpression -> if { 20 => kind! }
    rule == grammar.ruleIdMultiplicativeExpression -> if { 21 => kind! }
    rule == grammar.ruleIdUnaryExpression -> if { 22 => kind! }
    rule == grammar.ruleIdBoxExpression -> if { 23 => kind! }
    rule == grammar.ruleIdLogicalOrExpression -> if { 24 => kind! }
    rule == grammar.ruleIdLogicalAndExpression -> if { 25 => kind! }
    rule == grammar.ruleIdStructFieldDeclaration -> if { 26 => kind! }
    rule == grammar.ruleIdEnumVariantDeclaration -> if { 27 => kind! }
    rule == grammar.ruleIdTraitAssociatedTypeDeclaration -> if { 28 => kind! }
    rule == grammar.ruleIdTraitMethodDeclaration -> if { 29 => kind! }
    rule == grammar.ruleIdImplAssociatedTypeBinding -> if { 30 => kind! }
    rule == grammar.ruleIdMethodDeclaration -> if { 31 => kind! }
    rule == grammar.ruleIdGenericParameterClause -> if { 32 => kind! }
    rule == grammar.ruleIdGenericWhereClause -> if { 33 => kind! }
    rule == grammar.ruleIdAssociatedTypeEqualityConstraint -> if { 34 => kind! }
    rule == grammar.ruleIdPostfixExpression -> if { 36 => kind! }
    rule == grammar.ruleIdArrayExpression -> if { 37 => kind! }
    rule == grammar.ruleIdDictionaryExpression -> if { 38 => kind! }
    rule == grammar.ruleIdStructLiteralExpression -> if { 39 => kind! }
    rule == grammar.ruleIdStructFieldInitializer -> if { 40 => kind! }
    rule == grammar.ruleIdIfFlowTarget -> if { 42 => kind! }
    rule == grammar.ruleIdBlockBody -> if { 43 => kind! }
    rule == grammar.ruleIdWhileFlowTarget -> if { 44 => kind! }
    rule == grammar.ruleIdLoopControlStatement -> if { 45 => kind! }
    rule == grammar.ruleIdGuardLoopControlStatement -> if { 46 => kind! }
    rule == grammar.ruleIdReturnStatement -> if { 47 => kind! }
    rule == grammar.ruleIdBlockFunctionCallStatement -> if { 48 => kind! }
    rule == grammar.ruleIdMapExpression -> if { 49 => kind! }
    rule == grammar.ruleIdEffectDeclaration -> if { 50 => kind! }
    rule == grammar.ruleIdEffectOperationDeclaration -> if { 51 => kind! }
    rule == grammar.ruleIdEffectReference -> if { 52 => kind! }
    rule == grammar.ruleIdIndexAssignmentStatement -> if { 53 => kind! }
    rule == grammar.ruleIdFieldAssignmentStatement -> if { 54 => kind! }
    rule == grammar.ruleIdEnumConstructorExpression -> if { 55 => kind! }
    rule == grammar.ruleIdWhenFlowTarget -> if { 56 => kind! }
    rule == grammar.ruleIdWhenExpression -> if { 57 => kind! }
    (rule == grammar.ruleIdWhenArm or rule == grammar.ruleIdSubjectWhenArm) -> if { 58 => kind! }
    rule == grammar.ruleIdEnumPattern -> if { 59 => kind! }
    rule == grammar.ruleIdAdditionalFunctionInput -> if { 60 => kind! }
    kind! => selected
    selected
}

lowerFrom request: LowerRequest -> [AstNode; ~] {
    request.source => source
    cst.BuildRequest {
        source: source
        startRule: request.startRule
    } -> cst.buildRule => green!
    source -> lexer.lex => tokens!
    [AstNode; ~] => ast!
    [Int; ~] => cstToAst!
    [Int; ~] => indexedMemberParents!
    [Int; ~] => indexedMemberNodes!
    [UIntSize; ~] => indexedMemberBracketStarts!
    0 => cstIndex!
    0 - 1 => missingNode

    green! -> len => greenCount
    cstIndex! < greenCount -> while {
        green![cstIndex!] => node
        node.ruleId -> classifyRule => astKind!
        (node.ruleId == grammar.ruleIdTypeName and node.parent >= 0 and green![node.parent].ruleId == grammar.ruleIdBlockFunctionBody) -> if {
            12 => astKind!
        }
        -1 => operatorKind!
        -1 => operatorPayloadToken!
        -1 => memberDotToken!
        -1 => indexBracketToken!
        -1 => trailingMemberDotToken!
        node.firstToken => operatorTokenIndex!
        node.firstToken + node.tokenCount => operatorTokenEnd
        0 => operatorGroupDepth!
        operatorTokenIndex! < operatorTokenEnd -> while {
            tokens![operatorTokenIndex!].kind => candidateOperator
            tokens![operatorTokenIndex!] => candidateToken
            operatorPayloadToken! < 0 and operatorGroupDepth! == 0 and astKind! == 18 and (candidateOperator == grammar.tokenIdEqualEqual or candidateOperator == grammar.tokenIdBangEqual) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            operatorPayloadToken! < 0 and operatorGroupDepth! == 0 and astKind! == 19 and (candidateOperator == grammar.tokenIdLessEqual or candidateOperator == grammar.tokenIdGreaterEqual or candidateOperator == grammar.tokenIdLess or candidateOperator == grammar.tokenIdGreater) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            operatorPayloadToken! < 0 and operatorGroupDepth! == 0 and astKind! == 20 and (candidateOperator == grammar.tokenIdPlus or candidateOperator == grammar.tokenIdMinus) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            operatorPayloadToken! < 0 and operatorGroupDepth! == 0 and astKind! == 21 and (candidateOperator == grammar.tokenIdStar or candidateOperator == grammar.tokenIdSlash or candidateOperator == grammar.tokenIdPercent) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            operatorPayloadToken! < 0 and operatorGroupDepth! == 0 and astKind! == 22 and operatorTokenIndex! == node.firstToken and (candidateOperator == grammar.tokenIdMinus or candidateOperator == grammar.tokenIdBang) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            (operatorGroupDepth! == 0 and astKind! == 36 and candidateOperator == grammar.tokenIdDot) -> if {
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
                operatorTokenIndex! => memberDotToken!
            }
            (operatorGroupDepth! == 0 and astKind! == 36 and candidateOperator == grammar.tokenIdLeftBracket and operatorTokenIndex! > node.firstToken) -> if {
                41 => astKind!
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
                operatorTokenIndex! => indexBracketToken!
            }
            (operatorGroupDepth! == 0 and astKind! == 41 and candidateOperator == grammar.tokenIdDot and trailingMemberDotToken! < 0) -> if {
                operatorTokenIndex! => trailingMemberDotToken!
                36 => astKind!
                candidateOperator => operatorKind!
                operatorTokenIndex! => operatorPayloadToken!
            }
            (operatorGroupDepth! == 0 and candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(2)) -> if {
                source -> byte(candidateToken.span.start) => shortKeywordByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => shortKeywordByte1
                (operatorPayloadToken! < 0 and astKind! == 24 and shortKeywordByte0 == UInt8(111) and shortKeywordByte1 == UInt8(114)) -> if {
                    -24 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            (operatorGroupDepth! == 0 and candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(3)) -> if {
                source -> byte(candidateToken.span.start) => longKeywordByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => longKeywordByte1
                source -> byte(candidateToken.span.start + UIntSize(2)) => longKeywordByte2
                (operatorPayloadToken! < 0 and astKind! == 25 and longKeywordByte0 == UInt8(97) and longKeywordByte1 == UInt8(110) and longKeywordByte2 == UInt8(100)) -> if {
                    -25 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
                (operatorPayloadToken! < 0 and astKind! == 22 and longKeywordByte0 == UInt8(110) and longKeywordByte1 == UInt8(111) and longKeywordByte2 == UInt8(116)) -> if {
                    -26 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
                (astKind! == 22 and longKeywordByte0 == UInt8(98) and longKeywordByte1 == UInt8(111) and longKeywordByte2 == UInt8(120)) -> if {
                    23 => astKind!
                    -27 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            (operatorGroupDepth! == 0 and astKind! == 46 and candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(5)) -> if {
                source -> byte(candidateToken.span.start) => guardBreakByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => guardBreakByte1
                source -> byte(candidateToken.span.start + UIntSize(2)) => guardBreakByte2
                source -> byte(candidateToken.span.start + UIntSize(3)) => guardBreakByte3
                source -> byte(candidateToken.span.start + UIntSize(4)) => guardBreakByte4
                (guardBreakByte0 == UInt8(98) and guardBreakByte1 == UInt8(114) and guardBreakByte2 == UInt8(101) and guardBreakByte3 == UInt8(97) and guardBreakByte4 == UInt8(107)) -> if {
                    0 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            (operatorGroupDepth! == 0 and astKind! == 46 and candidateOperator == grammar.tokenIdIdentifier and candidateToken.span.length == UIntSize(8)) -> if {
                source -> byte(candidateToken.span.start) => guardContinueByte0
                source -> byte(candidateToken.span.start + UIntSize(1)) => guardContinueByte1
                source -> byte(candidateToken.span.start + UIntSize(2)) => guardContinueByte2
                source -> byte(candidateToken.span.start + UIntSize(3)) => guardContinueByte3
                source -> byte(candidateToken.span.start + UIntSize(4)) => guardContinueByte4
                source -> byte(candidateToken.span.start + UIntSize(5)) => guardContinueByte5
                source -> byte(candidateToken.span.start + UIntSize(6)) => guardContinueByte6
                source -> byte(candidateToken.span.start + UIntSize(7)) => guardContinueByte7
                (guardContinueByte0 == UInt8(99) and guardContinueByte1 == UInt8(111) and guardContinueByte2 == UInt8(110) and guardContinueByte3 == UInt8(116) and guardContinueByte4 == UInt8(105) and guardContinueByte5 == UInt8(110) and guardContinueByte6 == UInt8(117) and guardContinueByte7 == UInt8(101)) -> if {
                    1 => operatorKind!
                    operatorTokenIndex! => operatorPayloadToken!
                }
            }
            (candidateOperator == grammar.tokenIdLeftParen or candidateOperator == grammar.tokenIdLeftBracket or candidateOperator == grammar.tokenIdLeftBrace) -> if {
                operatorGroupDepth! + 1 => operatorGroupDepth!
            }
            (candidateOperator == grammar.tokenIdRightParen or candidateOperator == grammar.tokenIdRightBracket or candidateOperator == grammar.tokenIdRightBrace) -> if {
                operatorGroupDepth! - 1 => operatorGroupDepth!
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
                tertiaryToken: -1
                flags: 0
                firstToken: astFirstToken!
                tokenCount: astTokenCount!
                start: astStart!
                length: astLength!
            } => astNode
            ast! -> len => astIndex
            ast! -> push(astNode)
            astIndex => cstToAst![cstIndex!]
            (astKind! == 41 and memberDotToken! >= 0) -> if {
                ast! -> len => indexedMemberNode
                ast! -> push(AstNode {
                    kind: 36
                    parent: astIndex
                    cstRuleId: node.ruleId
                    operatorKind: grammar.tokenIdDot
                    payloadToken: memberDotToken!
                    secondaryToken: -1
                    tertiaryToken: -1
                    flags: 0
                    firstToken: astFirstToken!
                    tokenCount: operatorPayloadToken! - astFirstToken!
                    start: astStart!
                    length: tokens![operatorPayloadToken!].span.start - astStart!
                })
                indexedMemberParents! -> push(astIndex)
                indexedMemberNodes! -> push(indexedMemberNode)
                indexedMemberBracketStarts! -> push(tokens![operatorPayloadToken!].span.start)
            }
            (astKind! == 36 and trailingMemberDotToken! >= 0 and indexBracketToken! >= 0) -> if {
                ast! -> len => trailingIndexNode
                ast! -> push(AstNode {
                    kind: 41
                    parent: astIndex
                    cstRuleId: node.ruleId
                    operatorKind: grammar.tokenIdLeftBracket
                    payloadToken: indexBracketToken!
                    secondaryToken: -1
                    tertiaryToken: -1
                    flags: 0
                    firstToken: astFirstToken!
                    tokenCount: trailingMemberDotToken! - astFirstToken!
                    start: astStart!
                    length: tokens![trailingMemberDotToken!].span.start - astStart!
                })
                indexedMemberParents! -> push(astIndex)
                indexedMemberNodes! -> push(trailingIndexNode)
                indexedMemberBracketStarts! -> push(tokens![trailingMemberDotToken!].span.start)
                memberDotToken! >= 0 -> if {
                    ast! -> len => trailingIndexedMemberNode
                    ast! -> push(AstNode {
                        kind: 36
                        parent: trailingIndexNode
                        cstRuleId: node.ruleId
                        operatorKind: grammar.tokenIdDot
                        payloadToken: memberDotToken!
                        secondaryToken: -1
                        tertiaryToken: -1
                        flags: 0
                        firstToken: astFirstToken!
                        tokenCount: indexBracketToken! - astFirstToken!
                        start: astStart!
                        length: tokens![indexBracketToken!].span.start - astStart!
                    })
                    indexedMemberParents! -> push(trailingIndexNode)
                    indexedMemberNodes! -> push(trailingIndexedMemberNode)
                    indexedMemberBracketStarts! -> push(tokens![indexBracketToken!].span.start)
                }
            }
        }
        cstIndex! + 1 => cstIndex!
    }

    0 => indexedMemberFixup!
    indexedMemberFixup! < (indexedMemberParents! -> len) -> while {
        indexedMemberParents![indexedMemberFixup!] => indexedMemberParent
        indexedMemberNodes![indexedMemberFixup!] => indexedMemberNode
        indexedMemberBracketStarts![indexedMemberFixup!] => indexedMemberBracketStart
        0 => indexedMemberChild!
        indexedMemberChild! < (ast! -> len) -> while {
            ast![indexedMemberChild!] => indexedMemberChildNode!
            (indexedMemberChild! != indexedMemberNode and indexedMemberChildNode!.parent == indexedMemberParent and indexedMemberChildNode!.start < indexedMemberBracketStart) -> if {
                indexedMemberNode => indexedMemberChildNode!.parent
                indexedMemberChildNode! => ast![indexedMemberChild!]
            }
            indexedMemberChild! + 1 => indexedMemberChild!
        }
        indexedMemberFixup! + 1 => indexedMemberFixup!
    }

    # The grammar keeps repeated infix operands flat. Normalize them into an
    # explicit left-associative binary tree so typed IR never drops the third
    # and later operands in expressions such as a or b or c.
    ast! -> len => infixAstCount
    0 => infixIndex!
    infixIndex! < infixAstCount -> while {
        ast![infixIndex!] => infixNode!
        (infixNode!.kind >= 18 and infixNode!.kind <= 25) -> if {
            [Int; ~] => infixChildren!
            0 => infixChildSearch!
            infixChildSearch! < infixAstCount -> while {
                ast![infixChildSearch!].parent == infixIndex! -> if { infixChildren! -> push(infixChildSearch!) }
                infixChildSearch! + 1 => infixChildSearch!
            }
            (infixChildren! -> len) > 2 -> if {
                infixChildren![0] => infixLeft!
                1 => infixChildIndex!
                infixChildIndex! < (infixChildren! -> len) - 1 -> while {
                    infixChildren![infixChildIndex!] => infixRight
                    ast![infixLeft!] => infixLeftNode!
                    ast![infixRight] => infixRightNode!
                    infixChildren![infixChildIndex! - 1] => infixPrevious
                    ast![infixPrevious] => infixPreviousNode
                    infixPreviousNode.firstToken + infixPreviousNode.tokenCount => infixOperatorSearch!
                    infixNode!.operatorKind => nestedInfixOperator!
                    infixNode!.payloadToken => nestedInfixPayload!
                    infixOperatorSearch! < infixRightNode!.firstToken -> while {
                        tokens![infixOperatorSearch!].kind => infixCandidateOperator
                        (infixCandidateOperator != grammar.triviaIdWhitespace and infixCandidateOperator != grammar.triviaIdComment) -> if {
                            infixCandidateOperator => nestedInfixOperator!
                            infixNode!.kind == 24 -> if { -24 => nestedInfixOperator! }
                            infixNode!.kind == 25 -> if { -25 => nestedInfixOperator! }
                            infixOperatorSearch! => nestedInfixPayload!
                        }
                        infixOperatorSearch! + 1 => infixOperatorSearch!
                    }
                    ast! -> len => nestedInfixIndex
                    nestedInfixIndex => infixLeftNode!.parent
                    nestedInfixIndex => infixRightNode!.parent
                    infixLeftNode! => ast![infixLeft!]
                    infixRightNode! => ast![infixRight]
                    ast! -> push(AstNode {
                        kind: infixNode!.kind
                        parent: infixIndex!
                        cstRuleId: infixNode!.cstRuleId
                        operatorKind: nestedInfixOperator!
                        payloadToken: nestedInfixPayload!
                        secondaryToken: infixNode!.secondaryToken
                        tertiaryToken: infixNode!.tertiaryToken
                        flags: infixNode!.flags
                        firstToken: infixLeftNode!.firstToken
                        tokenCount: infixRightNode!.firstToken + infixRightNode!.tokenCount - infixLeftNode!.firstToken
                        start: infixLeftNode!.start
                        length: infixRightNode!.start + infixRightNode!.length - infixLeftNode!.start
                    })
                    nestedInfixIndex => infixLeft!
                    infixChildIndex! + 1 => infixChildIndex!
                }
                (infixChildren! -> len) - 1 => rootRightChildIndex
                infixChildren![rootRightChildIndex] => rootRightChild
                infixChildren![rootRightChildIndex - 1] => rootPreviousChild
                ast![rootRightChild] => rootRightNode
                ast![rootPreviousChild] => rootPreviousNode
                rootPreviousNode.firstToken + rootPreviousNode.tokenCount => rootOperatorSearch!
                rootOperatorSearch! < rootRightNode.firstToken -> while {
                    tokens![rootOperatorSearch!].kind => rootCandidateOperator
                    (rootCandidateOperator != grammar.triviaIdWhitespace and rootCandidateOperator != grammar.triviaIdComment) -> if {
                        rootCandidateOperator => infixNode!.operatorKind
                        infixNode!.kind == 24 -> if { -24 => infixNode!.operatorKind }
                        infixNode!.kind == 25 -> if { -25 => infixNode!.operatorKind }
                        rootOperatorSearch! => infixNode!.payloadToken
                    }
                    rootOperatorSearch! + 1 => rootOperatorSearch!
                }
                infixNode! => ast![infixIndex!]
            }
        }
        infixIndex! + 1 => infixIndex!
    }

    # The grammar stores every target of a flow chain under one flat node.
    # Normalize a -> f -> g into nested flow nodes equivalent to
    # (a -> f) -> g so call resolution and typed IR preserve every target.
    0 => flowAstCount!
    flowAstCount! < (ast! -> len) -> while {
        flowAstCount! + 1 => flowAstCount!
    }
    0 => appendedFlowCount!
    0 => flowIndex!
    flowIndex! < flowAstCount! -> while {
        ast![flowIndex!] => flowNode!
        flowNode!.kind == 10 -> if {
            [Int; ~] => flowArrows!
            flowNode!.firstToken => flowTokenIndex!
            flowNode!.firstToken + flowNode!.tokenCount => flowTokenEnd
            0 => flowGroupDepth!
            flowTokenIndex! < flowTokenEnd -> while {
                tokens![flowTokenIndex!].kind => flowTokenKind
                (flowGroupDepth! == 0 and flowTokenKind == grammar.tokenIdArrow) -> if {
                    flowArrows! -> push(flowTokenIndex!)
                }
                (flowTokenKind == grammar.tokenIdLeftParen or flowTokenKind == grammar.tokenIdLeftBracket or flowTokenKind == grammar.tokenIdLeftBrace) -> if {
                    flowGroupDepth! + 1 => flowGroupDepth!
                }
                (flowTokenKind == grammar.tokenIdRightParen or flowTokenKind == grammar.tokenIdRightBracket or flowTokenKind == grammar.tokenIdRightBrace) -> if {
                    flowGroupDepth! - 1 => flowGroupDepth!
                }
                flowTokenIndex! + 1 => flowTokenIndex!
            }
            (flowArrows! -> len) > 1 -> if {
                flowAstCount! + appendedFlowCount! => nestedFlowStart
                0 => nestedFlowIndex!
                nestedFlowIndex! < (flowArrows! -> len) - 1 -> while {
                    flowArrows![nestedFlowIndex! + 1] => nestedFlowNextArrow
                    nestedFlowNextArrow => nestedFlowEndToken!
                    (nestedFlowEndToken! > flowNode!.firstToken and (tokens![nestedFlowEndToken! - 1].kind == grammar.triviaIdWhitespace or tokens![nestedFlowEndToken! - 1].kind == grammar.triviaIdComment)) -> while {
                        nestedFlowEndToken! - 1 => nestedFlowEndToken!
                    }
                    flowIndex! => nestedFlowParent!
                    nestedFlowIndex! + 1 < (flowArrows! -> len) - 1 -> if {
                        nestedFlowStart + nestedFlowIndex! + 1 => nestedFlowParent!
                    }
                    tokens![nestedFlowEndToken! - 1] => nestedFlowLastToken
                    ast! -> push(AstNode {
                        kind: 10
                        parent: nestedFlowParent!
                        cstRuleId: flowNode!.cstRuleId
                        operatorKind: flowNode!.operatorKind
                        payloadToken: flowNode!.payloadToken
                        secondaryToken: flowNode!.secondaryToken
                        tertiaryToken: flowNode!.tertiaryToken
                        flags: flowNode!.flags
                        firstToken: flowNode!.firstToken
                        tokenCount: nestedFlowEndToken! - flowNode!.firstToken
                        start: flowNode!.start
                        length: nestedFlowLastToken.span.start + nestedFlowLastToken.span.length - flowNode!.start
                    })
                    appendedFlowCount! + 1 => appendedFlowCount!
                    nestedFlowIndex! + 1 => nestedFlowIndex!
                }
                0 => flowChildIndex!
                flowChildIndex! < flowAstCount! -> while {
                    ast![flowChildIndex!] => flowChild!
                    flowChild!.parent == flowIndex! -> if {
                        0 => flowChildSegment!
                        0 => flowArrowSearch!
                        flowArrowSearch! < (flowArrows! -> len) -> while {
                            flowArrows![flowArrowSearch!] < flowChild!.firstToken -> if {
                                flowChildSegment! + 1 => flowChildSegment!
                            }
                            flowArrowSearch! + 1 => flowArrowSearch!
                        }
                        flowChildSegment! < (flowArrows! -> len) -> if {
                            0 => flowChildNestedOffset!
                            flowChildSegment! > 0 -> if { flowChildSegment! - 1 => flowChildNestedOffset! }
                            nestedFlowStart + flowChildNestedOffset! => flowChild!.parent
                            flowChild! => ast![flowChildIndex!]
                        }
                    }
                    flowChildIndex! + 1 => flowChildIndex!
                }
            }
        }
        flowIndex! + 1 => flowIndex!
    }

    # Resolve declaration payloads after every semantic parent/child index is
    # known. Path children provide qualified names; direct declarations scan
    # only their header token range.
    ast! -> len => astCount
    0 => declarationIndex!
    declarationIndex! < astCount -> while {
        ast![declarationIndex!] => declaration!
        declaration!.kind == 60 -> if {
            false => parameterNameFound!
            false => parameterAfterColon!
            declaration!.firstToken => parameterTokenIndex!
            declaration!.firstToken + declaration!.tokenCount => parameterTokenEnd
            parameterTokenIndex! < parameterTokenEnd -> while {
                tokens![parameterTokenIndex!].kind == grammar.tokenIdColon -> if {
                    true => parameterAfterColon!
                }
                tokens![parameterTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                    parameterNameFound! -> if {
                        parameterAfterColon! -> if {
                            tokens![parameterTokenIndex!] => parameterModifier
                            parameterModifier.span.length == UIntSize(4) -> if {
                                source -> byte(parameterModifier.span.start) => parameterMove0
                                source -> byte(parameterModifier.span.start + UIntSize(1)) => parameterMove1
                                source -> byte(parameterModifier.span.start + UIntSize(2)) => parameterMove2
                                source -> byte(parameterModifier.span.start + UIntSize(3)) => parameterMove3
                                (parameterMove0 == UInt8(109) and parameterMove1 == UInt8(111) and parameterMove2 == UInt8(118) and parameterMove3 == UInt8(101)) -> if { 1 => declaration!.flags }
                            }
                            parameterModifier.span.length == UIntSize(3) -> if {
                                source -> byte(parameterModifier.span.start) => parameterMut0
                                source -> byte(parameterModifier.span.start + UIntSize(1)) => parameterMut1
                                source -> byte(parameterModifier.span.start + UIntSize(2)) => parameterMut2
                                (parameterMut0 == UInt8(109) and parameterMut1 == UInt8(117) and parameterMut2 == UInt8(116)) -> if { 2 => declaration!.flags }
                            }
                        }
                    } else {
                        parameterTokenIndex! => declaration!.payloadToken
                        true => parameterNameFound!
                    }
                }
                parameterTokenIndex! + 1 => parameterTokenIndex!
            }
        }
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
        (declaration!.kind == 3 or declaration!.kind == 4 or declaration!.kind == 5 or declaration!.kind == 50) -> if {
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
        declaration!.kind == 51 -> if {
            -1 => operationNameToken!
            -1 => operationInputToken!
            declaration!.firstToken => operationToken!
            declaration!.firstToken + declaration!.tokenCount => operationEnd!
            operationToken! < operationEnd! -> while {
                tokens![operationToken!].kind == grammar.tokenIdColon -> if {
                    operationEnd! => operationToken!
                } else {
                    tokens![operationToken!].kind == grammar.tokenIdIdentifier -> if {
                        operationNameToken! < 0 -> if {
                            operationToken! => operationNameToken!
                        } else {
                            operationInputToken! < 0 -> if { operationToken! => operationInputToken! }
                        }
                    }
                    operationToken! + 1 => operationToken!
                }
            }
            operationNameToken! => declaration!.payloadToken
            operationInputToken! => declaration!.secondaryToken
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
                        signatureToken! + 1 => asyncToken!
                        (asyncToken! < signatureEnd and (tokens![asyncToken!].kind == grammar.triviaIdWhitespace or tokens![asyncToken!].kind == grammar.triviaIdComment)) -> while {
                            asyncToken! + 1 => asyncToken!
                        }
                        asyncToken! < signatureEnd -> if {
                            tokens![asyncToken!] => asyncName
                            (asyncName.kind == grammar.tokenIdIdentifier and asyncName.span.length == UIntSize(5)) -> if {
                                source -> byte(asyncName.span.start) => asyncByte0
                                source -> byte(asyncName.span.start + UIntSize(1)) => asyncByte1
                                source -> byte(asyncName.span.start + UIntSize(2)) => asyncByte2
                                source -> byte(asyncName.span.start + UIntSize(3)) => asyncByte3
                                source -> byte(asyncName.span.start + UIntSize(4)) => asyncByte4
                                (asyncByte0 == UInt8(97) and asyncByte1 == UInt8(115) and asyncByte2 == UInt8(121) and asyncByte3 == UInt8(110) and asyncByte4 == UInt8(99)) -> if {
                                    declaration!.flags + 8 => declaration!.flags
                                }
                            }
                        }
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
        (declaration!.kind == 7 or declaration!.kind == 31) -> if {
            declaration!.firstToken => blockHeaderToken!
            declaration!.firstToken + declaration!.tokenCount => blockHeaderEnd!
            false => blockKeywordFound!
            false => blockNameFound!
            false => blockSignatureArrowSeen!
            (blockHeaderToken! < blockHeaderEnd! and not blockNameFound!) -> while {
                tokens![blockHeaderToken!].kind == grammar.tokenIdLeftBrace -> if {
                    blockHeaderEnd! => blockHeaderToken!
                } else {
                    tokens![blockHeaderToken!].kind == grammar.tokenIdArrow -> if {
                        true => blockSignatureArrowSeen!
                    }
                    tokens![blockHeaderToken!].kind == grammar.tokenIdIdentifier -> if {
                        tokens![blockHeaderToken!] => blockHeaderIdentifier
                        (blockSignatureArrowSeen! and not blockKeywordFound!) -> if {
                            blockHeaderIdentifier.span.length == UIntSize(5) -> if {
                                source -> byte(blockHeaderIdentifier.span.start) => blockByte0
                                source -> byte(blockHeaderIdentifier.span.start + UIntSize(1)) => blockByte1
                                source -> byte(blockHeaderIdentifier.span.start + UIntSize(2)) => blockByte2
                                source -> byte(blockHeaderIdentifier.span.start + UIntSize(3)) => blockByte3
                                source -> byte(blockHeaderIdentifier.span.start + UIntSize(4)) => blockByte4
                                (blockByte0 == UInt8(98) and blockByte1 == UInt8(108) and blockByte2 == UInt8(111) and blockByte3 == UInt8(99) and blockByte4 == UInt8(107)) -> if {
                                    true => blockKeywordFound!
                                }
                            }
                        } else {
                            blockHeaderToken! => declaration!.tertiaryToken
                            true => blockNameFound!
                        }
                    }
                    blockHeaderToken! + 1 => blockHeaderToken!
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
            declaration!.payloadToken + 1 => bindingSuffixToken!
            (bindingSuffixToken! < bindingEnd! and (tokens![bindingSuffixToken!].kind == grammar.triviaIdWhitespace or tokens![bindingSuffixToken!].kind == grammar.triviaIdComment)) -> while {
                bindingSuffixToken! + 1 => bindingSuffixToken!
            }
            (bindingSuffixToken! < bindingEnd! and tokens![bindingSuffixToken!].kind == grammar.tokenIdBang) -> if {
                1 => declaration!.flags
            }
        }
        declaration!.kind == 48 -> if {
            declaration!.firstToken => roleToken!
            declaration!.firstToken + declaration!.tokenCount => roleEnd!
            false => afterRoleArrow!
            false => roleNameFound!
            false => afterRoleResultArrow!
            false => roleBodySeen!
            0 => roleBraceDepth!
            false => afterRoleDot!
            roleToken! < roleEnd! -> while {
                tokens![roleToken!].kind == grammar.tokenIdLeftBrace -> if {
                    true => roleBodySeen!
                    roleBraceDepth! + 1 => roleBraceDepth!
                }
                tokens![roleToken!].kind == grammar.tokenIdRightBrace -> if {
                    roleBraceDepth! - 1 => roleBraceDepth!
                }
                (not roleBodySeen! and tokens![roleToken!].kind == grammar.tokenIdArrow) -> if {
                    not roleNameFound! -> if { true => afterRoleArrow! }
                }
                (afterRoleArrow! and not roleBodySeen! and tokens![roleToken!].kind == grammar.tokenIdDot) -> if {
                    true => afterRoleDot!
                }
                (afterRoleArrow! and not roleBodySeen! and tokens![roleToken!].kind == grammar.tokenIdIdentifier) -> if {
                    not roleNameFound! -> if {
                        roleToken! => declaration!.payloadToken
                        true => roleNameFound!
                    } else {
                        afterRoleDot! -> if {
                            roleToken! => declaration!.payloadToken
                        } else {
                            declaration!.tertiaryToken < 0 -> if { roleToken! => declaration!.tertiaryToken }
                        }
                    }
                    false => afterRoleDot!
                }
                (roleNameFound! and roleBodySeen! and roleBraceDepth! == 0 and tokens![roleToken!].kind == grammar.tokenIdFatArrow) -> if {
                    true => afterRoleResultArrow!
                } else {
                    (afterRoleResultArrow! and tokens![roleToken!].kind == grammar.tokenIdIdentifier) -> if {
                        roleToken! => declaration!.secondaryToken
                        false => afterRoleResultArrow!
                    }
                }
                roleToken! + 1 => roleToken!
            }
            declaration!.secondaryToken >= 0 -> if {
                declaration!.secondaryToken + 1 => roleSuffixToken!
                (roleSuffixToken! < roleEnd! and (tokens![roleSuffixToken!].kind == grammar.triviaIdWhitespace or tokens![roleSuffixToken!].kind == grammar.triviaIdComment)) -> while {
                    roleSuffixToken! + 1 => roleSuffixToken!
                }
                (roleSuffixToken! < roleEnd! and tokens![roleSuffixToken!].kind == grammar.tokenIdBang) -> if {
                    1 => declaration!.flags
                }
            }
        }
        declaration!.kind == 53 -> if {
            declaration!.firstToken => assignmentToken!
            declaration!.firstToken + declaration!.tokenCount => assignmentEnd!
            false => afterAssignmentArrow!
            assignmentToken! < assignmentEnd! -> while {
                tokens![assignmentToken!].kind == grammar.tokenIdFatArrow -> if {
                    true => afterAssignmentArrow!
                } else {
                    (afterAssignmentArrow! and tokens![assignmentToken!].kind == grammar.tokenIdIdentifier) -> if {
                        assignmentToken! => declaration!.payloadToken
                        assignmentEnd! => assignmentToken!
                    }
                }
                assignmentToken! + 1 => assignmentToken!
            }
        }
        declaration!.kind == 54 -> if {
            -1 => declaration!.payloadToken
            -1 => declaration!.secondaryToken
            declaration!.firstToken => fieldAssignmentToken!
            declaration!.firstToken + declaration!.tokenCount => fieldAssignmentEnd!
            false => afterFieldAssignmentArrow!
            false => afterFieldAssignmentDot!
            fieldAssignmentToken! < fieldAssignmentEnd! -> while {
                tokens![fieldAssignmentToken!].kind == grammar.tokenIdFatArrow -> if {
                    true => afterFieldAssignmentArrow!
                } else {
                    (afterFieldAssignmentArrow! and declaration!.payloadToken < 0 and tokens![fieldAssignmentToken!].kind == grammar.tokenIdIdentifier) -> if {
                        fieldAssignmentToken! => declaration!.payloadToken
                        1 => declaration!.flags
                    }
                    (afterFieldAssignmentArrow! and tokens![fieldAssignmentToken!].kind == grammar.tokenIdDot) -> if {
                        true => afterFieldAssignmentDot!
                    } else {
                        (afterFieldAssignmentDot! and tokens![fieldAssignmentToken!].kind == grammar.tokenIdIdentifier) -> if {
                            fieldAssignmentToken! => declaration!.secondaryToken
                            fieldAssignmentEnd! => fieldAssignmentToken!
                        }
                    }
                }
                fieldAssignmentToken! + 1 => fieldAssignmentToken!
            }
        }
        declaration!.kind == 32 -> if {
            -1 => firstGenericToken!
            -1 => secondGenericToken!
            -1 => thirdGenericToken!
            0 => genericCommaCount!
            declaration!.firstToken => genericToken!
            genericToken! < declaration!.firstToken + declaration!.tokenCount -> while {
                tokens![genericToken!].kind == grammar.tokenIdComma -> if {
                    genericCommaCount! + 1 => genericCommaCount!
                } else {
                    tokens![genericToken!].kind == grammar.tokenIdIdentifier -> if {
                        (genericCommaCount! == 0 and firstGenericToken! < 0) -> if {
                            genericToken! => firstGenericToken!
                        }
                        (genericCommaCount! == 1 and secondGenericToken! < 0) -> if {
                            genericToken! => secondGenericToken!
                        }
                        (genericCommaCount! == 2 and thirdGenericToken! < 0) -> if {
                            genericToken! => thirdGenericToken!
                        }
                    }
                }
                genericToken! + 1 => genericToken!
            }
            firstGenericToken! => declaration!.payloadToken
            secondGenericToken! => declaration!.secondaryToken
            thirdGenericToken! => declaration!.tertiaryToken
        }
        ((declaration!.kind >= 3 and declaration!.kind <= 7) or declaration!.kind == 50) -> if {
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
        declaration!.kind == 55 -> if {
            -1 => constructorDot!
            declaration!.firstToken => constructorToken!
            declaration!.firstToken + declaration!.tokenCount => constructorEnd
            0 => constructorAngleDepth!
            constructorToken! < constructorEnd -> while {
                tokens![constructorToken!].kind == grammar.tokenIdLess -> if { constructorAngleDepth! + 1 => constructorAngleDepth! }
                tokens![constructorToken!].kind == grammar.tokenIdGreater -> if { constructorAngleDepth! - 1 => constructorAngleDepth! }
                (constructorAngleDepth! == 0 and tokens![constructorToken!].kind == grammar.tokenIdDot) -> if { constructorToken! => constructorDot! }
                constructorToken! + 1 => constructorToken!
            }
            constructorDot! >= 0 -> if {
                constructorDot! + 1 => constructorName!
                (constructorName! < constructorEnd and (tokens![constructorName!].kind == grammar.triviaIdWhitespace or tokens![constructorName!].kind == grammar.triviaIdComment)) -> while { constructorName! + 1 => constructorName! }
                constructorName! < constructorEnd -> if { constructorName! => declaration!.payloadToken }
            }
        }
        declaration!.kind == 59 -> if {
            -1 => patternVariant!
            -1 => patternBinding!
            false => patternInsidePayload!
            declaration!.firstToken => patternToken!
            declaration!.firstToken + declaration!.tokenCount => patternEnd
            patternToken! < patternEnd -> while {
                tokens![patternToken!].kind == grammar.tokenIdLeftParen -> if { true => patternInsidePayload! }
                tokens![patternToken!].kind == grammar.tokenIdRightParen -> if { false => patternInsidePayload! }
                tokens![patternToken!].kind == grammar.tokenIdIdentifier -> if {
                    patternInsidePayload! -> if {
                        patternBinding! < 0 -> if { patternToken! => patternBinding! }
                    } else {
                        patternToken! => patternVariant!
                    }
                }
                patternToken! + 1 => patternToken!
            }
            patternVariant! => declaration!.payloadToken
            patternBinding! => declaration!.secondaryToken
        }
        declaration! => ast![declarationIndex!]
        declarationIndex! + 1 => declarationIndex!
    }

    ast!
}

public lowerSource source: file.SourceText -> [AstNode; ~] {
    source -> len => sourceLength
    source -> slice(UIntSize(0), sourceLength) => view
    grammar.startRule => sourceStartRule
    LowerRequest {
        source: view
        startRule: sourceStartRule
    } -> lowerFrom
}

public lowerSourceExpression source: file.SourceText -> [AstNode; ~] {
    source -> len => sourceLength
    source -> slice(UIntSize(0), sourceLength) => view
    grammar.ruleIdExpression => expressionStartRule
    LowerRequest {
        source: view
        startRule: expressionStartRule
    } -> lowerFrom
}

public lower source: Text -> [AstNode; ~] {
    grammar.startRule => sourceStartRule
    LowerRequest {
        source: source
        startRule: sourceStartRule
    } -> lowerFrom
}

public lowerExpression source: Text -> [AstNode; ~] {
    grammar.ruleIdExpression => expressionStartRule
    LowerRequest {
        source: source
        startRule: expressionStartRule
    } -> lowerFrom
}
