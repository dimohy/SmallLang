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
    firstToken: Int
    tokenCount: Int
    start: UIntSize
    length: UIntSize
}

# Kinds: 0 source, 1 namespace, 2 import, 3 struct, 4 enum, 5 trait,
# 6 impl, 7 function, 8 main, 9 binding, 10 flow, 11 call, 12 type,
# 13 string, 14 number, 15 name, 16 path, 17 function signature,
# 18 equality, 19 comparison, 20 additive, 21 multiplicative, 22 unary,
# 23 box, 24 logical-or, 25 logical-and. Keyword operator codes use the same
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

    ast!
}
