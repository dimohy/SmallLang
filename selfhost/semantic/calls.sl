namespace smalllang.compiler.semantic.calls

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

public struct CallResolution {
    callAst: Int
    functionSymbol: Int
    status: Int
}

# Status 0 is a resolved local function and 2 is an unresolved call target.
public resolve source: Text -> [CallResolution; ~] {
    source -> ast.lower => nodes!
    source -> lexer.lex => tokens!
    source -> symbols.collect => table!
    [CallResolution; ~] => resolved!
    0 => astIndex!
    astIndex! < (nodes! -> len) -> while {
        nodes![astIndex!] => node
        node.kind == 11 -> if {
            -1 => callNameToken!
            node.firstToken => tokenIndex!
            (tokenIndex! < node.firstToken + node.tokenCount and callNameToken! < 0) -> while {
                tokens![tokenIndex!].kind == grammar.tokenIdIdentifier -> if { tokenIndex! => callNameToken! }
                tokenIndex! + 1 => tokenIndex!
            }
            -1 => functionSymbol!
            0 => symbolIndex!
            (symbolIndex! < (table! -> len) and functionSymbol! < 0) -> while {
                table![symbolIndex!] => candidate
                (candidate.kind == 7 and candidate.parent < 0) -> if {
                    tokens![callNameToken!] => callName
                    tokens![candidate.nameToken] => functionName
                    callName.span.length == functionName.span.length => equal!
                    UIntSize(0) => nameByte!
                    (equal! and nameByte! < callName.span.length) -> while {
                        source -> byte(callName.span.start + nameByte!) => callByte
                        source -> byte(functionName.span.start + nameByte!) => functionByte
                        callByte != functionByte -> if { false => equal! }
                        nameByte! + UIntSize(1) => nameByte!
                    }
                    equal! -> if { symbolIndex! => functionSymbol! }
                }
                symbolIndex! + 1 => symbolIndex!
            }
            resolved! -> push(CallResolution {
                callAst: astIndex!
                functionSymbol: functionSymbol!
                status: functionSymbol! >= 0 -> if { 0 } else { 2 }
            })
        }
        astIndex! + 1 => astIndex!
    }
    resolved!
}
