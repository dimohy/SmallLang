namespace smalllang.compiler.llvm.text

import smalllang.compiler.ast as ast
import smalllang.compiler.ir.interpolation as interpolation
import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.lexer as lexer
import smalllang.compiler.llvm.target as llvmTarget
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.symbols as symbols
import syntax.generated.smalllang as grammar

# First LLVM text backend slice. Names are derived only from stable module and
# symbol indexes; SSA registers are derived from typed-IR indexes.
struct WhileBranchRequest {
    whileIndex: Int
    ownerIndex: Int
}

struct WhileValueRequest {
    whileIndex: Int
    ownerIndex: Int
    nodeIndex: Int
}

struct WhileBoolTask {
    kind: Int
    nodeIndex: Int
    trueKind: Int
    trueNode: Int
    falseKind: Int
    falseNode: Int
}

struct OwnedDropRequest {
    regionIndex: Int
    beforeAst: Int
    edgeIndex: Int
    transferredSymbol: Int
}

struct DropGlueRequest {
    typeOrigin: Int
    typeModule: Int
    typeSymbol: Int
    valueKind: Int
    valueIndex: Int
    nameRoot: Int
    pathCode: Int
    bindingIndex: Int
    regionIndex: Int
    beforeAst: Int
    parentTask: Int
    fieldOrdinal: Int
    hasPartialMoves: Bool
}

struct EmitContext {
    sources: [Text; ~]
    ir: [typedIr.TypedIrNode; ~]
    moves: [typedIr.MoveEvent; ~]
    nominal: [nominalTypes.NominalType; ~]
    composite: [compositeTypes.CompositeType; ~]
    modules: [modules.ModuleIdentity; ~]
}

isDynamicArrayType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if { node.typeKind == 3 } else { node.typeOrigin == 13 }
}

isDictionaryType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if { node.typeKind == 5 } else { node.typeOrigin == 15 }
}

isNominalStructType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if {
        node.typeKind == 1 and node.typeFlags % 2 == 1
    } else {
        node.typeOrigin == 0 or node.typeOrigin == 2
    }
}

ownsType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if {
        node.typeFlags % 2 == 1
    } else {
        node.typeOrigin == 13 or node.typeOrigin == 15 or node.typeOrigin == 0 or node.typeOrigin == 2
    }
}

# Transitional public boundary used by the emitter and regressions. Canonical
# kind/ownership wins whenever a type ID exists; the shallow branch remains
# only for IR nodes whose migration is not complete yet.
public writeType node: typedIr.TypedIrNode -> Unit {
    node -> isDynamicArrayType -> if {
        "%sl.array.i32" -> print
    } else {
    node -> isDictionaryType -> if {
        "%sl.dict" -> print
    } else {
    node -> isNominalStructType -> if {
        "%sl.struct.m$(node.typeModule)_s$(node.typeSymbol)" -> print
    } else {
        node.typeSymbol == 1 -> if { "%sl.text" -> print } else {
            node.typeSymbol == 2 -> if { "i32" -> print } else {
                node.typeSymbol == 23 -> if { "i1" -> print } else { "void" -> print }
            }
        }
    }
    }
    }
}

prepare sources: move [Text; ~] -> EmitContext {
    sources -> typedIr.lower => ir!
    ir! -> typedIr.movesFrom => moves!
    sources -> nominalTypes.resolve => nominal!
    sources -> compositeTypes.resolve => composite!
    sources -> modules.identities => moduleIdentities!
    EmitContext {
        sources: sources
        ir: ir!
        moves: moves!
        nominal: nominal!
        composite: composite!
        modules: moduleIdentities!
    } => context!
    context!
}

emitCore context: move EmitContext -> Unit {
    emitDropValueName request: DropGlueRequest -> Unit {
        request.valueKind == 0 -> if { "%v$(request.valueIndex)" -> print } else {
            request.valueKind == 1 -> if { "%arg" -> print } else { "%dropg$(request.nameRoot)_p$(request.pathCode)" -> print }
        }
    }
    emitDropPathName request: DropGlueRequest -> Unit {
        "%dropg$(request.nameRoot)_p$(request.pathCode)" -> print
    }
    emitDropGlue request: DropGlueRequest -> Unit {
        [request, ~] => dropTasks!
        0 => dropTaskIndex!
        dropTaskIndex! < (dropTasks! -> len) -> while {
        dropTasks![dropTaskIndex!] => dropTask
        false => dropTaskMoved!
        (dropTask.bindingIndex >= 0 and dropTask.hasPartialMoves) -> if {
            context.ir[dropTask.bindingIndex] => dropGlueBinding
            context.sources[dropGlueBinding.sourceModule] -> ast.lower => dropGlueAst!
            UIntSize(0) => dropGlueBeforeStart!
            dropTask.beforeAst >= 0 -> if { dropGlueAst![dropTask.beforeAst].start => dropGlueBeforeStart! }
            0 => dropGlueMoveIndex!
            (dropGlueMoveIndex! < (context.moves -> len) and not dropTaskMoved!) -> while {
                context.moves[dropGlueMoveIndex!] => dropGlueMove
                (dropGlueMove.memberIr >= 0 and dropGlueMove.sourceModule == dropGlueBinding.sourceModule and dropGlueMove.symbol == dropGlueBinding.symbol and dropGlueMove.regionIr == dropTask.regionIndex) -> if {
                    context.ir[dropGlueMove.siteIr] => dropGlueSite
                    true => dropGlueMoveBeforeEdge!
                    dropTask.beforeAst >= 0 -> if {
                        dropGlueAst![dropGlueSite.astNode].start >= dropGlueBeforeStart! -> if { false => dropGlueMoveBeforeEdge! }
                    }
                    dropGlueAst![dropGlueSite.astNode].start <= dropGlueAst![dropGlueBinding.astNode].start -> if { false => dropGlueMoveBeforeEdge! }
                    0 => dropTaskDepth!
                    dropTaskIndex! => dropTaskCursor!
                    (dropTaskCursor! >= 0 and dropTasks![dropTaskCursor!].fieldOrdinal >= 0) -> while {
                        dropTaskDepth! + 1 => dropTaskDepth!
                        dropTasks![dropTaskCursor!].parentTask => dropTaskCursor!
                    }
                    context.ir[dropGlueMove.memberIr] => dropMoveMember
                    context.sources[dropMoveMember.sourceModule] -> ast.lower => dropMoveNodes!
                    context.sources[dropMoveMember.sourceModule] -> lexer.lex => dropMoveTokens!
                    dropMoveNodes![dropMoveMember.astNode] => dropMoveAst
                    -1 => dropMoveDepth!
                    dropMoveAst.firstToken => dropMoveTokenIndex!
                    dropMoveTokenIndex! < dropMoveAst.firstToken + dropMoveAst.tokenCount -> while {
                        dropMoveTokens![dropMoveTokenIndex!].kind == grammar.tokenIdIdentifier -> if { dropMoveDepth! + 1 => dropMoveDepth! }
                        dropMoveTokenIndex! + 1 => dropMoveTokenIndex!
                    }
                    dropTaskDepth! == dropMoveDepth! => sameDropPath!
                    dropGlueBinding.typeOrigin => dropMoveCurrentOrigin!
                    dropGlueBinding.typeModule => dropMoveCurrentModule!
                    dropGlueBinding.typeSymbol => dropMoveCurrentSymbol!
                    0 => dropMoveIdentifierOrdinal!
                    dropMoveAst.firstToken => dropMoveTokenIndex!
                    (sameDropPath! and dropMoveTokenIndex! < dropMoveAst.firstToken + dropMoveAst.tokenCount) -> while {
                        dropMoveTokens![dropMoveTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                            dropMoveIdentifierOrdinal! > 0 -> if {
                                dropMoveCurrentModule! => dropMoveOwnerSource!
                                dropMoveCurrentOrigin! == 2 -> if { context.modules[dropMoveCurrentModule!].sourceIndex => dropMoveOwnerSource! }
                                context.sources[dropMoveOwnerSource!] -> symbols.collect => dropMoveOwnerTable!
                                context.sources[dropMoveOwnerSource!] -> lexer.lex => dropMoveOwnerTokens!
                                -1 => dropMoveFieldOrdinal!
                                -1 => dropMoveFieldTypeNode!
                                0 => dropMoveCandidateOrdinal!
                                0 => dropMoveFieldIndex!
                                dropMoveFieldIndex! < (dropMoveOwnerTable! -> len) -> while {
                                    dropMoveOwnerTable![dropMoveFieldIndex!] => dropMoveField
                                    (dropMoveField.kind == 26 and dropMoveField.parent == dropMoveCurrentSymbol!) -> if {
                                        dropMoveTokens![dropMoveTokenIndex!] => dropMoveName
                                        dropMoveOwnerTokens![dropMoveField.nameToken] => dropMoveFieldName
                                        dropMoveName.span.length == dropMoveFieldName.span.length => dropMoveEqual!
                                        UIntSize(0) => dropMoveNameByte!
                                        (dropMoveEqual! and dropMoveNameByte! < dropMoveName.span.length) -> while {
                                            context.sources[dropMoveMember.sourceModule] -> byte(dropMoveName.span.start + dropMoveNameByte!) => dropMoveByte
                                            context.sources[dropMoveOwnerSource!] -> byte(dropMoveFieldName.span.start + dropMoveNameByte!) => dropMoveFieldByte
                                            dropMoveByte != dropMoveFieldByte -> if { false => dropMoveEqual! }
                                            dropMoveNameByte! + UIntSize(1) => dropMoveNameByte!
                                        }
                                        dropMoveEqual! -> if {
                                            dropMoveCandidateOrdinal! => dropMoveFieldOrdinal!
                                            dropMoveField.typeNode => dropMoveFieldTypeNode!
                                        }
                                        dropMoveCandidateOrdinal! + 1 => dropMoveCandidateOrdinal!
                                    }
                                    dropMoveFieldIndex! + 1 => dropMoveFieldIndex!
                                }
                                dropTaskIndex! => dropTaskCursor!
                                dropTaskDepth! - dropMoveIdentifierOrdinal! => dropTaskParentSteps!
                                dropTaskParentSteps! > 0 -> while {
                                    dropTasks![dropTaskCursor!].parentTask => dropTaskCursor!
                                    dropTaskParentSteps! - 1 => dropTaskParentSteps!
                                }
                                (dropMoveFieldOrdinal! < 0 or dropTasks![dropTaskCursor!].fieldOrdinal != dropMoveFieldOrdinal!) -> if { false => sameDropPath! }
                                0 => dropMoveNominalIndex!
                                dropMoveNominalIndex! < (context.nominal -> len) -> while {
                                    context.nominal[dropMoveNominalIndex!] => dropMoveNominalType
                                    (dropMoveNominalType.sourceModule == dropMoveOwnerSource! and dropMoveNominalType.typeAst == dropMoveFieldTypeNode!) -> if {
                                        dropMoveNominalType.origin => dropMoveCurrentOrigin!
                                        dropMoveNominalType.targetModule => dropMoveCurrentModule!
                                        dropMoveNominalType.targetSymbol => dropMoveCurrentSymbol!
                                    }
                                    dropMoveNominalIndex! + 1 => dropMoveNominalIndex!
                                }
                            }
                            dropMoveIdentifierOrdinal! + 1 => dropMoveIdentifierOrdinal!
                        }
                        dropMoveTokenIndex! + 1 => dropMoveTokenIndex!
                    }
                    (dropGlueMoveBeforeEdge! and sameDropPath!) -> if { true => dropTaskMoved! }
                }
                dropGlueMoveIndex! + 1 => dropGlueMoveIndex!
            }
        }
        (not dropTaskMoved! and dropTask.typeOrigin == 13) -> if {
            "  " -> print
            dropTask -> emitDropPathName
            "_data = extractvalue %sl.array.i32 " -> print
            dropTask -> emitDropValueName
            ", 0" -> println
            "  call void @free(ptr " -> print
            dropTask -> emitDropPathName
            "_data)" -> println
        } else {
        (not dropTaskMoved! and dropTask.typeOrigin == 15) -> if {
            "  " -> print
            dropTask -> emitDropPathName
            "_keys = extractvalue %sl.dict " -> print
            dropTask -> emitDropValueName
            ", 0" -> println
            "  call void @free(ptr " -> print
            dropTask -> emitDropPathName
            "_keys)" -> println
            "  " -> print
            dropTask -> emitDropPathName
            "_values = extractvalue %sl.dict " -> print
            dropTask -> emitDropValueName
            ", 1" -> println
            "  call void @free(ptr " -> print
            dropTask -> emitDropPathName
            "_values)" -> println
        } else {
        (not dropTaskMoved! and (dropTask.typeOrigin == 0 or dropTask.typeOrigin == 2)) -> if {
            context.sources[dropTask.typeModule] -> symbols.collect => dropStructTable!
            0 => dropFieldOrdinal!
            0 => dropFieldIndex!
            dropFieldIndex! < (dropStructTable! -> len) -> while {
                dropStructTable![dropFieldIndex!] => dropField
                (dropField.kind == 26 and dropField.parent == dropTask.typeSymbol) -> if {
                    -1 => dropFieldOrigin!
                    -1 => dropFieldModule!
                    -1 => dropFieldSymbol!
                    0 => dropCompositeIndex!
                    dropCompositeIndex! < (context.composite -> len) -> while {
                        context.composite[dropCompositeIndex!] => dropCompositeType
                        (dropCompositeType.sourceModule == dropTask.typeModule and dropCompositeType.typeAst == dropField.typeNode) -> if {
                            dropCompositeType.kind == 3 -> if { 13 => dropFieldOrigin! }
                            dropCompositeType.kind == 5 -> if { 15 => dropFieldOrigin! }
                        }
                        dropCompositeIndex! + 1 => dropCompositeIndex!
                    }
                    0 => dropNominalIndex!
                    dropNominalIndex! < (context.nominal -> len) -> while {
                        context.nominal[dropNominalIndex!] => dropNominalType
                        (dropNominalType.sourceModule == dropTask.typeModule and dropNominalType.typeAst == dropField.typeNode and (dropNominalType.origin == 0 or dropNominalType.origin == 2)) -> if {
                            dropNominalType.origin => dropFieldOrigin!
                            dropNominalType.targetModule => dropFieldModule!
                            dropNominalType.targetSymbol => dropFieldSymbol!
                        }
                        dropNominalIndex! + 1 => dropNominalIndex!
                    }
                    dropFieldOrigin! >= 0 -> if {
                        dropTasks! -> len => dropFieldPath
                        "  %dropg$(dropTask.nameRoot)_p$dropFieldPath = extractvalue %sl.struct.m$(dropTask.typeModule)_s$(dropTask.typeSymbol) " -> print
                        dropTask -> emitDropValueName
                        ", $(dropFieldOrdinal!)" -> println
                        dropTasks! -> push(DropGlueRequest {
                            typeOrigin: dropFieldOrigin!
                            typeModule: dropFieldModule!
                            typeSymbol: dropFieldSymbol!
                            valueKind: 2
                            valueIndex: -1
                            nameRoot: dropTask.nameRoot
                            pathCode: dropFieldPath
                            bindingIndex: dropTask.bindingIndex
                            regionIndex: dropTask.regionIndex
                            beforeAst: dropTask.beforeAst
                            parentTask: dropTaskIndex!
                            fieldOrdinal: dropFieldOrdinal!
                            hasPartialMoves: dropTask.hasPartialMoves
                        })
                    }
                    dropFieldOrdinal! + 1 => dropFieldOrdinal!
                }
                dropFieldIndex! + 1 => dropFieldIndex!
            }
        }
        }
        }
        dropTaskIndex! + 1 => dropTaskIndex!
        }
    }
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
    mutableBindingRoot bindingIndex: Int -> Int {
        context.ir[bindingIndex] => binding
        binding.parent => bindingOwner!
        (bindingOwner! >= 0 and context.ir[bindingOwner!].kind != 0 and context.ir[bindingOwner!].kind != 11) -> while {
            context.ir[bindingOwner!].parent => bindingOwner!
        }
        context.sources[binding.sourceModule] -> lexer.lex => bindingTokens!
        bindingTokens![binding.payloadToken] => bindingName
        context.sources[binding.sourceModule] -> ast.lower => bindingNodes!
        bindingIndex => rootIndex!
        bindingNodes![binding.astNode].start => rootStart!
        0 => candidateIndex!
        candidateIndex! < (context.ir -> len) -> while {
            context.ir[candidateIndex!] => candidate
            (candidate.kind == 17 and candidate.flags == 1 and candidate.sourceModule == binding.sourceModule) -> if {
                candidate.parent => candidateOwner!
                (candidateOwner! >= 0 and context.ir[candidateOwner!].kind != 0 and context.ir[candidateOwner!].kind != 11) -> while {
                    context.ir[candidateOwner!].parent => candidateOwner!
                }
                candidateOwner! == bindingOwner! -> if {
                    bindingTokens![candidate.payloadToken] => candidateName
                    candidateName.span.length == bindingName.span.length => sameName!
                    UIntSize(0) => nameByte!
                    (sameName! and nameByte! < bindingName.span.length) -> while {
                        context.sources[binding.sourceModule] -> byte(bindingName.span.start + nameByte!) => bindingByte
                        context.sources[binding.sourceModule] -> byte(candidateName.span.start + nameByte!) => candidateByte
                        bindingByte != candidateByte -> if { false => sameName! }
                        nameByte! + UIntSize(1) => nameByte!
                    }
                    sameName! -> if {
                        bindingNodes![candidate.astNode].start => candidateStart
                        candidateStart < rootStart! -> if {
                            candidateIndex! => rootIndex!
                            candidateStart => rootStart!
                        }
                    }
                }
            }
            candidateIndex! + 1 => candidateIndex!
        }
        rootIndex!
    }
    writeWhileValue request: WhileValueRequest -> Unit {
        context.ir[request.nodeIndex] => value
        (value.kind == 3 or value.kind == 4) -> if {
            context.sources[value.sourceModule] -> lexer.lex => valueTokens!
            valueTokens![value.payloadToken] => valueToken
            value.kind == 3 -> if {
                context.sources[value.sourceModule] -> slice(valueToken.span.start, valueToken.span.length) -> print
            } else {
                ((context.sources[value.sourceModule] -> byte(valueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
            }
        } else {
            (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and value.kind == 5 and value.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if {
                "%arg" -> print
            } else {
                "%while$(request.whileIndex)_v$(request.nodeIndex)" -> print
            }
        }
    }
    emitWhileBranch request: WhileBranchRequest -> Unit {
        request.whileIndex => whileIndex
        request.ownerIndex => ownerIndex
        context.ir[whileIndex] => whileNode
        [WhileBoolTask; ~] => boolTasks!
        boolTasks! -> push(WhileBoolTask {
            kind: 0
            nodeIndex: whileNode.operand0
            trueKind: 0
            trueNode: whileIndex
            falseKind: 1
            falseNode: whileIndex
        })
        1 => boolTaskSize!
        boolTaskSize! > 0 -> while {
            boolTaskSize! - 1 => boolTaskSize!
            boolTasks![boolTaskSize!] => boolTask
            boolTask.kind == 1 -> if {
                "while$(whileIndex)_bool$(boolTask.nodeIndex)_rhs:" -> println
            } else {
                context.ir[boolTask.nodeIndex] => boolNode
                (boolNode.kind == 8 and (boolNode.opcode == -25 or boolNode.opcode == -24)) -> if {
                    WhileBoolTask {
                        kind: 0
                        nodeIndex: boolNode.operand1
                        trueKind: boolTask.trueKind
                        trueNode: boolTask.trueNode
                        falseKind: boolTask.falseKind
                        falseNode: boolTask.falseNode
                    } => rightTask
                    boolTaskSize! == (boolTasks! -> len) -> if { boolTasks! -> push(rightTask) } else { rightTask => boolTasks![boolTaskSize!] }
                    boolTaskSize! + 1 => boolTaskSize!
                    WhileBoolTask {
                        kind: 1
                        nodeIndex: boolTask.nodeIndex
                        trueKind: -1
                        trueNode: -1
                        falseKind: -1
                        falseNode: -1
                    } => labelTask
                    boolTaskSize! == (boolTasks! -> len) -> if { boolTasks! -> push(labelTask) } else { labelTask => boolTasks![boolTaskSize!] }
                    boolTaskSize! + 1 => boolTaskSize!
                    boolTask.trueKind => leftTrueKind!
                    boolTask.trueNode => leftTrueNode!
                    boolTask.falseKind => leftFalseKind!
                    boolTask.falseNode => leftFalseNode!
                    boolNode.opcode == -25 -> if {
                        2 => leftTrueKind!
                        boolTask.nodeIndex => leftTrueNode!
                    } else {
                        2 => leftFalseKind!
                        boolTask.nodeIndex => leftFalseNode!
                    }
                    WhileBoolTask {
                        kind: 0
                        nodeIndex: boolNode.operand0
                        trueKind: leftTrueKind!
                        trueNode: leftTrueNode!
                        falseKind: leftFalseKind!
                        falseNode: leftFalseNode!
                    } => leftTask
                    boolTaskSize! == (boolTasks! -> len) -> if { boolTasks! -> push(leftTask) } else { leftTask => boolTasks![boolTaskSize!] }
                    boolTaskSize! + 1 => boolTaskSize!
                } else {
                (boolNode.kind == 7 and boolNode.opcode == -26) -> if {
                    WhileBoolTask {
                        kind: 0
                        nodeIndex: boolNode.operand0
                        trueKind: boolTask.falseKind
                        trueNode: boolTask.falseNode
                        falseKind: boolTask.trueKind
                        falseNode: boolTask.trueNode
                    } => notTask
                    boolTaskSize! == (boolTasks! -> len) -> if { boolTasks! -> push(notTask) } else { notTask => boolTasks![boolTaskSize!] }
                    boolTaskSize! + 1 => boolTaskSize!
                } else {
                    [Int; ~] => valueTasks!
                    valueTasks! -> push(boolTask.nodeIndex + 1)
                    1 => valueTaskSize!
                    valueTaskSize! > 0 -> while {
                        valueTaskSize! - 1 => valueTaskSize!
                        valueTasks![valueTaskSize!] => valueTask
                        valueTask > 0 -> if {
                            valueTask - 1 => valueNodeIndex
                            context.ir[valueNodeIndex] => valueNode
                            0 - valueNodeIndex - 1 => emitValueTask
                            valueTaskSize! == (valueTasks! -> len) -> if { valueTasks! -> push(emitValueTask) } else { emitValueTask => valueTasks![valueTaskSize!] }
                            valueTaskSize! + 1 => valueTaskSize!
                            (valueNode.kind == 6 or valueNode.kind == 7 or valueNode.kind == 8) -> if {
                                (valueNode.kind == 8 and valueNode.operand1 >= 0) -> if {
                                    valueNode.operand1 + 1 => secondValueTask
                                    valueTaskSize! == (valueTasks! -> len) -> if { valueTasks! -> push(secondValueTask) } else { secondValueTask => valueTasks![valueTaskSize!] }
                                    valueTaskSize! + 1 => valueTaskSize!
                                }
                                valueNode.operand0 >= 0 -> if {
                                    valueNode.operand0 + 1 => firstValueTask
                                    valueTaskSize! == (valueTasks! -> len) -> if { valueTasks! -> push(firstValueTask) } else { firstValueTask => valueTasks![valueTaskSize!] }
                                    valueTaskSize! + 1 => valueTaskSize!
                                }
                            }
                        } else {
                            0 - valueTask - 1 => valueNodeIndex
                            context.ir[valueNodeIndex] => valueNode
                            valueNode.kind == 5 -> if {
                                false => parameterValue!
                                (ownerIndex >= 0 and context.ir[ownerIndex].kind == 0 and context.ir[ownerIndex].operand1 >= 0 and valueNode.symbol == context.ir[context.ir[ownerIndex].operand1].symbol) -> if { true => parameterValue! }
                                not parameterValue! -> if {
                                    -1 => valueBindingIndex!
                                    0 => valueBindingSearch!
                                    valueBindingSearch! < (context.ir -> len) -> while {
                                        (context.ir[valueBindingSearch!].kind == 17 and context.ir[valueBindingSearch!].symbol == valueNode.symbol) -> if { valueBindingSearch! => valueBindingIndex! }
                                        valueBindingSearch! + 1 => valueBindingSearch!
                                    }
                                    valueBindingIndex! >= 0 -> if {
                                        context.ir[valueBindingIndex!] => valueBinding
                                        valueBinding.flags == 1 -> if {
                                            valueBindingIndex! -> mutableBindingRoot => valueRoot
                                            "  %while$(whileIndex)_v$(valueNodeIndex) = load " -> print
                                            valueNode -> writeType
                                            ", ptr %slot$(valueRoot), align $(valueNode.typeSymbol -> storageAlign)" -> println
                                        } else {
                                            context.ir[valueBinding.operand0] => bindingOperand
                                            "  %while$(whileIndex)_v$(valueNodeIndex) = freeze " -> print
                                            valueNode -> writeType
                                            " " -> print
                                            (bindingOperand.kind == 3 or bindingOperand.kind == 4) -> if {
                                                context.sources[bindingOperand.sourceModule] -> lexer.lex => bindingTokens!
                                                bindingTokens![bindingOperand.payloadToken] => bindingToken
                                                bindingOperand.kind == 3 -> if { context.sources[bindingOperand.sourceModule] -> slice(bindingToken.span.start, bindingToken.span.length) -> print } else { ((context.sources[bindingOperand.sourceModule] -> byte(bindingToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print }
                                            } else { "%v$(valueBinding.operand0)" -> print }
                                            "" -> println
                                        }
                                    }
                                }
                            }
                            valueNode.kind == 6 -> if {
                                "  %while$(whileIndex)_v$(valueNodeIndex) = call " -> print
                                valueNode -> writeType
                                " @sl_m$(valueNode.targetModule)_s$(valueNode.symbol)(" -> print
                                valueNode.operand0 >= 0 -> if {
                                    context.ir[valueNode.operand0] => callArgument
                                    callArgument -> writeType
                                    " " -> print
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                }
                                ")" -> println
                            }
                            (valueNode.kind == 7 or valueNode.kind == 8) -> if {
                                context.ir[valueNode.operand0] => left
                                "  %while$(whileIndex)_v$(valueNodeIndex) = " -> print
                                valueNode.kind == 7 -> if {
                                    valueNode.opcode == -26 -> if { "xor" -> print } else { "sub" -> print }
                                } else {
                                    valueNode.opcode == grammar.tokenIdPlus -> if { "add" -> print }
                                    valueNode.opcode == grammar.tokenIdMinus -> if { "sub" -> print }
                                    valueNode.opcode == grammar.tokenIdStar -> if { "mul" -> print }
                                    valueNode.opcode == grammar.tokenIdSlash -> if { "sdiv" -> print }
                                    valueNode.opcode == grammar.tokenIdPercent -> if { "srem" -> print }
                                    valueNode.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" -> print }
                                    valueNode.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" -> print }
                                    valueNode.opcode == grammar.tokenIdLess -> if { "icmp slt" -> print }
                                    valueNode.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" -> print }
                                    valueNode.opcode == grammar.tokenIdGreater -> if { "icmp sgt" -> print }
                                    valueNode.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" -> print }
                                    valueNode.opcode == -24 -> if { "or" -> print }
                                    valueNode.opcode == -25 -> if { "and" -> print }
                                }
                                " " -> print
                                left -> writeType
                                " " -> print
                                valueNode.kind == 7 -> if {
                                    valueNode.opcode == -26 -> if {
                                        WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                        ", true" -> println
                                    } else {
                                        "0, " -> print
                                        WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                        "" -> println
                                    }
                                } else {
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                    ", " -> print
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                    "" -> println
                                }
                            }
                        }
                    }
                    "  br i1 " -> print
                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: boolTask.nodeIndex } -> writeWhileValue
                    ", label %" -> print
                    boolTask.trueKind == 0 -> if { "while$(whileIndex)_body" -> print }
                    boolTask.trueKind == 1 -> if { "while$(whileIndex)_exit" -> print }
                    boolTask.trueKind == 2 -> if { "while$(whileIndex)_bool$(boolTask.trueNode)_rhs" -> print }
                    ", label %" -> print
                    boolTask.falseKind == 0 -> if { "while$(whileIndex)_body" -> print }
                    boolTask.falseKind == 1 -> if { "while$(whileIndex)_exit" -> print }
                    boolTask.falseKind == 2 -> if { "while$(whileIndex)_bool$(boolTask.falseNode)_rhs" -> print }
                    "" -> println
                }
                }
            }
        }
    }
    emitOwnedDrops request: OwnedDropRequest -> Unit {
        context.ir[request.regionIndex] => dropRegion
        context.sources[dropRegion.sourceModule] -> ast.lower => dropAst!
        UIntSize(0) => dropBeforeStart!
        request.beforeAst >= 0 -> if { dropAst![request.beforeAst].start => dropBeforeStart! }
        context.ir -> len => ownedDropIndex!
        ownedDropIndex! > 0 -> while {
            ownedDropIndex! - 1 => ownedDropIndex!
            context.ir[ownedDropIndex!] => ownedDropCandidate
            true => ownedDropBeforeEdge!
            request.beforeAst >= 0 -> if {
                dropAst![ownedDropCandidate.astNode].start >= dropBeforeStart! -> if { false => ownedDropBeforeEdge! }
            }
            false => ownedDropMoved!
            false => ownedDropHasPartialMoves!
            (ownedDropCandidate.kind == 17 and (ownedDropCandidate -> ownsType)) -> if {
                0 => ownedMoveIndex!
                (ownedMoveIndex! < (context.moves -> len) and not ownedDropMoved!) -> while {
                    context.moves[ownedMoveIndex!] => ownedMove
                    (ownedMove.memberIr >= 0 and ownedMove.sourceModule == ownedDropCandidate.sourceModule and ownedMove.symbol == ownedDropCandidate.symbol) -> if { true => ownedDropHasPartialMoves! }
                    (ownedMove.memberIr < 0 and ownedMove.sourceModule == ownedDropCandidate.sourceModule and ownedMove.symbol == ownedDropCandidate.symbol and ownedMove.regionIr == request.regionIndex) -> if {
                        context.ir[ownedMove.siteIr] => ownedMoveCall
                        true => ownedMoveBeforeEdge!
                        request.beforeAst >= 0 -> if {
                            dropAst![ownedMoveCall.astNode].start >= dropBeforeStart! -> if { false => ownedMoveBeforeEdge! }
                        }
                        (dropAst![ownedMoveCall.astNode].start > dropAst![ownedDropCandidate.astNode].start and ownedMoveBeforeEdge!) -> if { true => ownedDropMoved! }
                    }
                    ownedMoveIndex! + 1 => ownedMoveIndex!
                }
            }
            (ownedDropBeforeEdge! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isDynamicArrayType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!) = extractvalue %sl.array.i32 %v$(ownedDropCandidate.operand0), 0" -> println
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!))" -> println
            }
            (ownedDropBeforeEdge! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isDictionaryType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_keys = extractvalue %sl.dict %v$(ownedDropCandidate.operand0), 0" -> println
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_keys)" -> println
                "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_values = extractvalue %sl.dict %v$(ownedDropCandidate.operand0), 1" -> println
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_values)" -> println
            }
            (ownedDropBeforeEdge! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isNominalStructType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                DropGlueRequest {
                    typeOrigin: ownedDropCandidate.typeOrigin
                    typeModule: ownedDropCandidate.typeModule
                    typeSymbol: ownedDropCandidate.typeSymbol
                    valueKind: 0
                    valueIndex: ownedDropCandidate.operand0
                    nameRoot: request.edgeIndex * 1000 + ownedDropIndex!
                    pathCode: 0
                    bindingIndex: ownedDropIndex!
                    regionIndex: request.regionIndex
                    beforeAst: request.beforeAst
                    parentTask: -1
                    fieldOrdinal: -1
                    hasPartialMoves: ownedDropHasPartialMoves!
                } -> emitDropGlue
            }
        }
    }
    regionReturns regionIndex: Int -> Bool {
        false => returns!
        0 => returnSearch!
        returnSearch! < (context.ir -> len) -> while {
            (context.ir[returnSearch!].kind == 23 and context.ir[returnSearch!].parent == regionIndex) -> if { true => returns! }
            returnSearch! + 1 => returnSearch!
        }
        returns!
    }
    emitRegion regionIndex: Int -> Unit {
        context.ir[regionIndex] => region
        context.sources[region.sourceModule] -> ast.lower => regionAst!
        region.parent => ownerIndex!
        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind != 0 and context.ir[ownerIndex!].kind != 11) -> while { context.ir[ownerIndex!].parent => ownerIndex! }
        [Int; ~] => regionEventKinds!
        [Int; ~] => regionOrder!
        [Int; ~] => regionTaskKinds!
        [Int; ~] => regionTaskNodes!
        [Bool; ~] => ifActive!
        [Bool; ~] => ifThenReachesMerge!
        [Bool; ~] => loopActive!
        0 => regionStateIndex!
        regionStateIndex! < (context.ir -> len) -> while {
            ifActive! -> push(false)
            ifThenReachesMerge! -> push(false)
            loopActive! -> push(false)
            regionStateIndex! + 1 => regionStateIndex!
        }
        false => regionTerminated!
        regionTaskKinds! -> push(0)
        regionTaskNodes! -> push(regionIndex)
        1 => regionTaskSize!
        regionTaskSize! > 0 -> while {
            regionTaskSize! - 1 => regionTaskSize!
            regionTaskKinds![regionTaskSize!] => regionTaskKind
            regionTaskNodes![regionTaskSize!] => regionTaskNode
            regionTaskKind == 0 -> if {
                [Bool; ~] => localScheduled!
                0 => localScheduleInit!
                localScheduleInit! < (context.ir -> len) -> while {
                    localScheduled! -> push(false)
                    localScheduleInit! + 1 => localScheduleInit!
                }
                [Int; ~] => localOrder!
                true => localProgress!
                localProgress! -> while {
                    false => localProgress!
                    0 => localCandidateIndex!
                    localCandidateIndex! < (context.ir -> len) -> while {
                        not localScheduled![localCandidateIndex!] -> if {
                            context.ir[localCandidateIndex!] => localCandidate
                            localCandidate.parent => localAncestor!
                            false => belongsToLocalRegion!
                            false => belongsToNestedRegion!
                            (localAncestor! >= 0 and not belongsToLocalRegion! and not belongsToNestedRegion!) -> while {
                                localAncestor! == regionTaskNode -> if { true => belongsToLocalRegion! } else {
                                    (context.ir[localAncestor!].kind == 19 or context.ir[localAncestor!].kind == 20) -> if { true => belongsToNestedRegion! } else { context.ir[localAncestor!].parent => localAncestor! }
                                }
                            }
                            belongsToLocalRegion! -> if {
                                localCandidate.kind == 19 -> if {
                                    true => localScheduled![localCandidateIndex!]
                                    true => localProgress!
                                }
                                localCandidate.kind != 19 -> if {
                                    true => localReady!
                                    localCandidateIndex! => localStatementRoot!
                                    localCandidate.parent => localRootParent!
                                    (localRootParent! >= 0 and localRootParent! != regionTaskNode) -> while {
                                        localRootParent! => localStatementRoot!
                                        context.ir[localRootParent!].parent => localRootParent!
                                    }
                                    -1 => localPreviousRoot!
                                    UIntSize(0) => localPreviousRootStart!
                                    0 => localRootSearch!
                                    localRootSearch! < (context.ir -> len) -> while {
                                        context.ir[localRootSearch!] => localRootCandidate
                                        (localRootCandidate.parent == regionTaskNode and regionAst![localRootCandidate.astNode].start < regionAst![context.ir[localStatementRoot!].astNode].start) -> if {
                                            regionAst![localRootCandidate.astNode].start => localRootCandidateStart
                                            (localPreviousRoot! < 0 or localRootCandidateStart > localPreviousRootStart!) -> if {
                                                localRootSearch! => localPreviousRoot!
                                                localRootCandidateStart => localPreviousRootStart!
                                            }
                                        }
                                        localRootSearch! + 1 => localRootSearch!
                                    }
                                    localPreviousRoot! >= 0 -> if {
                                        0 => localPreviousMemberSearch!
                                        localPreviousMemberSearch! < (context.ir -> len) -> while {
                                            not localScheduled![localPreviousMemberSearch!] -> if {
                                                localPreviousMemberSearch! => localPreviousMemberRoot!
                                                context.ir[localPreviousMemberSearch!].parent => localPreviousMemberParent!
                                                false => localPreviousMemberNested!
                                                (localPreviousMemberParent! >= 0 and localPreviousMemberParent! != regionTaskNode and not localPreviousMemberNested!) -> while {
                                                    (context.ir[localPreviousMemberParent!].kind == 19 or context.ir[localPreviousMemberParent!].kind == 20) -> if { true => localPreviousMemberNested! } else {
                                                        localPreviousMemberParent! => localPreviousMemberRoot!
                                                        context.ir[localPreviousMemberParent!].parent => localPreviousMemberParent!
                                                    }
                                                }
                                                (not localPreviousMemberNested! and localPreviousMemberParent! == regionTaskNode and localPreviousMemberRoot! == localPreviousRoot!) -> if { false => localReady! }
                                            }
                                            localPreviousMemberSearch! + 1 => localPreviousMemberSearch!
                                        }
                                    }
                                    (localCandidate.kind != 20 and localCandidate.operand0 >= 0) -> if {
                                        context.ir[localCandidate.operand0].parent => localOperandAncestor!
                                        false => localOperandBelongs!
                                        (localOperandAncestor! >= 0 and not localOperandBelongs!) -> while {
                                            localOperandAncestor! == regionTaskNode -> if { true => localOperandBelongs! } else { context.ir[localOperandAncestor!].parent => localOperandAncestor! }
                                        }
                                        (localOperandBelongs! and not localScheduled![localCandidate.operand0]) -> if { false => localReady! }
                                    }
                                    (localCandidate.kind != 18 and localCandidate.kind != 20 and localCandidate.operand1 >= 0) -> if {
                                        context.ir[localCandidate.operand1].parent => localSecondAncestor!
                                        false => localSecondBelongs!
                                        (localSecondAncestor! >= 0 and not localSecondBelongs!) -> while {
                                            localSecondAncestor! == regionTaskNode -> if { true => localSecondBelongs! } else { context.ir[localSecondAncestor!].parent => localSecondAncestor! }
                                        }
                                        (localSecondBelongs! and not localScheduled![localCandidate.operand1]) -> if { false => localReady! }
                                    }
                                    (localCandidate.kind == 12 or localCandidate.kind == 14 or localCandidate.kind == 16) -> if {
                                        localCandidate.operand0 => localAggregateOperand!
                                        localAggregateOperand! >= 0 -> while {
                                            not localScheduled![localAggregateOperand!] -> if { false => localReady! }
                                            context.ir[localAggregateOperand!].nextOperand => localAggregateOperand!
                                        }
                                    }
                                    localReady! -> if {
                                        localOrder! -> push(localCandidateIndex!)
                                        true => localScheduled![localCandidateIndex!]
                                        true => localProgress!
                                    }
                                }
                            }
                        }
                        localCandidateIndex! + 1 => localCandidateIndex!
                    }
                }
                localOrder! -> len => localReverseIndex!
                localReverseIndex! > 0 -> while {
                    localReverseIndex! - 1 => localReverseIndex!
                    localOrder![localReverseIndex!] => localNodeIndex
                    context.ir[localNodeIndex] => localNode
                    localNode.kind == 18 -> if {
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(4)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            4 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                        localNode.nextOperand >= 0 -> if {
                            regionTaskSize! == (regionTaskKinds! -> len) -> if {
                                regionTaskKinds! -> push(0)
                                regionTaskNodes! -> push(localNode.nextOperand)
                            } else {
                                0 => regionTaskKinds![regionTaskSize!]
                                localNode.nextOperand => regionTaskNodes![regionTaskSize!]
                            }
                            regionTaskSize! + 1 => regionTaskSize!
                            regionTaskSize! == (regionTaskKinds! -> len) -> if {
                                regionTaskKinds! -> push(3)
                                regionTaskNodes! -> push(localNodeIndex)
                            } else {
                                3 => regionTaskKinds![regionTaskSize!]
                                localNodeIndex => regionTaskNodes![regionTaskSize!]
                            }
                            regionTaskSize! + 1 => regionTaskSize!
                        }
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(0)
                            regionTaskNodes! -> push(localNode.operand1)
                        } else {
                            0 => regionTaskKinds![regionTaskSize!]
                            localNode.operand1 => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(2)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            2 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                    } else {
                    localNode.kind == 20 -> if {
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(8)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            8 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(0)
                            regionTaskNodes! -> push(localNode.operand1)
                        } else {
                            0 => regionTaskKinds![regionTaskSize!]
                            localNode.operand1 => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(7)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            7 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(6)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            6 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                    } else {
                        regionTaskSize! == (regionTaskKinds! -> len) -> if {
                            regionTaskKinds! -> push(1)
                            regionTaskNodes! -> push(localNodeIndex)
                        } else {
                            1 => regionTaskKinds![regionTaskSize!]
                            localNodeIndex => regionTaskNodes![regionTaskSize!]
                        }
                        regionTaskSize! + 1 => regionTaskSize!
                    }
                    }
                }
            } else {
                regionEventKinds! -> push(regionTaskKind)
                regionOrder! -> push(regionTaskNode)
            }
        }
        0 => regionOrderIndex!
        regionOrderIndex! < (regionOrder! -> len) -> while {
            regionOrder![regionOrderIndex!] => regionNodeIndex!
            context.ir[regionNodeIndex!] => regionNode
            regionEventKinds![regionOrderIndex!] => regionEventKind
            regionEventKind == 1 -> if {
            not regionTerminated! -> if {
            regionNode.kind == 23 -> if {
                -1 => returnedSymbol!
                regionNode.operand0 >= 0 -> if {
                    context.ir[regionNode.operand0] => returnedNode
                    returnedNode.kind == 5 -> if { returnedNode.symbol => returnedSymbol! }
                }
                regionNode.parent => returnCleanupAncestor!
                (returnCleanupAncestor! >= 0 and returnCleanupAncestor! != ownerIndex!) -> while {
                    (context.ir[returnCleanupAncestor!].kind == 19 or context.ir[returnCleanupAncestor!].kind == 1) -> if {
                        OwnedDropRequest { regionIndex: returnCleanupAncestor!, beforeAst: regionNode.astNode, edgeIndex: regionNodeIndex! * 10 + 7, transferredSymbol: returnedSymbol! } -> emitOwnedDrops
                    }
                    context.ir[returnCleanupAncestor!].parent => returnCleanupAncestor!
                }
                ownerIndex! >= 0 -> if {
                    context.ir[ownerIndex!] => returnOwner
                    returnOwner.operand1 >= 0 -> if {
                        context.ir[returnOwner.operand1] => returnParameter
                        returnParameter.symbol != returnedSymbol! -> if {
                            (returnParameter.typeOrigin == 13 and returnParameter.flags % 2 == 1) -> if {
                                "  %return$(regionNodeIndex!)_drop_arg = extractvalue %sl.array.i32 %arg, 0" -> println
                                "  call void @free(ptr %return$(regionNodeIndex!)_drop_arg)" -> println
                            }
                            (returnParameter.typeOrigin == 15 and returnParameter.flags % 2 == 1) -> if {
                                "  %return$(regionNodeIndex!)_drop_arg_keys = extractvalue %sl.dict %arg, 0" -> println
                                "  call void @free(ptr %return$(regionNodeIndex!)_drop_arg_keys)" -> println
                                "  %return$(regionNodeIndex!)_drop_arg_values = extractvalue %sl.dict %arg, 1" -> println
                                "  call void @free(ptr %return$(regionNodeIndex!)_drop_arg_values)" -> println
                            }
                        }
                    }
                    (returnOwner.typeOrigin == 1 and returnOwner.typeSymbol == 0) -> if {
                        "  ret void" -> println
                    } else {
                        "  ret " -> print
                        returnOwner -> writeType
                        " " -> print
                        context.ir[regionNode.operand0] => returnedValue
                        (returnedValue.kind == 3 or returnedValue.kind == 4) -> if {
                            context.sources[returnedValue.sourceModule] -> lexer.lex => returnValueTokens!
                            returnValueTokens![returnedValue.payloadToken] => returnValueToken
                            returnedValue.kind == 3 -> if { context.sources[returnedValue.sourceModule] -> slice(returnValueToken.span.start, returnValueToken.span.length) -> print } else {
                                ((context.sources[returnedValue.sourceModule] -> byte(returnValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (returnOwner.operand1 >= 0 and returnedValue.kind == 5 and returnedValue.symbol == context.ir[returnOwner.operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                        }
                        "" -> println
                    }
                }
                true => regionTerminated!
            }
            regionNode.kind == 22 -> if {
                context.ir[regionNode.operand1] => guardCondition
                "  br i1 " -> print
                guardCondition.kind == 4 -> if {
                    context.sources[guardCondition.sourceModule] -> lexer.lex => guardConditionTokens!
                    guardConditionTokens![guardCondition.payloadToken] => guardConditionToken
                    ((context.sources[guardCondition.sourceModule] -> byte(guardConditionToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                } else {
                    (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and guardCondition.kind == 5 and guardCondition.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand1)" -> print }
                }
                ", label %guard$(regionNodeIndex!)_exit, label %guard$(regionNodeIndex!)_next" -> println
                "guard$(regionNodeIndex!)_exit:" -> println
                regionNode.parent => guardCleanupAncestor!
                (guardCleanupAncestor! >= 0 and guardCleanupAncestor! != regionNode.operand0) -> while {
                    context.ir[guardCleanupAncestor!].kind == 19 -> if {
                        OwnedDropRequest { regionIndex: guardCleanupAncestor!, beforeAst: regionNode.astNode, edgeIndex: regionNodeIndex! * 10 + 8, transferredSymbol: -1 } -> emitOwnedDrops
                    }
                    context.ir[guardCleanupAncestor!].parent => guardCleanupAncestor!
                }
                regionNode.opcode == 1 -> if {
                    "  br label %while$(regionNode.operand0)_header" -> println
                } else {
                    "  br label %while$(regionNode.operand0)_exit" -> println
                }
                "guard$(regionNodeIndex!)_next:" -> println
                false => regionTerminated!
            }
            regionNode.kind == 21 -> if {
                "  br label %cleanup$(regionNodeIndex!)" -> println
                "cleanup$(regionNodeIndex!):" -> println
                regionNode.parent => cleanupAncestor!
                (cleanupAncestor! >= 0 and cleanupAncestor! != regionNode.operand0) -> while {
                    context.ir[cleanupAncestor!].kind == 19 -> if {
                        OwnedDropRequest { regionIndex: cleanupAncestor!, beforeAst: regionNode.astNode, edgeIndex: regionNodeIndex! * 10 + 9, transferredSymbol: -1 } -> emitOwnedDrops
                    }
                    context.ir[cleanupAncestor!].parent => cleanupAncestor!
                }
                regionNode.opcode == 1 -> if {
                    "  br label %while$(regionNode.operand0)_header" -> println
                } else {
                    "  br label %while$(regionNode.operand0)_exit" -> println
                }
                true => regionTerminated!
            }
            (regionNode.kind != 21 and regionNode.kind != 22 and regionNode.kind != 23) -> if {
            (regionNode.kind == 5 and ownerIndex! >= 0 and not (context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionNode.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol)) -> if {
                -1 => regionBindingIndex!
                0 => regionBindingSearch!
                regionBindingSearch! < (context.ir -> len) -> while {
                    (context.ir[regionBindingSearch!].kind == 17 and context.ir[regionBindingSearch!].symbol == regionNode.symbol) -> if { regionBindingSearch! => regionBindingIndex! }
                    regionBindingSearch! + 1 => regionBindingSearch!
                }
                regionBindingIndex! >= 0 -> if {
                    context.ir[regionBindingIndex!] => regionBinding
                    regionBinding.flags == 1 -> if {
                        regionBindingIndex! -> mutableBindingRoot => regionBindingRoot
                        "  %v$(regionNodeIndex!) = load " -> print
                        regionNode -> writeType
                        ", ptr %slot$(regionBindingRoot), align " -> print
                        "$(regionNode.typeSymbol -> storageAlign)" -> println
                    } else {
                        context.ir[regionBinding.operand0] => regionBindingValue
                        "  %v$(regionNodeIndex!) = freeze " -> print
                        regionNode -> writeType
                        " " -> print
                        (regionBindingValue.kind == 3 or regionBindingValue.kind == 4) -> if {
                            context.sources[regionBindingValue.sourceModule] -> lexer.lex => regionBindingTokens!
                            regionBindingTokens![regionBindingValue.payloadToken] => regionBindingToken
                            regionBindingValue.kind == 3 -> if { context.sources[regionBindingValue.sourceModule] -> slice(regionBindingToken.span.start, regionBindingToken.span.length) -> print } else {
                                ((context.sources[regionBindingValue.sourceModule] -> byte(regionBindingToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else { "%v$(regionBinding.operand0)" -> print }
                        "" -> println
                    }
                }
            }
            (regionNode.kind == 17 and regionNode.flags == 1) -> if {
                regionNodeIndex! -> mutableBindingRoot => mutableRegionRoot
                context.ir[regionNode.operand0] => mutableRegionValue
                "  store " -> print
                regionNode -> writeType
                " " -> print
                (mutableRegionValue.kind == 3 or mutableRegionValue.kind == 4) -> if {
                    context.sources[mutableRegionValue.sourceModule] -> lexer.lex => mutableRegionTokens!
                    mutableRegionTokens![mutableRegionValue.payloadToken] => mutableRegionToken
                    mutableRegionValue.kind == 3 -> if { context.sources[mutableRegionValue.sourceModule] -> slice(mutableRegionToken.span.start, mutableRegionToken.span.length) -> print } else {
                        ((context.sources[mutableRegionValue.sourceModule] -> byte(mutableRegionToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                    }
                } else { "%v$(regionNode.operand0)" -> print }
                ", ptr %slot$(mutableRegionRoot), align " -> print
                "$(regionNode.typeSymbol -> storageAlign)" -> println
            }
            (regionNode.kind == 14 and regionNode.typeOrigin == 13) -> if {
                0 => regionArrayLength!
                regionNode.operand0 => regionArrayCountIndex!
                regionArrayCountIndex! >= 0 -> while {
                    regionArrayLength! + 1 => regionArrayLength!
                    context.ir[regionArrayCountIndex!].nextOperand => regionArrayCountIndex!
                }
                regionArrayLength! * 4 => regionArrayByteLength
                "  %v$(regionNodeIndex!)_data = call ptr @malloc(i64 $regionArrayByteLength)" -> println
                regionNode.operand0 => regionArrayElementIndex!
                0 => regionArrayElementPosition!
                regionArrayElementIndex! >= 0 -> while {
                    context.ir[regionArrayElementIndex!] => regionArrayElement
                    "  %v$(regionNodeIndex!)_ptr$(regionArrayElementPosition!) = getelementptr i32, ptr %v$(regionNodeIndex!)_data, i64 $(regionArrayElementPosition!)" -> println
                    "  store i32 " -> print
                    regionArrayElement.kind == 3 -> if {
                        context.sources[regionArrayElement.sourceModule] -> lexer.lex => regionArrayElementTokens!
                        regionArrayElementTokens![regionArrayElement.payloadToken] => regionArrayElementToken
                        context.sources[regionArrayElement.sourceModule] -> slice(regionArrayElementToken.span.start, regionArrayElementToken.span.length) -> print
                    } else { "%v$(regionArrayElementIndex!)" -> print }
                    ", ptr %v$(regionNodeIndex!)_ptr$(regionArrayElementPosition!), align 4" -> println
                    regionArrayElement.nextOperand => regionArrayElementIndex!
                    regionArrayElementPosition! + 1 => regionArrayElementPosition!
                }
                "  %v$(regionNodeIndex!)_0 = insertvalue %sl.array.i32 poison, ptr %v$(regionNodeIndex!)_data, 0" -> println
                "  %v$(regionNodeIndex!)_1 = insertvalue %sl.array.i32 %v$(regionNodeIndex!)_0, i64 $(regionArrayLength!), 1" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.array.i32 %v$(regionNodeIndex!)_1, i64 $(regionArrayLength!), 2" -> println
            }
            (regionNode.kind == 16 and regionNode.typeOrigin == 15) -> if {
                0 => regionDictionaryItemCount!
                regionNode.operand0 => regionDictionaryCountIndex!
                regionDictionaryCountIndex! >= 0 -> while {
                    regionDictionaryItemCount! + 1 => regionDictionaryItemCount!
                    context.ir[regionDictionaryCountIndex!].nextOperand => regionDictionaryCountIndex!
                }
                regionDictionaryItemCount! / 2 => regionDictionaryLength
                regionDictionaryLength * (regionNode.typeModule -> storageSize) => regionDictionaryKeyByteLength
                regionDictionaryLength * (regionNode.typeSymbol -> storageSize) => regionDictionaryValueByteLength
                "  %v$(regionNodeIndex!)_keys = call ptr @malloc(i64 $regionDictionaryKeyByteLength)" -> println
                "  %v$(regionNodeIndex!)_values = call ptr @malloc(i64 $regionDictionaryValueByteLength)" -> println
                regionNode.operand0 => regionDictionaryItemIndex!
                0 => regionDictionaryItemPosition!
                regionDictionaryItemIndex! >= 0 -> while {
                    context.ir[regionDictionaryItemIndex!] => regionDictionaryItem
                    regionDictionaryItemPosition! / 2 => regionDictionaryEntryPosition
                    regionDictionaryItemPosition! % 2 == 0 -> if { "keys" } else { "values" } => regionDictionarySide
                    regionDictionaryItemPosition! % 2 == 0 -> if { regionNode.typeModule } else { regionNode.typeSymbol } => regionDictionaryItemSymbol
                    "  %v$(regionNodeIndex!)_$(regionDictionarySide)_ptr$(regionDictionaryEntryPosition) = getelementptr " -> print
                    regionDictionaryItemSymbol -> llvmType -> print
                    ", ptr %v$(regionNodeIndex!)_$(regionDictionarySide), i64 $(regionDictionaryEntryPosition)" -> println
                    "  store " -> print
                    regionDictionaryItemSymbol -> llvmType -> print
                    " " -> print
                    (regionDictionaryItem.kind == 3 or regionDictionaryItem.kind == 4) -> if {
                        context.sources[regionDictionaryItem.sourceModule] -> lexer.lex => regionDictionaryItemTokens!
                        regionDictionaryItemTokens![regionDictionaryItem.payloadToken] => regionDictionaryItemToken
                        regionDictionaryItem.kind == 3 -> if {
                            context.sources[regionDictionaryItem.sourceModule] -> slice(regionDictionaryItemToken.span.start, regionDictionaryItemToken.span.length) -> print
                        } else {
                            ((context.sources[regionDictionaryItem.sourceModule] -> byte(regionDictionaryItemToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else { "%v$(regionDictionaryItemIndex!)" -> print }
                    ", ptr %v$(regionNodeIndex!)_$(regionDictionarySide)_ptr$(regionDictionaryEntryPosition), align $(regionDictionaryItemSymbol -> storageAlign)" -> println
                    regionDictionaryItem.nextOperand => regionDictionaryItemIndex!
                    regionDictionaryItemPosition! + 1 => regionDictionaryItemPosition!
                }
                "  %v$(regionNodeIndex!)_0 = insertvalue %sl.dict poison, ptr %v$(regionNodeIndex!)_keys, 0" -> println
                "  %v$(regionNodeIndex!)_1 = insertvalue %sl.dict %v$(regionNodeIndex!)_0, ptr %v$(regionNodeIndex!)_values, 1" -> println
                "  %v$(regionNodeIndex!)_2 = insertvalue %sl.dict %v$(regionNodeIndex!)_1, i64 $(regionDictionaryLength), 2" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.dict %v$(regionNodeIndex!)_2, i64 $(regionDictionaryLength), 3" -> println
            }
            (regionNode.kind == 7 or regionNode.kind == 8) -> if {
                context.ir[regionNode.operand0] => regionLeft
                "" => regionOperation!
                regionNode.kind == 7 -> if {
                    regionNode.opcode == -26 -> if { "xor" => regionOperation! } else { "sub" => regionOperation! }
                } else {
                    regionNode.opcode == grammar.tokenIdPlus -> if { "add" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdMinus -> if { "sub" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdStar -> if { "mul" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdSlash -> if { "sdiv" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdPercent -> if { "srem" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdLess -> if { "icmp slt" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdGreater -> if { "icmp sgt" => regionOperation! }
                    regionNode.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" => regionOperation! }
                    regionNode.opcode == -24 -> if { "or" => regionOperation! }
                    regionNode.opcode == -25 -> if { "and" => regionOperation! }
                }
                "  %v$(regionNodeIndex!) = $(regionOperation!) " -> print
                regionLeft -> writeType
                " " -> print
                (regionNode.kind == 7 and regionNode.opcode != -26) -> if { "0" -> print } else {
                    (regionLeft.kind == 3 or regionLeft.kind == 4) -> if {
                        context.sources[regionLeft.sourceModule] -> lexer.lex => regionLeftTokens!
                        regionLeftTokens![regionLeft.payloadToken] => regionLeftToken
                        regionLeft.kind == 3 -> if { context.sources[regionLeft.sourceModule] -> slice(regionLeftToken.span.start, regionLeftToken.span.length) -> print } else {
                            ((context.sources[regionLeft.sourceModule] -> byte(regionLeftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else {
                        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionLeft.kind == 5 and regionLeft.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                    }
                }
                regionNode.kind == 7 -> if {
                    regionNode.opcode == -26 -> if { ", true" -> println } else {
                        (regionLeft.kind == 3 or regionLeft.kind == 4) -> if {
                            context.sources[regionLeft.sourceModule] -> lexer.lex => regionUnaryTokens!
                            regionUnaryTokens![regionLeft.payloadToken] => regionUnaryToken
                            context.sources[regionLeft.sourceModule] -> slice(regionUnaryToken.span.start, regionUnaryToken.span.length) -> println
                        } else { ", %v$(regionNode.operand0)" -> println }
                    }
                } else {
                    ", " -> print
                    context.ir[regionNode.operand1] => regionRight
                    (regionRight.kind == 3 or regionRight.kind == 4) -> if {
                        context.sources[regionRight.sourceModule] -> lexer.lex => regionRightTokens!
                        regionRightTokens![regionRight.payloadToken] => regionRightToken
                        regionRight.kind == 3 -> if { context.sources[regionRight.sourceModule] -> slice(regionRightToken.span.start, regionRightToken.span.length) -> println } else {
                            ((context.sources[regionRight.sourceModule] -> byte(regionRightToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                        }
                    } else { "%v$(regionNode.operand1)" -> println }
                }
            }
            regionNode.kind == 6 -> if {
                (regionNode.symbol == -101 or regionNode.symbol == -102) -> if {
                    context.ir[regionNode.operand0] => regionArgument
                    regionArgument.kind == 2 -> if {
                        context.sources[regionArgument.sourceModule] -> lexer.lex => regionArgumentTokens!
                        regionArgumentTokens![regionArgument.payloadToken] => regionArgumentToken
                        Int(regionArgumentToken.span.length) - 2 => regionArgumentLength
                        "  call void @sl_runtime_print(ptr @sl_str_$(regionNode.operand0), i64 $regionArgumentLength, i1 " -> print
                        regionNode.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                    }
                } else {
                    (regionNode.typeOrigin == 1 and regionNode.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(regionNodeIndex!) = call " -> print }
                    regionNode -> writeType
                    " @sl_m$(regionNode.targetModule)_s$(regionNode.symbol)(" -> print
                    regionNode.operand0 >= 0 -> if {
                        context.ir[regionNode.operand0] => regionCallArgument
                        regionCallArgument -> writeType
                        " " -> print
                        (regionCallArgument.kind == 3 or regionCallArgument.kind == 4) -> if {
                            context.sources[regionCallArgument.sourceModule] -> lexer.lex => regionCallTokens!
                            regionCallTokens![regionCallArgument.payloadToken] => regionCallToken
                            regionCallArgument.kind == 3 -> if { context.sources[regionCallArgument.sourceModule] -> slice(regionCallToken.span.start, regionCallToken.span.length) -> print } else {
                                ((context.sources[regionCallArgument.sourceModule] -> byte(regionCallToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionCallArgument.kind == 5 and regionCallArgument.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                        }
                    }
                    ")" -> println
                }
            }
            }
            }
            }
            regionEventKind == 2 -> if {
                not regionTerminated! -> if {
                true => ifActive![regionNodeIndex!]
                context.ir[regionNode.operand0] => nestedCondition
                "  br i1 " -> print
                nestedCondition.kind == 4 -> if {
                    context.sources[nestedCondition.sourceModule] -> lexer.lex => nestedConditionTokens!
                    nestedConditionTokens![nestedCondition.payloadToken] => nestedConditionToken
                    ((context.sources[nestedCondition.sourceModule] -> byte(nestedConditionToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                } else {
                    (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and nestedCondition.kind == 5 and nestedCondition.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                }
                regionNode.nextOperand >= 0 -> if {
                    ", label %if$(regionNodeIndex!)_then, label %if$(regionNodeIndex!)_else" -> println
                } else {
                    ", label %if$(regionNodeIndex!)_then, label %if$(regionNodeIndex!)_merge" -> println
                }
                "if$(regionNodeIndex!)_then:" -> println
                false => regionTerminated!
                }
            }
            regionEventKind == 3 -> if {
                ifActive![regionNodeIndex!] -> if {
                    not regionTerminated! -> if {
                        OwnedDropRequest { regionIndex: regionNode.operand1, beforeAst: -1, edgeIndex: regionNodeIndex! * 10 + 1, transferredSymbol: -1 } -> emitOwnedDrops
                        "  br label %if$(regionNodeIndex!)_merge" -> println
                        true => ifThenReachesMerge![regionNodeIndex!]
                    }
                    "if$(regionNodeIndex!)_else:" -> println
                    false => regionTerminated!
                }
            }
            regionEventKind == 4 -> if {
                ifActive![regionNodeIndex!] -> if {
                regionNode.nextOperand < 0 -> if { true => ifThenReachesMerge![regionNodeIndex!] }
                not regionTerminated! -> if {
                    regionNode.nextOperand >= 0 -> if {
                        OwnedDropRequest { regionIndex: regionNode.nextOperand, beforeAst: -1, edgeIndex: regionNodeIndex! * 10 + 2, transferredSymbol: -1 } -> emitOwnedDrops
                    } else {
                        OwnedDropRequest { regionIndex: regionNode.operand1, beforeAst: -1, edgeIndex: regionNodeIndex! * 10 + 2, transferredSymbol: -1 } -> emitOwnedDrops
                    }
                    "  br label %if$(regionNodeIndex!)_merge" -> println
                    true => ifThenReachesMerge![regionNodeIndex!]
                }
                ifThenReachesMerge![regionNodeIndex!] -> if {
                    "if$(regionNodeIndex!)_merge:" -> println
                    false => regionTerminated!
                }
                (regionNode.typeSymbol != 0 and regionNode.nextOperand >= 0) -> if {
                    context.ir[regionNode.operand1] => nestedThenRegion
                    context.ir[regionNode.nextOperand] => nestedElseRegion
                    context.ir[nestedThenRegion.operand1] => nestedThenValue
                    context.ir[nestedElseRegion.operand1] => nestedElseValue
                    "  %v$(regionNodeIndex!) = phi " -> print
                    regionNode -> writeType
                    " [ " -> print
                    (nestedThenValue.kind == 3 or nestedThenValue.kind == 4) -> if {
                        context.sources[nestedThenValue.sourceModule] -> lexer.lex => nestedThenValueTokens!
                        nestedThenValueTokens![nestedThenValue.payloadToken] => nestedThenValueToken
                        nestedThenValue.kind == 3 -> if { context.sources[nestedThenValue.sourceModule] -> slice(nestedThenValueToken.span.start, nestedThenValueToken.span.length) -> print } else {
                            ((context.sources[nestedThenValue.sourceModule] -> byte(nestedThenValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else { "%v$(nestedThenRegion.operand1)" -> print }
                    ", %if" -> print
                    nestedThenValue.kind == 18 -> if { "$(nestedThenRegion.operand1)_merge" -> print } else { "$(regionNodeIndex!)_then" -> print }
                    " ], [ " -> print
                    (nestedElseValue.kind == 3 or nestedElseValue.kind == 4) -> if {
                        context.sources[nestedElseValue.sourceModule] -> lexer.lex => nestedElseValueTokens!
                        nestedElseValueTokens![nestedElseValue.payloadToken] => nestedElseValueToken
                        nestedElseValue.kind == 3 -> if { context.sources[nestedElseValue.sourceModule] -> slice(nestedElseValueToken.span.start, nestedElseValueToken.span.length) -> print } else {
                            ((context.sources[nestedElseValue.sourceModule] -> byte(nestedElseValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else { "%v$(nestedElseRegion.operand1)" -> print }
                    ", %if" -> print
                    nestedElseValue.kind == 18 -> if { "$(nestedElseRegion.operand1)_merge" -> print } else { "$(regionNodeIndex!)_else" -> print }
                    " ]" -> println
                }
                }
            }
            regionEventKind == 6 -> if {
                not regionTerminated! -> if {
                    true => loopActive![regionNodeIndex!]
                    "  br label %while$(regionNodeIndex!)_header" -> println
                    "while$(regionNodeIndex!)_header:" -> println
                    false => regionTerminated!
                }
            }
            regionEventKind == 7 -> if {
                loopActive![regionNodeIndex!] -> if {
                    WhileBranchRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex! } -> emitWhileBranch
                    "while$(regionNodeIndex!)_body:" -> println
                    false => regionTerminated!
                }
            }
            regionEventKind == 8 -> if {
                loopActive![regionNodeIndex!] -> if {
                    not regionTerminated! -> if {
                        OwnedDropRequest { regionIndex: regionNode.operand1, beforeAst: -1, edgeIndex: regionNodeIndex! * 10 + 3, transferredSymbol: -1 } -> emitOwnedDrops
                        "  br label %while$(regionNodeIndex!)_header" -> println
                    }
                    "while$(regionNodeIndex!)_exit:" -> println
                    false => regionTerminated!
                }
            }
            regionOrderIndex! + 1 => regionOrderIndex!
        }
    }
    0 => structSourceIndex!
    structSourceIndex! < (context.sources -> len) -> while {
        context.sources[structSourceIndex!] -> symbols.collect => structTable!
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
                        fieldTypeSearch! < (context.nominal -> len) -> while {
                            context.nominal[fieldTypeSearch!] => fieldTypeCandidate
                            (fieldTypeCandidate.sourceModule == structSourceIndex! and fieldTypeCandidate.typeAst == fieldSymbol.typeNode) -> if { fieldTypeSearch! => fieldTypeIndex! }
                            fieldTypeSearch! + 1 => fieldTypeSearch!
                        }
                        fieldTypeIndex! >= 0 -> if {
                            context.nominal[fieldTypeIndex!] => fieldType
                            (fieldType.origin == 0 or fieldType.origin == 2) -> if {
                                "%sl.struct.m$(fieldType.targetModule)_s$(fieldType.targetSymbol)" -> print
                            } else {
                                fieldType.targetSymbol -> llvmType -> print
                            }
                        } else {
                            0 => fieldCompositeIndex!
                            fieldCompositeIndex! < (context.composite -> len) -> while {
                                context.composite[fieldCompositeIndex!] => fieldComposite
                                (fieldComposite.sourceModule == structSourceIndex! and fieldComposite.typeAst == fieldSymbol.typeNode) -> if {
                                    fieldComposite.kind == 3 -> if { "%sl.array.i32" -> print }
                                    fieldComposite.kind == 5 -> if { "%sl.dict" -> print }
                                }
                                fieldCompositeIndex! + 1 => fieldCompositeIndex!
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
    arrayTypeSearch! < (context.ir -> len) -> while {
        context.ir[arrayTypeSearch!].typeOrigin == 13 -> if { true => usesDynamicArray! }
        arrayTypeSearch! + 1 => arrayTypeSearch!
    }
    0 => arrayCompositeSearch!
    arrayCompositeSearch! < (context.composite -> len) -> while {
        context.composite[arrayCompositeSearch!].kind == 3 -> if { true => usesDynamicArray! }
        arrayCompositeSearch! + 1 => arrayCompositeSearch!
    }
    usesDynamicArray! -> if {
        "%sl.array.i32 = type { ptr, i64, i64 }" -> println
        "declare ptr @malloc(i64)" -> println
        "declare void @free(ptr)" -> println
    }
    false => usesDictionary!
    0 => dictionaryTypeSearch!
    dictionaryTypeSearch! < (context.ir -> len) -> while {
        context.ir[dictionaryTypeSearch!].typeOrigin == 15 -> if { true => usesDictionary! }
        dictionaryTypeSearch! + 1 => dictionaryTypeSearch!
    }
    0 => dictionaryCompositeSearch!
    dictionaryCompositeSearch! < (context.composite -> len) -> while {
        context.composite[dictionaryCompositeSearch!].kind == 5 -> if { true => usesDictionary! }
        dictionaryCompositeSearch! + 1 => dictionaryCompositeSearch!
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
    textTypeSearch! < (context.ir -> len) -> while {
        context.ir[textTypeSearch!].typeSymbol == 1 -> if { true => usesText! }
        textTypeSearch! + 1 => textTypeSearch!
    }
    usesText! -> if {
        "%sl.text = type { ptr, i64 }" -> println
        0 => textGlobalIndex!
        textGlobalIndex! < (context.ir -> len) -> while {
            context.ir[textGlobalIndex!] => textConstant
            textConstant.kind == 2 -> if {
                context.sources[textConstant.sourceModule] -> lexer.lex => textTokens!
                textTokens![textConstant.payloadToken] => textToken
                textToken.span.length - UIntSize(2) => textLength
                "@sl_str_$(textGlobalIndex!) = private unnamed_addr constant [$textLength x i8] c" -> print
                context.sources[textConstant.sourceModule] -> slice(textToken.span.start, UIntSize(1)) -> print
                textToken.span.start + UIntSize(1) => textByteIndex!
                textToken.span.start + textToken.span.length - UIntSize(1) => textByteEnd
                textByteIndex! < textByteEnd -> while {
                    context.sources[textConstant.sourceModule] -> byte(textByteIndex!) => textByte
                    (textByte >= UInt8(32) and textByte <= UInt8(126) and textByte != UInt8(34) and textByte != UInt8(92)) -> if {
                        context.sources[textConstant.sourceModule] -> slice(textByteIndex!, UIntSize(1)) -> print
                    } else {
                        "\\" -> print
                        Int(textByte) / 16 -> hexDigit -> print
                        Int(textByte) % 16 -> hexDigit -> print
                    }
                    textByteIndex! + UIntSize(1) => textByteIndex!
                }
                context.sources[textConstant.sourceModule] -> slice(textByteEnd, UIntSize(1)) -> println
            }
            textGlobalIndex! + 1 => textGlobalIndex!
        }
    }
    0 => functionIndex!
    functionIndex! < (context.ir -> len) -> while {
        context.ir[functionIndex!] => function
        function.kind == 0 -> if {
            functionIndex! + 1 => functionEnd!
            (functionEnd! < (context.ir -> len) and context.ir[functionEnd!].kind != 0 and context.ir[functionEnd!].kind != 11) -> while {
                functionEnd! + 1 => functionEnd!
            }
            "define " -> print
            function -> writeType
            " @sl_m$(function.sourceModule)_s$(function.symbol)(" -> print
            function.operand1 >= 0 -> if {
                context.ir[function.operand1] => parameter
                parameter -> writeType
                " %arg" -> print
            }
            ") {" -> println
            "entry:" -> println
            function.operand0 + 1 => expressionStart
            expressionStart => mutableSlotIndex!
            mutableSlotIndex! < functionEnd! -> while {
                context.ir[mutableSlotIndex!] => mutableSlotCandidate
                (mutableSlotCandidate.kind == 17 and mutableSlotCandidate.flags == 1) -> if {
                    mutableSlotIndex! -> mutableBindingRoot => mutableSlotRoot
                    mutableSlotRoot == mutableSlotIndex! -> if {
                        "  %slot$(mutableSlotRoot) = alloca " -> print
                        mutableSlotCandidate -> writeType
                        ", align $(mutableSlotCandidate.typeSymbol -> storageAlign)" -> println
                    }
                }
                mutableSlotIndex! + 1 => mutableSlotIndex!
            }
            [Bool; ~] => expressionScheduled!
            0 => scheduledInit!
            scheduledInit! < (context.ir -> len) -> while {
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
                        context.ir[scheduleCandidate!] => scheduleNode
                        scheduleNode.parent => scheduleAncestor!
                        scheduleNode.kind == 19 => insideControlRegion!
                        (scheduleAncestor! >= expressionStart and not insideControlRegion!) -> while {
                            (context.ir[scheduleAncestor!].kind == 19 or context.ir[scheduleAncestor!].kind == 20) -> if { true => insideControlRegion! } else { context.ir[scheduleAncestor!].parent => scheduleAncestor! }
                        }
                        (insideControlRegion! or scheduleNode.kind == 19) -> if {
                            true => expressionScheduled![scheduleCandidate!]
                            true => scheduleProgress!
                        }
                        not (insideControlRegion! or scheduleNode.kind == 19) => scheduleReady!
                        (scheduleReady! and scheduleNode.operand0 >= expressionStart and scheduleNode.operand0 < functionEnd! and not expressionScheduled![scheduleNode.operand0]) -> if { false => scheduleReady! }
                        (scheduleReady! and scheduleNode.kind != 18 and scheduleNode.kind != 20 and scheduleNode.operand1 >= expressionStart and scheduleNode.operand1 < functionEnd! and not expressionScheduled![scheduleNode.operand1]) -> if { false => scheduleReady! }
                        (scheduleReady! and (scheduleNode.kind == 12 or scheduleNode.kind == 14 or scheduleNode.kind == 16)) -> if {
                            scheduleNode.operand0 => scheduleSibling!
                            scheduleSibling! >= 0 -> while {
                                not expressionScheduled![scheduleSibling!] -> if { false => scheduleReady! }
                                context.ir[scheduleSibling!].nextOperand => scheduleSibling!
                            }
                        }
                        (scheduleReady! and scheduleNode.kind == 5) -> if {
                            false => mutableRead!
                            expressionStart => mutableReadBindingSearch!
                            mutableReadBindingSearch! < functionEnd! -> while {
                                (context.ir[mutableReadBindingSearch!].kind == 17 and context.ir[mutableReadBindingSearch!].symbol == scheduleNode.symbol and context.ir[mutableReadBindingSearch!].flags == 1) -> if { true => mutableRead! }
                                mutableReadBindingSearch! + 1 => mutableReadBindingSearch!
                            }
                            mutableRead! -> if {
                                context.sources[scheduleNode.sourceModule] -> ast.lower => mutableReadAst!
                                expressionStart => mutableReadBarrierSearch!
                                mutableReadBarrierSearch! < functionEnd! -> while {
                                    context.ir[mutableReadBarrierSearch!] => mutableReadBarrier
                                    (not expressionScheduled![mutableReadBarrierSearch!] and (mutableReadBarrier.kind == 20 or (mutableReadBarrier.kind == 17 and mutableReadBarrier.flags == 1)) and mutableReadAst![mutableReadBarrier.astNode].start < mutableReadAst![scheduleNode.astNode].start) -> if {
                                        false => scheduleReady!
                                    }
                                    mutableReadBarrierSearch! + 1 => mutableReadBarrierSearch!
                                }
                            }
                        }
                        (scheduleReady! and (scheduleNode.kind == 6 or scheduleNode.kind == 18 or scheduleNode.kind == 20 or (scheduleNode.kind == 17 and scheduleNode.flags == 1))) -> if {
                            true => rootEffect!
                            scheduleNode.parent => effectAncestor!
                            (effectAncestor! >= expressionStart and rootEffect!) -> while {
                                (context.ir[effectAncestor!].kind == 6 or context.ir[effectAncestor!].kind == 18 or context.ir[effectAncestor!].kind == 20 or (context.ir[effectAncestor!].kind == 17 and context.ir[effectAncestor!].flags == 1)) -> if { false => rootEffect! } else { context.ir[effectAncestor!].parent => effectAncestor! }
                            }
                            rootEffect! -> if {
                                context.sources[scheduleNode.sourceModule] -> ast.lower => effectAst!
                                expressionStart => earlierEffectSearch!
                                earlierEffectSearch! < functionEnd! -> while {
                                    context.ir[earlierEffectSearch!] => earlierEffect
                                (not expressionScheduled![earlierEffectSearch!] and (earlierEffect.kind == 6 or earlierEffect.kind == 18 or earlierEffect.kind == 20 or (earlierEffect.kind == 17 and earlierEffect.flags == 1)) and effectAst![earlierEffect.astNode].start < effectAst![scheduleNode.astNode].start) -> if {
                                        earlierEffect.parent => earlierEffectAncestor!
                                        true => earlierRootEffect!
                                        false => earlierInsideRegion!
                                        (earlierEffectAncestor! >= expressionStart and earlierRootEffect! and not earlierInsideRegion!) -> while {
                                            (context.ir[earlierEffectAncestor!].kind == 19 or context.ir[earlierEffectAncestor!].kind == 20) -> if { true => earlierInsideRegion! } else {
                                                (context.ir[earlierEffectAncestor!].kind == 6 or context.ir[earlierEffectAncestor!].kind == 18 or context.ir[earlierEffectAncestor!].kind == 20 or (context.ir[earlierEffectAncestor!].kind == 17 and context.ir[earlierEffectAncestor!].flags == 1)) -> if { false => earlierRootEffect! } else { context.ir[earlierEffectAncestor!].parent => earlierEffectAncestor! }
                                            }
                                        }
                                        (earlierRootEffect! and not earlierInsideRegion!) -> if { false => scheduleReady! }
                                    }
                                    earlierEffectSearch! + 1 => earlierEffectSearch!
                                }
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
                context.ir[expressionIndex!] => expression
                (expression.kind == 5 and not (function.operand1 >= 0 and expression.symbol == context.ir[function.operand1].symbol)) -> if {
                    -1 => bindingValueIr!
                    expressionStart => bindingValueSearch!
                    bindingValueSearch! < functionEnd! -> while {
                        (context.ir[bindingValueSearch!].kind == 17 and context.ir[bindingValueSearch!].symbol == expression.symbol) -> if { bindingValueSearch! => bindingValueIr! }
                        bindingValueSearch! + 1 => bindingValueSearch!
                    }
                    bindingValueIr! >= 0 -> if {
                        context.ir[bindingValueIr!] => bindingValue
                        bindingValue.flags == 1 -> if {
                            bindingValueIr! -> mutableBindingRoot => bindingRoot
                            "  %v$(expressionIndex!) = load " -> print
                            expression -> writeType
                            ", ptr %slot$(bindingRoot), align " -> print
                            "$(expression.typeSymbol -> storageAlign)" -> println
                        } else {
                            context.ir[bindingValue.operand0] => bindingOperand
                            "  %v$(expressionIndex!) = freeze " -> print
                            expression -> writeType
                            " " -> print
                            (bindingOperand.kind == 3 or bindingOperand.kind == 4) -> if {
                                context.sources[bindingOperand.sourceModule] -> lexer.lex => bindingOperandTokens!
                                bindingOperandTokens![bindingOperand.payloadToken] => bindingOperandToken
                                bindingOperand.kind == 3 -> if { context.sources[bindingOperand.sourceModule] -> slice(bindingOperandToken.span.start, bindingOperandToken.span.length) -> print } else {
                                    ((context.sources[bindingOperand.sourceModule] -> byte(bindingOperandToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(bindingValue.operand0)" -> print }
                            "" -> println
                        }
                    }
                }
                (expression.kind == 17 and expression.flags == 1) -> if {
                    expressionIndex! -> mutableBindingRoot => mutableRoot
                    context.ir[expression.operand0] => mutableValue
                    "  store " -> print
                    expression -> writeType
                    " " -> print
                    (mutableValue.kind == 3 or mutableValue.kind == 4) -> if {
                        context.sources[mutableValue.sourceModule] -> lexer.lex => mutableTokens!
                        mutableTokens![mutableValue.payloadToken] => mutableToken
                        mutableValue.kind == 3 -> if { context.sources[mutableValue.sourceModule] -> slice(mutableToken.span.start, mutableToken.span.length) -> print } else {
                            ((context.sources[mutableValue.sourceModule] -> byte(mutableToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        }
                    } else { "%v$(expression.operand0)" -> print }
                    ", ptr %slot$(mutableRoot), align " -> print
                    "$(expression.typeSymbol -> storageAlign)" -> println
                }
                expression.kind == 2 -> if {
                    context.sources[expression.sourceModule] -> lexer.lex => expressionTokens!
                    expressionTokens![expression.payloadToken] => expressionToken
                    expressionToken.span.length - UIntSize(2) => expressionLength
                    "  %v$(expressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(expressionIndex!), 0" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.text %v$(expressionIndex!)_ptr, i64 $expressionLength, 1" -> println
                }
                expression.kind == 12 -> if {
                    expression.operand0 => fieldValueIndex!
                    0 => fieldPosition!
                    fieldValueIndex! >= 0 -> while {
                        context.ir[fieldValueIndex!] => fieldValue
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
                            context.sources[fieldValue.sourceModule] -> lexer.lex => fieldTokens!
                            fieldTokens![fieldValue.payloadToken] => fieldToken
                            fieldValue.kind == 3 -> if {
                                context.sources[fieldValue.sourceModule] -> slice(fieldToken.span.start, fieldToken.span.length) -> print
                            } else {
                                ((context.sources[fieldValue.sourceModule] -> byte(fieldToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (fieldValue.kind == 5 and function.operand1 >= 0 and fieldValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(fieldValueIndex!)" -> print }
                        }
                        ", $(fieldPosition!)" -> println
                        fieldValue.nextOperand => fieldValueIndex!
                        fieldPosition! + 1 => fieldPosition!
                    }
                }
                expression.kind == 13 -> if {
                    context.ir[expression.operand0] => memberBase
                    memberBase.typeOrigin => memberCurrentOrigin!
                    memberBase.typeModule => memberCurrentModule!
                    memberBase.typeSymbol => memberCurrentSymbol!
                    context.sources[expression.sourceModule] -> ast.lower => memberNodes!
                    context.sources[expression.sourceModule] -> lexer.lex => memberTokens!
                    memberNodes![expression.astNode] => memberAst
                    0 => memberIdentifierCount!
                    memberAst.firstToken => memberTokenIndex!
                    memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                        memberTokens![memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if { memberIdentifierCount! + 1 => memberIdentifierCount! }
                        memberTokenIndex! + 1 => memberTokenIndex!
                    }
                    0 => memberIdentifierOrdinal!
                    0 => memberProjectionOrdinal!
                    memberAst.firstToken => memberTokenIndex!
                    memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                        memberTokens![memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                            memberIdentifierOrdinal! > 0 -> if {
                                memberCurrentModule! => ownerSourceModule!
                                memberCurrentOrigin! == 2 -> if { context.modules[memberCurrentModule!].sourceIndex => ownerSourceModule! }
                                context.sources[ownerSourceModule!] -> symbols.collect => ownerTable!
                                context.sources[ownerSourceModule!] -> lexer.lex => ownerTokens!
                                -1 => fieldOrdinal!
                                0 => candidateFieldOrdinal!
                                0 => memberFieldSearch!
                                memberFieldSearch! < (ownerTable! -> len) -> while {
                                    ownerTable![memberFieldSearch!] => memberField
                                    (memberField.kind == 26 and memberField.parent == memberCurrentSymbol!) -> if {
                                        memberTokens![memberTokenIndex!] => memberName
                                        ownerTokens![memberField.nameToken] => fieldName
                                        memberName.span.length == fieldName.span.length => equal!
                                        UIntSize(0) => fieldNameByte!
                                        (equal! and fieldNameByte! < memberName.span.length) -> while {
                                            context.sources[expression.sourceModule] -> byte(memberName.span.start + fieldNameByte!) => memberByte
                                            context.sources[ownerSourceModule!] -> byte(fieldName.span.start + fieldNameByte!) => fieldByte
                                            memberByte != fieldByte -> if { false => equal! }
                                            fieldNameByte! + UIntSize(1) => fieldNameByte!
                                        }
                                        equal! -> if {
                                            candidateFieldOrdinal! => fieldOrdinal!
                                            -1 => memberFieldTypeIndex!
                                            0 => memberFieldTypeSearch!
                                            memberFieldTypeSearch! < (context.nominal -> len) -> while {
                                                (context.nominal[memberFieldTypeSearch!].sourceModule == ownerSourceModule! and context.nominal[memberFieldTypeSearch!].typeAst == memberField.typeNode) -> if { memberFieldTypeSearch! => memberFieldTypeIndex! }
                                                memberFieldTypeSearch! + 1 => memberFieldTypeSearch!
                                            }
                                            -1 => memberNextOrigin!
                                            -1 => memberNextModule!
                                            -1 => memberNextSymbol!
                                            memberFieldTypeIndex! >= 0 -> if {
                                                context.nominal[memberFieldTypeIndex!] => memberFieldType
                                                memberFieldType.origin => memberNextOrigin!
                                                memberFieldType.targetModule => memberNextModule!
                                                memberFieldType.targetSymbol => memberNextSymbol!
                                            } else {
                                                0 => memberCompositeSearch!
                                                memberCompositeSearch! < (context.composite -> len) -> while {
                                                    context.composite[memberCompositeSearch!] => memberCompositeType
                                                    (memberCompositeType.sourceModule == ownerSourceModule! and memberCompositeType.typeAst == memberField.typeNode) -> if {
                                                        10 + memberCompositeType.kind => memberNextOrigin!
                                                        memberCompositeType.kind == 5 -> if {
                                                            memberCompositeType.keySymbol => memberNextModule!
                                                            memberCompositeType.valueSymbol => memberNextSymbol!
                                                        } else {
                                                            memberCompositeType.elementModule => memberNextModule!
                                                            memberCompositeType.elementSymbol => memberNextSymbol!
                                                        }
                                                    }
                                                    memberCompositeSearch! + 1 => memberCompositeSearch!
                                                }
                                            }
                                            "  " -> print
                                            memberIdentifierOrdinal! == memberIdentifierCount! - 1 -> if { "%v$(expressionIndex!)" -> print } else { "%v$(expressionIndex!)_m$(memberProjectionOrdinal!)" -> print }
                                            " = extractvalue %sl.struct.m$(ownerSourceModule!)_s$(memberCurrentSymbol!) " -> print
                                            memberProjectionOrdinal! == 0 -> if {
                                                (memberBase.kind == 5 and function.operand1 >= 0 and memberBase.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                                            } else { "%v$(expressionIndex!)_m$(memberProjectionOrdinal! - 1)" -> print }
                                            ", $(fieldOrdinal!)" -> println
                                            memberNextOrigin! => memberCurrentOrigin!
                                            memberNextModule! => memberCurrentModule!
                                            memberNextSymbol! => memberCurrentSymbol!
                                            memberProjectionOrdinal! + 1 => memberProjectionOrdinal!
                                        }
                                        candidateFieldOrdinal! + 1 => candidateFieldOrdinal!
                                    }
                                    memberFieldSearch! + 1 => memberFieldSearch!
                                }
                            }
                            memberIdentifierOrdinal! + 1 => memberIdentifierOrdinal!
                        }
                        memberTokenIndex! + 1 => memberTokenIndex!
                    }
                }
                (expression.kind == 14 and expression.typeOrigin == 13) -> if {
                    0 => arrayLength!
                    expression.operand0 => arrayCountIndex!
                    arrayCountIndex! >= 0 -> while {
                        arrayLength! + 1 => arrayLength!
                        context.ir[arrayCountIndex!].nextOperand => arrayCountIndex!
                    }
                    arrayLength! * 4 => arrayByteLength
                    "  %v$(expressionIndex!)_data = call ptr @malloc(i64 $arrayByteLength)" -> println
                    expression.operand0 => arrayElementIndex!
                    0 => arrayElementPosition!
                    arrayElementIndex! >= 0 -> while {
                        context.ir[arrayElementIndex!] => arrayElement
                        "  %v$(expressionIndex!)_ptr$(arrayElementPosition!) = getelementptr i32, ptr %v$(expressionIndex!)_data, i64 $(arrayElementPosition!)" -> println
                        "  store i32 " -> print
                        arrayElement.kind == 3 -> if {
                            context.sources[arrayElement.sourceModule] -> lexer.lex => arrayElementTokens!
                            arrayElementTokens![arrayElement.payloadToken] => arrayElementToken
                            context.sources[arrayElement.sourceModule] -> slice(arrayElementToken.span.start, arrayElementToken.span.length) -> print
                        } else {
                            (arrayElement.kind == 5 and function.operand1 >= 0 and arrayElement.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(arrayElementIndex!)" -> print }
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
                        context.ir[dictionaryCountIndex!].nextOperand => dictionaryCountIndex!
                    }
                    dictionaryItemCount! / 2 => dictionaryLength
                    dictionaryLength * (expression.typeModule -> storageSize) => dictionaryKeyByteLength
                    dictionaryLength * (expression.typeSymbol -> storageSize) => dictionaryValueByteLength
                    "  %v$(expressionIndex!)_keys = call ptr @malloc(i64 $dictionaryKeyByteLength)" -> println
                    "  %v$(expressionIndex!)_values = call ptr @malloc(i64 $dictionaryValueByteLength)" -> println
                    expression.operand0 => dictionaryItemIndex!
                    0 => dictionaryItemPosition!
                    dictionaryItemIndex! >= 0 -> while {
                        context.ir[dictionaryItemIndex!] => dictionaryItem
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
                            context.sources[dictionaryItem.sourceModule] -> lexer.lex => dictionaryItemTokens!
                            dictionaryItemTokens![dictionaryItem.payloadToken] => dictionaryItemToken
                            dictionaryItem.kind == 3 -> if {
                                context.sources[dictionaryItem.sourceModule] -> slice(dictionaryItemToken.span.start, dictionaryItemToken.span.length) -> print
                            } else {
                                ((context.sources[dictionaryItem.sourceModule] -> byte(dictionaryItemToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (dictionaryItem.kind == 5 and function.operand1 >= 0 and dictionaryItem.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(dictionaryItemIndex!)" -> print }
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
                    context.ir[expression.operand0] => indexedArray
                    context.ir[expression.operand1] => arrayIndex
                    indexedArray.typeOrigin == 15 -> if {
                        "  %v$(expressionIndex!)_keys = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", 0" -> println
                        "  %v$(expressionIndex!)_values = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", 1" -> println
                        "  %v$(expressionIndex!)_length = extractvalue %sl.dict " -> print
                        (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
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
                            context.sources[arrayIndex.sourceModule] -> lexer.lex => dictionaryIndexTokens!
                            dictionaryIndexTokens![arrayIndex.payloadToken] => dictionaryIndexToken
                            arrayIndex.kind == 3 -> if {
                                context.sources[arrayIndex.sourceModule] -> slice(dictionaryIndexToken.span.start, dictionaryIndexToken.span.length) -> println
                            } else {
                                ((context.sources[arrayIndex.sourceModule] -> byte(dictionaryIndexToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                            }
                        } else {
                            (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
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
                    (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    arrayIndex.kind != 3 -> if {
                        "  %v$(expressionIndex!)_index = sext i32 " -> print
                        (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand1)" -> print }
                        " to i64" -> println
                    }
                    "  %v$(expressionIndex!)_ptr = getelementptr i32, ptr %v$(expressionIndex!)_data, i64 " -> print
                    arrayIndex.kind == 3 -> if {
                        context.sources[arrayIndex.sourceModule] -> lexer.lex => indexTokens!
                        indexTokens![arrayIndex.payloadToken] => indexToken
                        context.sources[arrayIndex.sourceModule] -> slice(indexToken.span.start, indexToken.span.length) -> println
                    } else {
                        "%v$(expressionIndex!)_index" -> println
                    }
                    "  %v$(expressionIndex!) = load i32, ptr %v$(expressionIndex!)_ptr, align 4" -> println
                    }
                }
                (expression.kind == 7 or expression.kind == 8) -> if {
                    context.ir[expression.operand0] => leftOperand
                    "" => operation!
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if {
                            "xor" => operation!
                        } else {
                            "sub" => operation!
                        }
                    } else {
                        context.ir[expression.operand1] => rightOperand
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
                            context.sources[leftOperand.sourceModule] -> lexer.lex => leftTokens!
                            leftTokens![leftOperand.payloadToken] => leftToken
                            leftOperand.kind == 3 -> if {
                                context.sources[leftOperand.sourceModule] -> slice(leftToken.span.start, leftToken.span.length) -> print
                            } else {
                                ((context.sources[leftOperand.sourceModule] -> byte(leftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ", " -> print
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if { "true" -> println } else {
                            (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                                context.sources[leftOperand.sourceModule] -> lexer.lex => unaryTokens!
                                unaryTokens![leftOperand.payloadToken] => unaryToken
                                context.sources[leftOperand.sourceModule] -> slice(unaryToken.span.start, unaryToken.span.length) -> println
                            } else {
                                (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand0)" -> println }
                            }
                        }
                    } else {
                        context.ir[expression.operand1] => rightOperand
                        (rightOperand.kind == 3 or rightOperand.kind == 4) -> if {
                            context.sources[rightOperand.sourceModule] -> lexer.lex => rightTokens!
                            rightTokens![rightOperand.payloadToken] => rightToken
                            rightOperand.kind == 3 -> if {
                                context.sources[rightOperand.sourceModule] -> slice(rightToken.span.start, rightToken.span.length) -> println
                            } else {
                                ((context.sources[rightOperand.sourceModule] -> byte(rightToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                            }
                        } else {
                            (rightOperand.kind == 5 and function.operand1 >= 0 and rightOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                        }
                    }
                }
                expression.kind == 6 -> if {
                    (expression.symbol == -101 or expression.symbol == -102) -> if {
                        context.ir[expression.operand0] => runtimeArgument
                        runtimeArgument.kind == 2 -> if {
                            context.sources[runtimeArgument.sourceModule] -> lexer.lex => runtimeArgumentTokens!
                            runtimeArgumentTokens![runtimeArgument.payloadToken] => runtimeArgumentToken
                            Int(runtimeArgumentToken.span.length) - 2 => runtimeArgumentLength
                            context.sources[runtimeArgument.sourceModule] -> interpolation.lower => functionExpressionInterpolation!
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
                                                "" => functionExpressionOperation!
                                                functionExpressionNode.kind == 2 -> if { "sub" => functionExpressionOperation! }
                                                (functionExpressionNode.kind == 2 and functionExpressionNode.opcode == -26) -> if { "xor" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdPlus -> if { "add" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdMinus -> if { "sub" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdStar -> if { "mul" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdSlash -> if { "sdiv" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdPercent -> if { "srem" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdLess -> if { "icmp slt" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdGreater -> if { "icmp sgt" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == -24 -> if { "or" => functionExpressionOperation! }
                                                functionExpressionNode.opcode == -25 -> if { "and" => functionExpressionOperation! }
                                                (functionExpressionNode.opcode == -24 or functionExpressionNode.opcode == -25 or (functionExpressionNode.kind == 2 and functionExpressionNode.opcode == -26)) => functionExpressionBoolOperands!
                                                (functionExpressionNode.opcode == grammar.tokenIdEqualEqual or functionExpressionNode.opcode == grammar.tokenIdBangEqual) -> if {
                                                    functionExpressionInterpolation![functionExpressionNode.operand0] => functionExpressionTypeOperand
                                                    functionExpressionTypeOperand.typeSymbol == 23 -> if { true => functionExpressionBoolOperands! }
                                                    functionExpressionTypeOperand.kind == 1 -> if {
                                                        (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionTypeOperand.symbol and context.ir[function.operand1].typeSymbol == 23) -> if { true => functionExpressionBoolOperands! }
                                                        expressionStart => functionExpressionTypeBindingSearch!
                                                        functionExpressionTypeBindingSearch! < functionEnd! -> while {
                                                            (context.ir[functionExpressionTypeBindingSearch!].kind == 17 and context.ir[functionExpressionTypeBindingSearch!].symbol == functionExpressionTypeOperand.symbol and context.ir[functionExpressionTypeBindingSearch!].typeSymbol == 23) -> if { true => functionExpressionBoolOperands! }
                                                            functionExpressionTypeBindingSearch! + 1 => functionExpressionTypeBindingSearch!
                                                        }
                                                    }
                                                }
                                                functionExpressionBoolOperands! -> if { "i1" } else { "i32" } => functionExpressionOperandType
                                                "  %v$(expressionIndex!)_expression$(functionExpressionNodeIndex!) = $(functionExpressionOperation!) $functionExpressionOperandType " -> print
                                                (functionExpressionNode.kind == 2 and functionExpressionNode.opcode != -26) -> if { "0" -> print } else {
                                                    functionExpressionInterpolation![functionExpressionNode.operand0] => functionExpressionLeft
                                                    (functionExpressionLeft.kind == 0 or functionExpressionLeft.kind == 4) -> if {
                                                        functionExpressionLeft.kind == 4 -> if {
                                                            (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionLeft.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                        } else { context.sources[runtimeArgument.sourceModule] -> slice(functionExpressionLeft.payloadStart, functionExpressionLeft.payloadLength) -> print }
                                                    } else {
                                                    functionExpressionLeft.kind == 1 -> if {
                                                        (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionLeft.symbol) -> if { "%arg" -> print } else {
                                                            -1 => functionExpressionLeftBinding!
                                                            expressionStart => functionExpressionLeftBindingSearch!
                                                            functionExpressionLeftBindingSearch! < functionEnd! -> while {
                                                                (context.ir[functionExpressionLeftBindingSearch!].kind == 17 and context.ir[functionExpressionLeftBindingSearch!].symbol == functionExpressionLeft.symbol) -> if { functionExpressionLeftBindingSearch! => functionExpressionLeftBinding! }
                                                                functionExpressionLeftBindingSearch! + 1 => functionExpressionLeftBindingSearch!
                                                            }
                                                            functionExpressionLeftBinding! >= 0 -> if {
                                                                context.ir[context.ir[functionExpressionLeftBinding!].operand0] => functionExpressionLeftValue
                                                                functionExpressionLeftValue.kind == 3 -> if {
                                                                    context.sources[functionExpressionLeftValue.sourceModule] -> lexer.lex => functionExpressionLeftTokens!
                                                                    functionExpressionLeftTokens![functionExpressionLeftValue.payloadToken] => functionExpressionLeftToken
                                                                    context.sources[functionExpressionLeftValue.sourceModule] -> slice(functionExpressionLeftToken.span.start, functionExpressionLeftToken.span.length) -> print
                                                                } else { "%v$(context.ir[functionExpressionLeftBinding!].operand0)" -> print }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand0)" -> print }
                                                    }
                                                }
                                                ", " -> print
                                                functionExpressionNode.kind == 2 -> if {
                                                    functionExpressionInterpolation![functionExpressionNode.operand0] => functionExpressionUnary
                                                    functionExpressionNode.opcode == -26 -> if { "true" -> println } else {
                                                    (functionExpressionUnary.kind == 0 or functionExpressionUnary.kind == 4) -> if {
                                                        functionExpressionUnary.kind == 4 -> if {
                                                            (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionUnary.payloadStart)) == UInt8(116) -> if { "1" -> println } else { "0" -> println }
                                                        } else { context.sources[runtimeArgument.sourceModule] -> slice(functionExpressionUnary.payloadStart, functionExpressionUnary.payloadLength) -> println }
                                                    } else {
                                                    functionExpressionUnary.kind == 1 -> if {
                                                        (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionUnary.symbol) -> if { "%arg" -> println } else {
                                                            -1 => functionExpressionUnaryBinding!
                                                            expressionStart => functionExpressionUnaryBindingSearch!
                                                            functionExpressionUnaryBindingSearch! < functionEnd! -> while {
                                                                (context.ir[functionExpressionUnaryBindingSearch!].kind == 17 and context.ir[functionExpressionUnaryBindingSearch!].symbol == functionExpressionUnary.symbol) -> if { functionExpressionUnaryBindingSearch! => functionExpressionUnaryBinding! }
                                                                functionExpressionUnaryBindingSearch! + 1 => functionExpressionUnaryBindingSearch!
                                                            }
                                                            functionExpressionUnaryBinding! >= 0 -> if {
                                                                context.ir[context.ir[functionExpressionUnaryBinding!].operand0] => functionExpressionUnaryValue
                                                                functionExpressionUnaryValue.kind == 3 -> if {
                                                                    context.sources[functionExpressionUnaryValue.sourceModule] -> lexer.lex => functionExpressionUnaryTokens!
                                                                    functionExpressionUnaryTokens![functionExpressionUnaryValue.payloadToken] => functionExpressionUnaryToken
                                                                    context.sources[functionExpressionUnaryValue.sourceModule] -> slice(functionExpressionUnaryToken.span.start, functionExpressionUnaryToken.span.length) -> println
                                                                } else { "%v$(context.ir[functionExpressionUnaryBinding!].operand0)" -> println }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand0)" -> println }
                                                    }
                                                    }
                                                } else {
                                                    functionExpressionInterpolation![functionExpressionNode.operand1] => functionExpressionRight
                                                    (functionExpressionRight.kind == 0 or functionExpressionRight.kind == 4) -> if {
                                                        functionExpressionRight.kind == 4 -> if {
                                                            (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionRight.payloadStart)) == UInt8(116) -> if { "1" -> println } else { "0" -> println }
                                                        } else { context.sources[runtimeArgument.sourceModule] -> slice(functionExpressionRight.payloadStart, functionExpressionRight.payloadLength) -> println }
                                                    } else {
                                                    functionExpressionRight.kind == 1 -> if {
                                                        (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRight.symbol) -> if { "%arg" -> println } else {
                                                            -1 => functionExpressionRightBinding!
                                                            expressionStart => functionExpressionRightBindingSearch!
                                                            functionExpressionRightBindingSearch! < functionEnd! -> while {
                                                                (context.ir[functionExpressionRightBindingSearch!].kind == 17 and context.ir[functionExpressionRightBindingSearch!].symbol == functionExpressionRight.symbol) -> if { functionExpressionRightBindingSearch! => functionExpressionRightBinding! }
                                                                functionExpressionRightBindingSearch! + 1 => functionExpressionRightBindingSearch!
                                                            }
                                                            functionExpressionRightBinding! >= 0 -> if {
                                                                context.ir[context.ir[functionExpressionRightBinding!].operand0] => functionExpressionRightValue
                                                                functionExpressionRightValue.kind == 3 -> if {
                                                                    context.sources[functionExpressionRightValue.sourceModule] -> lexer.lex => functionExpressionRightTokens!
                                                                    functionExpressionRightTokens![functionExpressionRightValue.payloadToken] => functionExpressionRightToken
                                                                    context.sources[functionExpressionRightValue.sourceModule] -> slice(functionExpressionRightToken.span.start, functionExpressionRightToken.span.length) -> println
                                                                } else { "%v$(context.ir[functionExpressionRightBinding!].operand0)" -> println }
                                                            }
                                                        }
                                                    } else { "%v$(expressionIndex!)_expression$(functionExpressionNode.operand1)" -> println }
                                                    }
                                                }
                                            }
                                            functionExpressionNodeIndex! - 1 => functionExpressionNodeIndex!
                                        }
                                        functionExpressionRoot.typeSymbol => functionExpressionRootTypeSymbol!
                                        -1 => functionExpressionRootBinding!
                                        functionExpressionRoot.kind == 1 -> if {
                                            (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRoot.symbol) -> if {
                                                context.ir[function.operand1].typeSymbol => functionExpressionRootTypeSymbol!
                                            } else {
                                                expressionStart => functionExpressionRootBindingSearch!
                                                functionExpressionRootBindingSearch! < functionEnd! -> while {
                                                    (context.ir[functionExpressionRootBindingSearch!].kind == 17 and context.ir[functionExpressionRootBindingSearch!].symbol == functionExpressionRoot.symbol) -> if { functionExpressionRootBindingSearch! => functionExpressionRootBinding! }
                                                    functionExpressionRootBindingSearch! + 1 => functionExpressionRootBindingSearch!
                                                }
                                                functionExpressionRootBinding! >= 0 -> if { context.ir[functionExpressionRootBinding!].typeSymbol => functionExpressionRootTypeSymbol! }
                                            }
                                        }
                                        functionExpressionRootTypeSymbol! == 23 -> if { "  call void @sl_runtime_print_i1(i1 " -> print } else { "  call void @sl_runtime_print_i32(i32 " -> print }
                                        (functionExpressionRoot.kind == 0 or functionExpressionRoot.kind == 4) -> if {
                                            functionExpressionRoot.kind == 4 -> if {
                                                (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionRoot.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                            } else { context.sources[runtimeArgument.sourceModule] -> slice(functionExpressionRoot.payloadStart, functionExpressionRoot.payloadLength) -> print }
                                        } else {
                                        functionExpressionRoot.kind == 1 -> if {
                                            (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                functionExpressionRootBinding! >= 0 -> if {
                                                    context.ir[context.ir[functionExpressionRootBinding!].operand0] => functionExpressionRootValue
                                                    (functionExpressionRootValue.kind == 3 or functionExpressionRootValue.kind == 4) -> if {
                                                        context.sources[functionExpressionRootValue.sourceModule] -> lexer.lex => functionExpressionRootTokens!
                                                        functionExpressionRootTokens![functionExpressionRootValue.payloadToken] => functionExpressionRootToken
                                                        functionExpressionRootValue.kind == 4 -> if {
                                                            (context.sources[functionExpressionRootValue.sourceModule] -> byte(functionExpressionRootToken.span.start)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                        } else { context.sources[functionExpressionRootValue.sourceModule] -> slice(functionExpressionRootToken.span.start, functionExpressionRootToken.span.length) -> print }
                                                    } else { "%v$(context.ir[functionExpressionRootBinding!].operand0)" -> print }
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
                                    ((context.sources[runtimeArgument.sourceModule] -> byte(functionInterpolationDollar!)) == UInt8(36) and functionInterpolationDollar! + UIntSize(1) < functionInterpolationContentEnd) -> if {
                                        functionInterpolationDollar! + UIntSize(1) => functionInterpolationNameStart
                                        functionInterpolationNameStart => functionInterpolationNameEnd!
                                        true => functionInterpolationNameContinues!
                                        (functionInterpolationNameEnd! < functionInterpolationContentEnd and functionInterpolationNameContinues!) -> while {
                                            context.sources[runtimeArgument.sourceModule] -> byte(functionInterpolationNameEnd!) => functionInterpolationNameByte
                                            ((functionInterpolationNameByte >= UInt8(48) and functionInterpolationNameByte <= UInt8(57)) or (functionInterpolationNameByte >= UInt8(65) and functionInterpolationNameByte <= UInt8(90)) or (functionInterpolationNameByte >= UInt8(97) and functionInterpolationNameByte <= UInt8(122)) or functionInterpolationNameByte == UInt8(95)) -> if {
                                                functionInterpolationNameEnd! + UIntSize(1) => functionInterpolationNameEnd!
                                            } else { false => functionInterpolationNameContinues! }
                                        }
                                        functionInterpolationNameEnd! > functionInterpolationNameStart -> if {
                                            context.sources[runtimeArgument.sourceModule] -> symbols.collect => functionInterpolationSymbols!
                                            0 => functionInterpolationSymbolIndex!
                                            functionInterpolationSymbolIndex! < (functionInterpolationSymbols! -> len) -> while {
                                                functionInterpolationSymbols![functionInterpolationSymbolIndex!] => functionInterpolationSymbol
                                                (functionInterpolationSymbol.kind == 9 or functionInterpolationSymbol.kind == 35) -> if {
                                                    runtimeArgumentTokens![functionInterpolationSymbol.nameToken] => functionInterpolationSymbolToken
                                                    functionInterpolationSymbolToken.span.length == functionInterpolationNameEnd! - functionInterpolationNameStart => functionInterpolationNameEqual!
                                                    UIntSize(0) => functionInterpolationNameByteIndex!
                                                    (functionInterpolationNameEqual! and functionInterpolationNameByteIndex! < functionInterpolationSymbolToken.span.length) -> while {
                                                        (context.sources[runtimeArgument.sourceModule] -> byte(functionInterpolationNameStart + functionInterpolationNameByteIndex!)) != (context.sources[runtimeArgument.sourceModule] -> byte(functionInterpolationSymbolToken.span.start + functionInterpolationNameByteIndex!)) -> if { false => functionInterpolationNameEqual! }
                                                        functionInterpolationNameByteIndex! + UIntSize(1) => functionInterpolationNameByteIndex!
                                                    }
                                                    functionInterpolationNameEqual! -> if {
                                                        (functionInterpolationSymbol.kind == 35 and function.operand1 >= 0 and context.ir[function.operand1].symbol == functionInterpolationSymbolIndex! and context.ir[function.operand1].typeSymbol == 2) -> if {
                                                            true => functionInterpolationParameter!
                                                            functionInterpolationDollar! => functionInterpolationMatchStart!
                                                        } else {
                                                            expressionStart => functionInterpolationBindingSearch!
                                                            functionInterpolationBindingSearch! < functionEnd! -> while {
                                                                (context.ir[functionInterpolationBindingSearch!].kind == 17 and context.ir[functionInterpolationBindingSearch!].symbol == functionInterpolationSymbolIndex! and context.ir[functionInterpolationBindingSearch!].typeSymbol == 2) -> if {
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
                                        context.ir[functionInterpolationBindingIr!] => functionInterpolationBinding
                                        context.ir[functionInterpolationBinding.operand0] => functionInterpolationValue
                                        functionInterpolationValue.kind == 3 -> if {
                                            context.sources[functionInterpolationValue.sourceModule] -> lexer.lex => functionInterpolationValueTokens!
                                            functionInterpolationValueTokens![functionInterpolationValue.payloadToken] => functionInterpolationValueToken
                                            context.sources[functionInterpolationValue.sourceModule] -> slice(functionInterpolationValueToken.span.start, functionInterpolationValueToken.span.length) -> print
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
                            (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            ", 0" -> println
                            "  %v$(expressionIndex!)_runtime_len = extractvalue %sl.text " -> print
                            (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            ", 1" -> println
                            "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_runtime_ptr, i64 %v$(expressionIndex!)_runtime_len, i1 " -> print
                        }
                        expression.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                    } else {
                    (expression.typeOrigin == 1 and expression.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(expressionIndex!) = call " -> print }
                    expression -> writeType
                    " @sl_m$(expression.targetModule)_s$(expression.symbol)(" -> print
                    expression.operand0 >= 0 -> if {
                        context.ir[expression.operand0] => argument
                        argument -> writeType
                        " " -> print
                        (argument.kind == 3 or argument.kind == 4) -> if {
                            context.sources[argument.sourceModule] -> lexer.lex => argumentTokens!
                            argumentTokens![argument.payloadToken] => argumentToken
                            argument.kind == 3 -> if {
                                context.sources[argument.sourceModule] -> slice(argumentToken.span.start, argumentToken.span.length) -> print
                            } else {
                                ((context.sources[argument.sourceModule] -> byte(argumentToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else {
                            (argument.kind == 5 and function.operand1 >= 0 and argument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ")" -> println
                    }
                }
                expression.kind == 18 -> if {
                    context.ir[expression.operand0] => ifCondition
                    "  br i1 " -> print
                    ifCondition.kind == 4 -> if {
                        context.sources[ifCondition.sourceModule] -> lexer.lex => ifConditionTokens!
                        ifConditionTokens![ifCondition.payloadToken] => ifConditionToken
                        ((context.sources[ifCondition.sourceModule] -> byte(ifConditionToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                    } else {
                        (ifCondition.kind == 5 and function.operand1 >= 0 and ifCondition.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    }
                    expression.nextOperand >= 0 -> if {
                        ", label %if$(expressionIndex!)_then, label %if$(expressionIndex!)_else" -> println
                    } else {
                        ", label %if$(expressionIndex!)_then, label %if$(expressionIndex!)_merge" -> println
                    }
                    "if$(expressionIndex!)_then:" -> println
                    expression.operand1 -> regionReturns => thenReturns!
                    expression.operand1 -> emitRegion
                    not thenReturns! -> if { "  br label %if$(expressionIndex!)_merge" -> println }
                    false => elseReturns!
                    expression.nextOperand >= 0 -> if {
                        "if$(expressionIndex!)_else:" -> println
                        expression.nextOperand -> regionReturns => elseReturns!
                        expression.nextOperand -> emitRegion
                        not elseReturns! -> if { "  br label %if$(expressionIndex!)_merge" -> println }
                    }
                    not (thenReturns! and elseReturns!) -> if { "if$(expressionIndex!)_merge:" -> println }
                    (expression.typeSymbol != 0 and expression.nextOperand >= 0) -> if {
                        context.ir[expression.operand1] => thenRegion
                        context.ir[expression.nextOperand] => elseRegion
                        context.ir[thenRegion.operand1] => thenValue
                        context.ir[elseRegion.operand1] => elseValue
                        "  %v$(expressionIndex!) = phi " -> print
                        expression -> writeType
                        " [ " -> print
                        (thenValue.kind == 3 or thenValue.kind == 4) -> if {
                            context.sources[thenValue.sourceModule] -> lexer.lex => thenValueTokens!
                            thenValueTokens![thenValue.payloadToken] => thenValueToken
                            thenValue.kind == 3 -> if { context.sources[thenValue.sourceModule] -> slice(thenValueToken.span.start, thenValueToken.span.length) -> print } else {
                                ((context.sources[thenValue.sourceModule] -> byte(thenValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else { "%v$(thenRegion.operand1)" -> print }
                        ", %if" -> print
                        thenValue.kind == 18 -> if { "$(thenRegion.operand1)_merge" -> print } else { "$(expressionIndex!)_then" -> print }
                        " ], [ " -> print
                        (elseValue.kind == 3 or elseValue.kind == 4) -> if {
                            context.sources[elseValue.sourceModule] -> lexer.lex => elseValueTokens!
                            elseValueTokens![elseValue.payloadToken] => elseValueToken
                            elseValue.kind == 3 -> if { context.sources[elseValue.sourceModule] -> slice(elseValueToken.span.start, elseValueToken.span.length) -> print } else {
                                ((context.sources[elseValue.sourceModule] -> byte(elseValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else { "%v$(elseRegion.operand1)" -> print }
                        ", %if" -> print
                        elseValue.kind == 18 -> if { "$(elseRegion.operand1)_merge" -> print } else { "$(expressionIndex!)_else" -> print }
                        " ]" -> println
                    }
                }
                expression.kind == 20 -> if {
                    "  br label %while$(expressionIndex!)_header" -> println
                    "while$(expressionIndex!)_header:" -> println
                    WhileBranchRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex! } -> emitWhileBranch
                    "while$(expressionIndex!)_body:" -> println
                    expression.operand1 -> emitRegion
                    OwnedDropRequest { regionIndex: expression.operand1, beforeAst: -1, edgeIndex: expressionIndex! * 10 + 3, transferredSymbol: -1 } -> emitOwnedDrops
                    "  br label %while$(expressionIndex!)_header" -> println
                    "while$(expressionIndex!)_exit:" -> println
                }
                expressionOrderIndex! + 1 => expressionOrderIndex!
            }
            context.ir[function.operand0] => returnNode
            context.ir[returnNode.operand0] => returnOperand
            expressionStart => dropIndex!
            dropIndex! < functionEnd! -> while {
                context.ir[dropIndex!] => dropCandidate
                false => dropCandidateMoved!
                false => dropCandidateHasPartialMoves!
                (dropCandidate.kind == 17 and (dropCandidate.typeOrigin == 13 or dropCandidate.typeOrigin == 15 or dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2)) -> if {
                    0 => dropMoveIndex!
                    (dropMoveIndex! < (context.moves -> len) and not dropCandidateMoved!) -> while {
                        context.moves[dropMoveIndex!] => dropMove
                        (dropMove.memberIr >= 0 and dropMove.sourceModule == dropCandidate.sourceModule and dropMove.symbol == dropCandidate.symbol) -> if { true => dropCandidateHasPartialMoves! }
                        (dropMove.memberIr < 0 and dropMove.sourceModule == dropCandidate.sourceModule and dropMove.symbol == dropCandidate.symbol and dropMove.regionIr == function.operand0) -> if {
                            context.sources[dropCandidate.sourceModule] -> ast.lower => dropCandidateAst!
                            context.ir[dropMove.siteIr] => dropMoveCall
                            dropCandidateAst![dropMoveCall.astNode].start > dropCandidateAst![dropCandidate.astNode].start -> if { true => dropCandidateMoved! }
                        }
                        dropMoveIndex! + 1 => dropMoveIndex!
                    }
                }
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
                (dropCandidate.kind == 12 and (dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2) and dropCandidate.parent == function.operand0 and dropIndex! != returnNode.operand0) -> if {
                    DropGlueRequest {
                        typeOrigin: dropCandidate.typeOrigin
                        typeModule: dropCandidate.typeModule
                        typeSymbol: dropCandidate.typeSymbol
                        valueKind: 0
                        valueIndex: dropIndex!
                        nameRoot: dropIndex!
                        pathCode: 0
                        bindingIndex: -1
                        regionIndex: function.operand0
                        beforeAst: -1
                        parentTask: -1
                        fieldOrdinal: -1
                        hasPartialMoves: false
                    } -> emitDropGlue
                }
                (dropCandidate.kind == 17 and dropCandidate.typeOrigin == 13 and not dropCandidateMoved! and not (returnOperand.kind == 5 and returnOperand.symbol == dropCandidate.symbol)) -> if {
                    "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %v$(dropCandidate.operand0), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!))" -> println
                }
                (dropCandidate.kind == 17 and dropCandidate.typeOrigin == 15 and not dropCandidateMoved! and not (returnOperand.kind == 5 and returnOperand.symbol == dropCandidate.symbol)) -> if {
                    "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %v$(dropCandidate.operand0), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_keys)" -> println
                    "  %drop$(dropIndex!)_values = extractvalue %sl.dict %v$(dropCandidate.operand0), 1" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_values)" -> println
                }
                (dropCandidate.kind == 17 and (dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2) and not dropCandidateMoved! and not (returnOperand.kind == 5 and returnOperand.symbol == dropCandidate.symbol)) -> if {
                    DropGlueRequest {
                        typeOrigin: dropCandidate.typeOrigin
                        typeModule: dropCandidate.typeModule
                        typeSymbol: dropCandidate.typeSymbol
                        valueKind: 0
                        valueIndex: dropCandidate.operand0
                        nameRoot: dropIndex!
                        pathCode: 0
                        bindingIndex: dropIndex!
                        regionIndex: function.operand0
                        beforeAst: -1
                        parentTask: -1
                        fieldOrdinal: -1
                        hasPartialMoves: dropCandidateHasPartialMoves!
                    } -> emitDropGlue
                }
                dropIndex! + 1 => dropIndex!
            }
            function.operand1 >= 0 -> if {
                context.ir[function.operand1] => ownedParameter
                false => ownedParameterHasPartialMoves!
                0 => ownedParameterMoveIndex!
                ownedParameterMoveIndex! < (context.moves -> len) -> while {
                    context.moves[ownedParameterMoveIndex!] => ownedParameterMove
                    (ownedParameterMove.memberIr >= 0 and ownedParameterMove.sourceModule == ownedParameter.sourceModule and ownedParameterMove.symbol == ownedParameter.symbol) -> if { true => ownedParameterHasPartialMoves! }
                    ownedParameterMoveIndex! + 1 => ownedParameterMoveIndex!
                }
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
                ((ownedParameter.typeOrigin == 0 or ownedParameter.typeOrigin == 2) and ownedParameter.flags % 2 == 1) -> if {
                    not (returnOperand.kind == 5 and returnOperand.symbol == ownedParameter.symbol) -> if {
                        DropGlueRequest {
                            typeOrigin: ownedParameter.typeOrigin
                            typeModule: ownedParameter.typeModule
                            typeSymbol: ownedParameter.typeSymbol
                            valueKind: 1
                            valueIndex: -1
                            nameRoot: functionIndex!
                            pathCode: 0
                            bindingIndex: function.operand1
                            regionIndex: function.operand0
                            beforeAst: -1
                            parentTask: -1
                            fieldOrdinal: -1
                            hasPartialMoves: ownedParameterHasPartialMoves!
                        } -> emitDropGlue
                    }
                }
            }
            (function.typeOrigin == 1 and function.typeSymbol == 0) -> if { "  ret void" -> println } else {
            "  ret " -> print
            function -> writeType
            " " -> print
            (returnOperand.kind == 3 or returnOperand.kind == 4) -> if {
                context.sources[returnOperand.sourceModule] -> lexer.lex => returnTokens!
                returnTokens![returnOperand.payloadToken] => returnToken
                returnOperand.kind == 3 -> if {
                    context.sources[returnOperand.sourceModule] -> slice(returnToken.span.start, returnToken.span.length) -> println
                } else {
                    ((context.sources[returnOperand.sourceModule] -> byte(returnToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                }
            } else {
                (returnOperand.kind == 5 and function.operand1 >= 0 and returnOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(returnNode.operand0)" -> println }
            }
            }
            "}" -> println
            functionEnd! => functionIndex!
        } else {
            function.kind == 11 -> if {
                functionIndex! + 1 => entryEnd!
                (entryEnd! < (context.ir -> len) and context.ir[entryEnd!].kind != 0 and context.ir[entryEnd!].kind != 11) -> while { entryEnd! + 1 => entryEnd! }
                "define i32 @main() {" -> println
                "entry:" -> println
                functionIndex! + 1 => entryMutableSlotIndex!
                entryMutableSlotIndex! < entryEnd! -> while {
                    context.ir[entryMutableSlotIndex!] => entryMutableSlotCandidate
                    (entryMutableSlotCandidate.kind == 17 and entryMutableSlotCandidate.flags == 1) -> if {
                        entryMutableSlotIndex! -> mutableBindingRoot => entryMutableSlotRoot
                        entryMutableSlotRoot == entryMutableSlotIndex! -> if {
                            "  %slot$(entryMutableSlotRoot) = alloca " -> print
                            entryMutableSlotCandidate -> writeType
                            ", align $(entryMutableSlotCandidate.typeSymbol -> storageAlign)" -> println
                        }
                    }
                    entryMutableSlotIndex! + 1 => entryMutableSlotIndex!
                }
                [Bool; ~] => entryScheduled!
                0 => entryScheduleInit!
                entryScheduleInit! < (context.ir -> len) -> while {
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
                            context.ir[entryScheduleCandidate!] => entryScheduleNode
                            entryScheduleNode.parent => entryScheduleAncestor!
                            entryScheduleNode.kind == 19 => entryInsideControlRegion!
                            (entryScheduleAncestor! > functionIndex! and not entryInsideControlRegion!) -> while {
                                (context.ir[entryScheduleAncestor!].kind == 19 or context.ir[entryScheduleAncestor!].kind == 20) -> if { true => entryInsideControlRegion! } else { context.ir[entryScheduleAncestor!].parent => entryScheduleAncestor! }
                            }
                            (entryInsideControlRegion! or entryScheduleNode.kind == 19) -> if {
                                true => entryScheduled![entryScheduleCandidate!]
                                true => entryScheduleProgress!
                            }
                            not (entryInsideControlRegion! or entryScheduleNode.kind == 19) => entryScheduleReady!
                            (entryScheduleReady! and entryScheduleNode.operand0 > functionIndex! and entryScheduleNode.operand0 < entryEnd! and not entryScheduled![entryScheduleNode.operand0]) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and entryScheduleNode.kind != 18 and entryScheduleNode.kind != 20 and entryScheduleNode.operand1 > functionIndex! and entryScheduleNode.operand1 < entryEnd! and not entryScheduled![entryScheduleNode.operand1]) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and entryScheduleNode.kind == 5) -> if {
                                false => entryMutableRead!
                                functionIndex! + 1 => entryMutableReadBindingSearch!
                                entryMutableReadBindingSearch! < entryEnd! -> while {
                                    (context.ir[entryMutableReadBindingSearch!].kind == 17 and context.ir[entryMutableReadBindingSearch!].symbol == entryScheduleNode.symbol and context.ir[entryMutableReadBindingSearch!].flags == 1) -> if { true => entryMutableRead! }
                                    entryMutableReadBindingSearch! + 1 => entryMutableReadBindingSearch!
                                }
                                entryMutableRead! -> if {
                                    context.sources[entryScheduleNode.sourceModule] -> ast.lower => entryMutableReadAst!
                                    functionIndex! + 1 => entryMutableReadBarrierSearch!
                                    entryMutableReadBarrierSearch! < entryEnd! -> while {
                                        context.ir[entryMutableReadBarrierSearch!] => entryMutableReadBarrier
                                        (not entryScheduled![entryMutableReadBarrierSearch!] and (entryMutableReadBarrier.kind == 20 or (entryMutableReadBarrier.kind == 17 and entryMutableReadBarrier.flags == 1)) and entryMutableReadAst![entryMutableReadBarrier.astNode].start < entryMutableReadAst![entryScheduleNode.astNode].start) -> if {
                                            false => entryScheduleReady!
                                        }
                                        entryMutableReadBarrierSearch! + 1 => entryMutableReadBarrierSearch!
                                    }
                                }
                            }
                            (entryScheduleReady! and (entryScheduleNode.kind == 6 or entryScheduleNode.kind == 18 or entryScheduleNode.kind == 20 or (entryScheduleNode.kind == 17 and entryScheduleNode.flags == 1))) -> if {
                                true => entryRootEffect!
                                entryScheduleNode.parent => entryEffectAncestor!
                                (entryEffectAncestor! > functionIndex! and entryRootEffect!) -> while {
                                    (context.ir[entryEffectAncestor!].kind == 6 or context.ir[entryEffectAncestor!].kind == 18 or context.ir[entryEffectAncestor!].kind == 20 or (context.ir[entryEffectAncestor!].kind == 17 and context.ir[entryEffectAncestor!].flags == 1)) -> if { false => entryRootEffect! } else { context.ir[entryEffectAncestor!].parent => entryEffectAncestor! }
                                }
                                entryRootEffect! -> if {
                                    context.sources[entryScheduleNode.sourceModule] -> ast.lower => entryEffectAst!
                                    functionIndex! + 1 => entryEarlierEffectSearch!
                                    entryEarlierEffectSearch! < entryEnd! -> while {
                                        context.ir[entryEarlierEffectSearch!] => entryEarlierEffect
                                        (not entryScheduled![entryEarlierEffectSearch!] and (entryEarlierEffect.kind == 6 or entryEarlierEffect.kind == 18 or entryEarlierEffect.kind == 20 or (entryEarlierEffect.kind == 17 and entryEarlierEffect.flags == 1)) and entryEffectAst![entryEarlierEffect.astNode].start < entryEffectAst![entryScheduleNode.astNode].start) -> if {
                                            entryEarlierEffect.parent => entryEarlierEffectAncestor!
                                            true => entryEarlierRootEffect!
                                            false => entryEarlierInsideRegion!
                                            (entryEarlierEffectAncestor! > functionIndex! and entryEarlierRootEffect! and not entryEarlierInsideRegion!) -> while {
                                                (context.ir[entryEarlierEffectAncestor!].kind == 19 or context.ir[entryEarlierEffectAncestor!].kind == 20) -> if { true => entryEarlierInsideRegion! } else {
                                                    (context.ir[entryEarlierEffectAncestor!].kind == 6 or context.ir[entryEarlierEffectAncestor!].kind == 18 or context.ir[entryEarlierEffectAncestor!].kind == 20 or (context.ir[entryEarlierEffectAncestor!].kind == 17 and context.ir[entryEarlierEffectAncestor!].flags == 1)) -> if { false => entryEarlierRootEffect! } else { context.ir[entryEarlierEffectAncestor!].parent => entryEarlierEffectAncestor! }
                                                }
                                            }
                                            (entryEarlierRootEffect! and not entryEarlierInsideRegion!) -> if { false => entryScheduleReady! }
                                        }
                                        entryEarlierEffectSearch! + 1 => entryEarlierEffectSearch!
                                    }
                                }
                            }
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
                    context.ir[entryExpressionIndex!] => entryExpression
                    (entryExpression.kind == 2 and entryExpression.parent >= 0 and context.ir[entryExpression.parent].kind == 17) -> if {
                        context.sources[entryExpression.sourceModule] -> lexer.lex => entryExpressionTokens!
                        entryExpressionTokens![entryExpression.payloadToken] => entryExpressionToken
                        entryExpressionToken.span.length - UIntSize(2) => entryExpressionLength
                        "  %v$(entryExpressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(entryExpressionIndex!), 0" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.text %v$(entryExpressionIndex!)_ptr, i64 $entryExpressionLength, 1" -> println
                    }
                    entryExpression.kind == 5 -> if {
                        -1 => entryBindingValueIr!
                        functionIndex! + 1 => entryBindingValueSearch!
                        entryBindingValueSearch! < entryEnd! -> while {
                            (context.ir[entryBindingValueSearch!].kind == 17 and context.ir[entryBindingValueSearch!].symbol == entryExpression.symbol) -> if { entryBindingValueSearch! => entryBindingValueIr! }
                            entryBindingValueSearch! + 1 => entryBindingValueSearch!
                        }
                        entryBindingValueIr! >= 0 -> if {
                            context.ir[entryBindingValueIr!] => entryBindingValue
                            entryBindingValue.flags == 1 -> if {
                                entryBindingValueIr! -> mutableBindingRoot => entryBindingRoot
                                "  %v$(entryExpressionIndex!) = load " -> print
                                entryExpression -> writeType
                                ", ptr %slot$(entryBindingRoot), align " -> print
                                "$(entryExpression.typeSymbol -> storageAlign)" -> println
                            } else {
                                context.ir[entryBindingValue.operand0] => entryBindingOperand
                                "  %v$(entryExpressionIndex!) = freeze " -> print
                                entryExpression -> writeType
                                " " -> print
                                (entryBindingOperand.kind == 3 or entryBindingOperand.kind == 4) -> if {
                                    context.sources[entryBindingOperand.sourceModule] -> lexer.lex => entryBindingOperandTokens!
                                    entryBindingOperandTokens![entryBindingOperand.payloadToken] => entryBindingOperandToken
                                    entryBindingOperand.kind == 3 -> if { context.sources[entryBindingOperand.sourceModule] -> slice(entryBindingOperandToken.span.start, entryBindingOperandToken.span.length) -> print } else {
                                        ((context.sources[entryBindingOperand.sourceModule] -> byte(entryBindingOperandToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                    }
                                } else { "%v$(entryBindingValue.operand0)" -> print }
                                "" -> println
                            }
                        }
                    }
                    (entryExpression.kind == 17 and entryExpression.flags == 1) -> if {
                        entryExpressionIndex! -> mutableBindingRoot => entryMutableRoot
                        context.ir[entryExpression.operand0] => entryMutableValue
                        "  store " -> print
                        entryExpression -> writeType
                        " " -> print
                        (entryMutableValue.kind == 3 or entryMutableValue.kind == 4) -> if {
                            context.sources[entryMutableValue.sourceModule] -> lexer.lex => entryMutableTokens!
                            entryMutableTokens![entryMutableValue.payloadToken] => entryMutableToken
                            entryMutableValue.kind == 3 -> if { context.sources[entryMutableValue.sourceModule] -> slice(entryMutableToken.span.start, entryMutableToken.span.length) -> print } else {
                                ((context.sources[entryMutableValue.sourceModule] -> byte(entryMutableToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                            }
                        } else { "%v$(entryExpression.operand0)" -> print }
                        ", ptr %slot$(entryMutableRoot), align " -> print
                        "$(entryExpression.typeSymbol -> storageAlign)" -> println
                    }
                    (entryExpression.kind == 7 or entryExpression.kind == 8) -> if {
                        context.ir[entryExpression.operand0] => entryLeft
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
                                context.sources[entryLeft.sourceModule] -> lexer.lex => entryLeftTokens!
                                entryLeftTokens![entryLeft.payloadToken] => entryLeftToken
                                entryLeft.kind == 3 -> if { context.sources[entryLeft.sourceModule] -> slice(entryLeftToken.span.start, entryLeftToken.span.length) -> print } else {
                                    ((context.sources[entryLeft.sourceModule] -> byte(entryLeftToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryExpression.operand0)" -> print }
                        }
                        ", " -> print
                        entryExpression.kind == 7 -> if {
                            entryExpression.opcode == -26 -> if { "true" -> println } else {
                                (entryLeft.kind == 3 or entryLeft.kind == 4) -> if {
                                    context.sources[entryLeft.sourceModule] -> lexer.lex => entryUnaryTokens!
                                    entryUnaryTokens![entryLeft.payloadToken] => entryUnaryToken
                                    context.sources[entryLeft.sourceModule] -> slice(entryUnaryToken.span.start, entryUnaryToken.span.length) -> println
                                } else { "%v$(entryExpression.operand0)" -> println }
                            }
                        } else {
                            context.ir[entryExpression.operand1] => entryRight
                            (entryRight.kind == 3 or entryRight.kind == 4) -> if {
                                context.sources[entryRight.sourceModule] -> lexer.lex => entryRightTokens!
                                entryRightTokens![entryRight.payloadToken] => entryRightToken
                                entryRight.kind == 3 -> if { context.sources[entryRight.sourceModule] -> slice(entryRightToken.span.start, entryRightToken.span.length) -> println } else {
                                    ((context.sources[entryRight.sourceModule] -> byte(entryRightToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> println
                                }
                            } else { "%v$(entryExpression.operand1)" -> println }
                        }
                    }
                    entryExpression.kind == 6 -> if {
                        (entryExpression.symbol == -101 or entryExpression.symbol == -102) -> if {
                            context.ir[entryExpression.operand0] => runtimeArgument
                            runtimeArgument.kind == 2 -> if {
                                context.sources[runtimeArgument.sourceModule] -> lexer.lex => runtimeArgumentTokens!
                                runtimeArgumentTokens![runtimeArgument.payloadToken] => runtimeArgumentToken
                                Int(runtimeArgumentToken.span.length) - 2 => runtimeArgumentLength
                                context.sources[runtimeArgument.sourceModule] -> interpolation.lower => entryExpressionInterpolation!
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
                                                    "" => entryExpressionOperation!
                                                    entryInterpolationNode.kind == 2 -> if { "sub" => entryExpressionOperation! }
                                                    (entryInterpolationNode.kind == 2 and entryInterpolationNode.opcode == -26) -> if { "xor" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdPlus -> if { "add" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdMinus -> if { "sub" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdStar -> if { "mul" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdSlash -> if { "sdiv" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdPercent -> if { "srem" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdEqualEqual -> if { "icmp eq" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdBangEqual -> if { "icmp ne" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdLess -> if { "icmp slt" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdLessEqual -> if { "icmp sle" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdGreater -> if { "icmp sgt" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == grammar.tokenIdGreaterEqual -> if { "icmp sge" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == -24 -> if { "or" => entryExpressionOperation! }
                                                    entryInterpolationNode.opcode == -25 -> if { "and" => entryExpressionOperation! }
                                                    (entryInterpolationNode.opcode == -24 or entryInterpolationNode.opcode == -25 or (entryInterpolationNode.kind == 2 and entryInterpolationNode.opcode == -26)) => entryExpressionBoolOperands!
                                                    (entryInterpolationNode.opcode == grammar.tokenIdEqualEqual or entryInterpolationNode.opcode == grammar.tokenIdBangEqual) -> if {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand0] => entryExpressionTypeOperand
                                                        entryExpressionTypeOperand.typeSymbol == 23 -> if { true => entryExpressionBoolOperands! }
                                                        entryExpressionTypeOperand.kind == 1 -> if {
                                                            functionIndex! + 1 => entryExpressionTypeBindingSearch!
                                                            entryExpressionTypeBindingSearch! < entryEnd! -> while {
                                                                (context.ir[entryExpressionTypeBindingSearch!].kind == 17 and context.ir[entryExpressionTypeBindingSearch!].symbol == entryExpressionTypeOperand.symbol and context.ir[entryExpressionTypeBindingSearch!].typeSymbol == 23) -> if { true => entryExpressionBoolOperands! }
                                                                entryExpressionTypeBindingSearch! + 1 => entryExpressionTypeBindingSearch!
                                                            }
                                                        }
                                                    }
                                                    entryExpressionBoolOperands! -> if { "i1" } else { "i32" } => entryExpressionOperandType
                                                    "  %v$(entryExpressionIndex!)_expression$(entryExpressionNodeIndex!) = $(entryExpressionOperation!) $entryExpressionOperandType " -> print
                                                    (entryInterpolationNode.kind == 2 and entryInterpolationNode.opcode != -26) -> if { "0" -> print } else {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand0] => entryExpressionLeft
                                                        (entryExpressionLeft.kind == 0 or entryExpressionLeft.kind == 4) -> if {
                                                            entryExpressionLeft.kind == 4 -> if {
                                                                (context.sources[runtimeArgument.sourceModule] -> byte(entryExpressionLeft.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                            } else { context.sources[runtimeArgument.sourceModule] -> slice(entryExpressionLeft.payloadStart, entryExpressionLeft.payloadLength) -> print }
                                                        } else {
                                                        entryExpressionLeft.kind == 1 -> if {
                                                            -1 => entryExpressionLeftBinding!
                                                            functionIndex! + 1 => entryExpressionLeftBindingSearch!
                                                            entryExpressionLeftBindingSearch! < entryEnd! -> while {
                                                                (context.ir[entryExpressionLeftBindingSearch!].kind == 17 and context.ir[entryExpressionLeftBindingSearch!].symbol == entryExpressionLeft.symbol) -> if { entryExpressionLeftBindingSearch! => entryExpressionLeftBinding! }
                                                                entryExpressionLeftBindingSearch! + 1 => entryExpressionLeftBindingSearch!
                                                            }
                                                            entryExpressionLeftBinding! >= 0 -> if {
                                                                context.ir[context.ir[entryExpressionLeftBinding!].operand0] => entryExpressionLeftValue
                                                                entryExpressionLeftValue.kind == 3 -> if {
                                                                    context.sources[entryExpressionLeftValue.sourceModule] -> lexer.lex => entryExpressionLeftTokens!
                                                                    entryExpressionLeftTokens![entryExpressionLeftValue.payloadToken] => entryExpressionLeftToken
                                                                    context.sources[entryExpressionLeftValue.sourceModule] -> slice(entryExpressionLeftToken.span.start, entryExpressionLeftToken.span.length) -> print
                                                                } else { "%v$(context.ir[entryExpressionLeftBinding!].operand0)" -> print }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand0)" -> print }
                                                        }
                                                    }
                                                    ", " -> print
                                                    entryInterpolationNode.kind == 2 -> if {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand0] => entryExpressionUnary
                                                        entryInterpolationNode.opcode == -26 -> if { "true" -> println } else {
                                                        (entryExpressionUnary.kind == 0 or entryExpressionUnary.kind == 4) -> if {
                                                            entryExpressionUnary.kind == 4 -> if {
                                                                (context.sources[runtimeArgument.sourceModule] -> byte(entryExpressionUnary.payloadStart)) == UInt8(116) -> if { "1" -> println } else { "0" -> println }
                                                            } else { context.sources[runtimeArgument.sourceModule] -> slice(entryExpressionUnary.payloadStart, entryExpressionUnary.payloadLength) -> println }
                                                        } else {
                                                        entryExpressionUnary.kind == 1 -> if {
                                                            -1 => entryExpressionUnaryBinding!
                                                            functionIndex! + 1 => entryExpressionUnaryBindingSearch!
                                                            entryExpressionUnaryBindingSearch! < entryEnd! -> while {
                                                                (context.ir[entryExpressionUnaryBindingSearch!].kind == 17 and context.ir[entryExpressionUnaryBindingSearch!].symbol == entryExpressionUnary.symbol) -> if { entryExpressionUnaryBindingSearch! => entryExpressionUnaryBinding! }
                                                                entryExpressionUnaryBindingSearch! + 1 => entryExpressionUnaryBindingSearch!
                                                            }
                                                            entryExpressionUnaryBinding! >= 0 -> if {
                                                                context.ir[context.ir[entryExpressionUnaryBinding!].operand0] => entryExpressionUnaryValue
                                                                entryExpressionUnaryValue.kind == 3 -> if {
                                                                    context.sources[entryExpressionUnaryValue.sourceModule] -> lexer.lex => entryExpressionUnaryTokens!
                                                                    entryExpressionUnaryTokens![entryExpressionUnaryValue.payloadToken] => entryExpressionUnaryToken
                                                                    context.sources[entryExpressionUnaryValue.sourceModule] -> slice(entryExpressionUnaryToken.span.start, entryExpressionUnaryToken.span.length) -> println
                                                                } else { "%v$(context.ir[entryExpressionUnaryBinding!].operand0)" -> println }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand0)" -> println }
                                                        }
                                                        }
                                                    } else {
                                                        entryExpressionInterpolation![entryInterpolationNode.operand1] => entryExpressionRight
                                                        (entryExpressionRight.kind == 0 or entryExpressionRight.kind == 4) -> if {
                                                            entryExpressionRight.kind == 4 -> if {
                                                                (context.sources[runtimeArgument.sourceModule] -> byte(entryExpressionRight.payloadStart)) == UInt8(116) -> if { "1" -> println } else { "0" -> println }
                                                            } else { context.sources[runtimeArgument.sourceModule] -> slice(entryExpressionRight.payloadStart, entryExpressionRight.payloadLength) -> println }
                                                        } else {
                                                        entryExpressionRight.kind == 1 -> if {
                                                            -1 => entryExpressionRightBinding!
                                                            functionIndex! + 1 => entryExpressionRightBindingSearch!
                                                            entryExpressionRightBindingSearch! < entryEnd! -> while {
                                                                (context.ir[entryExpressionRightBindingSearch!].kind == 17 and context.ir[entryExpressionRightBindingSearch!].symbol == entryExpressionRight.symbol) -> if { entryExpressionRightBindingSearch! => entryExpressionRightBinding! }
                                                                entryExpressionRightBindingSearch! + 1 => entryExpressionRightBindingSearch!
                                                            }
                                                            entryExpressionRightBinding! >= 0 -> if {
                                                                context.ir[context.ir[entryExpressionRightBinding!].operand0] => entryExpressionRightValue
                                                                entryExpressionRightValue.kind == 3 -> if {
                                                                    context.sources[entryExpressionRightValue.sourceModule] -> lexer.lex => entryExpressionRightTokens!
                                                                    entryExpressionRightTokens![entryExpressionRightValue.payloadToken] => entryExpressionRightToken
                                                                    context.sources[entryExpressionRightValue.sourceModule] -> slice(entryExpressionRightToken.span.start, entryExpressionRightToken.span.length) -> println
                                                                } else { "%v$(context.ir[entryExpressionRightBinding!].operand0)" -> println }
                                                            }
                                                        } else { "%v$(entryExpressionIndex!)_expression$(entryInterpolationNode.operand1)" -> println }
                                                        }
                                                    }
                                                }
                                                entryExpressionNodeIndex! - 1 => entryExpressionNodeIndex!
                                            }
                                            entryExpressionRoot.typeSymbol => entryExpressionRootTypeSymbol!
                                            -1 => entryExpressionRootBinding!
                                            entryExpressionRoot.kind == 1 -> if {
                                                functionIndex! + 1 => entryExpressionRootBindingSearch!
                                                entryExpressionRootBindingSearch! < entryEnd! -> while {
                                                    (context.ir[entryExpressionRootBindingSearch!].kind == 17 and context.ir[entryExpressionRootBindingSearch!].symbol == entryExpressionRoot.symbol) -> if { entryExpressionRootBindingSearch! => entryExpressionRootBinding! }
                                                    entryExpressionRootBindingSearch! + 1 => entryExpressionRootBindingSearch!
                                                }
                                                entryExpressionRootBinding! >= 0 -> if { context.ir[entryExpressionRootBinding!].typeSymbol => entryExpressionRootTypeSymbol! }
                                            }
                                            entryExpressionRootTypeSymbol! == 23 -> if { "  call void @sl_runtime_print_i1(i1 " -> print } else { "  call void @sl_runtime_print_i32(i32 " -> print }
                                            (entryExpressionRoot.kind == 0 or entryExpressionRoot.kind == 4) -> if {
                                                entryExpressionRoot.kind == 4 -> if {
                                                    (context.sources[runtimeArgument.sourceModule] -> byte(entryExpressionRoot.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                } else { context.sources[runtimeArgument.sourceModule] -> slice(entryExpressionRoot.payloadStart, entryExpressionRoot.payloadLength) -> print }
                                            } else {
                                            entryExpressionRoot.kind == 1 -> if {
                                                entryExpressionRootBinding! >= 0 -> if {
                                                    context.ir[context.ir[entryExpressionRootBinding!].operand0] => entryExpressionRootValue
                                                    (entryExpressionRootValue.kind == 3 or entryExpressionRootValue.kind == 4) -> if {
                                                        context.sources[entryExpressionRootValue.sourceModule] -> lexer.lex => entryExpressionRootTokens!
                                                        entryExpressionRootTokens![entryExpressionRootValue.payloadToken] => entryExpressionRootToken
                                                        entryExpressionRootValue.kind == 4 -> if {
                                                            (context.sources[entryExpressionRootValue.sourceModule] -> byte(entryExpressionRootToken.span.start)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                        } else { context.sources[entryExpressionRootValue.sourceModule] -> slice(entryExpressionRootToken.span.start, entryExpressionRootToken.span.length) -> print }
                                                    } else { "%v$(context.ir[entryExpressionRootBinding!].operand0)" -> print }
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
                                        ((context.sources[runtimeArgument.sourceModule] -> byte(interpolationDollar!)) == UInt8(36) and interpolationDollar! + UIntSize(1) < interpolationContentEnd) -> if {
                                            interpolationDollar! + UIntSize(1) => interpolationNameStart
                                            interpolationNameStart => interpolationNameEnd!
                                            true => interpolationNameContinues!
                                            (interpolationNameEnd! < interpolationContentEnd and interpolationNameContinues!) -> while {
                                                context.sources[runtimeArgument.sourceModule] -> byte(interpolationNameEnd!) => interpolationNameByte
                                                ((interpolationNameByte >= UInt8(48) and interpolationNameByte <= UInt8(57)) or (interpolationNameByte >= UInt8(65) and interpolationNameByte <= UInt8(90)) or (interpolationNameByte >= UInt8(97) and interpolationNameByte <= UInt8(122)) or interpolationNameByte == UInt8(95)) -> if {
                                                    interpolationNameEnd! + UIntSize(1) => interpolationNameEnd!
                                                } else { false => interpolationNameContinues! }
                                            }
                                            interpolationNameEnd! > interpolationNameStart -> if {
                                                context.sources[runtimeArgument.sourceModule] -> symbols.collect => interpolationSymbols!
                                                0 => interpolationSymbolIndex!
                                                interpolationSymbolIndex! < (interpolationSymbols! -> len) -> while {
                                                    interpolationSymbols![interpolationSymbolIndex!] => interpolationSymbol
                                                    interpolationSymbol.kind == 9 -> if {
                                                        runtimeArgumentTokens![interpolationSymbol.nameToken] => interpolationSymbolToken
                                                        interpolationSymbolToken.span.length == interpolationNameEnd! - interpolationNameStart => interpolationNameEqual!
                                                        UIntSize(0) => interpolationNameByteIndex!
                                                        (interpolationNameEqual! and interpolationNameByteIndex! < interpolationSymbolToken.span.length) -> while {
                                                            (context.sources[runtimeArgument.sourceModule] -> byte(interpolationNameStart + interpolationNameByteIndex!)) != (context.sources[runtimeArgument.sourceModule] -> byte(interpolationSymbolToken.span.start + interpolationNameByteIndex!)) -> if { false => interpolationNameEqual! }
                                                            interpolationNameByteIndex! + UIntSize(1) => interpolationNameByteIndex!
                                                        }
                                                        interpolationNameEqual! -> if {
                                                            functionIndex! + 1 => interpolationBindingSearch!
                                                            interpolationBindingSearch! < entryEnd! -> while {
                                                                (context.ir[interpolationBindingSearch!].kind == 17 and context.ir[interpolationBindingSearch!].symbol == interpolationSymbolIndex! and context.ir[interpolationBindingSearch!].typeSymbol == 2) -> if {
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
                                        context.ir[interpolationBindingIr!] => interpolationBinding
                                        context.ir[interpolationBinding.operand0] => interpolationValue
                                        "  call void @sl_runtime_print_i32(i32 " -> print
                                        interpolationValue.kind == 3 -> if {
                                            context.sources[interpolationValue.sourceModule] -> lexer.lex => interpolationValueTokens!
                                            interpolationValueTokens![interpolationValue.payloadToken] => interpolationValueToken
                                            context.sources[interpolationValue.sourceModule] -> slice(interpolationValueToken.span.start, interpolationValueToken.span.length) -> print
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
                            context.ir[entryExpression.operand0] => entryArgument
                            entryArgument -> writeType
                            " " -> print
                            entryArgument.kind == 2 -> if {
                                context.sources[entryArgument.sourceModule] -> lexer.lex => entryArgumentTokens!
                                entryArgumentTokens![entryArgument.payloadToken] => entryArgumentToken
                                Int(entryArgumentToken.span.length) - 2 => entryArgumentLength
                                "{ ptr @sl_str_$(entryExpression.operand0), i64 $entryArgumentLength }" -> print
                            } else {
                            (entryArgument.kind == 3 or entryArgument.kind == 4) -> if {
                                context.sources[entryArgument.sourceModule] -> lexer.lex => entryArgumentTokens!
                                entryArgumentTokens![entryArgument.payloadToken] => entryArgumentToken
                                entryArgument.kind == 3 -> if {
                                    context.sources[entryArgument.sourceModule] -> slice(entryArgumentToken.span.start, entryArgumentToken.span.length) -> print
                                } else {
                                    ((context.sources[entryArgument.sourceModule] -> byte(entryArgumentToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryExpression.operand0)" -> print }
                            }
                        }
                        ")" -> println
                        }
                    }
                    entryExpression.kind == 18 -> if {
                        context.ir[entryExpression.operand0] => entryIfCondition
                        "  br i1 " -> print
                        entryIfCondition.kind == 4 -> if {
                            context.sources[entryIfCondition.sourceModule] -> lexer.lex => entryIfConditionTokens!
                            entryIfConditionTokens![entryIfCondition.payloadToken] => entryIfConditionToken
                            ((context.sources[entryIfCondition.sourceModule] -> byte(entryIfConditionToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                        } else { "%v$(entryExpression.operand0)" -> print }
                        entryExpression.nextOperand >= 0 -> if {
                            ", label %if$(entryExpressionIndex!)_then, label %if$(entryExpressionIndex!)_else" -> println
                        } else {
                            ", label %if$(entryExpressionIndex!)_then, label %if$(entryExpressionIndex!)_merge" -> println
                        }
                        "if$(entryExpressionIndex!)_then:" -> println
                        entryExpression.operand1 -> emitRegion
                        "  br label %if$(entryExpressionIndex!)_merge" -> println
                        entryExpression.nextOperand >= 0 -> if {
                            "if$(entryExpressionIndex!)_else:" -> println
                            entryExpression.nextOperand -> emitRegion
                            "  br label %if$(entryExpressionIndex!)_merge" -> println
                        }
                        "if$(entryExpressionIndex!)_merge:" -> println
                        (entryExpression.typeSymbol != 0 and entryExpression.nextOperand >= 0) -> if {
                            context.ir[entryExpression.operand1] => entryThenRegion
                            context.ir[entryExpression.nextOperand] => entryElseRegion
                            context.ir[entryThenRegion.operand1] => entryThenValue
                            context.ir[entryElseRegion.operand1] => entryElseValue
                            "  %v$(entryExpressionIndex!) = phi " -> print
                            entryExpression -> writeType
                            " [ " -> print
                            (entryThenValue.kind == 3 or entryThenValue.kind == 4) -> if {
                                context.sources[entryThenValue.sourceModule] -> lexer.lex => entryThenValueTokens!
                                entryThenValueTokens![entryThenValue.payloadToken] => entryThenValueToken
                                entryThenValue.kind == 3 -> if { context.sources[entryThenValue.sourceModule] -> slice(entryThenValueToken.span.start, entryThenValueToken.span.length) -> print } else {
                                    ((context.sources[entryThenValue.sourceModule] -> byte(entryThenValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryThenRegion.operand1)" -> print }
                            ", %if" -> print
                            entryThenValue.kind == 18 -> if { "$(entryThenRegion.operand1)_merge" -> print } else { "$(entryExpressionIndex!)_then" -> print }
                            " ], [ " -> print
                            (entryElseValue.kind == 3 or entryElseValue.kind == 4) -> if {
                                context.sources[entryElseValue.sourceModule] -> lexer.lex => entryElseValueTokens!
                                entryElseValueTokens![entryElseValue.payloadToken] => entryElseValueToken
                                entryElseValue.kind == 3 -> if { context.sources[entryElseValue.sourceModule] -> slice(entryElseValueToken.span.start, entryElseValueToken.span.length) -> print } else {
                                    ((context.sources[entryElseValue.sourceModule] -> byte(entryElseValueToken.span.start)) == UInt8(116)) -> if { "1" } else { "0" } -> print
                                }
                            } else { "%v$(entryElseRegion.operand1)" -> print }
                            ", %if" -> print
                            entryElseValue.kind == 18 -> if { "$(entryElseRegion.operand1)_merge" -> print } else { "$(entryExpressionIndex!)_else" -> print }
                            " ]" -> println
                        }
                    }
                    entryExpression.kind == 20 -> if {
                        "  br label %while$(entryExpressionIndex!)_header" -> println
                        "while$(entryExpressionIndex!)_header:" -> println
                        WhileBranchRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex! } -> emitWhileBranch
                        "while$(entryExpressionIndex!)_body:" -> println
                        entryExpression.operand1 -> emitRegion
                        OwnedDropRequest { regionIndex: entryExpression.operand1, beforeAst: -1, edgeIndex: entryExpressionIndex! * 10 + 3, transferredSymbol: -1 } -> emitOwnedDrops
                        "  br label %while$(entryExpressionIndex!)_header" -> println
                        "while$(entryExpressionIndex!)_exit:" -> println
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

usesTextRuntime context: EmitContext -> Bool {
    false => usesRuntime!
    0 => nodeIndex!
    nodeIndex! < (context.ir -> len) -> while {
        (context.ir[nodeIndex!].symbol == -101 or context.ir[nodeIndex!].symbol == -102) -> if { true => usesRuntime! }
        nodeIndex! + 1 => nodeIndex!
    }
    usesRuntime!
}

usesIntInterpolation context: EmitContext -> Bool {
    false => usesInterpolation!
    0 => nodeIndex!
    nodeIndex! < (context.ir -> len) -> while {
        context.ir[nodeIndex!] => node
        (node.kind == 6 and (node.symbol == -101 or node.symbol == -102) and node.operand0 >= 0 and context.ir[node.operand0].kind == 2) -> if {
            context.ir[node.operand0] => argument
            context.sources[argument.sourceModule] -> lexer.lex => tokens!
            tokens![argument.payloadToken] => token
            token.span.start + UIntSize(1) => byteIndex!
            token.span.start + token.span.length - UIntSize(1) => byteEnd
            byteIndex! < byteEnd -> while {
                ((context.sources[argument.sourceModule] -> byte(byteIndex!)) == UInt8(36) and byteIndex! + UIntSize(1) < byteEnd) -> if {
                    context.sources[argument.sourceModule] -> byte(byteIndex! + UIntSize(1)) => interpolationNextByte
                    (interpolationNextByte != UInt8(40) and ((interpolationNextByte >= UInt8(65) and interpolationNextByte <= UInt8(90)) or (interpolationNextByte >= UInt8(97) and interpolationNextByte <= UInt8(122)) or interpolationNextByte == UInt8(95) or interpolationNextByte >= UInt8(128))) -> if { true => usesInterpolation! }
                }
                byteIndex! + UIntSize(1) => byteIndex!
            }
            context.sources[argument.sourceModule] -> interpolation.lower => interpolationNodes!
            0 => interpolationIndex!
            interpolationIndex! < (interpolationNodes! -> len) -> while {
                interpolationNodes![interpolationIndex!] => interpolationNode
                (interpolationNode.sourceToken == argument.payloadToken and interpolationNode.parent < 0) -> if {
                    interpolationNode.typeSymbol == 2 -> if { true => usesInterpolation! }
                    interpolationNode.kind == 1 -> if {
                        0 => valueSearch!
                        valueSearch! < (context.ir -> len) -> while {
                            context.ir[valueSearch!] => valueNode
                            ((valueNode.kind == 10 or valueNode.kind == 17) and valueNode.sourceModule == argument.sourceModule and valueNode.symbol == interpolationNode.symbol and valueNode.typeSymbol == 2) -> if { true => usesInterpolation! }
                            valueSearch! + 1 => valueSearch!
                        }
                    }
                }
                interpolationIndex! + 1 => interpolationIndex!
            }
        }
        nodeIndex! + 1 => nodeIndex!
    }
    usesInterpolation!
}

usesBoolInterpolation context: EmitContext -> Bool {
    false => usesInterpolation!
    0 => nodeIndex!
    nodeIndex! < (context.ir -> len) -> while {
        context.ir[nodeIndex!] => node
        (node.kind == 6 and (node.symbol == -101 or node.symbol == -102) and node.operand0 >= 0 and context.ir[node.operand0].kind == 2) -> if {
            context.ir[node.operand0] => argument
            context.sources[argument.sourceModule] -> interpolation.lower => interpolationNodes!
            0 => interpolationIndex!
            interpolationIndex! < (interpolationNodes! -> len) -> while {
                interpolationNodes![interpolationIndex!] => interpolationNode
                (interpolationNode.sourceToken == argument.payloadToken and interpolationNode.parent < 0) -> if {
                    interpolationNode.typeSymbol == 23 -> if { true => usesInterpolation! }
                    interpolationNode.kind == 1 -> if {
                        0 => valueSearch!
                        valueSearch! < (context.ir -> len) -> while {
                            context.ir[valueSearch!] => valueNode
                            ((valueNode.kind == 10 or valueNode.kind == 17) and valueNode.sourceModule == argument.sourceModule and valueNode.symbol == interpolationNode.symbol and valueNode.typeSymbol == 23) -> if { true => usesInterpolation! }
                            valueSearch! + 1 => valueSearch!
                        }
                    }
                }
                interpolationIndex! + 1 => interpolationIndex!
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

emitBoolTextRuntime: -> Unit {
    """
    @sl_runtime_bool_true = private constant [4 x i8] c"true"
    @sl_runtime_bool_false = private constant [5 x i8] c"false"
    define internal void @sl_runtime_print_i1(i1 %value, i1 %newline) {
    entry:
      %data = select i1 %value, ptr @sl_runtime_bool_true, ptr @sl_runtime_bool_false
      %length = select i1 %value, i64 4, i64 5
      call void @sl_runtime_print(ptr %data, i64 %length, i1 %newline)
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
    sources -> prepare => context!
    context! -> usesTextRuntime -> if { emitWindowsTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> emitCore
}

public emitLinux sources: move [Text; ~] -> Unit {
    llvmTarget.linuxX64 => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    sources -> prepare => context!
    context! -> usesTextRuntime -> if { emitLinuxTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> emitCore
}

public emitWasm sources: move [Text; ~] -> Unit {
    llvmTarget.wasm32Browser => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    sources -> prepare => context!
    context! -> usesTextRuntime -> if { emitWasmTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> emitCore
}
