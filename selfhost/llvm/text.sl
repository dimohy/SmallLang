namespace smalllang.compiler.llvm.text

import smalllang.compiler.ast as ast
import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

# First LLVM text backend slice. Names are derived only from stable module and
# symbol indexes; SSA registers are derived from typed-IR indexes.
public emit sources: [Text; ~] -> Unit {
    llvmType symbol: Int -> Text => when {
        symbol == 1 => "%sl.text"
        symbol == 2 => "i32"
        symbol == 23 => "i1"
        else => "void"
    }
    hexDigit value: Int -> Text => when {
        value == 0 => "0"
        value == 1 => "1"
        value == 2 => "2"
        value == 3 => "3"
        value == 4 => "4"
        value == 5 => "5"
        value == 6 => "6"
        value == 7 => "7"
        value == 8 => "8"
        value == 9 => "9"
        value == 10 => "A"
        value == 11 => "B"
        value == 12 => "C"
        value == 13 => "D"
        value == 14 => "E"
        else => "F"
    }
    writeType node: typedIr.TypedIrNode -> Unit {
        (node.typeOrigin == 0 or node.typeOrigin == 2) -> if {
            "%sl.struct.m$(node.typeModule)_s$(node.typeSymbol)" -> print
        } else {
            node.typeSymbol -> llvmType -> print
        }
    }
    sources -> typedIr.lower => ir!
    sources -> nominalTypes.resolve => nominal!
    sources -> modules.identities => moduleIdentities!
    0 => structSourceIndex!
    structSourceIndex! < (sources -> len) -> while {
        sources[structSourceIndex!] -> symbols.collect => structTable!
        0 => structSymbolIndex!
        structSymbolIndex! < (structTable! -> len) -> while {
            structTable![structSymbolIndex!] => structSymbol
            (structSymbol.kind == 3 and structSymbol.parent < 0) -> if {
                "%sl.struct.m$(structSourceIndex!)_s$(structSymbolIndex!) = type { " -> print
                true => firstField!
                0 => fieldSymbolIndex!
                fieldSymbolIndex! < (structTable! -> len) -> while {
                    structTable![fieldSymbolIndex!] => fieldSymbol
                    (fieldSymbol.kind == 26 and fieldSymbol.parent == structSymbolIndex!) -> if {
                        not firstField! -> if { ", " -> print }
                        -1 => fieldTypeIndex!
                        0 => fieldTypeSearch!
                        fieldTypeSearch! < (nominal! -> len) -> while {
                            nominal![fieldTypeSearch!] => fieldTypeCandidate
                            (fieldTypeCandidate.sourceModule == structSourceIndex! and fieldTypeCandidate.typeAst == fieldSymbol.typeNode) -> if { fieldTypeSearch! => fieldTypeIndex! }
                            fieldTypeSearch! + 1 => fieldTypeSearch!
                        }
                        fieldTypeIndex! >= 0 -> if {
                            nominal![fieldTypeIndex!] => fieldType
                            (fieldType.origin == 0 or fieldType.origin == 2) -> if {
                                "%sl.struct.m$(fieldType.targetModule)_s$(fieldType.targetSymbol)" -> print
                            } else {
                                fieldType.targetSymbol -> llvmType -> print
                            }
                        }
                        false => firstField!
                    }
                    fieldSymbolIndex! + 1 => fieldSymbolIndex!
                }
                " }" -> println
            }
            structSymbolIndex! + 1 => structSymbolIndex!
        }
        structSourceIndex! + 1 => structSourceIndex!
    }
    false => usesText!
    0 => textTypeSearch!
    textTypeSearch! < (ir! -> len) -> while {
        ir![textTypeSearch!].typeSymbol == 1 -> if { true => usesText! }
        textTypeSearch! + 1 => textTypeSearch!
    }
    usesText! -> if {
        "%sl.text = type { ptr, i64 }" -> println
        0 => textGlobalIndex!
        textGlobalIndex! < (ir! -> len) -> while {
            ir![textGlobalIndex!] => textConstant
            textConstant.kind == 2 -> if {
                sources[textConstant.sourceModule] -> lexer.lex => textTokens!
                textTokens![textConstant.payloadToken] => textToken
                textToken.span.length - UIntSize(2) => textLength
                "@sl_str_$(textGlobalIndex!) = private unnamed_addr constant [$textLength x i8] c" -> print
                sources[textConstant.sourceModule] -> slice(textToken.span.start, UIntSize(1)) -> print
                textToken.span.start + UIntSize(1) => textByteIndex!
                textToken.span.start + textToken.span.length - UIntSize(1) => textByteEnd
                textByteIndex! < textByteEnd -> while {
                    sources[textConstant.sourceModule] -> byte(textByteIndex!) => textByte
                    (textByte >= UInt8(32) and textByte <= UInt8(126) and textByte != UInt8(34) and textByte != UInt8(92)) -> if {
                        sources[textConstant.sourceModule] -> slice(textByteIndex!, UIntSize(1)) -> print
                    } else {
                        "\\" -> print
                        Int(textByte) / 16 -> hexDigit -> print
                        Int(textByte) % 16 -> hexDigit -> print
                    }
                    textByteIndex! + UIntSize(1) => textByteIndex!
                }
                sources[textConstant.sourceModule] -> slice(textByteEnd, UIntSize(1)) -> println
            }
            textGlobalIndex! + 1 => textGlobalIndex!
        }
    }
    0 => functionIndex!
    functionIndex! < (ir! -> len) -> while {
        ir![functionIndex!] => function
        function.kind == 0 -> if {
            functionIndex! + 1 => functionEnd!
            (functionEnd! < (ir! -> len) and ir![functionEnd!].kind != 0 and ir![functionEnd!].kind != 11) -> while {
                functionEnd! + 1 => functionEnd!
            }
            "define " -> print
            function -> writeType
            " @sl_m$(function.sourceModule)_s$(function.symbol)(" -> print
            function.operand1 >= 0 -> if {
                ir![function.operand1] => parameter
                parameter -> writeType
                " %arg" -> print
            }
            ") {" -> println
            "entry:" -> println
            functionEnd! - 1 => expressionIndex!
            function.operand0 + 1 => expressionStart
            expressionIndex! >= expressionStart -> while {
                ir![expressionIndex!] => expression
                expression.kind == 2 -> if {
                    sources[expression.sourceModule] -> lexer.lex => expressionTokens!
                    expressionTokens![expression.payloadToken] => expressionToken
                    expressionToken.span.length - UIntSize(2) => expressionLength
                    "  %v$(expressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(expressionIndex!), 0" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.text %v$(expressionIndex!)_ptr, i64 $expressionLength, 1" -> println
                }
                expression.kind == 12 -> if {
                    expression.operand0 => fieldValueIndex!
                    0 => fieldPosition!
                    fieldValueIndex! >= 0 -> while {
                        ir![fieldValueIndex!] => fieldValue
                        "  " -> print
                        fieldValue.nextOperand < 0 -> if { "%v$(expressionIndex!)" -> print } else { "%v$(expressionIndex!)_f$(fieldPosition!)" -> print }
                        " = insertvalue " -> print
                        expression -> writeType
                        " " -> print
                        fieldPosition! == 0 -> if { "poison" -> print } else { "%v$(expressionIndex!)_f$(fieldPosition! - 1)" -> print }
                        ", " -> print
                        fieldValue -> writeType
                        " " -> print
                        (fieldValue.kind == 3 or fieldValue.kind == 4) -> if {
                            sources[fieldValue.sourceModule] -> lexer.lex => fieldTokens!
                            fieldTokens![fieldValue.payloadToken] => fieldToken
                            fieldValue.kind == 3 -> if {
                                sources[fieldValue.sourceModule] -> slice(fieldToken.span.start, fieldToken.span.length) -> print
                            } else {
                                ((sources[fieldValue.sourceModule] -> byte(fieldToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            fieldValue.kind == 5 -> if { "%arg" -> print } else { "%v$(fieldValueIndex!)" -> print }
                        }
                        ", $(fieldPosition!)" -> println
                        fieldValue.nextOperand => fieldValueIndex!
                        fieldPosition! + 1 => fieldPosition!
                    }
                }
                expression.kind == 13 -> if {
                    ir![expression.operand0] => memberBase
                    memberBase.typeModule => ownerSourceModule!
                    memberBase.typeOrigin == 2 -> if { moduleIdentities![memberBase.typeModule].sourceIndex => ownerSourceModule! }
                    sources[ownerSourceModule!] -> symbols.collect => ownerTable!
                    sources[ownerSourceModule!] -> lexer.lex => ownerTokens!
                    sources[expression.sourceModule] -> ast.lower => memberNodes!
                    sources[expression.sourceModule] -> lexer.lex => memberTokens!
                    memberNodes![expression.astNode] => memberAst
                    -1 => memberNameToken!
                    memberAst.firstToken => memberTokenIndex!
                    memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                        memberTokens![memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if { memberTokenIndex! => memberNameToken! }
                        memberTokenIndex! + 1 => memberTokenIndex!
                    }
                    -1 => fieldOrdinal!
                    0 => candidateFieldOrdinal!
                    0 => memberFieldSearch!
                    memberFieldSearch! < (ownerTable! -> len) -> while {
                        ownerTable![memberFieldSearch!] => memberField
                        (memberField.kind == 26 and memberField.parent == memberBase.typeSymbol) -> if {
                            memberTokens![memberNameToken!] => memberName
                            ownerTokens![memberField.nameToken] => fieldName
                            memberName.span.length == fieldName.span.length => equal!
                            UIntSize(0) => fieldNameByte!
                            (equal! and fieldNameByte! < memberName.span.length) -> while {
                                sources[expression.sourceModule] -> byte(memberName.span.start + fieldNameByte!) => memberByte
                                sources[ownerSourceModule!] -> byte(fieldName.span.start + fieldNameByte!) => fieldByte
                                memberByte != fieldByte -> if { false => equal! }
                                fieldNameByte! + UIntSize(1) => fieldNameByte!
                            }
                            equal! -> if { candidateFieldOrdinal! => fieldOrdinal! }
                            candidateFieldOrdinal! + 1 => candidateFieldOrdinal!
                        }
                        memberFieldSearch! + 1 => memberFieldSearch!
                    }
                    "  %v$(expressionIndex!) = extractvalue " -> print
                    memberBase -> writeType
                    " " -> print
                    memberBase.kind == 5 -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", $(fieldOrdinal!)" -> println
                }
                (expression.kind == 7 or expression.kind == 8) -> if {
                    ir![expression.operand0] => leftOperand
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
                    "  %v$(expressionIndex!) = $(operation!) " -> print
                    leftOperand -> writeType
                    " " -> print
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
                    "  %v$(expressionIndex!) = call " -> print
                    expression -> writeType
                    " @sl_m$(expression.targetModule)_s$(expression.symbol)(" -> print
                    expression.operand0 >= 0 -> if {
                        ir![expression.operand0] => argument
                        argument -> writeType
                        " " -> print
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
            "  ret " -> print
            function -> writeType
            " " -> print
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
