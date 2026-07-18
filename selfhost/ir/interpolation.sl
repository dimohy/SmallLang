namespace smalllang.compiler.ir.interpolation

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

# Relocatable interpolation IR. Segment offsets are relative to the first byte
# inside the source string token. Expression-node indexes are global offsets in
# one owned table, matching the rest of the self-hosted compiler IR.
# Kinds: 0 integer literal, 1 lexical name, 2 unary, 3 binary,
# 4 boolean literal, 5 member access. Builtin result ids match the nominal
# table. Member nodes retain their absolute source span and point at the
# lexical base through operand0 so code generation can project the field.
public struct InterpolationNode {
    kind: Int
    segment: Int
    parent: Int
    symbol: Int
    ownerSymbol: Int
    opcode: Int
    payloadStart: UIntSize
    payloadLength: UIntSize
    operand0: Int
    operand1: Int
    sourceToken: Int
    literalStart: UIntSize
    literalLength: UIntSize
    expressionStart: UIntSize
    expressionLength: UIntSize
    typeSymbol: Int
}

public struct InterpolationSourceRange {
    sourceModule: Int
    nodeStart: Int
    nodeCount: Int
}

public struct PreparedInterpolationRequest {
    source: Text
    nodes: [ast.AstNode; ~]
    tokens: [syntax.SyntaxToken; ~]
    symbols: [symbols.Symbol; ~]
}

public lowerPrepared request: move PreparedInterpolationRequest -> [InterpolationNode; ~] {
    isIdentifierStart value: UInt8 -> Bool {
        (value >= UInt8(65) and value <= UInt8(90)) or (value >= UInt8(97) and value <= UInt8(122)) or value == UInt8(95) or value >= UInt8(128)
    }
    isIdentifierContinue value: UInt8 -> Bool {
        (value -> isIdentifierStart) or (value >= UInt8(48) and value <= UInt8(57))
    }
    [InterpolationNode; ~] => nodes!
    request.source => source
    0 => segmentIndex!

    0 => stringTokenIndex!
    stringTokenIndex! < (request.tokens -> len) -> while {
        request.tokens[stringTokenIndex!] => stringToken
        stringToken.kind == grammar.tokenIdString -> if {
            stringToken.span.start + UIntSize(1) => contentStart
            stringToken.span.start + stringToken.span.length - UIntSize(1) => contentEnd
            contentStart => literalStart!
            contentStart => cursor!
            cursor! < contentEnd -> while {
                false => interpolationStart!
                (source -> byte(cursor!)) == UInt8(36) -> if {
                    cursor! + UIntSize(1) < contentEnd -> if {
                        source -> byte(cursor! + UIntSize(1)) => interpolationNextByte
                        interpolationNextByte == UInt8(40) -> if { true => interpolationStart! } else {
                            interpolationNextByte -> isIdentifierStart => interpolationStart!
                        }
                    }
                }
                interpolationStart! -> if {
                    cursor! + UIntSize(1) => fragmentStart!
                    fragmentStart! => fragmentEnd!
                    0 => depth!
                    false => inString!
                    false => parenthesizedInterpolation!
                    (source -> byte(cursor! + UIntSize(1))) == UInt8(40) -> if {
                        true => parenthesizedInterpolation!
                        cursor! + UIntSize(2) => fragmentStart!
                        fragmentStart! => fragmentEnd!
                        1 => depth!
                        (fragmentEnd! < contentEnd and depth! > 0) -> while {
                            source -> byte(fragmentEnd!) => fragmentByte
                            fragmentByte == UInt8(34) -> if {
                                not inString! => inString!
                            } else {
                                not inString! -> if {
                                    fragmentByte == UInt8(40) -> if { depth! + 1 => depth! }
                                    fragmentByte == UInt8(41) -> if { depth! - 1 => depth! }
                                }
                            }
                            depth! > 0 -> if { fragmentEnd! + UIntSize(1) => fragmentEnd! }
                        }
                    } else {
                        fragmentStart! => fragmentEnd!
                        (fragmentEnd! < contentEnd and ((source -> byte(fragmentEnd!)) -> isIdentifierContinue)) -> while { fragmentEnd! + UIntSize(1) => fragmentEnd! }
                    }
                    depth! == 0 -> if {
                        source -> slice(fragmentStart!, fragmentEnd! - fragmentStart!) => fragment
                        fragment -> ast.lowerExpression => fragmentAst!
                        fragment -> lexer.lex => fragmentTokens!
                        [Int; ~] => astToNode!
                        0 => astMapIndex!
                        astMapIndex! < (fragmentAst! -> len) -> while {
                            astToNode! -> push(-1)
                            astMapIndex! + 1 => astMapIndex!
                        }
                        nodes! -> len => firstNode
                        0 => fragmentAstIndex!
                        fragmentAstIndex! < (fragmentAst! -> len) -> while {
                            fragmentAst![fragmentAstIndex!] => fragmentNode
                            0 => fragmentChildCount!
                            0 => fragmentChildSearch!
                            fragmentChildSearch! < (fragmentAst! -> len) -> while {
                                fragmentAst![fragmentChildSearch!].parent == fragmentAstIndex! -> if { fragmentChildCount! + 1 => fragmentChildCount! }
                                fragmentChildSearch! + 1 => fragmentChildSearch!
                            }
                            -1 => loweredKind!
                            fragmentNode.kind == 14 -> if { 0 => loweredKind! }
                            false => fragmentBooleanLiteral!
                            fragmentNode.kind == 15 -> if {
                                fragmentTokens![fragmentNode.payloadToken] => fragmentNameToken
                                fragmentNameToken.span.length == UIntSize(4) -> if {
                                    ((fragment -> byte(fragmentNameToken.span.start)) == UInt8(116) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(1))) == UInt8(114) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(2))) == UInt8(117) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(3))) == UInt8(101)) -> if { true => fragmentBooleanLiteral! }
                                }
                                fragmentNameToken.span.length == UIntSize(5) -> if {
                                    ((fragment -> byte(fragmentNameToken.span.start)) == UInt8(102) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(1))) == UInt8(97) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(2))) == UInt8(108) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(3))) == UInt8(115) and (fragment -> byte(fragmentNameToken.span.start + UIntSize(4))) == UInt8(101)) -> if { true => fragmentBooleanLiteral! }
                                }
                                fragmentBooleanLiteral! -> if { 4 => loweredKind! } else { 1 => loweredKind! }
                            }
                            fragmentNode.kind == 22 -> if { 2 => loweredKind! }
                            fragmentNode.kind == 36 -> if { 5 => loweredKind! }
                            (fragmentChildCount! >= 2 and ((fragmentNode.kind >= 18 and fragmentNode.kind <= 21) or fragmentNode.kind == 24 or fragmentNode.kind == 25)) -> if { 3 => loweredKind! }
                            loweredKind! >= 0 -> if {
                                -1 => loweredParent!
                                fragmentNode.parent => parentAst!
                                (parentAst! >= 0 and loweredParent! < 0) -> while {
                                    astToNode![parentAst!] => mappedParent
                                    mappedParent >= 0 -> if { mappedParent => loweredParent! } else { fragmentAst![parentAst!].parent => parentAst! }
                                }
                                -1 => resolvedSymbol!
                                -1 => resolvedOwner!
                                loweredKind! == 1 -> if {
                                    fragmentTokens![fragmentNode.payloadToken] => fragmentName
                                    -1 => ownerSymbol!
                                    -1 => referenceAst!
                                    0 => referenceAstSearch!
                                    referenceAstSearch! < (request.nodes -> len) -> while {
                                        request.nodes[referenceAstSearch!] => referenceCandidateAst
                                        (referenceCandidateAst.start <= stringToken.span.start and stringToken.span.start + stringToken.span.length <= referenceCandidateAst.start + referenceCandidateAst.length) -> if {
                                            (referenceAst! < 0 or referenceCandidateAst.length <= request.nodes[referenceAst!].length) -> if {
                                                referenceAstSearch! => referenceAst!
                                            }
                                        }
                                        referenceAstSearch! + 1 => referenceAstSearch!
                                    }
                                    0 => ownerAstSearch!
                                    ownerAstSearch! < (request.nodes -> len) -> while {
                                        request.nodes[ownerAstSearch!] => ownerCandidateAst
                                        (ownerCandidateAst.kind == 7 and stringToken.span.start >= ownerCandidateAst.start and stringToken.span.start < ownerCandidateAst.start + ownerCandidateAst.length) -> if {
                                            0 => ownerSearch!
                                            ownerSearch! < (request.symbols -> len) -> while {
                                                (request.symbols[ownerSearch!].kind == 7 and request.symbols[ownerSearch!].astNode == ownerAstSearch!) -> if {
                                                    ownerSearch! => ownerSymbol!
                                                }
                                                ownerSearch! + 1 => ownerSearch!
                                            }
                                        }
                                        ownerAstSearch! + 1 => ownerAstSearch!
                                    }
                                    ownerSymbol! => resolvedOwner!
                                    ownerSymbol! => searchScope!
                                    false => scopesDone!
                                    (resolvedSymbol! < 0 and not scopesDone!) -> while {
                                        -1 => nearestSymbol!
                                        UIntSize(0) => nearestStart!
                                        0 => candidateSymbol!
                                        candidateSymbol! < (request.symbols -> len) -> while {
                                            request.symbols[candidateSymbol!] => candidate
                                            ((candidate.kind == 9 or candidate.kind == 35) and candidate.parent == searchScope!) -> if {
                                                request.tokens[candidate.nameToken] => candidateName
                                                candidateName.span.length => candidateNameLength
                                                fragmentName.span.length => fragmentNameLength
                                                candidateNameLength == fragmentNameLength => nameEqual!
                                                UIntSize(0) => nameByte!
                                                (nameEqual! and nameByte! < fragmentNameLength) -> while {
                                                    (fragment -> byte(fragmentName.span.start + nameByte!)) != (source -> byte(candidateName.span.start + nameByte!)) -> if { false => nameEqual! }
                                                    nameByte! + UIntSize(1) => nameByte!
                                                }
                                                nameEqual! -> if {
                                                    request.nodes[candidate.astNode].start => candidateStart
                                                    candidate.kind == 35 => candidateVisible!
                                                    candidate.kind == 9 -> if {
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
                                                        referenceAst! => referenceBlockSearch!
                                                        referenceBlockSearch! >= 0 -> while {
                                                            referenceBlockSearch! == candidateBlockAst! -> if { true => candidateVisible! }
                                                            request.nodes[referenceBlockSearch!].parent => referenceBlockSearch!
                                                        }
                                                        (searchScope! == ownerSymbol! and candidateStart >= stringToken.span.start) -> if { false => candidateVisible! }
                                                    }
                                                    candidateVisible! -> if {
                                                        (nearestSymbol! < 0 or candidateStart >= nearestStart!) -> if {
                                                            candidateSymbol! => nearestSymbol!
                                                            candidateStart => nearestStart!
                                                        }
                                                    }
                                                }
                                            }
                                            candidateSymbol! + 1 => candidateSymbol!
                                        }
                                        nearestSymbol! >= 0 -> if { nearestSymbol! => resolvedSymbol! }
                                        resolvedSymbol! < 0 -> if {
                                            searchScope! >= 0 -> if {
                                                request.symbols[searchScope!].parent => searchScope!
                                            } else {
                                                true => scopesDone!
                                            }
                                        }
                                    }
                                }
                                -1 => loweredTypeSymbol!
                                loweredKind! == 0 -> if { 2 => loweredTypeSymbol! }
                                loweredKind! == 4 -> if { 23 => loweredTypeSymbol! }
                                loweredKind! == 2 -> if {
                                    fragmentNode.operatorKind == -26 -> if { 23 => loweredTypeSymbol! } else { 2 => loweredTypeSymbol! }
                                }
                                loweredKind! == 3 -> if {
                                    (fragmentNode.operatorKind == grammar.tokenIdEqualEqual or fragmentNode.operatorKind == grammar.tokenIdBangEqual or fragmentNode.operatorKind == grammar.tokenIdLess or fragmentNode.operatorKind == grammar.tokenIdLessEqual or fragmentNode.operatorKind == grammar.tokenIdGreater or fragmentNode.operatorKind == grammar.tokenIdGreaterEqual or fragmentNode.operatorKind == -24 or fragmentNode.operatorKind == -25) -> if { 23 => loweredTypeSymbol! } else { 2 => loweredTypeSymbol! }
                                }
                                fragmentStart! - contentStart => loweredExpressionStart!
                                not parenthesizedInterpolation! -> if { cursor! - contentStart => loweredExpressionStart! }
                                InterpolationNode {
                                    kind: loweredKind!
                                    segment: segmentIndex!
                                    parent: loweredParent!
                                    symbol: resolvedSymbol!
                                    ownerSymbol: resolvedOwner!
                                    opcode: fragmentNode.operatorKind
                                    payloadStart: fragmentStart! + fragmentNode.start
                                    payloadLength: fragmentNode.length
                                    operand0: -1
                                    operand1: -1
                                    sourceToken: stringTokenIndex!
                                    literalStart: literalStart! - contentStart
                                    literalLength: cursor! - literalStart!
                                    expressionStart: loweredExpressionStart!
                                    expressionLength: fragmentEnd! - fragmentStart!
                                    typeSymbol: loweredTypeSymbol!
                                } => lowered
                                nodes! -> len => loweredIndex
                                nodes! -> push(lowered)
                                loweredIndex => astToNode![fragmentAstIndex!]
                            }
                            fragmentAstIndex! + 1 => fragmentAstIndex!
                        }
                        segmentIndex! + 1 => segmentIndex!
                        fragmentEnd! => interpolationEnd!
                        parenthesizedInterpolation! -> if { fragmentEnd! + UIntSize(1) => interpolationEnd! }
                        interpolationEnd! => literalStart!
                        interpolationEnd! - UIntSize(1) => cursor!
                    }
                }
                cursor! + UIntSize(1) => cursor!
            }
        }
        stringTokenIndex! + 1 => stringTokenIndex!
    }

    [InterpolationNode; ~] => wiredNodes!
    0 => nodeIndex!
    nodeIndex! < (nodes! -> len) -> while {
        nodes![nodeIndex!] => node
        node.operand0 => operand0!
        node.operand1 => operand1!
        0 => childIndex!
        childIndex! < (nodes! -> len) -> while {
            nodes![childIndex!].parent == nodeIndex! -> if {
                operand0! < 0 => firstOperand
                firstOperand -> if { childIndex! => operand0! }
                not firstOperand -> if { childIndex! => operand1! }
            }
            childIndex! + 1 => childIndex!
        }
        wiredNodes! -> push(InterpolationNode {
            kind: node.kind
            segment: node.segment
            parent: node.parent
            symbol: node.symbol
            ownerSymbol: node.ownerSymbol
            opcode: node.opcode
            payloadStart: node.payloadStart
            payloadLength: node.payloadLength
            operand0: operand0!
            operand1: operand1!
            sourceToken: node.sourceToken
            literalStart: node.literalStart
            literalLength: node.literalLength
            expressionStart: node.expressionStart
            expressionLength: node.expressionLength
            typeSymbol: node.typeSymbol
        })
        nodeIndex! + 1 => nodeIndex!
    }
    wiredNodes!
}

public lower source: Text -> [InterpolationNode; ~] {
    source -> lexer.lex => sourceTokens!
    source -> ast.lower => sourceAst!
    sourceAst! -> symbols.collectPrepared => table!
    PreparedInterpolationRequest {
        source: source
        nodes: sourceAst!
        tokens: sourceTokens!
        symbols: table!
    } => request!
    request! -> lowerPrepared
}
