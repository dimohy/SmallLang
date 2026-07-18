namespace smalllang.compiler.diagnostics

import smalllang.compiler.lexer as lexer
import smalllang.compiler.parser as parser
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

public struct SyntaxDiagnostic {
    code: Int
    span: syntax.SourceSpan
    foundKind: Int
    tokenIndex: Int
}

# Code 1 is an invalid source byte. Code 2 is an unexpected token at the
# furthest point reached by any parser alternative.
public analyze source: Text -> [SyntaxDiagnostic; ~] {
    source -> lexer.lex => tokens!
    source -> parser.parseEvents => events!
    [SyntaxDiagnostic; ~] => diagnostics!
    events! -> len => eventCount
    events![eventCount - 1] => outcome

    outcome.value == 0 -> if {
        source -> len => sourceLength
        syntax.SourceSpan { fileId: 0, start: sourceLength, length: UIntSize(0) } => failureSpan!
        grammar.tokenIdEnd => foundKind!
        outcome.tokenIndex < (tokens! -> len) -> if {
            tokens![outcome.tokenIndex] => found
            found.span.fileId => failureSpan!.fileId
            found.span.start => failureSpan!.start
            found.span.length => failureSpan!.length
            found.kind => foundKind!
        }
        foundKind! == grammar.tokenIdInvalid -> if { 1 } else { 2 } => code
        SyntaxDiagnostic {
            code: code
            span: failureSpan!
            foundKind: foundKind!
            tokenIndex: outcome.tokenIndex
        } => diagnostic
        diagnostics! -> push(diagnostic)
    }

    diagnostics!
}

# Non-negative values are token kinds. Negative values encode grammar keywords
# as -(keywordIndex + 1), keeping the representation compact and deterministic.
public expectedCodes source: Text -> [Int; ~] {
    source -> parser.parseEvents => events!
    [Int; ~] => expected!
    0 => eventIndex!
    eventIndex! < (events! -> len) -> while {
        events![eventIndex!] => event
        event.kind == 4 -> if {
            expected! -> push(event.value)
        }
        eventIndex! + 1 => eventIndex!
    }
    expected!
}
