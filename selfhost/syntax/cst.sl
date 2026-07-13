namespace smalllang.compiler.cst

import smalllang.compiler.lexer as lexer
import smalllang.compiler.parser as parser
import syntax.generated.smalllang as grammar

# Flat green nodes use stable array indexes instead of pointers. Parent links
# and token ranges make the tree traversable without per-node heap allocation.
public struct GreenNode {
    ruleId: Int
    parent: Int
    firstToken: Int
    tokenCount: Int
    start: UIntSize
    length: UIntSize
}

public struct BuildRequest {
    source: Text
    startRule: Int
}

public buildRule request: BuildRequest -> [GreenNode; ~] {
    request.source => source
    source -> lexer.lex => tokens!
    parser.ParseRequest {
        source: source
        startRule: request.startRule
    } -> parser.parseRuleEvents => events!
    [GreenNode; ~] => nodes!
    [Int; ~] => nodeStack!
    0 => stackDepth!

    events! -> each event {
        event.kind == 0 -> if {
            -1 => parent!
            stackDepth! > 0 -> if {
                nodeStack![stackDepth! - 1] => parent!
            }
            source -> len => nodeStart!
            event.tokenIndex < (tokens! -> len) -> if {
                tokens![event.tokenIndex].span.start => nodeStart!
            }
            GreenNode {
                ruleId: event.value
                parent: parent!
                firstToken: -1
                tokenCount: 0
                start: nodeStart!
                length: UIntSize(0)
            } => node
            nodes! -> len => nodeIndex
            nodes! -> push(node)
            nodeStack! -> len => stackCapacity
            stackDepth! < stackCapacity -> if {
                nodeIndex => nodeStack![stackDepth!]
            } else {
                nodeStack! -> push(nodeIndex)
            }
            stackDepth! + 1 => stackDepth!
        } else {
            event.kind == 1 -> if {
                stackDepth! - 1 => stackDepth!
            } else {
                event.kind == 2 -> if {
                    tokens![event.tokenIndex] => token
                    0 => ancestor!
                    ancestor! < stackDepth! -> while {
                        nodeStack![ancestor!] => nodeIndex
                        nodes![nodeIndex] => current!
                        current!.firstToken < 0 -> if {
                            event.tokenIndex => current!.firstToken
                        }
                        current!.tokenCount + 1 => current!.tokenCount
                        token.span.start + token.span.length - current!.start => current!.length
                        current! => nodes![nodeIndex]
                        ancestor! + 1 => ancestor!
                    }
                } else {
                    event.kind == 5 -> if {
                        event.value - event.tokenIndex => errorTokenCount
                        source -> len => errorStart!
                        UIntSize(0) => errorLength!
                        event.tokenIndex < (tokens! -> len) -> if {
                            tokens![event.tokenIndex].span.start => errorStart!
                        }
                        errorTokenCount > 0 -> if {
                            tokens![event.value - 1] => lastErrorToken
                            lastErrorToken.span.start + lastErrorToken.span.length - errorStart! => errorLength!
                        }
                        -1 => errorParent!
                        stackDepth! > 0 -> if {
                            nodeStack![stackDepth! - 1] => errorParent!
                        } else {
                            (nodes! -> len) > 0 -> if {
                                0 => errorParent!
                            }
                        }
                        GreenNode {
                            ruleId: -1
                            parent: errorParent!
                            firstToken: event.tokenIndex
                            tokenCount: errorTokenCount
                            start: errorStart!
                            length: errorLength!
                        } => errorNode
                        nodes! -> push(errorNode)

                        false => rangeAlreadyCounted!
                        errorTokenCount > 0 -> if {
                            tokens![event.tokenIndex].kind == grammar.tokenIdInvalid -> if {
                                true => rangeAlreadyCounted!
                            }
                        }
                        (not rangeAlreadyCounted! and errorParent! >= 0) -> if {
                            errorParent! => affectedNode!
                            affectedNode! >= 0 -> while {
                                affectedNode! => currentAffectedNode
                                nodes![affectedNode!] => affected!
                                affected!.tokenCount + errorTokenCount => affected!.tokenCount
                                errorStart! + errorLength! - affected!.start => affected!.length
                                affected!.parent => affectedNode!
                                affected! => nodes![currentAffectedNode]
                            }
                        }
                    }
                }
            }
        }
    }

    # The root is the lossless file envelope even when parser recovery had to
    # abandon nested rule events.
    (nodes! -> len) > 0 -> if {
        nodes![0] => root!
        0 => root!.firstToken
        tokens! -> len => root!.tokenCount
        UIntSize(0) => root!.start
        source -> len => root!.length
        root! => nodes![0]
    }

    nodes!
}

public build source: Text -> [GreenNode; ~] {
    BuildRequest {
        source: source
        startRule: grammar.startRule
    } -> buildRule
}

public buildExpression source: Text -> [GreenNode; ~] {
    BuildRequest {
        source: source
        startRule: grammar.ruleIdExpression
    } -> buildRule
}
