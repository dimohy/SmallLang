namespace smalllang.compiler.semantic.diagnostics

import smalllang.compiler.lexer as lexer
import smalllang.compiler.ast as ast
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.syntax as syntax

public struct SemanticDiagnostic {
    code: Int
    symbol: Int
    previousSymbol: Int
    span: syntax.SourceSpan
}

# Code 1 identifies a duplicate declaration in the same lexical owner.
# Code 2 identifies an unresolved name expression.
public analyze source: Text -> [SemanticDiagnostic; ~] {
    source -> symbols.collect => table!
    source -> lexer.lex => tokens!
    [SemanticDiagnostic; ~] => diagnostics!
    table! -> len => symbolCount
    0 => symbolIndex!

    symbolIndex! < symbolCount -> while {
        table![symbolIndex!] => current
        -1 => duplicateOf!
        0 => candidateIndex!
        (candidateIndex! < symbolIndex! and duplicateOf! < 0) -> while {
            table![candidateIndex!] => candidate
            current.parent == candidate.parent -> if {
                tokens![current.nameToken] => currentName
                tokens![candidate.nameToken] => candidateName
                currentName.span.length == candidateName.span.length => namesEqual!
                UIntSize(0) => nameByte!
                (namesEqual! and nameByte! < currentName.span.length) -> while {
                    source -> byte(currentName.span.start + nameByte!) => currentByte
                    source -> byte(candidateName.span.start + nameByte!) => candidateByte
                    currentByte != candidateByte -> if {
                        false => namesEqual!
                    }
                    nameByte! + UIntSize(1) => nameByte!
                }
                namesEqual! -> if {
                    not (current.kind == 9 and candidate.kind == 9 and current.flags == 1 and candidate.flags == 1) -> if {
                        candidateIndex! => duplicateOf!
                    }
                }
            }
            candidateIndex! + 1 => candidateIndex!
        }
        duplicateOf! >= 0 -> if {
            tokens![current.nameToken] => duplicateName
            SemanticDiagnostic {
                code: 1
                symbol: symbolIndex!
                previousSymbol: duplicateOf!
                span: duplicateName.span
            } => diagnostic
            diagnostics! -> push(diagnostic)
        }
        symbolIndex! + 1 => symbolIndex!
    }

    source -> ast.lower => nodes!
    source -> resolution.resolve => resolved!
    nodes! -> len => astCount
    resolved! -> len => resolvedCount
    0 => diagnosticAstIndex!
    diagnosticAstIndex! < astCount -> while {
        nodes![diagnosticAstIndex!] => nameAst
        nameAst.kind == 15 -> if {
            false => nameResolved!
            0 => resolutionIndex!
            resolutionIndex! < resolvedCount -> while {
                resolved![resolutionIndex!].astNode == diagnosticAstIndex! -> if {
                    true => nameResolved!
                }
                resolutionIndex! + 1 => resolutionIndex!
            }
            not nameResolved! -> if {
                tokens![nameAst.payloadToken] => unresolvedName
                false => booleanLiteral!
                unresolvedName.span.length == UIntSize(4) -> if {
                    source -> byte(unresolvedName.span.start) => boolByte0
                    source -> byte(unresolvedName.span.start + UIntSize(1)) => boolByte1
                    source -> byte(unresolvedName.span.start + UIntSize(2)) => boolByte2
                    source -> byte(unresolvedName.span.start + UIntSize(3)) => boolByte3
                    (boolByte0 == UInt8(116) and boolByte1 == UInt8(114) and boolByte2 == UInt8(117) and boolByte3 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                unresolvedName.span.length == UIntSize(5) -> if {
                    source -> byte(unresolvedName.span.start) => falseByte0
                    source -> byte(unresolvedName.span.start + UIntSize(1)) => falseByte1
                    source -> byte(unresolvedName.span.start + UIntSize(2)) => falseByte2
                    source -> byte(unresolvedName.span.start + UIntSize(3)) => falseByte3
                    source -> byte(unresolvedName.span.start + UIntSize(4)) => falseByte4
                    (falseByte0 == UInt8(102) and falseByte1 == UInt8(97) and falseByte2 == UInt8(108) and falseByte3 == UInt8(115) and falseByte4 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                not booleanLiteral! -> if {
                    SemanticDiagnostic {
                    code: 2
                    symbol: -1
                    previousSymbol: -1
                    span: unresolvedName.span
                    } => unresolvedDiagnostic
                    diagnostics! -> push(unresolvedDiagnostic)
                }
            }
        }
        diagnosticAstIndex! + 1 => diagnosticAstIndex!
    }

    diagnostics!
}
