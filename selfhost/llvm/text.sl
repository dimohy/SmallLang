namespace smalllang.compiler.llvm.text

import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer as lexer
import syntax.generated.smalllang as grammar

# First LLVM text backend slice. Names are derived only from stable module and
# symbol indexes; SSA registers are derived from typed-IR indexes.
public emit sources: [Text; ~] -> Unit {
    llvmType symbol: Int -> Text => when {
        symbol == 2 => "i32"
        symbol == 23 => "i1"
        else => "void"
    }
    sources -> typedIr.lower => ir!
    0 => functionIndex!
    functionIndex! < (ir! -> len) -> while {
        ir![functionIndex!] => function
        function.kind == 0 -> if {
            functionIndex! + 1 => functionEnd!
            (functionEnd! < (ir! -> len) and ir![functionEnd!].kind != 0 and ir![functionEnd!].kind != 11) -> while {
                functionEnd! + 1 => functionEnd!
            }
            function.typeSymbol -> llvmType => returnType
            function.operand1 >= 0 -> if {
                ir![function.operand1] => parameter
                parameter.typeSymbol -> llvmType => parameterType
                "define $returnType @sl_m$(function.sourceModule)_s$(function.symbol)($parameterType %arg) {" -> println
            } else {
                "define $returnType @sl_m$(function.sourceModule)_s$(function.symbol)() {" -> println
            }
            "entry:" -> println
            functionEnd! - 1 => expressionIndex!
            function.operand0 + 1 => expressionStart
            expressionIndex! >= expressionStart -> while {
                ir![expressionIndex!] => expression
                (expression.kind == 7 or expression.kind == 8) -> if {
                    ir![expression.operand0] => leftOperand
                    leftOperand.typeSymbol -> llvmType => operandType
                    "" => operation!
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if {
                            "xor" => operation!
                        } else {
                            "sub" => operation!
                        }
                    } else {
                        ir![expression.operand1] => rightOperand
                        expression.opcode == grammar.tokenIdPlus -> if { "add" => operation! }
                        expression.opcode == grammar.tokenIdMinus -> if { "sub" => operation! }
                        expression.opcode == grammar.tokenIdStar -> if { "mul" => operation! }
                        expression.opcode == grammar.tokenIdSlash -> if { "sdiv" => operation! }
                        expression.opcode == grammar.tokenIdPercent -> if { "srem" => operation! }
                        expression.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" => operation! }
                        expression.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" => operation! }
                        expression.opcode == grammar.tokenIdLess -> if { "icmp slt" => operation! }
                        expression.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" => operation! }
                        expression.opcode == grammar.tokenIdGreater -> if { "icmp sgt" => operation! }
                        expression.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" => operation! }
                        expression.opcode == -24 -> if { "or" => operation! }
                        expression.opcode == -25 -> if { "and" => operation! }
                    }
                    "  %v$(expressionIndex!) = $(operation!) $operandType " -> print
                    (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                        sources[leftOperand.sourceModule] -> lexer.lex => leftTokens!
                        leftTokens![leftOperand.payloadToken] => leftToken
                        leftOperand.kind == 3 -> if {
                            sources[leftOperand.sourceModule] -> slice(leftToken.span.start, leftToken.span.length) -> print
                        } else {
                            ((sources[leftOperand.sourceModule] -> byte(leftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else {
                        leftOperand.kind == 5 -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    }
                    ", " -> print
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if { "true" -> println } else { "0" -> println }
                    } else {
                        ir![expression.operand1] => rightOperand
                        (rightOperand.kind == 3 or rightOperand.kind == 4) -> if {
                            sources[rightOperand.sourceModule] -> lexer.lex => rightTokens!
                            rightTokens![rightOperand.payloadToken] => rightToken
                            rightOperand.kind == 3 -> if {
                                sources[rightOperand.sourceModule] -> slice(rightToken.span.start, rightToken.span.length) -> println
                            } else {
                                ((sources[rightOperand.sourceModule] -> byte(rightToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                            }
                        } else {
                            rightOperand.kind == 5 -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                        }
                    }
                }
                expression.kind == 6 -> if {
                    expression.typeSymbol -> llvmType => callType
                    "  %v$(expressionIndex!) = call $callType @sl_m$(expression.targetModule)_s$(expression.symbol)(" -> print
                    expression.operand0 >= 0 -> if {
                        ir![expression.operand0] => argument
                        argument.typeSymbol -> llvmType => argumentType
                        "$argumentType " -> print
                        (argument.kind == 3 or argument.kind == 4) -> if {
                            sources[argument.sourceModule] -> lexer.lex => argumentTokens!
                            argumentTokens![argument.payloadToken] => argumentToken
                            argument.kind == 3 -> if {
                                sources[argument.sourceModule] -> slice(argumentToken.span.start, argumentToken.span.length) -> print
                            } else {
                                ((sources[argument.sourceModule] -> byte(argumentToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            argument.kind == 5 -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ")" -> println
                }
                expressionIndex! - 1 => expressionIndex!
            }
            ir![function.operand0] => returnNode
            ir![returnNode.operand0] => returnOperand
            "  ret $returnType " -> print
            (returnOperand.kind == 3 or returnOperand.kind == 4) -> if {
                sources[returnOperand.sourceModule] -> lexer.lex => returnTokens!
                returnTokens![returnOperand.payloadToken] => returnToken
                returnOperand.kind == 3 -> if {
                    sources[returnOperand.sourceModule] -> slice(returnToken.span.start, returnToken.span.length) -> println
                } else {
                    ((sources[returnOperand.sourceModule] -> byte(returnToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                }
            } else {
                returnOperand.kind == 5 -> if { "%arg" -> println } else { "%v$(returnNode.operand0)" -> println }
            }
            "}" -> println
            functionEnd! => functionIndex!
        } else {
            function.kind == 11 -> if {
                "define i32 @main() {" -> println
                "entry:" -> println
                "  ret i32 0" -> println
                "}" -> println
            }
            functionIndex! + 1 => functionIndex!
        }
    }
}
