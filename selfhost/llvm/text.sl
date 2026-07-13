namespace smalllang.compiler.llvm.text

import smalllang.compiler.ast as ast
import smalllang.compiler.ir.interpolation as interpolation
import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer as lexer
import smalllang.compiler.llvm.target as llvmTarget
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

# First LLVM text backend slice. Names are derived only from stable module and
# symbol indexes; SSA registers are derived from typed-IR indexes.
emitCore sources: move [Text; ~] -> Unit {
    llvmType symbol: Int -> Text => when {
        symbol == 1 => "%sl.text"
        symbol == 2 => "i32"
        symbol == 23 => "i1"
        else => "void"
    }
    storageSize symbol: Int -> Int => when {
        symbol == 1 => 16
        symbol == 23 => 1
        else => 4
    }
    storageAlign symbol: Int -> Int => when {
        symbol == 1 => 8
        symbol == 23 => 1
        else => 4
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
        node.typeOrigin == 13 -> if {
            "%sl.array.i32" -> print
        } else {
        node.typeOrigin == 15 -> if {
            "%sl.dict" -> print
        } else {
        (node.typeOrigin == 0 or node.typeOrigin == 2) -> if {
            "%sl.struct.m$(node.typeModule)_s$(node.typeSymbol)" -> print
        } else {
            node.typeSymbol -> llvmType -> print
        }
        }
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
    false => usesDynamicArray!
    0 => arrayTypeSearch!
    arrayTypeSearch! < (ir! -> len) -> while {
        ir![arrayTypeSearch!].typeOrigin == 13 -> if { true => usesDynamicArray! }
        arrayTypeSearch! + 1 => arrayTypeSearch!
    }
    usesDynamicArray! -> if {
        "%sl.array.i32 = type { ptr, i64, i64 }" -> println
        "declare ptr @malloc(i64)" -> println
        "declare void @free(ptr)" -> println
    }
    false => usesDictionary!
    0 => dictionaryTypeSearch!
    dictionaryTypeSearch! < (ir! -> len) -> while {
        ir![dictionaryTypeSearch!].typeOrigin == 15 -> if { true => usesDictionary! }
        dictionaryTypeSearch! + 1 => dictionaryTypeSearch!
    }
    usesDictionary! -> if {
        "%sl.dict = type { ptr, ptr, i64, i64 }" -> println
        "declare void @llvm.trap()" -> println
        not usesDynamicArray! -> if {
            "declare ptr @malloc(i64)" -> println
            "declare void @free(ptr)" -> println
        }
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
            function.operand0 + 1 => expressionStart
            [Bool; ~] => expressionScheduled!
            0 => scheduledInit!
            scheduledInit! < (ir! -> len) -> while {
                expressionScheduled! -> push(false)
                scheduledInit! + 1 => scheduledInit!
            }
            [Int; ~] => expressionOrder!
            true => scheduleProgress!
            scheduleProgress! -> while {
                false => scheduleProgress!
                expressionStart => scheduleCandidate!
                scheduleCandidate! < functionEnd! -> while {
                    not expressionScheduled![scheduleCandidate!] -> if {
                        ir![scheduleCandidate!] => scheduleNode
                        true => scheduleReady!
                        (scheduleNode.operand0 >= expressionStart and scheduleNode.operand0 < functionEnd! and not expressionScheduled![scheduleNode.operand0]) -> if { false => scheduleReady! }
                        (scheduleNode.operand1 >= expressionStart and scheduleNode.operand1 < functionEnd! and not expressionScheduled![scheduleNode.operand1]) -> if { false => scheduleReady! }
                        (scheduleReady! and (scheduleNode.kind == 12 or scheduleNode.kind == 14 or scheduleNode.kind == 16)) -> if {
                            scheduleNode.operand0 => scheduleSibling!
                            scheduleSibling! >= 0 -> while {
                                not expressionScheduled![scheduleSibling!] -> if { false => scheduleReady! }
                                ir![scheduleSibling!].nextOperand => scheduleSibling!
                            }
                        }
                        scheduleReady! -> if {
                            expressionOrder! -> push(scheduleCandidate!)
                            true => expressionScheduled![scheduleCandidate!]
                            true => scheduleProgress!
                        }
                    }
                    scheduleCandidate! + 1 => scheduleCandidate!
                }
            }
            0 => expressionOrderIndex!
            expressionOrderIndex! < (expressionOrder! -> len) -> while {
                expressionOrder![expressionOrderIndex!] => expressionIndex!
                ir![expressionIndex!] => expression
                (expression.kind == 5 and not (function.operand1 >= 0 and expression.symbol == ir![function.operand1].symbol)) -> if {
                    -1 => bindingValueIr!
                    expressionStart => bindingValueSearch!
                    bindingValueSearch! < functionEnd! -> while {
                        (ir![bindingValueSearch!].kind == 17 and ir![bindingValueSearch!].symbol == expression.symbol) -> if { bindingValueSearch! => bindingValueIr! }
                        bindingValueSearch! + 1 => bindingValueSearch!
                    }
                    bindingValueIr! >= 0 -> if {
                        ir![ir![bindingValueIr!].operand0] => bindingOperand
                        "  %v$(expressionIndex!) = freeze " -> print
                        expression -> writeType
                        " " -> print
                        (bindingOperand.kind == 3 or bindingOperand.kind == 4) -> if {
                            sources[bindingOperand.sourceModule] -> lexer.lex => bindingOperandTokens!
                            bindingOperandTokens![bindingOperand.payloadToken] => bindingOperandToken
                            bindingOperand.kind == 3 -> if { sources[bindingOperand.sourceModule] -> slice(bindingOperandToken.span.start, bindingOperandToken.span.length) -> print } else {
                                ((sources[bindingOperand.sourceModule] -> byte(bindingOperandToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else { "%v$(ir![bindingValueIr!].operand0)" -> print }
                        "" -> println
                    }
                }
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
                            (fieldValue.kind == 5 and function.operand1 >= 0 and fieldValue.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(fieldValueIndex!)" -> print }
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
                    (memberBase.kind == 5 and function.operand1 >= 0 and memberBase.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", $(fieldOrdinal!)" -> println
                }
                (expression.kind == 14 and expression.typeOrigin == 13) -> if {
                    0 => arrayLength!
                    expression.operand0 => arrayCountIndex!
                    arrayCountIndex! >= 0 -> while {
                        arrayLength! + 1 => arrayLength!
                        ir![arrayCountIndex!].nextOperand => arrayCountIndex!
                    }
                    arrayLength! * 4 => arrayByteLength
                    "  %v$(expressionIndex!)_data = call ptr @malloc(i64 $arrayByteLength)" -> println
                    expression.operand0 => arrayElementIndex!
                    0 => arrayElementPosition!
                    arrayElementIndex! >= 0 -> while {
                        ir![arrayElementIndex!] => arrayElement
                        "  %v$(expressionIndex!)_ptr$(arrayElementPosition!) = getelementptr i32, ptr %v$(expressionIndex!)_data, i64 $(arrayElementPosition!)" -> println
                        "  store i32 " -> print
                        arrayElement.kind == 3 -> if {
                            sources[arrayElement.sourceModule] -> lexer.lex => arrayElementTokens!
                            arrayElementTokens![arrayElement.payloadToken] => arrayElementToken
                            sources[arrayElement.sourceModule] -> slice(arrayElementToken.span.start, arrayElementToken.span.length) -> print
                        } else {
                            (arrayElement.kind == 5 and function.operand1 >= 0 and arrayElement.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(arrayElementIndex!)" -> print }
                        }
                        ", ptr %v$(expressionIndex!)_ptr$(arrayElementPosition!), align 4" -> println
                        arrayElement.nextOperand => arrayElementIndex!
                        arrayElementPosition! + 1 => arrayElementPosition!
                    }
                    "  %v$(expressionIndex!)_0 = insertvalue %sl.array.i32 poison, ptr %v$(expressionIndex!)_data, 0" -> println
                    "  %v$(expressionIndex!)_1 = insertvalue %sl.array.i32 %v$(expressionIndex!)_0, i64 $(arrayLength!), 1" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.array.i32 %v$(expressionIndex!)_1, i64 $(arrayLength!), 2" -> println
                }
                (expression.kind == 16 and expression.typeOrigin == 15) -> if {
                    0 => dictionaryItemCount!
                    expression.operand0 => dictionaryCountIndex!
                    dictionaryCountIndex! >= 0 -> while {
                        dictionaryItemCount! + 1 => dictionaryItemCount!
                        ir![dictionaryCountIndex!].nextOperand => dictionaryCountIndex!
                    }
                    dictionaryItemCount! / 2 => dictionaryLength
                    dictionaryLength * (expression.typeModule -> storageSize) => dictionaryKeyByteLength
                    dictionaryLength * (expression.typeSymbol -> storageSize) => dictionaryValueByteLength
                    "  %v$(expressionIndex!)_keys = call ptr @malloc(i64 $dictionaryKeyByteLength)" -> println
                    "  %v$(expressionIndex!)_values = call ptr @malloc(i64 $dictionaryValueByteLength)" -> println
                    expression.operand0 => dictionaryItemIndex!
                    0 => dictionaryItemPosition!
                    dictionaryItemIndex! >= 0 -> while {
                        ir![dictionaryItemIndex!] => dictionaryItem
                        dictionaryItemPosition! / 2 => dictionaryEntryPosition
                        dictionaryItemPosition! % 2 == 0 -> if { "keys" } else { "values" } => dictionarySide
                        dictionaryItemPosition! % 2 == 0 -> if { expression.typeModule } else { expression.typeSymbol } => dictionaryItemSymbol
                        "  %v$(expressionIndex!)_$(dictionarySide)_ptr$(dictionaryEntryPosition) = getelementptr " -> print
                        dictionaryItemSymbol -> llvmType -> print
                        ", ptr %v$(expressionIndex!)_$(dictionarySide), i64 $(dictionaryEntryPosition)" -> println
                        "  store " -> print
                        dictionaryItemSymbol -> llvmType -> print
                        " " -> print
                        (dictionaryItem.kind == 3 or dictionaryItem.kind == 4) -> if {
                            sources[dictionaryItem.sourceModule] -> lexer.lex => dictionaryItemTokens!
                            dictionaryItemTokens![dictionaryItem.payloadToken] => dictionaryItemToken
                            dictionaryItem.kind == 3 -> if {
                                sources[dictionaryItem.sourceModule] -> slice(dictionaryItemToken.span.start, dictionaryItemToken.span.length) -> print
                            } else {
                                ((sources[dictionaryItem.sourceModule] -> byte(dictionaryItemToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (dictionaryItem.kind == 5 and function.operand1 >= 0 and dictionaryItem.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(dictionaryItemIndex!)" -> print }
                        }
                        ", ptr %v$(expressionIndex!)_$(dictionarySide)_ptr$(dictionaryEntryPosition), align $(dictionaryItemSymbol -> storageAlign)" -> println
                        dictionaryItem.nextOperand => dictionaryItemIndex!
                        dictionaryItemPosition! + 1 => dictionaryItemPosition!
                    }
                    "  %v$(expressionIndex!)_0 = insertvalue %sl.dict poison, ptr %v$(expressionIndex!)_keys, 0" -> println
                    "  %v$(expressionIndex!)_1 = insertvalue %sl.dict %v$(expressionIndex!)_0, ptr %v$(expressionIndex!)_values, 1" -> println
                    "  %v$(expressionIndex!)_2 = insertvalue %sl.dict %v$(expressionIndex!)_1, i64 $(dictionaryLength), 2" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.dict %v$(expressionIndex!)_2, i64 $(dictionaryLength), 3" -> println
                }
                expression.kind == 15 -> if {
                    ir![expression.operand0] => indexedArray
                    ir![expression.operand1] => arrayIndex
                    indexedArray.typeOrigin == 15 -> if {
                        "  %v$(expressionIndex!)_keys = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", 0" -> println
                        "  %v$(expressionIndex!)_values = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", 1" -> println
                        "  %v$(expressionIndex!)_length = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", 2" -> println
                        "  br label %dict$(expressionIndex!)_start" -> println
                        "dict$(expressionIndex!)_start:" -> println
                        "  br label %dict$(expressionIndex!)_loop" -> println
                        "dict$(expressionIndex!)_loop:" -> println
                        "  %v$(expressionIndex!)_slot = phi i64 [ 0, %dict$(expressionIndex!)_start ], [ %v$(expressionIndex!)_next, %dict$(expressionIndex!)_advance ]" -> println
                        "  %v$(expressionIndex!)_in_range = icmp ult i64 %v$(expressionIndex!)_slot, %v$(expressionIndex!)_length" -> println
                        "  br i1 %v$(expressionIndex!)_in_range, label %dict$(expressionIndex!)_check, label %dict$(expressionIndex!)_missing" -> println
                        "dict$(expressionIndex!)_check:" -> println
                        "  %v$(expressionIndex!)_key_ptr = getelementptr " -> print
                        indexedArray.typeModule -> llvmType -> print
                        ", ptr %v$(expressionIndex!)_keys, i64 %v$(expressionIndex!)_slot" -> println
                        "  %v$(expressionIndex!)_key = load " -> print
                        indexedArray.typeModule -> llvmType -> print
                        ", ptr %v$(expressionIndex!)_key_ptr, align $(indexedArray.typeModule -> storageAlign)" -> println
                        "  %v$(expressionIndex!)_found = icmp eq " -> print
                        indexedArray.typeModule -> llvmType -> print
                        " %v$(expressionIndex!)_key, " -> print
                        (arrayIndex.kind == 3 or arrayIndex.kind == 4) -> if {
                            sources[arrayIndex.sourceModule] -> lexer.lex => dictionaryIndexTokens!
                            dictionaryIndexTokens![arrayIndex.payloadToken] => dictionaryIndexToken
                            arrayIndex.kind == 3 -> if {
                                sources[arrayIndex.sourceModule] -> slice(dictionaryIndexToken.span.start, dictionaryIndexToken.span.length) -> println
                            } else {
                                ((sources[arrayIndex.sourceModule] -> byte(dictionaryIndexToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                            }
                        } else {
                            (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == ir![function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                        }
                        "  br i1 %v$(expressionIndex!)_found, label %dict$(expressionIndex!)_hit, label %dict$(expressionIndex!)_advance" -> println
                        "dict$(expressionIndex!)_advance:" -> println
                        "  %v$(expressionIndex!)_next = add i64 %v$(expressionIndex!)_slot, 1" -> println
                        "  br label %dict$(expressionIndex!)_loop" -> println
                        "dict$(expressionIndex!)_missing:" -> println
                        "  call void @llvm.trap()" -> println
                        "  unreachable" -> println
                        "dict$(expressionIndex!)_hit:" -> println
                        "  %v$(expressionIndex!)_value_ptr = getelementptr " -> print
                        indexedArray.typeSymbol -> llvmType -> print
                        ", ptr %v$(expressionIndex!)_values, i64 %v$(expressionIndex!)_slot" -> println
                        "  %v$(expressionIndex!) = load " -> print
                        indexedArray.typeSymbol -> llvmType -> print
                        ", ptr %v$(expressionIndex!)_value_ptr, align $(indexedArray.typeSymbol -> storageAlign)" -> println
                    } else {
                    "  %v$(expressionIndex!)_data = extractvalue %sl.array.i32 " -> print
                    (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    arrayIndex.kind != 3 -> if {
                        "  %v$(expressionIndex!)_index = sext i32 " -> print
                        (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand1)" -> print }
                        " to i64" -> println
                    }
                    "  %v$(expressionIndex!)_ptr = getelementptr i32, ptr %v$(expressionIndex!)_data, i64 " -> print
                    arrayIndex.kind == 3 -> if {
                        sources[arrayIndex.sourceModule] -> lexer.lex => indexTokens!
                        indexTokens![arrayIndex.payloadToken] => indexToken
                        sources[arrayIndex.sourceModule] -> slice(indexToken.span.start, indexToken.span.length) -> println
                    } else {
                        "%v$(expressionIndex!)_index" -> println
                    }
                    "  %v$(expressionIndex!) = load i32, ptr %v$(expressionIndex!)_ptr, align 4" -> println
                    }
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
                    (expression.kind == 7 and expression.opcode != -26) -> if { "0" -> print } else {
                        (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                            sources[leftOperand.sourceModule] -> lexer.lex => leftTokens!
                            leftTokens![leftOperand.payloadToken] => leftToken
                            leftOperand.kind == 3 -> if {
                                sources[leftOperand.sourceModule] -> slice(leftToken.span.start, leftToken.span.length) -> print
                            } else {
                                ((sources[leftOperand.sourceModule] -> byte(leftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ", " -> print
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if { "true" -> println } else {
                            (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                                sources[leftOperand.sourceModule] -> lexer.lex => unaryTokens!
                                unaryTokens![leftOperand.payloadToken] => unaryToken
                                sources[leftOperand.sourceModule] -> slice(unaryToken.span.start, unaryToken.span.length) -> println
                            } else {
                                (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == ir![function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand0)" -> println }
                            }
                        }
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
                            (rightOperand.kind == 5 and function.operand1 >= 0 and rightOperand.symbol == ir![function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                        }
                    }
                }
                expression.kind == 6 -> if {
                    (expression.symbol == -101 or expression.symbol == -102) -> if {
                        ir![expression.operand0] => runtimeArgument
                        runtimeArgument.kind == 2 -> if {
                            sources[runtimeArgument.sourceModule] -> lexer.lex => runtimeArgumentTokens!
                            runtimeArgumentTokens![runtimeArgument.payloadToken] => runtimeArgumentToken
                            Int(runtimeArgumentToken.span.length) - 2 => runtimeArgumentLength
                            sources[runtimeArgument.sourceModule] -> interpolation.lower => functionExpressionInterpolation!
                            false => hasFunctionExpressionInterpolation!
                            0 => functionExpressionSearch!
                            functionExpressionSearch! < (functionExpressionInterpolation! -> len) -> while {
                                (functionExpressionInterpolation![functionExpressionSearch!].sourceToken == runtimeArgument.payloadToken and functionExpressionInterpolation![functionExpressionSearch!].parent < 0) -> if { true => hasFunctionExpressionInterpolation! }
                                functionExpressionSearch! + 1 => functionExpressionSearch!
                            }
                            hasFunctionExpressionInterpolation! -> if {
                                0 => functionExpressionSegment!
                                UIntSize(0) => functionExpressionSuffixStart!
                                0 => functionExpressionRootSearch!
                                functionExpressionRootSearch! < (functionExpressionInterpolation! -> len) -> while {
                                    functionExpressionInterpolation![functionExpressionRootSearch!] => functionExpressionRoot
                                    (functionExpressionRoot.sourceToken == runtimeArgument.payloadToken and functionExpressionRoot.parent < 0) -> if {
                                        "  %v$(expressionIndex!)_expression_literal$(functionExpressionSegment!) = getelementptr i8, ptr @sl_str_$(expression.operand0), i64 $(functionExpressionRoot.literalStart)" -> println
                                        "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_expression_literal$(functionExpressionSegment!), i64 $(functionExpressionRoot.literalLength), i1 false)" -> println
                                        (functionExpressionInterpolation! -> len) - 1 => functionExpressionNodeIndex!
                                        functionExpressionNodeIndex! >= 0 -> while {
                                            functionExpressionInterpolation![functionExpressionNodeIndex!] => functionExpressionNode
                                            (functionExpressionNode.sourceToken == runtimeArgument.payloadToken and functionExpressionNode.segment == functionExpressionRoot.segment and (functionExpressionNode.kind == 2 or functionExpressionNode.kind == 3)) -> if {
                                                functionExpressionNode.kind == 2 -> if { "sub" } else {
                                                    functionExpressionNode.opcode == grammar.tokenIdPlus -> if { "add" } else {
                                                    functionExpressionNode.opcode == grammar.tokenIdMinus -> if { "sub" } else {
                                                    functionExpressionNode.opcode == grammar.tokenIdStar -> if { "mul" } else {
                                                    functionExpressionNode.opcode == grammar.tokenIdSlash -> if { "sdiv" } else { "srem" }
                                                    }
                                                    }
                                                    }
                                                } => functionExpressionOperation
                                                "  %v$(expressionIndex!)_expression$(functionExpressionNodeIndex!) = $functionExpressionOperation i32 " -> print
                                                functionExpressionNode.kind == 2 -> if { "0" -> print } else {
                                                    functionExpressionInterpolation![functionExpressionNode.operand0] => functionExpressionLeft
                                                    functionExpressionLeft.kind == 0 -> if {
                                                        sources[runtimeArgument.sourceModule] -> slice(functionExpressionLeft.payloadStart, functionExpressionLeft.payloadLength) -> print
                                                    } else {
                                                    functionExpressionLeft.kind == 1 -> if {
                                                        (function.operand1 >= 0 and ir![function.operand1].symbol == functionExpressionLeft.symbol) -> if { "%arg" -> print } else {
                                                            -1 => functionExpressionLeftBinding!
                                                            expressionStart => functionExpressionLeftBindingSearch!
                                                            functionExpressionLeftBindingSearch! < functionEnd! -> while {
                                                                (ir![functionExpressionLeftBindingSearch!].kind == 17 and ir![functionExpressionLeftBindingSearch!].symbol == functionExpressionLeft.symbol) -> if { functionExpressionLeftBindingSearch! => functionExpressionLeftBinding! }
                                                                functionExpressionLeftBindingSearch! + 1 => functionExpressionLeftBindingSearch!
                                                            }
                                                            functionExpressionLeftBinding! >= 0 -> if {
                                                                ir![ir![functionExpressionLeftBinding!].operand0] => functionExpressionLeftValue
                                                                functionExpressionLeftValue.kind == 3 -> if {
                                                                    sources[functionExpressionLeftValue.sourceModule] -> lexer.lex => functionExpressionLeftTokens!
                                                                    functionExpressionLeftTokens![functionExpressionLeftValue.payloadToken] => functionExpressionLeftToken
                                                                    sources[functionExpressionLeftValue.sourceModule] -> slice(functionExpressionLeftToken.span.start, functionExpressionLeftToken.span.length) -> print
                                                                } else { "%v$(ir![functionExpressionLeftBinding!].operand0)" -> print }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand0)" -> print }
                                                    }
                                                }
                                                ", " -> print
                                                functionExpressionNode.kind == 2 -> if {
                                                    functionExpressionInterpolation![functionExpressionNode.operand0] => functionExpressionUnary
                                                    functionExpressionUnary.kind == 0 -> if {
                                                        sources[runtimeArgument.sourceModule] -> slice(functionExpressionUnary.payloadStart, functionExpressionUnary.payloadLength) -> println
                                                    } else {
                                                    functionExpressionUnary.kind == 1 -> if {
                                                        (function.operand1 >= 0 and ir![function.operand1].symbol == functionExpressionUnary.symbol) -> if { "%arg" -> println } else {
                                                            -1 => functionExpressionUnaryBinding!
                                                            expressionStart => functionExpressionUnaryBindingSearch!
                                                            functionExpressionUnaryBindingSearch! < functionEnd! -> while {
                                                                (ir![functionExpressionUnaryBindingSearch!].kind == 17 and ir![functionExpressionUnaryBindingSearch!].symbol == functionExpressionUnary.symbol) -> if { functionExpressionUnaryBindingSearch! => functionExpressionUnaryBinding! }
                                                                functionExpressionUnaryBindingSearch! + 1 => functionExpressionUnaryBindingSearch!
                                                            }
                                                            functionExpressionUnaryBinding! >= 0 -> if {
                                                                ir![ir![functionExpressionUnaryBinding!].operand0] => functionExpressionUnaryValue
                                                                functionExpressionUnaryValue.kind == 3 -> if {
                                                                    sources[functionExpressionUnaryValue.sourceModule] -> lexer.lex => functionExpressionUnaryTokens!
                                                                    functionExpressionUnaryTokens![functionExpressionUnaryValue.payloadToken] => functionExpressionUnaryToken
                                                                    sources[functionExpressionUnaryValue.sourceModule] -> slice(functionExpressionUnaryToken.span.start, functionExpressionUnaryToken.span.length) -> println
                                                                } else { "%v$(ir![functionExpressionUnaryBinding!].operand0)" -> println }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand0)" -> println }
                                                    }
                                                } else {
                                                    functionExpressionInterpolation![functionExpressionNode.operand1] => functionExpressionRight
                                                    functionExpressionRight.kind == 0 -> if {
                                                        sources[runtimeArgument.sourceModule] -> slice(functionExpressionRight.payloadStart, functionExpressionRight.payloadLength) -> println
                                                    } else {
                                                    functionExpressionRight.kind == 1 -> if {
                                                        (function.operand1 >= 0 and ir![function.operand1].symbol == functionExpressionRight.symbol) -> if { "%arg" -> println } else {
                                                            -1 => functionExpressionRightBinding!
                                                            expressionStart => functionExpressionRightBindingSearch!
                                                            functionExpressionRightBindingSearch! < functionEnd! -> while {
                                                                (ir![functionExpressionRightBindingSearch!].kind == 17 and ir![functionExpressionRightBindingSearch!].symbol == functionExpressionRight.symbol) -> if { functionExpressionRightBindingSearch! => functionExpressionRightBinding! }
                                                                functionExpressionRightBindingSearch! + 1 => functionExpressionRightBindingSearch!
                                                            }
                                                            functionExpressionRightBinding! >= 0 -> if {
                                                                ir![ir![functionExpressionRightBinding!].operand0] => functionExpressionRightValue
                                                                functionExpressionRightValue.kind == 3 -> if {
                                                                    sources[functionExpressionRightValue.sourceModule] -> lexer.lex => functionExpressionRightTokens!
                                                                    functionExpressionRightTokens![functionExpressionRightValue.payloadToken] => functionExpressionRightToken
                                                                    sources[functionExpressionRightValue.sourceModule] -> slice(functionExpressionRightToken.span.start, functionExpressionRightToken.span.length) -> println
                                                                } else { "%v$(ir![functionExpressionRightBinding!].operand0)" -> println }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand1)" -> println }
                                                    }
                                                }
                                            }
                                            functionExpressionNodeIndex! - 1 => functionExpressionNodeIndex!
                                        }
                                        "  call void @sl_runtime_print_i32(i32 " -> print
                                        functionExpressionRoot.kind == 0 -> if {
                                            sources[runtimeArgument.sourceModule] -> slice(functionExpressionRoot.payloadStart, functionExpressionRoot.payloadLength) -> print
                                        } else {
                                        functionExpressionRoot.kind == 1 -> if {
                                            (function.operand1 >= 0 and ir![function.operand1].symbol == functionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                -1 => functionExpressionRootBinding!
                                                expressionStart => functionExpressionRootBindingSearch!
                                                functionExpressionRootBindingSearch! < functionEnd! -> while {
                                                    (ir![functionExpressionRootBindingSearch!].kind == 17 and ir![functionExpressionRootBindingSearch!].symbol == functionExpressionRoot.symbol) -> if { functionExpressionRootBindingSearch! => functionExpressionRootBinding! }
                                                    functionExpressionRootBindingSearch! + 1 => functionExpressionRootBindingSearch!
                                                }
                                                functionExpressionRootBinding! >= 0 -> if {
                                                    ir![ir![functionExpressionRootBinding!].operand0] => functionExpressionRootValue
                                                    functionExpressionRootValue.kind == 3 -> if {
                                                        sources[functionExpressionRootValue.sourceModule] -> lexer.lex => functionExpressionRootTokens!
                                                        functionExpressionRootTokens![functionExpressionRootValue.payloadToken] => functionExpressionRootToken
                                                        sources[functionExpressionRootValue.sourceModule] -> slice(functionExpressionRootToken.span.start, functionExpressionRootToken.span.length) -> print
                                                    } else { "%v$(ir![functionExpressionRootBinding!].operand0)" -> print }
                                                }
                                            }
                                        } else { "%v$(expressionIndex!)_expression$(functionExpressionRootSearch!)" -> print }
                                        }
                                        ", i1 false)" -> println
                                        functionExpressionRoot.expressionStart + functionExpressionRoot.expressionLength + UIntSize(1) => functionExpressionSuffixStart!
                                        functionExpressionSegment! + 1 => functionExpressionSegment!
                                    }
                                    functionExpressionRootSearch! + 1 => functionExpressionRootSearch!
                                }
                                UIntSize(runtimeArgumentLength) - functionExpressionSuffixStart! => functionExpressionSuffixLength
                                "  %v$(expressionIndex!)_expression_suffix = getelementptr i8, ptr @sl_str_$(expression.operand0), i64 $(functionExpressionSuffixStart!)" -> println
                                "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_expression_suffix, i64 $functionExpressionSuffixLength, i1 " -> print
                            } else {
                            runtimeArgumentToken.span.start + UIntSize(1) => functionInterpolationContentStart
                            runtimeArgumentToken.span.start + runtimeArgumentToken.span.length - UIntSize(1) => functionInterpolationContentEnd
                            functionInterpolationContentStart => functionInterpolationSegmentStart!
                            0 => functionInterpolationPartIndex!
                            false => functionEmittedInterpolation!
                            true => functionInterpolationSegmentsRemain!
                            functionInterpolationSegmentsRemain! -> while {
                                functionInterpolationSegmentStart! => functionInterpolationDollar!
                                -1 => functionInterpolationBindingIr!
                                false => functionInterpolationParameter!
                                functionInterpolationSegmentStart! => functionInterpolationMatchStart!
                                functionInterpolationSegmentStart! => functionInterpolationNameEnd!
                                (functionInterpolationDollar! < functionInterpolationContentEnd and functionInterpolationBindingIr! < 0 and not functionInterpolationParameter!) -> while {
                                    ((sources[runtimeArgument.sourceModule] -> byte(functionInterpolationDollar!)) == UInt8(36) and functionInterpolationDollar! + UIntSize(1) < functionInterpolationContentEnd) -> if {
                                        functionInterpolationDollar! + UIntSize(1) => functionInterpolationNameStart
                                        functionInterpolationNameStart => functionInterpolationNameEnd!
                                        true => functionInterpolationNameContinues!
                                        (functionInterpolationNameEnd! < functionInterpolationContentEnd and functionInterpolationNameContinues!) -> while {
                                            sources[runtimeArgument.sourceModule] -> byte(functionInterpolationNameEnd!) => functionInterpolationNameByte
                                            ((functionInterpolationNameByte >= UInt8(48) and functionInterpolationNameByte <= UInt8(57)) or (functionInterpolationNameByte >= UInt8(65) and functionInterpolationNameByte <= UInt8(90)) or (functionInterpolationNameByte >= UInt8(97) and functionInterpolationNameByte <= UInt8(122)) or functionInterpolationNameByte == UInt8(95)) -> if {
                                                functionInterpolationNameEnd! + UIntSize(1) => functionInterpolationNameEnd!
                                            } else { false => functionInterpolationNameContinues! }
                                        }
                                        functionInterpolationNameEnd! > functionInterpolationNameStart -> if {
                                            sources[runtimeArgument.sourceModule] -> symbols.collect => functionInterpolationSymbols!
                                            0 => functionInterpolationSymbolIndex!
                                            functionInterpolationSymbolIndex! < (functionInterpolationSymbols! -> len) -> while {
                                                functionInterpolationSymbols![functionInterpolationSymbolIndex!] => functionInterpolationSymbol
                                                (functionInterpolationSymbol.kind == 9 or functionInterpolationSymbol.kind == 35) -> if {
                                                    runtimeArgumentTokens![functionInterpolationSymbol.nameToken] => functionInterpolationSymbolToken
                                                    functionInterpolationSymbolToken.span.length == functionInterpolationNameEnd! - functionInterpolationNameStart => functionInterpolationNameEqual!
                                                    UIntSize(0) => functionInterpolationNameByteIndex!
                                                    (functionInterpolationNameEqual! and functionInterpolationNameByteIndex! < functionInterpolationSymbolToken.span.length) -> while {
                                                        (sources[runtimeArgument.sourceModule] -> byte(functionInterpolationNameStart + functionInterpolationNameByteIndex!)) != (sources[runtimeArgument.sourceModule] -> byte(functionInterpolationSymbolToken.span.start + functionInterpolationNameByteIndex!)) -> if { false => functionInterpolationNameEqual! }
                                                        functionInterpolationNameByteIndex! + UIntSize(1) => functionInterpolationNameByteIndex!
                                                    }
                                                    functionInterpolationNameEqual! -> if {
                                                        (functionInterpolationSymbol.kind == 35 and function.operand1 >= 0 and ir![function.operand1].symbol == functionInterpolationSymbolIndex! and ir![function.operand1].typeSymbol == 2) -> if {
                                                            true => functionInterpolationParameter!
                                                            functionInterpolationDollar! => functionInterpolationMatchStart!
                                                        } else {
                                                            expressionStart => functionInterpolationBindingSearch!
                                                            functionInterpolationBindingSearch! < functionEnd! -> while {
                                                                (ir![functionInterpolationBindingSearch!].kind == 17 and ir![functionInterpolationBindingSearch!].symbol == functionInterpolationSymbolIndex! and ir![functionInterpolationBindingSearch!].typeSymbol == 2) -> if {
                                                                    functionInterpolationBindingSearch! => functionInterpolationBindingIr!
                                                                    functionInterpolationDollar! => functionInterpolationMatchStart!
                                                                }
                                                                functionInterpolationBindingSearch! + 1 => functionInterpolationBindingSearch!
                                                            }
                                                        }
                                                    }
                                                }
                                                functionInterpolationSymbolIndex! + 1 => functionInterpolationSymbolIndex!
                                            }
                                        }
                                    }
                                    (functionInterpolationBindingIr! < 0 and not functionInterpolationParameter!) -> if { functionInterpolationDollar! + UIntSize(1) => functionInterpolationDollar! }
                                }
                                (functionInterpolationBindingIr! >= 0 or functionInterpolationParameter!) -> if {
                                    Int(functionInterpolationSegmentStart! - functionInterpolationContentStart) => functionInterpolationPartOffset
                                    Int(functionInterpolationMatchStart! - functionInterpolationSegmentStart!) => functionInterpolationPartLength
                                    "  %v$(expressionIndex!)_interpolation_part$(functionInterpolationPartIndex!) = getelementptr i8, ptr @sl_str_$(expression.operand0), i64 $functionInterpolationPartOffset" -> println
                                    "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_interpolation_part$(functionInterpolationPartIndex!), i64 $functionInterpolationPartLength, i1 false)" -> println
                                    "  call void @sl_runtime_print_i32(i32 " -> print
                                    functionInterpolationParameter! -> if { "%arg" -> print } else {
                                        ir![functionInterpolationBindingIr!] => functionInterpolationBinding
                                        ir![functionInterpolationBinding.operand0] => functionInterpolationValue
                                        functionInterpolationValue.kind == 3 -> if {
                                            sources[functionInterpolationValue.sourceModule] -> lexer.lex => functionInterpolationValueTokens!
                                            functionInterpolationValueTokens![functionInterpolationValue.payloadToken] => functionInterpolationValueToken
                                            sources[functionInterpolationValue.sourceModule] -> slice(functionInterpolationValueToken.span.start, functionInterpolationValueToken.span.length) -> print
                                        } else { "%v$(functionInterpolationBinding.operand0)" -> print }
                                    }
                                    ", i1 false)" -> println
                                    functionInterpolationNameEnd! => functionInterpolationSegmentStart!
                                    functionInterpolationPartIndex! + 1 => functionInterpolationPartIndex!
                                    true => functionEmittedInterpolation!
                                } else { false => functionInterpolationSegmentsRemain! }
                            }
                            functionEmittedInterpolation! -> if {
                                Int(functionInterpolationSegmentStart! - functionInterpolationContentStart) => functionInterpolationSuffixOffset
                                Int(functionInterpolationContentEnd - functionInterpolationSegmentStart!) => functionInterpolationSuffixLength
                                "  %v$(expressionIndex!)_interpolation_suffix = getelementptr i8, ptr @sl_str_$(expression.operand0), i64 $functionInterpolationSuffixOffset" -> println
                                "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_interpolation_suffix, i64 $functionInterpolationSuffixLength, i1 " -> print
                            } else {
                                "  call void @sl_runtime_print(ptr @sl_str_$(expression.operand0), i64 $runtimeArgumentLength, i1 " -> print
                            }
                            }
                        } else {
                            "  %v$(expressionIndex!)_runtime_ptr = extractvalue %sl.text " -> print
                            (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            ", 0" -> println
                            "  %v$(expressionIndex!)_runtime_len = extractvalue %sl.text " -> print
                            (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            ", 1" -> println
                            "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_runtime_ptr, i64 %v$(expressionIndex!)_runtime_len, i1 " -> print
                        }
                        expression.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                    } else {
                    (expression.typeOrigin == 1 and expression.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(expressionIndex!) = call " -> print }
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
                            (argument.kind == 5 and function.operand1 >= 0 and argument.symbol == ir![function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ")" -> println
                    }
                }
                expressionOrderIndex! + 1 => expressionOrderIndex!
            }
            ir![function.operand0] => returnNode
            ir![returnNode.operand0] => returnOperand
            expressionStart => dropIndex!
            dropIndex! < functionEnd! -> while {
                ir![dropIndex!] => dropCandidate
                (dropCandidate.kind == 14 and dropCandidate.typeOrigin == 13 and dropCandidate.parent == function.operand0 and dropIndex! != returnNode.operand0) -> if {
                    "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %v$(dropIndex!), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!))" -> println
                }
                (dropCandidate.kind == 16 and dropCandidate.typeOrigin == 15 and dropCandidate.parent == function.operand0 and dropIndex! != returnNode.operand0) -> if {
                    "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %v$(dropIndex!), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_keys)" -> println
                    "  %drop$(dropIndex!)_values = extractvalue %sl.dict %v$(dropIndex!), 1" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_values)" -> println
                }
                (dropCandidate.kind == 17 and dropCandidate.typeOrigin == 13 and not (returnOperand.kind == 5 and returnOperand.symbol == dropCandidate.symbol)) -> if {
                    "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %v$(dropCandidate.operand0), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!))" -> println
                }
                (dropCandidate.kind == 17 and dropCandidate.typeOrigin == 15 and not (returnOperand.kind == 5 and returnOperand.symbol == dropCandidate.symbol)) -> if {
                    "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %v$(dropCandidate.operand0), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_keys)" -> println
                    "  %drop$(dropIndex!)_values = extractvalue %sl.dict %v$(dropCandidate.operand0), 1" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_values)" -> println
                }
                dropIndex! + 1 => dropIndex!
            }
            function.operand1 >= 0 -> if {
                ir![function.operand1] => ownedParameter
                (ownedParameter.typeOrigin == 13 and ownedParameter.flags % 2 == 1) -> if {
                    not (returnOperand.kind == 5 and returnOperand.symbol == ownedParameter.symbol) -> if {
                        "  %drop_arg = extractvalue %sl.array.i32 %arg, 0" -> println
                        "  call void @free(ptr %drop_arg)" -> println
                    }
                }
                (ownedParameter.typeOrigin == 15 and ownedParameter.flags % 2 == 1) -> if {
                    not (returnOperand.kind == 5 and returnOperand.symbol == ownedParameter.symbol) -> if {
                        "  %drop_arg_keys = extractvalue %sl.dict %arg, 0" -> println
                        "  call void @free(ptr %drop_arg_keys)" -> println
                        "  %drop_arg_values = extractvalue %sl.dict %arg, 1" -> println
                        "  call void @free(ptr %drop_arg_values)" -> println
                    }
                }
            }
            (function.typeOrigin == 1 and function.typeSymbol == 0) -> if { "  ret void" -> println } else {
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
                (returnOperand.kind == 5 and function.operand1 >= 0 and returnOperand.symbol == ir![function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(returnNode.operand0)" -> println }
            }
            }
            "}" -> println
            functionEnd! => functionIndex!
        } else {
            function.kind == 11 -> if {
                functionIndex! + 1 => entryEnd!
                (entryEnd! < (ir! -> len) and ir![entryEnd!].kind != 0 and ir![entryEnd!].kind != 11) -> while { entryEnd! + 1 => entryEnd! }
                "define i32 @main() {" -> println
                "entry:" -> println
                [Bool; ~] => entryScheduled!
                0 => entryScheduleInit!
                entryScheduleInit! < (ir! -> len) -> while {
                    entryScheduled! -> push(false)
                    entryScheduleInit! + 1 => entryScheduleInit!
                }
                [Int; ~] => entryOrder!
                true => entryScheduleProgress!
                entryScheduleProgress! -> while {
                    false => entryScheduleProgress!
                    functionIndex! + 1 => entryScheduleCandidate!
                    entryScheduleCandidate! < entryEnd! -> while {
                        not entryScheduled![entryScheduleCandidate!] -> if {
                            ir![entryScheduleCandidate!] => entryScheduleNode
                            true => entryScheduleReady!
                            (entryScheduleNode.operand0 > functionIndex! and entryScheduleNode.operand0 < entryEnd! and not entryScheduled![entryScheduleNode.operand0]) -> if { false => entryScheduleReady! }
                            (entryScheduleNode.operand1 > functionIndex! and entryScheduleNode.operand1 < entryEnd! and not entryScheduled![entryScheduleNode.operand1]) -> if { false => entryScheduleReady! }
                            entryScheduleReady! -> if {
                                entryOrder! -> push(entryScheduleCandidate!)
                                true => entryScheduled![entryScheduleCandidate!]
                                true => entryScheduleProgress!
                            }
                        }
                        entryScheduleCandidate! + 1 => entryScheduleCandidate!
                    }
                }
                0 => entryOrderIndex!
                entryOrderIndex! < (entryOrder! -> len) -> while {
                    entryOrder![entryOrderIndex!] => entryExpressionIndex!
                    ir![entryExpressionIndex!] => entryExpression
                    (entryExpression.kind == 2 and entryExpression.parent >= 0 and ir![entryExpression.parent].kind == 17) -> if {
                        sources[entryExpression.sourceModule] -> lexer.lex => entryExpressionTokens!
                        entryExpressionTokens![entryExpression.payloadToken] => entryExpressionToken
                        entryExpressionToken.span.length - UIntSize(2) => entryExpressionLength
                        "  %v$(entryExpressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(entryExpressionIndex!), 0" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.text %v$(entryExpressionIndex!)_ptr, i64 $entryExpressionLength, 1" -> println
                    }
                    entryExpression.kind == 5 -> if {
                        -1 => entryBindingValueIr!
                        functionIndex! + 1 => entryBindingValueSearch!
                        entryBindingValueSearch! < entryEnd! -> while {
                            (ir![entryBindingValueSearch!].kind == 17 and ir![entryBindingValueSearch!].symbol == entryExpression.symbol) -> if { entryBindingValueSearch! => entryBindingValueIr! }
                            entryBindingValueSearch! + 1 => entryBindingValueSearch!
                        }
                        entryBindingValueIr! >= 0 -> if {
                            ir![ir![entryBindingValueIr!].operand0] => entryBindingOperand
                            "  %v$(entryExpressionIndex!) = freeze " -> print
                            entryExpression -> writeType
                            " " -> print
                            (entryBindingOperand.kind == 3 or entryBindingOperand.kind == 4) -> if {
                                sources[entryBindingOperand.sourceModule] -> lexer.lex => entryBindingOperandTokens!
                                entryBindingOperandTokens![entryBindingOperand.payloadToken] => entryBindingOperandToken
                                entryBindingOperand.kind == 3 -> if { sources[entryBindingOperand.sourceModule] -> slice(entryBindingOperandToken.span.start, entryBindingOperandToken.span.length) -> print } else {
                                    ((sources[entryBindingOperand.sourceModule] -> byte(entryBindingOperandToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(ir![entryBindingValueIr!].operand0)" -> print }
                            "" -> println
                        }
                    }
                    (entryExpression.kind == 7 or entryExpression.kind == 8) -> if {
                        ir![entryExpression.operand0] => entryLeft
                        "" => entryOperation!
                        entryExpression.kind == 7 -> if {
                            entryExpression.opcode == -26 -> if { "xor" => entryOperation! } else { "sub" => entryOperation! }
                        } else {
                            entryExpression.opcode == grammar.tokenIdPlus -> if { "add" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdMinus -> if { "sub" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdStar -> if { "mul" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdSlash -> if { "sdiv" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdPercent -> if { "srem" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdLess -> if { "icmp slt" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdGreater -> if { "icmp sgt" => entryOperation! }
                            entryExpression.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" => entryOperation! }
                            entryExpression.opcode == -24 -> if { "or" => entryOperation! }
                            entryExpression.opcode == -25 -> if { "and" => entryOperation! }
                        }
                        "  %v$(entryExpressionIndex!) = $(entryOperation!) " -> print
                        entryLeft -> writeType
                        " " -> print
                        (entryExpression.kind == 7 and entryExpression.opcode != -26) -> if { "0" -> print } else {
                            (entryLeft.kind == 3 or entryLeft.kind == 4) -> if {
                                sources[entryLeft.sourceModule] -> lexer.lex => entryLeftTokens!
                                entryLeftTokens![entryLeft.payloadToken] => entryLeftToken
                                entryLeft.kind == 3 -> if { sources[entryLeft.sourceModule] -> slice(entryLeftToken.span.start, entryLeftToken.span.length) -> print } else {
                                    ((sources[entryLeft.sourceModule] -> byte(entryLeftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryExpression.operand0)" -> print }
                        }
                        ", " -> print
                        entryExpression.kind == 7 -> if {
                            entryExpression.opcode == -26 -> if { "true" -> println } else {
                                (entryLeft.kind == 3 or entryLeft.kind == 4) -> if {
                                    sources[entryLeft.sourceModule] -> lexer.lex => entryUnaryTokens!
                                    entryUnaryTokens![entryLeft.payloadToken] => entryUnaryToken
                                    sources[entryLeft.sourceModule] -> slice(entryUnaryToken.span.start, entryUnaryToken.span.length) -> println
                                } else { "%v$(entryExpression.operand0)" -> println }
                            }
                        } else {
                            ir![entryExpression.operand1] => entryRight
                            (entryRight.kind == 3 or entryRight.kind == 4) -> if {
                                sources[entryRight.sourceModule] -> lexer.lex => entryRightTokens!
                                entryRightTokens![entryRight.payloadToken] => entryRightToken
                                entryRight.kind == 3 -> if { sources[entryRight.sourceModule] -> slice(entryRightToken.span.start, entryRightToken.span.length) -> println } else {
                                    ((sources[entryRight.sourceModule] -> byte(entryRightToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                                }
                            } else { "%v$(entryExpression.operand1)" -> println }
                        }
                    }
                    entryExpression.kind == 6 -> if {
                        (entryExpression.symbol == -101 or entryExpression.symbol == -102) -> if {
                            ir![entryExpression.operand0] => runtimeArgument
                            runtimeArgument.kind == 2 -> if {
                                sources[runtimeArgument.sourceModule] -> lexer.lex => runtimeArgumentTokens!
                                runtimeArgumentTokens![runtimeArgument.payloadToken] => runtimeArgumentToken
                                Int(runtimeArgumentToken.span.length) - 2 => runtimeArgumentLength
                                sources[runtimeArgument.sourceModule] -> interpolation.lower => entryExpressionInterpolation!
                                false => hasEntryExpressionInterpolation!
                                0 => entryExpressionInterpolationSearch!
                                entryExpressionInterpolationSearch! < (entryExpressionInterpolation! -> len) -> while {
                                    (entryExpressionInterpolation![entryExpressionInterpolationSearch!].sourceToken == runtimeArgument.payloadToken and entryExpressionInterpolation![entryExpressionInterpolationSearch!].parent < 0) -> if { true => hasEntryExpressionInterpolation! }
                                    entryExpressionInterpolationSearch! + 1 => entryExpressionInterpolationSearch!
                                }
                                hasEntryExpressionInterpolation! -> if {
                                    0 => entryExpressionSegment!
                                    UIntSize(0) => entryExpressionSuffixStart!
                                    0 => entryExpressionRootSearch!
                                    entryExpressionRootSearch! < (entryExpressionInterpolation! -> len) -> while {
                                        entryExpressionInterpolation![entryExpressionRootSearch!] => entryExpressionRoot
                                        (entryExpressionRoot.sourceToken == runtimeArgument.payloadToken and entryExpressionRoot.parent < 0) -> if {
                                            "  %v$(entryExpressionIndex!)_expression_literal$(entryExpressionSegment!) = getelementptr i8, ptr @sl_str_$(entryExpression.operand0), i64 $(entryExpressionRoot.literalStart)" -> println
                                            "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_expression_literal$(entryExpressionSegment!), i64 $(entryExpressionRoot.literalLength), i1 false)" -> println
                                            (entryExpressionInterpolation! -> len) - 1 => entryExpressionNodeIndex!
                                            entryExpressionNodeIndex! >= 0 -> while {
                                                entryExpressionInterpolation![entryExpressionNodeIndex!] => entryInterpolationNode
                                                (entryInterpolationNode.sourceToken == runtimeArgument.payloadToken and entryInterpolationNode.segment == entryExpressionRoot.segment and (entryInterpolationNode.kind == 2 or entryInterpolationNode.kind == 3)) -> if {
                                                    entryInterpolationNode.kind == 2 -> if { "sub" } else {
                                                        entryInterpolationNode.opcode == grammar.tokenIdPlus -> if { "add" } else {
                                                        entryInterpolationNode.opcode == grammar.tokenIdMinus -> if { "sub" } else {
                                                        entryInterpolationNode.opcode == grammar.tokenIdStar -> if { "mul" } else {
                                                        entryInterpolationNode.opcode == grammar.tokenIdSlash -> if { "sdiv" } else { "srem" }
                                                        }
                                                        }
                                                        }
                                                    } => entryExpressionOperation
                                                    "  %v$(entryExpressionIndex!)_expression$(entryExpressionNodeIndex!) = $entryExpressionOperation i32 " -> print
                                                    entryInterpolationNode.kind == 2 -> if { "0" -> print } else {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand0] => entryExpressionLeft
                                                        entryExpressionLeft.kind == 0 -> if {
                                                            sources[runtimeArgument.sourceModule] -> slice(entryExpressionLeft.payloadStart, entryExpressionLeft.payloadLength) -> print
                                                        } else {
                                                        entryExpressionLeft.kind == 1 -> if {
                                                            -1 => entryExpressionLeftBinding!
                                                            functionIndex! + 1 => entryExpressionLeftBindingSearch!
                                                            entryExpressionLeftBindingSearch! < entryEnd! -> while {
                                                                (ir![entryExpressionLeftBindingSearch!].kind == 17 and ir![entryExpressionLeftBindingSearch!].symbol == entryExpressionLeft.symbol) -> if { entryExpressionLeftBindingSearch! => entryExpressionLeftBinding! }
                                                                entryExpressionLeftBindingSearch! + 1 => entryExpressionLeftBindingSearch!
                                                            }
                                                            entryExpressionLeftBinding! >= 0 -> if {
                                                                ir![ir![entryExpressionLeftBinding!].operand0] => entryExpressionLeftValue
                                                                entryExpressionLeftValue.kind == 3 -> if {
                                                                    sources[entryExpressionLeftValue.sourceModule] -> lexer.lex => entryExpressionLeftTokens!
                                                                    entryExpressionLeftTokens![entryExpressionLeftValue.payloadToken] => entryExpressionLeftToken
                                                                    sources[entryExpressionLeftValue.sourceModule] -> slice(entryExpressionLeftToken.span.start, entryExpressionLeftToken.span.length) -> print
                                                                } else { "%v$(ir![entryExpressionLeftBinding!].operand0)" -> print }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand0)" -> print }
                                                        }
                                                    }
                                                    ", " -> print
                                                    entryInterpolationNode.kind == 2 -> if {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand0] => entryExpressionUnary
                                                        entryExpressionUnary.kind == 0 -> if {
                                                            sources[runtimeArgument.sourceModule] -> slice(entryExpressionUnary.payloadStart, entryExpressionUnary.payloadLength) -> println
                                                        } else {
                                                        entryExpressionUnary.kind == 1 -> if {
                                                            -1 => entryExpressionUnaryBinding!
                                                            functionIndex! + 1 => entryExpressionUnaryBindingSearch!
                                                            entryExpressionUnaryBindingSearch! < entryEnd! -> while {
                                                                (ir![entryExpressionUnaryBindingSearch!].kind == 17 and ir![entryExpressionUnaryBindingSearch!].symbol == entryExpressionUnary.symbol) -> if { entryExpressionUnaryBindingSearch! => entryExpressionUnaryBinding! }
                                                                entryExpressionUnaryBindingSearch! + 1 => entryExpressionUnaryBindingSearch!
                                                            }
                                                            entryExpressionUnaryBinding! >= 0 -> if {
                                                                ir![ir![entryExpressionUnaryBinding!].operand0] => entryExpressionUnaryValue
                                                                entryExpressionUnaryValue.kind == 3 -> if {
                                                                    sources[entryExpressionUnaryValue.sourceModule] -> lexer.lex => entryExpressionUnaryTokens!
                                                                    entryExpressionUnaryTokens![entryExpressionUnaryValue.payloadToken] => entryExpressionUnaryToken
                                                                    sources[entryExpressionUnaryValue.sourceModule] -> slice(entryExpressionUnaryToken.span.start, entryExpressionUnaryToken.span.length) -> println
                                                                } else { "%v$(ir![entryExpressionUnaryBinding!].operand0)" -> println }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand0)" -> println }
                                                        }
                                                    } else {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand1] => entryExpressionRight
                                                        entryExpressionRight.kind == 0 -> if {
                                                            sources[runtimeArgument.sourceModule] -> slice(entryExpressionRight.payloadStart, entryExpressionRight.payloadLength) -> println
                                                        } else {
                                                        entryExpressionRight.kind == 1 -> if {
                                                            -1 => entryExpressionRightBinding!
                                                            functionIndex! + 1 => entryExpressionRightBindingSearch!
                                                            entryExpressionRightBindingSearch! < entryEnd! -> while {
                                                                (ir![entryExpressionRightBindingSearch!].kind == 17 and ir![entryExpressionRightBindingSearch!].symbol == entryExpressionRight.symbol) -> if { entryExpressionRightBindingSearch! => entryExpressionRightBinding! }
                                                                entryExpressionRightBindingSearch! + 1 => entryExpressionRightBindingSearch!
                                                            }
                                                            entryExpressionRightBinding! >= 0 -> if {
                                                                ir![ir![entryExpressionRightBinding!].operand0] => entryExpressionRightValue
                                                                entryExpressionRightValue.kind == 3 -> if {
                                                                    sources[entryExpressionRightValue.sourceModule] -> lexer.lex => entryExpressionRightTokens!
                                                                    entryExpressionRightTokens![entryExpressionRightValue.payloadToken] => entryExpressionRightToken
                                                                    sources[entryExpressionRightValue.sourceModule] -> slice(entryExpressionRightToken.span.start, entryExpressionRightToken.span.length) -> println
                                                                } else { "%v$(ir![entryExpressionRightBinding!].operand0)" -> println }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand1)" -> println }
                                                        }
                                                    }
                                                }
                                                entryExpressionNodeIndex! - 1 => entryExpressionNodeIndex!
                                            }
                                            "  call void @sl_runtime_print_i32(i32 " -> print
                                            entryExpressionRoot.kind == 0 -> if {
                                                sources[runtimeArgument.sourceModule] -> slice(entryExpressionRoot.payloadStart, entryExpressionRoot.payloadLength) -> print
                                            } else {
                                            entryExpressionRoot.kind == 1 -> if {
                                                -1 => entryExpressionRootBinding!
                                                functionIndex! + 1 => entryExpressionRootBindingSearch!
                                                entryExpressionRootBindingSearch! < entryEnd! -> while {
                                                    (ir![entryExpressionRootBindingSearch!].kind == 17 and ir![entryExpressionRootBindingSearch!].symbol == entryExpressionRoot.symbol) -> if { entryExpressionRootBindingSearch! => entryExpressionRootBinding! }
                                                    entryExpressionRootBindingSearch! + 1 => entryExpressionRootBindingSearch!
                                                }
                                                entryExpressionRootBinding! >= 0 -> if {
                                                    ir![ir![entryExpressionRootBinding!].operand0] => entryExpressionRootValue
                                                    entryExpressionRootValue.kind == 3 -> if {
                                                        sources[entryExpressionRootValue.sourceModule] -> lexer.lex => entryExpressionRootTokens!
                                                        entryExpressionRootTokens![entryExpressionRootValue.payloadToken] => entryExpressionRootToken
                                                        sources[entryExpressionRootValue.sourceModule] -> slice(entryExpressionRootToken.span.start, entryExpressionRootToken.span.length) -> print
                                                    } else { "%v$(ir![entryExpressionRootBinding!].operand0)" -> print }
                                                }
                                            } else { "%v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)" -> print }
                                            }
                                            ", i1 false)" -> println
                                            entryExpressionRoot.expressionStart + entryExpressionRoot.expressionLength + UIntSize(1) => entryExpressionSuffixStart!
                                            entryExpressionSegment! + 1 => entryExpressionSegment!
                                        }
                                        entryExpressionRootSearch! + 1 => entryExpressionRootSearch!
                                    }
                                    UIntSize(runtimeArgumentLength) - entryExpressionSuffixStart! => entryExpressionSuffixLength
                                    "  %v$(entryExpressionIndex!)_expression_suffix = getelementptr i8, ptr @sl_str_$(entryExpression.operand0), i64 $(entryExpressionSuffixStart!)" -> println
                                    "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_expression_suffix, i64 $entryExpressionSuffixLength, i1 " -> print
                                } else {
                                runtimeArgumentToken.span.start + UIntSize(1) => interpolationContentStart
                                runtimeArgumentToken.span.start + runtimeArgumentToken.span.length - UIntSize(1) => interpolationContentEnd
                                interpolationContentStart => interpolationSegmentStart!
                                0 => interpolationPartIndex!
                                false => emittedInterpolation!
                                true => interpolationSegmentsRemain!
                                interpolationSegmentsRemain! -> while {
                                    interpolationSegmentStart! => interpolationDollar!
                                    -1 => interpolationBindingIr!
                                    interpolationSegmentStart! => interpolationMatchStart!
                                    interpolationSegmentStart! => interpolationNameEnd!
                                    (interpolationDollar! < interpolationContentEnd and interpolationBindingIr! < 0) -> while {
                                        ((sources[runtimeArgument.sourceModule] -> byte(interpolationDollar!)) == UInt8(36) and interpolationDollar! + UIntSize(1) < interpolationContentEnd) -> if {
                                            interpolationDollar! + UIntSize(1) => interpolationNameStart
                                            interpolationNameStart => interpolationNameEnd!
                                            true => interpolationNameContinues!
                                            (interpolationNameEnd! < interpolationContentEnd and interpolationNameContinues!) -> while {
                                                sources[runtimeArgument.sourceModule] -> byte(interpolationNameEnd!) => interpolationNameByte
                                                ((interpolationNameByte >= UInt8(48) and interpolationNameByte <= UInt8(57)) or (interpolationNameByte >= UInt8(65) and interpolationNameByte <= UInt8(90)) or (interpolationNameByte >= UInt8(97) and interpolationNameByte <= UInt8(122)) or interpolationNameByte == UInt8(95)) -> if {
                                                    interpolationNameEnd! + UIntSize(1) => interpolationNameEnd!
                                                } else { false => interpolationNameContinues! }
                                            }
                                            interpolationNameEnd! > interpolationNameStart -> if {
                                                sources[runtimeArgument.sourceModule] -> symbols.collect => interpolationSymbols!
                                                0 => interpolationSymbolIndex!
                                                interpolationSymbolIndex! < (interpolationSymbols! -> len) -> while {
                                                    interpolationSymbols![interpolationSymbolIndex!] => interpolationSymbol
                                                    interpolationSymbol.kind == 9 -> if {
                                                        runtimeArgumentTokens![interpolationSymbol.nameToken] => interpolationSymbolToken
                                                        interpolationSymbolToken.span.length == interpolationNameEnd! - interpolationNameStart => interpolationNameEqual!
                                                        UIntSize(0) => interpolationNameByteIndex!
                                                        (interpolationNameEqual! and interpolationNameByteIndex! < interpolationSymbolToken.span.length) -> while {
                                                            (sources[runtimeArgument.sourceModule] -> byte(interpolationNameStart + interpolationNameByteIndex!)) != (sources[runtimeArgument.sourceModule] -> byte(interpolationSymbolToken.span.start + interpolationNameByteIndex!)) -> if { false => interpolationNameEqual! }
                                                            interpolationNameByteIndex! + UIntSize(1) => interpolationNameByteIndex!
                                                        }
                                                        interpolationNameEqual! -> if {
                                                            functionIndex! + 1 => interpolationBindingSearch!
                                                            interpolationBindingSearch! < entryEnd! -> while {
                                                                (ir![interpolationBindingSearch!].kind == 17 and ir![interpolationBindingSearch!].symbol == interpolationSymbolIndex! and ir![interpolationBindingSearch!].typeSymbol == 2) -> if {
                                                                    interpolationBindingSearch! => interpolationBindingIr!
                                                                    interpolationDollar! => interpolationMatchStart!
                                                                }
                                                                interpolationBindingSearch! + 1 => interpolationBindingSearch!
                                                            }
                                                        }
                                                    }
                                                    interpolationSymbolIndex! + 1 => interpolationSymbolIndex!
                                                }
                                            }
                                        }
                                        interpolationBindingIr! < 0 -> if { interpolationDollar! + UIntSize(1) => interpolationDollar! }
                                    }
                                    interpolationBindingIr! >= 0 -> if {
                                        Int(interpolationSegmentStart! - interpolationContentStart) => interpolationPartOffset
                                        Int(interpolationMatchStart! - interpolationSegmentStart!) => interpolationPartLength
                                        "  %v$(entryExpressionIndex!)_interpolation_part$(interpolationPartIndex!) = getelementptr i8, ptr @sl_str_$(entryExpression.operand0), i64 $interpolationPartOffset" -> println
                                        "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_interpolation_part$(interpolationPartIndex!), i64 $interpolationPartLength, i1 false)" -> println
                                        ir![interpolationBindingIr!] => interpolationBinding
                                        ir![interpolationBinding.operand0] => interpolationValue
                                        "  call void @sl_runtime_print_i32(i32 " -> print
                                        interpolationValue.kind == 3 -> if {
                                            sources[interpolationValue.sourceModule] -> lexer.lex => interpolationValueTokens!
                                            interpolationValueTokens![interpolationValue.payloadToken] => interpolationValueToken
                                            sources[interpolationValue.sourceModule] -> slice(interpolationValueToken.span.start, interpolationValueToken.span.length) -> print
                                        } else { "%v$(interpolationBinding.operand0)" -> print }
                                        ", i1 false)" -> println
                                        interpolationNameEnd! => interpolationSegmentStart!
                                        interpolationPartIndex! + 1 => interpolationPartIndex!
                                        true => emittedInterpolation!
                                    } else { false => interpolationSegmentsRemain! }
                                }
                                emittedInterpolation! -> if {
                                    Int(interpolationSegmentStart! - interpolationContentStart) => interpolationSuffixOffset
                                    Int(interpolationContentEnd - interpolationSegmentStart!) => interpolationSuffixLength
                                    "  %v$(entryExpressionIndex!)_interpolation_suffix = getelementptr i8, ptr @sl_str_$(entryExpression.operand0), i64 $interpolationSuffixOffset" -> println
                                    "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_interpolation_suffix, i64 $interpolationSuffixLength, i1 " -> print
                                } else {
                                    "  call void @sl_runtime_print(ptr @sl_str_$(entryExpression.operand0), i64 $runtimeArgumentLength, i1 " -> print
                                }
                                }
                            } else {
                                "  %v$(entryExpressionIndex!)_runtime_ptr = extractvalue %sl.text %v$(entryExpression.operand0), 0" -> println
                                "  %v$(entryExpressionIndex!)_runtime_len = extractvalue %sl.text %v$(entryExpression.operand0), 1" -> println
                                "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_runtime_ptr, i64 %v$(entryExpressionIndex!)_runtime_len, i1 " -> print
                            }
                            entryExpression.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                        } else {
                        (entryExpression.typeOrigin == 1 and entryExpression.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(entryExpressionIndex!) = call " -> print }
                        entryExpression -> writeType
                        " @sl_m$(entryExpression.targetModule)_s$(entryExpression.symbol)(" -> print
                        entryExpression.operand0 >= 0 -> if {
                            ir![entryExpression.operand0] => entryArgument
                            entryArgument -> writeType
                            " " -> print
                            entryArgument.kind == 2 -> if {
                                sources[entryArgument.sourceModule] -> lexer.lex => entryArgumentTokens!
                                entryArgumentTokens![entryArgument.payloadToken] => entryArgumentToken
                                Int(entryArgumentToken.span.length) - 2 => entryArgumentLength
                                "{ ptr @sl_str_$(entryExpression.operand0), i64 $entryArgumentLength }" -> print
                            } else {
                            (entryArgument.kind == 3 or entryArgument.kind == 4) -> if {
                                sources[entryArgument.sourceModule] -> lexer.lex => entryArgumentTokens!
                                entryArgumentTokens![entryArgument.payloadToken] => entryArgumentToken
                                entryArgument.kind == 3 -> if {
                                    sources[entryArgument.sourceModule] -> slice(entryArgumentToken.span.start, entryArgumentToken.span.length) -> print
                                } else {
                                    ((sources[entryArgument.sourceModule] -> byte(entryArgumentToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryExpression.operand0)" -> print }
                            }
                        }
                        ")" -> println
                        }
                    }
                    entryOrderIndex! + 1 => entryOrderIndex!
                }
                "  ret i32 0" -> println
                "}" -> println
                entryEnd! => functionIndex!
            } else {
                functionIndex! + 1 => functionIndex!
            }
        }
    }
}

usesTextRuntime sources: [Text; ~] -> Bool {
    sources -> typedIr.lower => ir!
    false => usesRuntime!
    0 => nodeIndex!
    nodeIndex! < (ir! -> len) -> while {
        (ir![nodeIndex!].symbol == -101 or ir![nodeIndex!].symbol == -102) -> if { true => usesRuntime! }
        nodeIndex! + 1 => nodeIndex!
    }
    usesRuntime!
}

usesIntInterpolation sources: [Text; ~] -> Bool {
    sources -> typedIr.lower => ir!
    false => usesInterpolation!
    0 => nodeIndex!
    nodeIndex! < (ir! -> len) -> while {
        ir![nodeIndex!] => node
        (node.kind == 6 and (node.symbol == -101 or node.symbol == -102) and node.operand0 >= 0 and ir![node.operand0].kind == 2) -> if {
            ir![node.operand0] => argument
            sources[argument.sourceModule] -> lexer.lex => tokens!
            tokens![argument.payloadToken] => token
            token.span.start + UIntSize(1) => byteIndex!
            token.span.start + token.span.length - UIntSize(1) => byteEnd
            byteIndex! < byteEnd -> while {
                (sources[argument.sourceModule] -> byte(byteIndex!)) == UInt8(36) -> if { true => usesInterpolation! }
                byteIndex! + UIntSize(1) => byteIndex!
            }
        }
        nodeIndex! + 1 => nodeIndex!
    }
    usesInterpolation!
}

emitIntTextRuntime: -> Unit {
    """
    define internal void @sl_runtime_print_i32(i32 %value, i1 %newline) {
    entry:
      %buffer = alloca [12 x i8], align 1
      %end = getelementptr [12 x i8], ptr %buffer, i64 0, i64 12
      %wide = sext i32 %value to i64
      %negative = icmp slt i64 %wide, 0
      %negated = sub i64 0, %wide
      %magnitude = select i1 %negative, i64 %negated, i64 %wide
      br label %digits
    digits:
      %current = phi i64 [ %magnitude, %entry ], [ %quotient, %digits ]
      %cursor = phi ptr [ %end, %entry ], [ %digit_slot, %digits ]
      %digit = urem i64 %current, 10
      %quotient = udiv i64 %current, 10
      %digit_slot = getelementptr i8, ptr %cursor, i64 -1
      %digit8 = trunc i64 %digit to i8
      %ascii = add i8 %digit8, 48
      store i8 %ascii, ptr %digit_slot, align 1
      %more = icmp ne i64 %quotient, 0
      br i1 %more, label %digits, label %digits_done
    digits_done:
      br i1 %negative, label %write_sign, label %emit
    write_sign:
      %sign_slot = getelementptr i8, ptr %digit_slot, i64 -1
      store i8 45, ptr %sign_slot, align 1
      br label %emit
    emit:
      %start = phi ptr [ %digit_slot, %digits_done ], [ %sign_slot, %write_sign ]
      %end_address = ptrtoint ptr %end to i64
      %start_address = ptrtoint ptr %start to i64
      %length = sub i64 %end_address, %start_address
      call void @sl_runtime_print(ptr %start, i64 %length, i1 %newline)
      ret void
    }
    """ -> println
}

emitWindowsTextRuntime: -> Unit {
    """
    @sl_runtime_newline = private constant [1 x i8] c"\0A"
    declare i32 @putchar(i32)
    define internal void @sl_runtime_print(ptr %data, i64 %len, i1 %newline) {
    entry:
      br label %loop
    loop:
      %index = phi i64 [ 0, %entry ], [ %next, %body ]
      %in_range = icmp ult i64 %index, %len
      br i1 %in_range, label %body, label %end
    body:
      %slot = getelementptr i8, ptr %data, i64 %index
      %byte = load i8, ptr %slot, align 1
      %value = zext i8 %byte to i32
      %written = call i32 @putchar(i32 %value)
      %next = add i64 %index, 1
      br label %loop
    end:
      br i1 %newline, label %write_newline, label %done
    write_newline:
      %newline_written = call i32 @putchar(i32 10)
      br label %done
    done:
      ret void
    }
    """ -> println
}

emitLinuxTextRuntime: -> Unit {
    """
    @sl_runtime_newline = private constant [1 x i8] c"\0A"
    declare i64 @write(i32, ptr, i64)
    define internal void @sl_runtime_print(ptr %data, i64 %len, i1 %newline) {
    entry:
      %written = call i64 @write(i32 1, ptr %data, i64 %len)
      br i1 %newline, label %write_newline, label %done
    write_newline:
      %newline_written = call i64 @write(i32 1, ptr @sl_runtime_newline, i64 1)
      br label %done
    done:
      ret void
    }
    """ -> println
}

emitWasmTextRuntime: -> Unit {
    """"
    @sl_runtime_newline = private constant [1 x i8] c"\0A"
    declare void @smalllang_browser_write(ptr, i32) #1
    define internal void @sl_runtime_print(ptr %data, i64 %len, i1 %newline) {
    entry:
      %len32 = trunc i64 %len to i32
      call void @smalllang_browser_write(ptr %data, i32 %len32)
      br i1 %newline, label %write_newline, label %done
    write_newline:
      call void @smalllang_browser_write(ptr @sl_runtime_newline, i32 1)
      br label %done
    done:
      ret void
    }
    attributes #1 = { "wasm-import-module"="env" "wasm-import-name"="smalllang_write" }
    """" -> println
}

public emit sources: move [Text; ~] -> Unit {
    llvmTarget.windowsX64 => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    sources -> usesTextRuntime -> if { emitWindowsTextRuntime }
    sources -> usesIntInterpolation -> if { emitIntTextRuntime }
    emitCore(sources)
}

public emitLinux sources: move [Text; ~] -> Unit {
    llvmTarget.linuxX64 => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    sources -> usesTextRuntime -> if { emitLinuxTextRuntime }
    sources -> usesIntInterpolation -> if { emitIntTextRuntime }
    emitCore(sources)
}

public emitWasm sources: move [Text; ~] -> Unit {
    llvmTarget.wasm32Browser => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    sources -> usesTextRuntime -> if { emitWasmTextRuntime }
    sources -> usesIntInterpolation -> if { emitIntTextRuntime }
    emitCore(sources)
}
