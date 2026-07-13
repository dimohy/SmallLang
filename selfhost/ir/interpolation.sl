namespace smalllang.compiler.ir.interpolation

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

# Relocatable interpolation IR. Segment offsets are relative to the first byte
# inside the source string token. Expression-node indexes are global offsets in
# one owned table, matching the rest of the self-hosted compiler IR.
# Kinds: 0 integer literal, 1 lexical name, 2 unary, 3 binary.
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
}

public lower source: Text -> [InterpolationNode; ~] {
    source -> lexer.lex => sourceTokens!
    source -> ast.lower => sourceAst!
    source -> symbols.collect => table!
    [InterpolationNode; ~] => nodes!
    0 => segmentIndex!

    0 => stringTokenIndex!
    stringTokenIndex! < (sourceTokens! -> len) -> while {
        sourceTokens![stringTokenIndex!] => stringToken
        stringToken.kind == grammar.tokenIdString -> if {
            stringToken.span.start + UIntSize(1) => contentStart
            stringToken.span.start + stringToken.span.length - UIntSize(1) => contentEnd
            contentStart => literalStart!
            contentStart => cursor!
            cursor! < contentEnd -> while {
                ((source -> byte(cursor!)) == UInt8(36) and cursor! + UIntSize(1) < contentEnd and (source -> byte(cursor! + UIntSize(1))) == UInt8(40)) -> if {
                    cursor! + UIntSize(2) => fragmentStart
                    fragmentStart => fragmentEnd!
                    1 => depth!
                    false => inString!
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
                    depth! == 0 -> if {
                        source -> slice(fragmentStart, fragmentEnd! - fragmentStart) => fragment
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
                            fragmentNode.kind == 15 -> if { 1 => loweredKind! }
                            fragmentNode.kind == 22 -> if { 2 => loweredKind! }
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
                                    0 => ownerAstSearch!
                                    ownerAstSearch! < (sourceAst! -> len) -> while {
                                        sourceAst![ownerAstSearch!] => ownerCandidateAst
                                        (ownerCandidateAst.kind == 7 and stringToken.span.start >= ownerCandidateAst.start and stringToken.span.start < ownerCandidateAst.start + ownerCandidateAst.length) -> if {
                                            0 => ownerSearch!
                                            ownerSearch! < (table! -> len) -> while {
                                                (table![ownerSearch!].kind == 7 and table![ownerSearch!].astNode == ownerAstSearch!) -> if {
                                                    ownerSearch! => ownerSymbol!
                                                }
                                                ownerSearch! + 1 => ownerSearch!
                                            }
                                        }
                                        ownerAstSearch! + 1 => ownerAstSearch!
                                    }
                                    ownerSymbol! => resolvedOwner!
                                    0 => candidateSymbol!
                                    candidateSymbol! < (table! -> len) -> while {
                                        table![candidateSymbol!] => candidate
                                        ((candidate.kind == 9 or candidate.kind == 35) and candidate.parent == ownerSymbol!) -> if {
                                            sourceTokens![candidate.nameToken] => candidateName
                                            candidateName.span.length == fragmentName.span.length => nameEqual!
                                            UIntSize(0) => nameByte!
                                            (nameEqual! and nameByte! < fragmentName.span.length) -> while {
                                                (fragment -> byte(fragmentName.span.start + nameByte!)) != (source -> byte(candidateName.span.start + nameByte!)) -> if { false => nameEqual! }
                                                nameByte! + UIntSize(1) => nameByte!
                                            }
                                            nameEqual! -> if { candidateSymbol! => resolvedSymbol! }
                                        }
                                        candidateSymbol! + 1 => candidateSymbol!
                                    }
                                }
                                InterpolationNode {
                                    kind: loweredKind!
                                    segment: segmentIndex!
                                    parent: loweredParent!
                                    symbol: resolvedSymbol!
                                    ownerSymbol: resolvedOwner!
                                    opcode: fragmentNode.operatorKind
                                    payloadStart: fragmentStart + fragmentNode.start
                                    payloadLength: fragmentNode.length
                                    operand0: -1
                                    operand1: -1
                                    sourceToken: stringTokenIndex!
                                    literalStart: literalStart! - contentStart
                                    literalLength: cursor! - literalStart!
                                    expressionStart: fragmentStart - contentStart
                                    expressionLength: fragmentEnd! - fragmentStart
                                } => lowered
                                nodes! -> len => loweredIndex
                                nodes! -> push(lowered)
                                loweredIndex => astToNode![fragmentAstIndex!]
                            }
                            fragmentAstIndex! + 1 => fragmentAstIndex!
                        }
                        firstNode => nodeIndex!
                        nodeIndex! < (nodes! -> len) -> while {
                            nodes![nodeIndex!] => node!
                            firstNode => childIndex!
                            childIndex! < (nodes! -> len) -> while {
                                nodes![childIndex!].parent == nodeIndex! -> if {
                                    node!.operand0 < 0 -> if { childIndex! => node!.operand0 } else { childIndex! => node!.operand1 }
                                }
                                childIndex! + 1 => childIndex!
                            }
                            node! => nodes![nodeIndex!]
                            nodeIndex! + 1 => nodeIndex!
                        }
                        segmentIndex! + 1 => segmentIndex!
                        fragmentEnd! + UIntSize(1) => literalStart!
                        fragmentEnd! => cursor!
                    }
                }
                cursor! + UIntSize(1) => cursor!
            }
        }
        stringTokenIndex! + 1 => stringTokenIndex!
    }

    nodes!
}
