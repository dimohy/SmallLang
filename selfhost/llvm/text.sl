namespace smalllang.compiler.llvm.text

import smalllang.compiler.ast as ast
import smalllang.compiler.ir.interpolation as interpolation
import smalllang.compiler.ir.typed as typedIr
import smalllang.compiler.llvm.target as llvmTarget
import smalllang.compiler.llvm.runtime as llvmRuntime
import smalllang.compiler.semantic.analysis as analysis
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.semantic.expression_type_ids as expressionTypeIds
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.semantic.type_ids as typeIds
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar
import sys.file as file

# First LLVM text backend slice. Names are derived only from stable module and
# symbol indexes; SSA registers are derived from typed-IR indexes.
struct WhileBranchRequest {
    whileIndex: Int
    ownerIndex: Int
    conditionIndex: Int
}

struct WhileValueRequest {
    whileIndex: Int
    ownerIndex: Int
    nodeIndex: Int
}

struct BindingOwnerRequest {
    bindingIndex: Int
    ownerIndex: Int
}

struct IntegerPromotionRequest {
    expressionIndex: Int
    operandIndex: Int
    targetWidth: Int
    ownerIndex: Int
    right: Bool
}

struct CallEnvironmentRequest {
    callerIndex: Int
    callIndex: Int
    targetModule: Int
    targetSymbol: Int
    hasArgument: Bool
}

struct CaptureValueRequest {
    callerIndex: Int
    callIndex: Int
    bindingIndex: Int
    captureIndex: Int
}

struct NodeAncestorRequest {
    nodeIndex: Int
    ownerIndex: Int
}

struct TextLiteralRequest {
    sourceModule: Int
    token: syntax.SyntaxToken
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
    ranges: [analysis.SourceAnalysisRange; ~]
    nodes: [ast.AstNode; ~]
    tokens: [syntax.SyntaxToken; ~]
    symbols: [symbols.Symbol; ~]
    interpolationRanges: [interpolation.InterpolationSourceRange; ~]
    interpolations: [interpolation.InterpolationNode; ~]
    ir: [typedIr.TypedIrNode; ~]
    moves: [typedIr.MoveEvent; ~]
    nominal: [nominalTypes.NominalType; ~]
    composite: [compositeTypes.CompositeType; ~]
    modules: [modules.ModuleIdentity; ~]
    types: [typeIds.SemanticType; ~]
    fields: [typeIds.NominalField; ~]
    typeSizes: [Int; ~]
    typeAligns: [Int; ~]
    typeLayoutStatuses: [Int; ~]
    pointerBitWidth: Int
}

struct PrepareRequest {
    sources: [Text; ~]
    pointerBitWidth: Int
}

public struct TypeLayoutRequest {
    types: [typeIds.SemanticType; ~]
    fields: [typeIds.NominalField; ~]
    typeId: Int
    pointerBitWidth: Int
}

public struct TypeLayout {
    size: Int
    align: Int
    # 0 known, 1 unresolved recursive dependency, 2 unsupported type.
    status: Int
}

public struct TypeLayouts {
    sizes: [Int; ~]
    aligns: [Int; ~]
    statuses: [Int; ~]
}

# Computes target-aware storage facts from canonical recursive identity. Nominal
# layouts are a fixed point over owner-to-field edges, so declaration order does
# not affect nested structs and box indirection terminates recursive layouts.
public layoutsFor request: TypeLayoutRequest -> TypeLayouts {
    request.pointerBitWidth / 8 => pointerSize
    [Int; ~] => sizes!
    [Int; ~] => aligns!
    [Int; ~] => statuses!
    0 => seedIndex!
    seedIndex! < (request.types -> len) -> while {
        request.types[seedIndex!] => current
        -1 => size!
        1 => align!
        1 => status!
        current.status != 0 -> if { 2 => status! } else {
            current.kind == 1 -> if {
                current.origin == 1 -> if {
                    current.symbol == 0 -> if {
                        0 => size!
                        0 => status!
                    }
                    current.symbol == 1 -> if {
                        pointerSize * 2 => size!
                        pointerSize => align!
                        0 => status!
                    }
                    (current.symbol == 2 or current.symbol == 5 or current.symbol == 10 or current.symbol == 14 or current.symbol == 19 or current.symbol == 20) -> if {
                        4 => size!
                        4 => align!
                        0 => status!
                    }
                    (current.symbol == 3 or current.symbol == 8 or current.symbol == 23) -> if {
                        1 => size!
                        1 => align!
                        0 => status!
                    }
                    (current.symbol == 4 or current.symbol == 9) -> if {
                        2 => size!
                        2 => align!
                        0 => status!
                    }
                    (current.symbol == 6 or current.symbol == 7 or current.symbol == 11 or current.symbol == 21 or current.symbol == 22) -> if {
                        8 => size!
                        8 => align!
                        0 => status!
                    }
                    (current.symbol == 12 or current.symbol == 13) -> if {
                        pointerSize => size!
                        pointerSize => align!
                        0 => status!
                    }
                    current.symbol == 24 -> if {
                        pointerSize * 4 => size!
                        pointerSize => align!
                        0 => status!
                    }
                }
            }
            current.kind == 2 -> if {
                pointerSize * 2 => size!
                pointerSize => align!
                0 => status!
            }
            current.kind == 3 -> if {
                pointerSize + 16 => size!
                pointerSize => align!
                0 => status!
            }
            current.kind == 5 -> if {
                pointerSize * 2 + 16 => size!
                pointerSize => align!
                0 => status!
            }
            current.kind == 6 -> if {
                pointerSize => size!
                pointerSize => align!
                0 => status!
            }
            current.kind == 7 -> if { 2 => status! }
        }
        sizes! -> push(size!)
        aligns! -> push(align!)
        statuses! -> push(status!)
        seedIndex! + 1 => seedIndex!
    }
    true => changed!
    changed! -> while {
        false => changed!
        0 => typeIndex!
        typeIndex! < (request.types -> len) -> while {
            statuses![typeIndex!] == 1 -> if {
                request.types[typeIndex!] => current
                current.kind == 4 -> if {
                    (current.first >= 0 and statuses![current.first] == 0 and current.length >= 0) -> if {
                        sizes![current.first] * current.length => sizes![typeIndex!]
                        aligns![current.first] => aligns![typeIndex!]
                        0 => statuses![typeIndex!]
                        true => changed!
                    }
                }
                (current.kind == 1 and (current.origin == 0 or current.origin == 2)) -> if {
                    0 => size!
                    1 => align!
                    true => ready!
                    0 => fieldIndex!
                    fieldIndex! < (request.fields -> len) -> while {
                        request.fields[fieldIndex!] => field
                        (field.status == 0 and field.ownerType == typeIndex!) -> if {
                            (field.fieldType < 0 or statuses![field.fieldType] != 0) -> if { false => ready! } else {
                                aligns![field.fieldType] > align! -> if { aligns![field.fieldType] => align! }
                                ((size! + aligns![field.fieldType] - 1) / aligns![field.fieldType]) * aligns![field.fieldType] => size!
                                size! + sizes![field.fieldType] => size!
                            }
                        }
                        fieldIndex! + 1 => fieldIndex!
                    }
                    ready! -> if {
                        ((size! + align! - 1) / align!) * align! => sizes![typeIndex!]
                        align! => aligns![typeIndex!]
                        0 => statuses![typeIndex!]
                        true => changed!
                    }
                }
            }
            typeIndex! + 1 => typeIndex!
        }
    }
    TypeLayouts { sizes: sizes!, aligns: aligns!, statuses: statuses! } => result!
    result!
}

public layoutOf request: TypeLayoutRequest -> TypeLayout {
    request.typeId => requestedType
    request -> layoutsFor => layouts
    (requestedType >= 0 and requestedType < (layouts.sizes -> len)) -> if {
        TypeLayout { size: layouts.sizes[requestedType], align: layouts.aligns[requestedType], status: layouts.statuses[requestedType] }
    } else {
        TypeLayout { size: -1, align: 1, status: 2 }
    }
}

isDynamicArrayType node: typedIr.TypedIrNode -> Bool {
    (node.typeOrigin == 13 or node.typeOrigin == 14) -> if { true } else {
        node.typeId >= 0 -> if { node.typeKind == 3 } else { false }
    }
}

isDictionaryType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if { node.typeKind == 5 } else { node.typeOrigin == 15 }
}

isNominalStructType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if {
        # Canonical nominal types always retain their defining module. Builtin
        # scalars also have kind 1, but have no module. Ownership is separate:
        # plain value structs and affine structs share the same LLVM shape.
        node.typeKind == 1 and node.typeModule >= 0
    } else {
        node.typeOrigin == 0 or node.typeOrigin == 2
    }
}

isSourceTextType node: typedIr.TypedIrNode -> Bool {
    node.typeId >= 0 -> if {
        node.typeKind == 1 and node.typeOrigin == 1 and node.typeSymbol == 24
    } else {
        node.typeOrigin == 1 and node.typeSymbol == 24
    }
}

writeTextLikeType node: typedIr.TypedIrNode -> Unit uses Console {
    node -> isSourceTextType -> if { "%sl.source_text" -> print } else { "%sl.text" -> print }
}

ownsType node: typedIr.TypedIrNode -> Bool {
    (node.typeOrigin == 13 or node.typeOrigin == 14 or node.typeOrigin == 15) -> if { true } else {
    node.typeId >= 0 -> if {
        node.typeFlags % 2 == 1
    } else {
        node.typeOrigin == 13 or node.typeOrigin == 15 or node.typeOrigin == 0 or node.typeOrigin == 2
    }
    }
}

# Transitional public boundary used by the emitter and regressions. Canonical
# kind/ownership wins whenever a type ID exists; the shallow branch remains
# only for IR nodes whose migration is not complete yet.
public writeType node: typedIr.TypedIrNode -> Unit uses Console {
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
            (node.typeSymbol == 2 or node.typeSymbol == 5 or node.typeSymbol == 10 or node.typeSymbol == 14) -> if { "i32" -> print } else {
                (node.typeSymbol == 3 or node.typeSymbol == 8) -> if { "i8" -> print } else {
                    (node.typeSymbol == 4 or node.typeSymbol == 9) -> if { "i16" -> print } else {
                        (node.typeSymbol == 6 or node.typeSymbol == 7 or node.typeSymbol == 11 or node.typeSymbol == 12 or node.typeSymbol == 13) -> if { "i64" -> print } else {
                            (node.typeSymbol == 19 or node.typeSymbol == 20) -> if { "float" -> print } else {
                            (node.typeSymbol == 21 or node.typeSymbol == 22) -> if { "double" -> print } else {
                                    node.typeSymbol == 23 -> if { "i1" -> print } else {
                                        node.typeSymbol == 24 -> if { "%sl.source_text" -> print } else { "void" -> print }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    }
    }
}

prepare request: move PrepareRequest -> EmitContext {
    request.pointerBitWidth => pointerBitWidth
    request.sources -> semanticContext.prepare => prepared
    prepared -> typedIr.lowerContext => ir!
    prepared -> expressionTypeIds.resolveContext => recursiveTypes
    [typeIds.SemanticType; ~] => semanticTypes!
    0 => semanticTypeCopyIndex351!
    semanticTypeCopyIndex351! < (recursiveTypes.types -> len) -> while {
        recursiveTypes.types[semanticTypeCopyIndex351!] => semanticType
        semanticTypes! -> push(semanticType)
        semanticTypeCopyIndex351! + 1 => semanticTypeCopyIndex351!
    }
    [typeIds.NominalField; ~] => semanticFields!
    0 => semanticFieldCopyIndex353!
    semanticFieldCopyIndex353! < (recursiveTypes.fields -> len) -> while {
        recursiveTypes.fields[semanticFieldCopyIndex353!] => semanticField
        semanticFields! -> push(semanticField)
        semanticFieldCopyIndex353! + 1 => semanticFieldCopyIndex353!
    }
    [nominalTypes.NominalType; ~] => nominal!
    0 => nominalTypeCopyIndex355!
    nominalTypeCopyIndex355! < (prepared.nominal -> len) -> while {
        prepared.nominal[nominalTypeCopyIndex355!] => nominalType
        nominal! -> push(nominalType)
        nominalTypeCopyIndex355! + 1 => nominalTypeCopyIndex355!
    }
    [compositeTypes.CompositeType; ~] => composite!
    0 => compositeTypeCopyIndex357!
    compositeTypeCopyIndex357! < (prepared.composite -> len) -> while {
        prepared.composite[compositeTypeCopyIndex357!] => compositeType
        composite! -> push(compositeType)
        compositeTypeCopyIndex357! + 1 => compositeTypeCopyIndex357!
    }
    [modules.ModuleIdentity; ~] => moduleIdentities!
    0 => moduleIdentityCopyIndex359!
    moduleIdentityCopyIndex359! < (prepared.modules -> len) -> while {
        prepared.modules[moduleIdentityCopyIndex359!] => moduleIdentity
        moduleIdentities! -> push(moduleIdentity)
        moduleIdentityCopyIndex359! + 1 => moduleIdentityCopyIndex359!
    }
    [analysis.SourceAnalysisRange; ~] => analysisRanges!
    0 => sourceRangeCopyIndex361!
    sourceRangeCopyIndex361! < (prepared.package.ranges -> len) -> while {
        prepared.package.ranges[sourceRangeCopyIndex361!] => sourceRange
        analysisRanges! -> push(sourceRange)
        sourceRangeCopyIndex361! + 1 => sourceRangeCopyIndex361!
    }
    [ast.AstNode; ~] => analysisNodes!
    0 => nodeCopyIndex363!
    nodeCopyIndex363! < (prepared.package.nodes -> len) -> while {
        prepared.package.nodes[nodeCopyIndex363!] => node
        analysisNodes! -> push(node)
        nodeCopyIndex363! + 1 => nodeCopyIndex363!
    }
    [syntax.SyntaxToken; ~] => analysisTokens!
    0 => tokenCopyIndex365!
    tokenCopyIndex365! < (prepared.package.tokens -> len) -> while {
        prepared.package.tokens[tokenCopyIndex365!] => token
        analysisTokens! -> push(token)
        tokenCopyIndex365! + 1 => tokenCopyIndex365!
    }
    [symbols.Symbol; ~] => analysisSymbols!
    0 => symbolCopyIndex367!
    symbolCopyIndex367! < (prepared.package.symbols -> len) -> while {
        prepared.package.symbols[symbolCopyIndex367!] => symbol
        analysisSymbols! -> push(symbol)
        symbolCopyIndex367! + 1 => symbolCopyIndex367!
    }
    [interpolation.InterpolationSourceRange; ~] => analysisInterpolationRanges!
    [interpolation.InterpolationNode; ~] => analysisInterpolations!
    0 => interpolationSourceIndex!
    interpolationSourceIndex! < (prepared.package.sources -> len) -> while {
        prepared.package.ranges[interpolationSourceIndex!] => interpolationSourceRange
        [ast.AstNode; ~] => interpolationNodes!
        0 => interpolationAstIndex!
        interpolationAstIndex! < interpolationSourceRange.astCount -> while {
            interpolationNodes! -> push(prepared.package.nodes[interpolationSourceRange.astStart + interpolationAstIndex!])
            interpolationAstIndex! + 1 => interpolationAstIndex!
        }
        [syntax.SyntaxToken; ~] => interpolationTokens!
        0 => interpolationTokenIndex!
        interpolationTokenIndex! < interpolationSourceRange.tokenCount -> while {
            interpolationTokens! -> push(prepared.package.tokens[interpolationSourceRange.tokenStart + interpolationTokenIndex!])
            interpolationTokenIndex! + 1 => interpolationTokenIndex!
        }
        [symbols.Symbol; ~] => interpolationSymbols!
        0 => interpolationSymbolIndex!
        interpolationSymbolIndex! < interpolationSourceRange.symbolCount -> while {
            interpolationSymbols! -> push(prepared.package.symbols[interpolationSourceRange.symbolStart + interpolationSymbolIndex!])
            interpolationSymbolIndex! + 1 => interpolationSymbolIndex!
        }
        prepared.package.sources[interpolationSourceIndex!] -> len => interpolationSourceLength
        prepared.package.sources[interpolationSourceIndex!] -> slice(UIntSize(0), interpolationSourceLength) => interpolationSource
        interpolation.PreparedInterpolationRequest {
            source: interpolationSource
            nodes: interpolationNodes!
            tokens: interpolationTokens!
            symbols: interpolationSymbols!
        } => interpolationRequest!
        interpolationRequest! -> interpolation.lowerPrepared => sourceInterpolations!
        analysisInterpolations! -> len => interpolationStart
        analysisInterpolationRanges! -> push(interpolation.InterpolationSourceRange {
            sourceModule: interpolationSourceIndex!
            nodeStart: interpolationStart
            nodeCount: sourceInterpolations! -> len
        })
        0 => sourceInterpolationIndex!
        sourceInterpolationIndex! < (sourceInterpolations! -> len) -> while {
            sourceInterpolations![sourceInterpolationIndex!] => globalInterpolation!
            globalInterpolation!.parent >= 0 -> if { globalInterpolation!.parent + interpolationStart => globalInterpolation!.parent }
            globalInterpolation!.operand0 >= 0 -> if { globalInterpolation!.operand0 + interpolationStart => globalInterpolation!.operand0 }
            globalInterpolation!.operand1 >= 0 -> if { globalInterpolation!.operand1 + interpolationStart => globalInterpolation!.operand1 }
            analysisInterpolations! -> push(globalInterpolation!)
            sourceInterpolationIndex! + 1 => sourceInterpolationIndex!
        }
        interpolationSourceIndex! + 1 => interpolationSourceIndex!
    }
    [typeIds.SemanticType; ~] => layoutTypes!
    0 => layoutTypeCopyIndex418!
    layoutTypeCopyIndex418! < (semanticTypes! -> len) -> while {
        semanticTypes![layoutTypeCopyIndex418!] => layoutType
        layoutTypes! -> push(layoutType)
        layoutTypeCopyIndex418! + 1 => layoutTypeCopyIndex418!
    }
    [typeIds.NominalField; ~] => layoutFields!
    0 => layoutFieldCopyIndex420!
    layoutFieldCopyIndex420! < (semanticFields! -> len) -> while {
        semanticFields![layoutFieldCopyIndex420!] => layoutField
        layoutFields! -> push(layoutField)
        layoutFieldCopyIndex420! + 1 => layoutFieldCopyIndex420!
    }
    TypeLayoutRequest { types: layoutTypes!, fields: layoutFields!, typeId: -1, pointerBitWidth: pointerBitWidth } => layoutRequest!
    layoutRequest! -> layoutsFor => layouts
    [Int; ~] => typeSizes!
    0 => typeSizeCopyIndex424!
    typeSizeCopyIndex424! < (layouts.sizes -> len) -> while {
        layouts.sizes[typeSizeCopyIndex424!] => typeSize
        typeSizes! -> push(typeSize)
        typeSizeCopyIndex424! + 1 => typeSizeCopyIndex424!
    }
    [Int; ~] => typeAligns!
    0 => typeAlignCopyIndex426!
    typeAlignCopyIndex426! < (layouts.aligns -> len) -> while {
        layouts.aligns[typeAlignCopyIndex426!] => typeAlign
        typeAligns! -> push(typeAlign)
        typeAlignCopyIndex426! + 1 => typeAlignCopyIndex426!
    }
    [Int; ~] => typeLayoutStatuses!
    0 => layoutStatusCopyIndex428!
    layoutStatusCopyIndex428! < (layouts.statuses -> len) -> while {
        layouts.statuses[layoutStatusCopyIndex428!] => layoutStatus
        typeLayoutStatuses! -> push(layoutStatus)
        layoutStatusCopyIndex428! + 1 => layoutStatusCopyIndex428!
    }
    ir! -> typedIr.movesFrom => moves!
    request.sources => sources
    EmitContext {
        sources: sources
        ranges: analysisRanges!
        nodes: analysisNodes!
        tokens: analysisTokens!
        symbols: analysisSymbols!
        interpolationRanges: analysisInterpolationRanges!
        interpolations: analysisInterpolations!
        ir: ir!
        moves: moves!
        nominal: nominal!
        composite: composite!
        modules: moduleIdentities!
        types: semanticTypes!
        fields: semanticFields!
        typeSizes: typeSizes!
        typeAligns: typeAligns!
        typeLayoutStatuses: typeLayoutStatuses!
        pointerBitWidth: pointerBitWidth
    } => context!
    context!
}

writeDecimal value: Int -> Unit uses Console {
    "$(value)" -> print
}

writeDecimalLine value: Int -> Unit uses Console {
    "$(value)" -> println
}

emitCore context: move EmitContext -> Unit uses Console {
    functionEnd functionIndex: Int -> Int {
        functionIndex + 1 => end!
        (end! < (context.ir -> len) and context.ir[end!].kind != 0 and context.ir[end!].kind != 11) -> while { end! + 1 => end! }
        end!
    }
    nodeDescendsFrom request: NodeAncestorRequest -> Bool {
        false => descends!
        context.ir[request.nodeIndex] => descendantNode
        context.ir[request.ownerIndex] => ownerNode
        (descendantNode.sourceModule == ownerNode.sourceModule and descendantNode.astNode >= 0 and ownerNode.astNode >= 0) -> if {
            context.ranges[descendantNode.sourceModule] => descendantRange
            context.nodes[descendantRange.astStart + descendantNode.astNode].parent => ancestorAst!
            (ancestorAst! >= 0 and not descends!) -> while {
                ancestorAst! == ownerNode.astNode -> if { true => descends! } else { context.nodes[descendantRange.astStart + ancestorAst!].parent => ancestorAst! }
            }
        }
        descends!
    }
    irDescendsFrom request: NodeAncestorRequest -> Bool {
        false => descends!
        context.ir[request.nodeIndex].parent => ancestor!
        (ancestor! >= 0 and not descends!) -> while {
            ancestor! == request.ownerIndex -> if { true => descends! } else { context.ir[ancestor!].parent => ancestor! }
        }
        descends!
    }
    eachRoleOwner nodeIndex: Int -> Int {
        -1 => owner!
        context.ir[nodeIndex] => roleUse
        (roleUse.kind == 5 and roleUse.operand0 < 0 and roleUse.symbol >= 0 and roleUse.sourceModule >= 0) -> if {
            context.ranges[roleUse.sourceModule] => roleRange
            roleUse.symbol < roleRange.symbolCount -> if {
                context.symbols[roleRange.symbolStart + roleUse.symbol] => roleSymbol
                (roleSymbol.kind == 35 and roleSymbol.astNode >= 0) -> if {
                    0 => roleOwnerSearch!
                    roleOwnerSearch! < (context.ir -> len) -> while {
                        context.ir[roleOwnerSearch!] => roleOwnerCandidate
                        (roleOwnerCandidate.kind == 6 and roleOwnerCandidate.opcode == -208 and roleOwnerCandidate.sourceModule == roleUse.sourceModule and roleOwnerCandidate.astNode == roleSymbol.astNode) -> if { roleOwnerSearch! => owner! }
                        roleOwnerSearch! + 1 => roleOwnerSearch!
                    }
                }
            }
        }
        owner!
    }
    functionCaptures functionIndex: Int -> [Int; ~] {
        [Int; ~] => captures!
        context.ir[functionIndex] => targetFunction
        functionIndex -> functionEnd => targetEnd
        -1 => captureOuterAst!
        targetFunction.astNode >= 0 -> if {
            context.ranges[targetFunction.sourceModule] => captureSourceRange
            context.nodes[captureSourceRange.astStart + targetFunction.astNode].parent => captureAncestor!
            (captureAncestor! >= 0 and captureOuterAst! < 0) -> while {
                context.nodes[captureSourceRange.astStart + captureAncestor!] => captureAncestorNode
                captureAncestorNode.kind == 7 -> if { captureAncestor! => captureOuterAst! } else { captureAncestorNode.parent => captureAncestor! }
            }
        }
        [Int; ~] => captureFunctions!
        captureFunctions! -> push(functionIndex)
        0 => captureFunctionIndex!
        captureFunctionIndex! < (captureFunctions! -> len) -> while {
            captureFunctions![captureFunctionIndex!] => currentFunctionIndex
            context.ir[currentFunctionIndex] => currentFunction
            currentFunctionIndex -> functionEnd => currentFunctionEnd
            currentFunctionIndex + 1 => useIndex!
            useIndex! < currentFunctionEnd -> while {
                context.ir[useIndex!] => use
                (use.kind == 5 and use.operand0 < 0 and use.symbol >= 0 and not (currentFunction.operand1 >= 0 and use.symbol == context.ir[currentFunction.operand1].symbol)) -> if {
                    -1 => bindingIndex!
                    0 => candidateIndex!
                    candidateIndex! < (context.ir -> len) -> while {
                        context.ir[candidateIndex!] => candidate
                        ((candidate.kind == 17 or candidate.kind == 10) and candidate.sourceModule == use.sourceModule and candidate.symbol == use.symbol and (candidateIndex! < currentFunctionIndex or candidateIndex! >= currentFunctionEnd)) -> if { candidateIndex! => bindingIndex! }
                        candidateIndex! + 1 => candidateIndex!
                    }
                    (bindingIndex! >= 0 and (bindingIndex! < functionIndex or bindingIndex! >= targetEnd)) -> if {
                        false => duplicate!
                        0 => captureSearch!
                        captureSearch! < (captures! -> len) -> while {
                            captures![captureSearch!] == bindingIndex! -> if { true => duplicate! }
                            captureSearch! + 1 => captureSearch!
                        }
                        not duplicate! -> if { captures! -> push(bindingIndex!) }
                    }
                }
                (use.kind == 6 and use.targetModule >= 0 and use.symbol >= 0) -> if {
                    -1 => calledFunctionIndex!
                    0 => calledFunctionSearch!
                    calledFunctionSearch! < (context.ir -> len) -> while {
                        context.ir[calledFunctionSearch!] => calledFunctionCandidate
                        (calledFunctionCandidate.kind == 0 and calledFunctionCandidate.sourceModule == use.targetModule and calledFunctionCandidate.symbol == use.symbol) -> if { calledFunctionSearch! => calledFunctionIndex! }
                        calledFunctionSearch! + 1 => calledFunctionSearch!
                    }
                    calledFunctionIndex! >= 0 -> if {
                        false => calledFunctionInClosure!
                        context.ir[calledFunctionIndex!] => calledFunction
                        (captureOuterAst! >= 0 and calledFunction.sourceModule == targetFunction.sourceModule and calledFunction.astNode >= 0) -> if {
                            context.ranges[calledFunction.sourceModule] => calledFunctionRange
                            context.nodes[calledFunctionRange.astStart + calledFunction.astNode].parent => calledFunctionAncestor!
                            (calledFunctionAncestor! >= 0 and not calledFunctionInClosure!) -> while {
                                calledFunctionAncestor! == captureOuterAst! -> if { true => calledFunctionInClosure! } else { context.nodes[calledFunctionRange.astStart + calledFunctionAncestor!].parent => calledFunctionAncestor! }
                            }
                        }
                        false => calledFunctionVisited!
                        0 => visitedFunctionSearch!
                        visitedFunctionSearch! < (captureFunctions! -> len) -> while {
                            captureFunctions![visitedFunctionSearch!] == calledFunctionIndex! -> if { true => calledFunctionVisited! }
                            visitedFunctionSearch! + 1 => visitedFunctionSearch!
                        }
                        (calledFunctionInClosure! and not calledFunctionVisited!) -> if { captureFunctions! -> push(calledFunctionIndex!) }
                    }
                }
                useIndex! + 1 => useIndex!
            }
            captureFunctionIndex! + 1 => captureFunctionIndex!
        }
        captures!
    }
    emitCaptureValue request: CaptureValueRequest -> Unit uses Console {
        context.ir[request.callerIndex] => caller
        context.ir[request.bindingIndex] => binding
        request.callerIndex -> functionEnd => callerEnd
        (request.bindingIndex > request.callerIndex and request.bindingIndex < callerEnd) -> if {
            binding.kind == 10 -> if { "%arg" -> print } else {
                binding.flags == 1 -> if { "%callenv$(request.callIndex)_capture$(request.captureIndex)" -> print } else { "%v$(binding.operand0)" -> print }
            }
        } else {
            request.callerIndex -> functionCaptures => callerCaptures!
            -1 => capturePosition!
            0 => callerCaptureIndex!
            callerCaptureIndex! < (callerCaptures! -> len) -> while {
                context.ir[callerCaptures![callerCaptureIndex!]].symbol == binding.symbol -> if { callerCaptureIndex! => capturePosition! }
                callerCaptureIndex! + 1 => callerCaptureIndex!
            }
            capturePosition! >= 0 -> if { "%capture_$(capturePosition!)" -> print } else {
                (caller.operand1 >= 0 and context.ir[caller.operand1].symbol == binding.symbol) -> if { "%arg" -> print } else { "poison" -> print }
            }
        }
    }
    emitCallEnvironmentValues request: CallEnvironmentRequest -> Unit uses Console {
        -1 => targetFunctionIr!
        0 => targetSearch!
        targetSearch! < (context.ir -> len) -> while {
            (context.ir[targetSearch!].kind == 0 and context.ir[targetSearch!].sourceModule == request.targetModule and context.ir[targetSearch!].symbol == request.targetSymbol) -> if { targetSearch! => targetFunctionIr! }
            targetSearch! + 1 => targetSearch!
        }
        targetFunctionIr! >= 0 -> if { targetFunctionIr! -> functionCaptures } else { [Int; ~] } => callCaptures!
        request.callerIndex -> functionEnd => callerEnd
        0 => captureIndex!
        captureIndex! < (callCaptures! -> len) -> while {
            callCaptures![captureIndex!] => bindingIndex
            context.ir[bindingIndex] => binding
            (bindingIndex > request.callerIndex and bindingIndex < callerEnd and binding.kind != 10 and binding.flags == 1) -> if {
                bindingIndex -> mutableBindingRoot => bindingRoot
                "  %callenv$(request.callIndex)_capture$(captureIndex!) = load " -> print
                binding -> writeIrType
                ", ptr %slot$(bindingRoot), align " -> print
                binding -> storageAlign -> writeDecimalLine
            }
            captureIndex! + 1 => captureIndex!
        }
    }
    emitCallEnvironment request: CallEnvironmentRequest -> Bool uses Console {
        -1 => targetFunctionIr!
        0 => targetSearch!
        targetSearch! < (context.ir -> len) -> while {
            (context.ir[targetSearch!].kind == 0 and context.ir[targetSearch!].sourceModule == request.targetModule and context.ir[targetSearch!].symbol == request.targetSymbol) -> if { targetSearch! => targetFunctionIr! }
            targetSearch! + 1 => targetSearch!
        }
        targetFunctionIr! >= 0 -> if { targetFunctionIr! -> functionCaptures } else { [Int; ~] } => callCaptures!
        0 => captureIndex!
        captureIndex! < (callCaptures! -> len) -> while {
            context.ir[callCaptures![captureIndex!]] -> writeIrType
            " " -> print
            CaptureValueRequest { callerIndex: request.callerIndex, callIndex: request.callIndex, bindingIndex: callCaptures![captureIndex!], captureIndex: captureIndex! } -> emitCaptureValue
            (captureIndex! + 1 < (callCaptures! -> len) or request.hasArgument) -> if { ", " -> print }
            captureIndex! + 1 => captureIndex!
        }
        (callCaptures! -> len) > 0
    }
    sourceToken node: typedIr.TypedIrNode -> syntax.SyntaxToken {
        context.ranges[node.sourceModule] => tokenRange
        tokenRange.tokenStart + node.payloadToken => tokenIndex
        context.tokens[tokenIndex]
    }
    aggregateValueIndex wrapperIndex: Int -> Int {
        wrapperIndex => resolved!
        (wrapperIndex >= 0 and context.ir[wrapperIndex].kind == 9) -> if {
            0 => controlSearch!
            controlSearch! < (context.ir -> len) -> while {
                context.ir[controlSearch!] => aggregateCandidate
                # Explicit conversions around aggregate fields are represented
                # by a value wrapper whose direct result is a call node.
                (aggregateCandidate.parent == wrapperIndex and (aggregateCandidate.kind == 6 or aggregateCandidate.kind == 12 or aggregateCandidate.kind == 14 or aggregateCandidate.kind == 16 or aggregateCandidate.kind == 18)) -> if { controlSearch! => resolved! }
                controlSearch! + 1 => controlSearch!
            }
        }
        resolved! => result
        result
    }
    regionResultValueIndex regionIndex: Int -> Int {
        context.ir[regionIndex].operand1 => resolved!
        true => resolving!
        resolving! -> while {
            false => resolving!
            (resolved! >= 0 and context.ir[resolved!].kind == 17 and context.ir[resolved!].operand0 >= 0) -> if {
                context.ir[resolved!].operand0 => resolved!
                true => resolving!
            }
            resolved! -> aggregateValueIndex => aggregateResolved
            aggregateResolved != resolved! -> if {
                aggregateResolved => resolved!
                true => resolving!
            }
        }
        resolved!
    }
    textLiteralIsRaw request: TextLiteralRequest -> Bool {
        request.token.span.length >= UIntSize(6) and (context.sources[request.sourceModule] -> byte(request.token.span.start)) == UInt8(34) and (context.sources[request.sourceModule] -> byte(request.token.span.start + UIntSize(1))) == UInt8(34) and (context.sources[request.sourceModule] -> byte(request.token.span.start + UIntSize(2))) == UInt8(34)
    }
    textLiteralStart request: TextLiteralRequest -> UIntSize {
        request.token.span.start + UIntSize(1) => start!
        request -> textLiteralIsRaw -> if {
            request.token.span.start + UIntSize(3) => start!
            start! < request.token.span.start + request.token.span.length -> if {
                context.sources[request.sourceModule] -> byte(start!) => first
                first == UInt8(13) -> if {
                    start! + UIntSize(1) => start!
                    (start! < request.token.span.start + request.token.span.length and (context.sources[request.sourceModule] -> byte(start!)) == UInt8(10)) -> if { start! + UIntSize(1) => start! }
                } else {
                    first == UInt8(10) -> if { start! + UIntSize(1) => start! }
                }
            }
        }
        start!
    }
    textLiteralClosingLineStart request: TextLiteralRequest -> UIntSize {
        request.token.span.start + request.token.span.length - UIntSize(3) => closing!
        request -> textLiteralIsRaw -> if {
            closing! => cursor!
            (cursor! > request.token.span.start and (context.sources[request.sourceModule] -> byte(cursor! - UIntSize(1))) != UInt8(10)) -> while { cursor! - UIntSize(1) => cursor! }
            cursor! => closing!
        }
        closing! => result
        result
    }
    textLiteralEnd request: TextLiteralRequest -> UIntSize {
        request.token.span.start + request.token.span.length - UIntSize(1) => end!
        request -> textLiteralIsRaw -> if {
            request.token.span.start + request.token.span.length - UIntSize(3) => rawEnd
            request -> textLiteralClosingLineStart => closingLineStart
            closingLineStart < rawEnd -> if {
                closingLineStart - UIntSize(1) => end!
                (end! > request.token.span.start and (context.sources[request.sourceModule] -> byte(end! - UIntSize(1))) == UInt8(13)) -> if { end! - UIntSize(1) => end! }
            } else { rawEnd => end! }
        }
        end!
    }
    textLiteralIndent request: TextLiteralRequest -> UIntSize {
        UIntSize(0) => indentation!
        request -> textLiteralIsRaw -> if {
            request.token.span.start + request.token.span.length - UIntSize(3) => rawEnd
            request -> textLiteralClosingLineStart => closingLineStart
            closingLineStart < rawEnd -> if { rawEnd - closingLineStart => indentation! }
        }
        indentation!
    }
    textLiteralLength request: TextLiteralRequest -> UIntSize {
        request -> textLiteralStart => byteIndex!
        request -> textLiteralEnd => byteEnd
        request -> textLiteralIndent => indentation
        UIntSize(0) => length!
        true => lineStart!
        byteIndex! < byteEnd -> while {
            lineStart! -> if {
                UIntSize(0) => skipped!
                (skipped! < indentation and byteIndex! < byteEnd) -> while {
                    context.sources[request.sourceModule] -> byte(byteIndex!) => indentByte
                    (indentByte == UInt8(32) or indentByte == UInt8(9)) -> if {
                        byteIndex! + UIntSize(1) => byteIndex!
                        skipped! + UIntSize(1) => skipped!
                    } else { indentation => skipped! }
                }
                false => lineStart!
            }
            byteIndex! < byteEnd -> if {
                context.sources[request.sourceModule] -> byte(byteIndex!) => currentByte
                (currentByte == UInt8(13) and byteIndex! + UIntSize(1) < byteEnd and (context.sources[request.sourceModule] -> byte(byteIndex! + UIntSize(1))) == UInt8(10)) -> if {
                    length! + UIntSize(1) => length!
                    true => lineStart!
                    byteIndex! + UIntSize(1) => byteIndex!
                } else {
                    length! + UIntSize(1) => length!
                    currentByte == UInt8(10) -> if { true => lineStart! }
                }
                byteIndex! + UIntSize(1) => byteIndex!
            }
        }
        length! => result
        result
    }
    emitIntegerPromotion request: IntegerPromotionRequest -> Bool uses Console {
        context.ir[request.operandIndex] => operand
        operand -> integerNodeWidth => operandWidth!
        (operandWidth! > 0 and operandWidth! < request.targetWidth and operand.kind != 3 and operand.kind != 4) => promoted!
        promoted! -> if {
            "  %v$(request.expressionIndex)_" -> print
            request.right -> if { "right" -> print } else { "left" -> print }
            "_wide = " -> print
            operand -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
            " i$(operandWidth!) " -> print
            false => parameterOperand!
            (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and operand.kind == 5 and operand.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { true => parameterOperand! }
            parameterOperand! -> if { "%arg" -> print } else { "%v$(request.operandIndex)" -> print }
            " to i$(request.targetWidth)" -> println
        }
        promoted!
    }
    sourceNode node: typedIr.TypedIrNode -> ast.AstNode {
        context.nodes[context.ranges[node.sourceModule].astStart + node.astNode]
    }
    sourceStart node: typedIr.TypedIrNode -> UIntSize {
        context.nodes[context.ranges[node.sourceModule].astStart + node.astNode].start
    }
    sourceInterpolations node: typedIr.TypedIrNode -> [interpolation.InterpolationNode; ~] {
        context.interpolationRanges[node.sourceModule] => interpolationRange
        [interpolation.InterpolationNode; ~] => sourceNodes!
        0 => interpolationIndex!
        interpolationIndex! < interpolationRange.nodeCount -> while {
            context.interpolations[interpolationRange.nodeStart + interpolationIndex!] => interpolationNode!
            interpolationNode!.parent >= 0 -> if { interpolationNode!.parent - interpolationRange.nodeStart => interpolationNode!.parent }
            interpolationNode!.operand0 >= 0 -> if { interpolationNode!.operand0 - interpolationRange.nodeStart => interpolationNode!.operand0 }
            interpolationNode!.operand1 >= 0 -> if { interpolationNode!.operand1 - interpolationRange.nodeStart => interpolationNode!.operand1 }
            sourceNodes! -> push(interpolationNode!)
            interpolationIndex! + 1 => interpolationIndex!
        }
        sourceNodes!
    }
    emitDropValueName request: DropGlueRequest -> Unit uses Console {
        request.valueKind == 0 -> if { "%v$(request.valueIndex)" -> print } else {
            request.valueKind == 1 -> if { "%arg" -> print } else { "%dropg$(request.nameRoot)_p$(request.pathCode)" -> print }
        }
    }
    emitDropPathName request: DropGlueRequest -> Unit uses Console {
        "%dropg$(request.nameRoot)_p$(request.pathCode)" -> print
    }
    emitDropGlue request: DropGlueRequest -> Unit uses Console {
        [request, ~] => dropTasks!
        0 => dropTaskIndex!
        dropTaskIndex! < (dropTasks! -> len) -> while {
        dropTasks![dropTaskIndex!] => dropTask
        false => dropTaskMoved!
        (dropTask.bindingIndex >= 0 and dropTask.hasPartialMoves) -> if {
            context.ir[dropTask.bindingIndex] => dropGlueBinding
            context.ranges[dropGlueBinding.sourceModule] => dropGlueRange
            UIntSize(0) => dropGlueBeforeStart!
            dropTask.beforeAst >= 0 -> if { context.nodes[dropGlueRange.astStart + dropTask.beforeAst].start => dropGlueBeforeStart! }
            0 => dropGlueMoveIndex!
            (dropGlueMoveIndex! < (context.moves -> len) and not dropTaskMoved!) -> while {
                context.moves[dropGlueMoveIndex!] => dropGlueMove
                (dropGlueMove.memberIr >= 0 and dropGlueMove.sourceModule == dropGlueBinding.sourceModule and dropGlueMove.symbol == dropGlueBinding.symbol and dropGlueMove.regionIr == dropTask.regionIndex) -> if {
                    context.ir[dropGlueMove.siteIr] => dropGlueSite
                    true => dropGlueMoveBeforeEdge!
                    dropTask.beforeAst >= 0 -> if {
                        context.nodes[dropGlueRange.astStart + dropGlueSite.astNode].start >= dropGlueBeforeStart! -> if { false => dropGlueMoveBeforeEdge! }
                    }
                    context.nodes[dropGlueRange.astStart + dropGlueSite.astNode].start <= context.nodes[dropGlueRange.astStart + dropGlueBinding.astNode].start -> if { false => dropGlueMoveBeforeEdge! }
                    0 => dropTaskDepth!
                    dropTaskIndex! => dropTaskCursor!
                    (dropTaskCursor! >= 0 and dropTasks![dropTaskCursor!].fieldOrdinal >= 0) -> while {
                        dropTaskDepth! + 1 => dropTaskDepth!
                        dropTasks![dropTaskCursor!].parentTask => dropTaskCursor!
                    }
                    context.ir[dropGlueMove.memberIr] => dropMoveMember
                    context.ranges[dropMoveMember.sourceModule] => dropMoveRange
                    context.nodes[dropMoveRange.astStart + dropMoveMember.astNode] => dropMoveAst
                    -1 => dropMoveDepth!
                    dropMoveAst.firstToken => dropMoveTokenIndex!
                    dropMoveTokenIndex! < dropMoveAst.firstToken + dropMoveAst.tokenCount -> while {
                        context.tokens[dropMoveRange.tokenStart + dropMoveTokenIndex!].kind == grammar.tokenIdIdentifier -> if { dropMoveDepth! + 1 => dropMoveDepth! }
                        dropMoveTokenIndex! + 1 => dropMoveTokenIndex!
                    }
                    dropTaskDepth! == dropMoveDepth! => sameDropPath!
                    dropGlueBinding.typeOrigin => dropMoveCurrentOrigin!
                    dropGlueBinding.typeModule => dropMoveCurrentModule!
                    dropGlueBinding.typeSymbol => dropMoveCurrentSymbol!
                    0 => dropMoveIdentifierOrdinal!
                    dropMoveAst.firstToken => dropMoveTokenIndex!
                    (sameDropPath! and dropMoveTokenIndex! < dropMoveAst.firstToken + dropMoveAst.tokenCount) -> while {
                        context.tokens[dropMoveRange.tokenStart + dropMoveTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                            dropMoveIdentifierOrdinal! > 0 -> if {
                                dropMoveCurrentModule! => dropMoveOwnerSource!
                                dropMoveCurrentOrigin! == 2 -> if { context.modules[dropMoveCurrentModule!].sourceIndex => dropMoveOwnerSource! }
                                context.ranges[dropMoveOwnerSource!] => dropMoveOwnerRange
                                -1 => dropMoveFieldOrdinal!
                                -1 => dropMoveFieldTypeNode!
                                0 => dropMoveCandidateOrdinal!
                                0 => dropMoveFieldIndex!
                                dropMoveFieldIndex! < dropMoveOwnerRange.symbolCount -> while {
                                    context.symbols[dropMoveOwnerRange.symbolStart + dropMoveFieldIndex!] => dropMoveField
                                    (dropMoveField.kind == 26 and dropMoveField.parent == dropMoveCurrentSymbol!) -> if {
                                        context.tokens[dropMoveRange.tokenStart + dropMoveTokenIndex!] => dropMoveName
                                        context.tokens[dropMoveOwnerRange.tokenStart + dropMoveField.nameToken] => dropMoveFieldName
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
        (not dropTaskMoved! and (dropTask.typeOrigin == 13 or dropTask.typeOrigin == 14)) -> if {
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
            context.ranges[dropTask.typeModule] => dropStructRange
            0 => dropFieldOrdinal!
            0 => dropFieldIndex!
            dropFieldIndex! < dropStructRange.symbolCount -> while {
                context.symbols[dropStructRange.symbolStart + dropFieldIndex!] => dropField
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
    writeSemanticTypeId typeId: Int -> Unit uses Console {
        [typeId, ~] => typeTasks!
        typeTasks! -> len => typeTaskSize!
        typeTaskSize! > 0 -> while {
            typeTaskSize! - 1 => typeTaskSize!
            typeTasks![typeTaskSize!] => typeTask
            typeTask < 0 -> if { "]" -> print } else {
                context.types[typeTask] => current
                current.kind == 2 -> if { "{ ptr, i64 }" -> print }
                current.kind == 3 -> if { "%sl.array.i32" -> print }
                current.kind == 5 -> if { "%sl.dict" -> print }
                current.kind == 6 -> if { "ptr" -> print }
                current.kind == 4 -> if {
                    "[$(current.length) x " -> print
                    typeTaskSize! == (typeTasks! -> len) -> if { typeTasks! -> push(-1) } else { -1 => typeTasks![typeTaskSize!] }
                    typeTaskSize! + 1 => typeTaskSize!
                    typeTaskSize! == (typeTasks! -> len) -> if { typeTasks! -> push(current.first) } else { current.first => typeTasks![typeTaskSize!] }
                    typeTaskSize! + 1 => typeTaskSize!
                }
                current.kind == 1 -> if {
                    (current.origin == 0 or current.origin == 2) -> if {
                        "%sl.struct.m$(current.module)_s$(current.symbol)" -> print
                    } else {
                        current.symbol == 0 -> if { "void" -> print }
                        current.symbol == 1 -> if { "%sl.text" -> print }
                        (current.symbol == 2 or current.symbol == 5 or current.symbol == 10 or current.symbol == 14) -> if { "i32" -> print }
                        (current.symbol == 3 or current.symbol == 8) -> if { "i8" -> print }
                        (current.symbol == 4 or current.symbol == 9) -> if { "i16" -> print }
                        (current.symbol == 6 or current.symbol == 7 or current.symbol == 11) -> if { "i64" -> print }
                        (current.symbol == 12 or current.symbol == 13) -> if {
                            context.pointerBitWidth == 32 -> if { "i32" -> print } else { "i64" -> print }
                        }
                        (current.symbol == 19 or current.symbol == 20) -> if { "float" -> print }
                        (current.symbol == 21 or current.symbol == 22) -> if { "double" -> print }
                        current.symbol == 23 -> if { "i1" -> print }
                        current.symbol == 24 -> if { "%sl.source_text" -> print }
                    }
                }
            }
        }
    }
    effectiveTypeId node: typedIr.TypedIrNode -> Int {
        node.typeId => effective!
        (node.kind == 5 and node.operand0 >= 0 and context.ir[node.operand0].kind == 17 and context.ir[node.operand0].flags == 1) -> if {
            node.operand0 -> mutableBindingRoot => effectiveBindingRoot
            context.ir[effectiveBindingRoot].typeId >= 0 -> if { context.ir[effectiveBindingRoot].typeId => effective! }
        }
        effective!
    }
    writeIrType node: typedIr.TypedIrNode -> Unit uses Console {
        # Recursive semantic identity is authoritative. The legacy projection
        # can still carry a stale imported nominal module on region nodes, so
        # consulting it first reintroduces sys.file.SourceText as an unrelated
        # user struct even when typeId already names the canonical builtin.
        node -> integerNodeWidth => effectiveIntegerWidth
        effectiveIntegerWidth > 0 -> if {
            "i$(effectiveIntegerWidth)" -> print
        } else {
            node -> effectiveTypeId => typeId
            typeId >= 0 -> if { typeId -> writeSemanticTypeId } else { node -> writeType }
        }
    }
    llvmType symbol: Int -> Text {
        "void" => result!
        symbol == 1 -> if { "%sl.text" => result! }
        (symbol == 2 or symbol == 5 or symbol == 10 or symbol == 14) -> if { "i32" => result! }
        (symbol == 3 or symbol == 8) -> if { "i8" => result! }
        (symbol == 4 or symbol == 9) -> if { "i16" => result! }
        (symbol == 6 or symbol == 7 or symbol == 11 or symbol == 12 or symbol == 13) -> if { "i64" => result! }
        (symbol == 19 or symbol == 20) -> if { "float" => result! }
        (symbol == 21 or symbol == 22) -> if { "double" => result! }
        symbol == 23 -> if { "i1" => result! }
        symbol == 24 -> if { "%sl.source_text" => result! }
        result! => selected
        selected
    }
    storageSize symbol: Int -> Int {
        4 => result!
        symbol == 1 -> if { 16 => result! }
        (symbol == 3 or symbol == 8 or symbol == 23) -> if { 1 => result! }
        symbol == 24 -> if { context.pointerBitWidth / 8 * 4 => result! }
        (symbol == 4 or symbol == 9) -> if { 2 => result! }
        (symbol == 6 or symbol == 7 or symbol == 11 or symbol == 12 or symbol == 13 or symbol == 21 or symbol == 22) -> if { 8 => result! }
        result! => selected
        selected
    }
    integerWidth symbol: Int -> Int {
        0 => result!
        (symbol == 3 or symbol == 8) -> if { 8 => result! }
        (symbol == 4 or symbol == 9) -> if { 16 => result! }
        (symbol == 2 or symbol == 5 or symbol == 10 or symbol == 14) -> if { 32 => result! }
        (symbol == 6 or symbol == 7 or symbol == 11 or symbol == 12 or symbol == 13) -> if { 64 => result! }
        result! => selected
        selected
    }
    integerSigned symbol: Int -> Bool {
        symbol == 2 or symbol == 3 or symbol == 4 or symbol == 5 or symbol == 6 or symbol == 7
    }
    integerNodeSigned node: typedIr.TypedIrNode -> Bool {
        node.typeSymbol => symbol!
        node -> effectiveTypeId => typeId
        (typeId >= 0 and typeId < (context.types -> len)) -> if {
            context.types[typeId] => canonicalType
            (canonicalType.kind == 1 and canonicalType.origin == 1) -> if {
                canonicalType.symbol => symbol!
            }
        }
        symbol! => canonicalSymbol
        canonicalSymbol -> integerSigned
    }
    integerNodeWidth node: typedIr.TypedIrNode -> Int {
        -1 => symbol!
        node.typeOrigin == 1 -> if { node.typeSymbol => symbol! }
        node -> effectiveTypeId => typeId
        (typeId >= 0 and typeId < (context.types -> len)) -> if {
            context.types[typeId] => canonicalType
            (canonicalType.kind == 1 and canonicalType.origin == 1) -> if {
                canonicalType.symbol => symbol!
            }
        }
        symbol! -> integerWidth => width!
        symbol! == 23 -> if { 1 => width! }
        (node.kind == 5 and node.operand0 >= 0 and context.ir[node.operand0].kind == 17 and context.ir[node.operand0].flags != 1 and context.ir[node.operand0].operand0 >= 0) -> if {
            context.ir[context.ir[node.operand0].operand0] -> integerNodeWidth => width!
        }
        (node.kind == 7 and node.operand0 >= 0) -> if {
            node.opcode == -26 -> if { 1 => width! } else { context.ir[node.operand0] -> integerNodeWidth => width! }
        }
        (node.kind == 8 and node.operand0 >= 0 and node.operand1 >= 0) -> if {
            (node.opcode == grammar.tokenIdEqualEqual or node.opcode == grammar.tokenIdBangEqual or node.opcode == grammar.tokenIdLess or node.opcode == grammar.tokenIdLessEqual or node.opcode == grammar.tokenIdGreater or node.opcode == grammar.tokenIdGreaterEqual or node.opcode == -24 or node.opcode == -25) -> if {
                1 => width!
            } else {
                context.ir[node.operand0] -> integerNodeWidth => leftWidth
                context.ir[node.operand1] -> integerNodeWidth => rightWidth
                leftWidth > rightWidth -> if { leftWidth => width! } else { rightWidth => width! }
            }
        }
        width!
    }
    integerComparisonOperation expression: typedIr.TypedIrNode -> Text {
        context.ir[expression.operand0] => operand
        operand -> integerNodeSigned => signed
        expression.opcode == grammar.tokenIdLess -> if {
            signed -> if { "icmp slt" } else { "icmp ult" }
        } else {
            expression.opcode == grammar.tokenIdLessEqual -> if {
                signed -> if { "icmp sle" } else { "icmp ule" }
            } else {
                expression.opcode == grammar.tokenIdGreater -> if {
                    signed -> if { "icmp sgt" } else { "icmp ugt" }
                } else {
                    signed -> if { "icmp sge" } else { "icmp uge" }
                }
            }
        }
    }
    legacyStorageAlign symbol: Int -> Int {
        4 => result!
        symbol == 1 -> if { 8 => result! }
        (symbol == 3 or symbol == 8 or symbol == 23) -> if { 1 => result! }
        symbol == 24 -> if { context.pointerBitWidth / 8 => result! }
        (symbol == 4 or symbol == 9) -> if { 2 => result! }
        (symbol == 6 or symbol == 7 or symbol == 11 or symbol == 12 or symbol == 13 or symbol == 21 or symbol == 22) -> if { 8 => result! }
        result! => selected
        selected
    }
    storageAlign node: typedIr.TypedIrNode -> Int {
        node -> effectiveTypeId => typeId
        typeId >= 0 -> if {
            context.typeLayoutStatuses[typeId] == 0 -> if { context.typeAligns[typeId] } else { 1 }
        } else {
            node.typeSymbol -> legacyStorageAlign
        }
    }
    arrayElementTypeId node: typedIr.TypedIrNode -> Int {
        -1 => elementTypeId!
        node.typeId >= 0 -> if {
            context.types[node.typeId].kind == 3 -> if { context.types[node.typeId].first => elementTypeId! }
        } else {
            # The legacy array projection stores a nominal element as
            # typeModule/typeSymbol. Recover its canonical ID so arrays of
            # user structs do not fall through to builtin symbol 0 (Unit).
            node.typeModule >= 0 -> if {
                0 => legacyElementSearch!
                legacyElementSearch! < (context.types -> len) -> while {
                    context.types[legacyElementSearch!] => legacyElementCandidate
                    (legacyElementCandidate.kind == 1 and (legacyElementCandidate.origin == 0 or legacyElementCandidate.origin == 2) and legacyElementCandidate.module == node.typeModule and legacyElementCandidate.symbol == node.typeSymbol) -> if {
                        legacyElementSearch! => elementTypeId!
                    }
                    legacyElementSearch! + 1 => legacyElementSearch!
                }
            }
        }
        elementTypeId!
    }
    arrayElementSize node: typedIr.TypedIrNode -> Int {
        node -> arrayElementTypeId => elementTypeId
        elementTypeId >= 0 -> if { context.typeSizes[elementTypeId] } else { node.typeSymbol -> storageSize }
    }
    arrayElementAlign node: typedIr.TypedIrNode -> Int {
        node -> arrayElementTypeId => elementTypeId
        elementTypeId >= 0 -> if { context.typeAligns[elementTypeId] } else { node.typeSymbol -> legacyStorageAlign }
    }
    writeArrayElementType node: typedIr.TypedIrNode -> Unit uses Console {
        node -> arrayElementTypeId => elementTypeId
        elementTypeId >= 0 -> if { elementTypeId -> writeSemanticTypeId } else { node.typeSymbol -> llvmType -> print }
    }
    hexDigit value: Int -> Text {
        "F" => result!
        value == 0 -> if { "0" => result! }
        value == 1 -> if { "1" => result! }
        value == 2 -> if { "2" => result! }
        value == 3 -> if { "3" => result! }
        value == 4 -> if { "4" => result! }
        value == 5 -> if { "5" => result! }
        value == 6 -> if { "6" => result! }
        value == 7 -> if { "7" => result! }
        value == 8 -> if { "8" => result! }
        value == 9 -> if { "9" => result! }
        value == 10 -> if { "A" => result! }
        value == 11 -> if { "B" => result! }
        value == 12 -> if { "C" => result! }
        value == 13 -> if { "D" => result! }
        value == 14 -> if { "E" => result! }
        result! => selected
        selected
    }
    emitTextLiteralConstant request: TextLiteralRequest -> Unit uses Console {
        "c" -> print
        context.sources[request.sourceModule] -> slice(request.token.span.start, UIntSize(1)) -> print
        request -> textLiteralStart => byteIndex!
        request -> textLiteralEnd => byteEnd
        request -> textLiteralIndent => indentation
        true => lineStart!
        byteIndex! < byteEnd -> while {
            lineStart! -> if {
                UIntSize(0) => skipped!
                (skipped! < indentation and byteIndex! < byteEnd) -> while {
                    context.sources[request.sourceModule] -> byte(byteIndex!) => indentByte
                    (indentByte == UInt8(32) or indentByte == UInt8(9)) -> if {
                        byteIndex! + UIntSize(1) => byteIndex!
                        skipped! + UIntSize(1) => skipped!
                    } else { indentation => skipped! }
                }
                false => lineStart!
            }
            byteIndex! < byteEnd -> if {
                context.sources[request.sourceModule] -> byte(byteIndex!) => textByte!
                (textByte! == UInt8(13) and byteIndex! + UIntSize(1) < byteEnd and (context.sources[request.sourceModule] -> byte(byteIndex! + UIntSize(1))) == UInt8(10)) -> if {
                    UInt8(10) => textByte!
                    byteIndex! + UIntSize(1) => byteIndex!
                }
                (textByte! >= UInt8(32) and textByte! <= UInt8(126) and textByte! != UInt8(34) and textByte! != UInt8(92)) -> if {
                    context.sources[request.sourceModule] -> slice(byteIndex!, UIntSize(1)) -> print
                } else {
                    "\\" -> print
                    Int(textByte!) / 16 -> hexDigit -> print
                    Int(textByte!) % 16 -> hexDigit -> print
                }
                textByte! == UInt8(10) -> if { true => lineStart! }
                byteIndex! + UIntSize(1) => byteIndex!
            }
        }
        context.sources[request.sourceModule] -> slice(request.token.span.start, UIntSize(1)) -> println
    }
    mutableBindingRoot bindingIndex: Int -> Int {
        context.ir[bindingIndex] => binding
        binding.parent => bindingOwner!
        (bindingOwner! >= 0 and context.ir[bindingOwner!].kind != 0 and context.ir[bindingOwner!].kind != 1 and context.ir[bindingOwner!].kind != 11) -> while {
            context.ir[bindingOwner!].parent => bindingOwner!
        }
        binding.parent => bindingScope!
        (bindingScope! >= 0 and context.ir[bindingScope!].kind != 0 and context.ir[bindingScope!].kind != 1 and context.ir[bindingScope!].kind != 11 and context.ir[bindingScope!].kind != 19) -> while {
            context.ir[bindingScope!].parent => bindingScope!
        }
        binding -> sourceToken => bindingName
        bindingIndex => rootIndex!
        binding -> sourceStart => rootStart!
        0 => candidateIndex!
        candidateIndex! < (context.ir -> len) -> while {
            context.ir[candidateIndex!] => candidate
            # Mutable rebindings can receive a fresh semantic symbol even
            # though they target the same lexical name. Function ownership,
            # ancestor visibility, and the byte-exact name comparison below
            # identify the storage root without merging sibling scopes.
            (candidate.kind == 17 and candidate.flags == 1 and candidate.sourceModule == binding.sourceModule) -> if {
                candidate.parent => candidateOwner!
                (candidateOwner! >= 0 and context.ir[candidateOwner!].kind != 0 and context.ir[candidateOwner!].kind != 1 and context.ir[candidateOwner!].kind != 11) -> while {
                    context.ir[candidateOwner!].parent => candidateOwner!
                }
                candidateOwner! == bindingOwner! -> if {
                    candidate.parent => candidateScope!
                    (candidateScope! >= 0 and context.ir[candidateScope!].kind != 0 and context.ir[candidateScope!].kind != 1 and context.ir[candidateScope!].kind != 11 and context.ir[candidateScope!].kind != 19) -> while {
                        context.ir[candidateScope!].parent => candidateScope!
                    }
                    false => candidateVisible!
                    bindingScope! => visibleScope!
                    (visibleScope! >= 0 and not candidateVisible!) -> while {
                        visibleScope! == candidateScope! -> if { true => candidateVisible! } else {
                            context.ir[visibleScope!].parent => visibleScope!
                            (visibleScope! >= 0 and context.ir[visibleScope!].kind != 0 and context.ir[visibleScope!].kind != 1 and context.ir[visibleScope!].kind != 11 and context.ir[visibleScope!].kind != 19) -> while {
                                context.ir[visibleScope!].parent => visibleScope!
                            }
                        }
                    }
                    candidate -> sourceToken => candidateName
                    candidateName.span.length == bindingName.span.length => sameName!
                    UIntSize(0) => nameByte!
                    (sameName! and nameByte! < bindingName.span.length) -> while {
                        context.sources[binding.sourceModule] -> byte(bindingName.span.start + nameByte!) => bindingByte
                        context.sources[binding.sourceModule] -> byte(candidateName.span.start + nameByte!) => candidateByte
                        bindingByte != candidateByte -> if { false => sameName! }
                        nameByte! + UIntSize(1) => nameByte!
                    }
                    (sameName! and candidateVisible!) -> if {
                        candidate -> sourceStart => candidateStart
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
    bindingBelongsToOwner request: BindingOwnerRequest -> Bool {
        context.ir[request.bindingIndex].parent => bindingOwner!
        (bindingOwner! >= 0 and context.ir[bindingOwner!].kind != 0 and context.ir[bindingOwner!].kind != 11) -> while {
            context.ir[bindingOwner!].parent => bindingOwner!
        }
        bindingOwner! == request.ownerIndex
    }
    emitArrayPush request: WhileValueRequest -> Unit uses Console {
        request.whileIndex => pushIndex
        context.ir[pushIndex] => pushNode
        context.ir[pushNode.operand0] => pushSource
        pushNode.operand1 -> aggregateValueIndex => pushValueIndex
        context.ir[pushValueIndex] => pushValue
        pushNode.operand0 -> mutableBindingRoot => pushRoot
        "  %push$(pushIndex)_data = extractvalue %sl.array.i32 " -> print
        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and pushSource.kind == 5 and pushSource.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(pushNode.operand0)" -> print }
        ", 0" -> println
        "  %push$(pushIndex)_length = extractvalue %sl.array.i32 " -> print
        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and pushSource.kind == 5 and pushSource.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(pushNode.operand0)" -> print }
        ", 1" -> println
        "  %push$(pushIndex)_capacity = extractvalue %sl.array.i32 " -> print
        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and pushSource.kind == 5 and pushSource.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(pushNode.operand0)" -> print }
        ", 2" -> println
        "  %push$(pushIndex)_full = icmp eq i64 %push$(pushIndex)_length, %push$(pushIndex)_capacity" -> println
        "  %push$(pushIndex)_empty = icmp eq i64 %push$(pushIndex)_capacity, 0" -> println
        "  %push$(pushIndex)_doubled = mul i64 %push$(pushIndex)_capacity, 2" -> println
        "  %push$(pushIndex)_grown_capacity = select i1 %push$(pushIndex)_empty, i64 4, i64 %push$(pushIndex)_doubled" -> println
        "  %push$(pushIndex)_append_capacity = select i1 %push$(pushIndex)_full, i64 %push$(pushIndex)_grown_capacity, i64 %push$(pushIndex)_capacity" -> println
        pushSource -> arrayElementSize => pushElementSize
        "  %push$(pushIndex)_append_bytes = mul i64 %push$(pushIndex)_append_capacity, $pushElementSize" -> println
        "  %push$(pushIndex)_append_data = call ptr @realloc(ptr %push$(pushIndex)_data, i64 %push$(pushIndex)_append_bytes)" -> println
        "  %push$(pushIndex)_slot = getelementptr " -> print
        pushSource -> writeArrayElementType
        ", ptr %push$(pushIndex)_append_data, i64 %push$(pushIndex)_length" -> println
        pushSource -> arrayElementTypeId => pushTargetElementTypeId
        0 => pushTargetWidth!
        0 => pushSourceWidth!
        pushTargetElementTypeId >= 0 -> if {
            context.types[pushTargetElementTypeId] => pushTargetElementType
            (pushTargetElementType.kind == 1 and pushTargetElementType.origin == 1) -> if {
                pushTargetElementType.symbol -> integerWidth => pushTargetWidth!
            }
        } else {
            pushSource.typeSymbol -> integerWidth => pushTargetWidth!
        }
        pushTargetWidth! > 0 -> if {
            pushValue -> integerNodeWidth => pushSourceWidth!
        }
        # Array push follows the same numeric conversion rule as indexed
        # assignment. Literal constants are emitted directly at the target
        # width; computed values require an explicit LLVM integer cast.
        (pushSourceWidth! > 0 and pushTargetWidth! > 0 and pushSourceWidth! != pushTargetWidth! and pushValue.kind != 3 and pushValue.kind != 4) => castPushValue!
        castPushValue! -> if {
            "  %push$(pushIndex)_value_cast = " -> print
            pushTargetWidth! < pushSourceWidth! -> if { "trunc " -> print } else {
                pushValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
            }
            pushValue -> writeIrType
            " " -> print
            (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and pushValue.kind == 5 and pushValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(pushValueIndex)" -> print }
            " to " -> print
            pushSource -> writeArrayElementType
            "" -> println
        }
        "  store " -> print
        pushSource -> writeArrayElementType
        " " -> print
        (pushValue.kind == 3 or pushValue.kind == 4) -> if {
            pushValue -> sourceToken => pushValueToken
            pushValue.kind == 3 -> if { context.sources[pushValue.sourceModule] -> slice(pushValueToken.span.start, pushValueToken.span.length) -> print } else {
                ((context.sources[pushValue.sourceModule] -> byte(pushValueToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
            }
        } else {
            castPushValue! -> if { "%push$(pushIndex)_value_cast" -> print } else {
                (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and pushValue.kind == 5 and pushValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(pushValueIndex)" -> print }
            }
        }
        pushSource -> arrayElementAlign => pushElementAlign
        ", ptr %push$(pushIndex)_slot, align $pushElementAlign" -> println
        "  %push$(pushIndex)_new_length = add i64 %push$(pushIndex)_length, 1" -> println
        "  %push$(pushIndex)_array0 = insertvalue %sl.array.i32 poison, ptr %push$(pushIndex)_append_data, 0" -> println
        "  %push$(pushIndex)_array1 = insertvalue %sl.array.i32 %push$(pushIndex)_array0, i64 %push$(pushIndex)_new_length, 1" -> println
        "  %push$(pushIndex)_array2 = insertvalue %sl.array.i32 %push$(pushIndex)_array1, i64 %push$(pushIndex)_append_capacity, 2" -> println
        "  store %sl.array.i32 %push$(pushIndex)_array2, ptr %slot$(pushRoot), align 8" -> println
    }
    writeWhileValue request: WhileValueRequest -> Unit uses Console {
        context.ir[request.nodeIndex] => value
        (value.kind == 3 or value.kind == 4) -> if {
            value -> sourceToken => valueToken
            value.kind == 3 -> if {
                context.sources[value.sourceModule] -> slice(valueToken.span.start, valueToken.span.length) -> print
            } else {
                ((context.sources[value.sourceModule] -> byte(valueToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
            }
        } else {
            (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and value.kind == 5 and value.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if {
                "%arg" -> print
            } else {
                false => capturedWhileValue!
                (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and value.kind == 5 and value.operand0 < 0) -> if {
                    request.ownerIndex -> functionCaptures => whileOwnerCaptures!
                    0 => whileOwnerCaptureIndex!
                    whileOwnerCaptureIndex! < (whileOwnerCaptures! -> len) -> while {
                        context.ir[whileOwnerCaptures![whileOwnerCaptureIndex!]].symbol == value.symbol -> if { true => capturedWhileValue! }
                        whileOwnerCaptureIndex! + 1 => whileOwnerCaptureIndex!
                    }
                    context.ir[request.ownerIndex] => whileOwnerFunction
                    context.ranges[whileOwnerFunction.sourceModule] => whileOwnerRange
                    context.symbols[whileOwnerRange.symbolStart + whileOwnerFunction.symbol] => whileOwnerSymbol
                    whileOwnerSymbol.parent >= 0 -> if {
                        0 => whileParentSearch!
                        (whileParentSearch! < (context.ir -> len) and not capturedWhileValue!) -> while {
                            context.ir[whileParentSearch!] => whileParentCandidate
                            (whileParentCandidate.kind == 0 and whileParentCandidate.sourceModule == whileOwnerFunction.sourceModule and whileParentCandidate.symbol == whileOwnerSymbol.parent and whileParentCandidate.operand1 >= 0 and context.ir[whileParentCandidate.operand1].symbol == value.symbol) -> if {
                                true => capturedWhileValue!
                            }
                            whileParentSearch! + 1 => whileParentSearch!
                        }
                    }
                }
                request.nodeIndex -> eachRoleOwner => whileEachRoleOwner
                (capturedWhileValue! or whileEachRoleOwner >= 0) -> if { "%v$(request.nodeIndex)" -> print } else { "%while$(request.whileIndex)_v$(request.nodeIndex)" -> print }
            }
        }
    }
    emitIndexAssignment request: WhileValueRequest -> Unit uses Console {
        context.ir[request.nodeIndex] => assignment
        assignment.operand0 >= 0 -> if {
            assignment.operand1 >= 0 -> if {
                request.nodeIndex -> mutableBindingRoot => assignmentRoot
                context.ir[assignmentRoot] => assignmentBinding
                context.ir[assignment.operand0] => assignmentValue
                context.ir[assignment.operand1] => assignmentIndex
                "  %assign$(request.nodeIndex)_array = load %sl.array.i32, ptr %slot$(assignmentRoot), align 4" -> println
                "  %assign$(request.nodeIndex)_data = extractvalue %sl.array.i32 %assign$(request.nodeIndex)_array, 0" -> println
                assignmentIndex -> integerNodeWidth => assignmentIndexWidth
                assignmentIndexWidth == 64 -> if {
                    "  %assign$(request.nodeIndex)_index = add i64 0, " -> print
                    assignmentIndex.kind == 3 -> if {
                        assignmentIndex -> sourceToken => assignmentIndexToken
                        context.sources[assignmentIndex.sourceModule] -> slice(assignmentIndexToken.span.start, assignmentIndexToken.span.length) -> print
                    } else {
                        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentIndex.kind == 5 and assignmentIndex.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand1)" -> print }
                    }
                    "" -> println
                } else {
                    "  %assign$(request.nodeIndex)_index = " -> print
                    assignmentIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                    " i$(assignmentIndexWidth) " -> print
                    assignmentIndex.kind == 3 -> if {
                        assignmentIndex -> sourceToken => assignmentIndexToken
                        context.sources[assignmentIndex.sourceModule] -> slice(assignmentIndexToken.span.start, assignmentIndexToken.span.length) -> print
                    } else {
                        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentIndex.kind == 5 and assignmentIndex.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand1)" -> print }
                    }
                    " to i64" -> println
                }
                "  %assign$(request.nodeIndex)_ptr = getelementptr " -> print
                assignmentBinding -> writeArrayElementType
                ", ptr %assign$(request.nodeIndex)_data, i64 %assign$(request.nodeIndex)_index" -> println
                assignmentBinding -> arrayElementTypeId => assignmentTargetElementTypeId
                assignmentValue -> integerNodeWidth => assignmentSourceWidth
                0 => assignmentTargetWidth!
                assignmentTargetElementTypeId >= 0 -> if {
                    context.types[assignmentTargetElementTypeId] => assignmentTargetElementType
                    (assignmentTargetElementType.kind == 1 and assignmentTargetElementType.origin == 1) -> if {
                        assignmentTargetElementType.symbol -> integerWidth => assignmentTargetWidth!
                    }
                } else {
                    assignmentBinding.typeSymbol -> integerWidth => assignmentTargetWidth!
                }
                (assignmentSourceWidth > 0 and assignmentTargetWidth! > 0 and assignmentSourceWidth != assignmentTargetWidth! and assignmentValue.kind != 3 and assignmentValue.kind != 4) => castAssignmentValue!
                castAssignmentValue! -> if {
                    "  %assign$(request.nodeIndex)_value_cast = " -> print
                    assignmentTargetWidth! < assignmentSourceWidth -> if { "trunc " -> print } else {
                        assignmentValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                    }
                    assignmentValue -> writeIrType
                    " " -> print
                    (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentValue.kind == 5 and assignmentValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand0)" -> print }
                    " to " -> print
                    assignmentBinding -> writeArrayElementType
                    "" -> println
                }
                "  store " -> print
                assignmentBinding -> writeArrayElementType
                " " -> print
                (assignmentValue.kind == 3 or assignmentValue.kind == 4) -> if {
                    assignmentValue -> sourceToken => assignmentValueToken
                    assignmentValue.kind == 3 -> if { context.sources[assignmentValue.sourceModule] -> slice(assignmentValueToken.span.start, assignmentValueToken.span.length) -> print } else {
                        ((context.sources[assignmentValue.sourceModule] -> byte(assignmentValueToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                    }
                } else {
                    castAssignmentValue! -> if { "%assign$(request.nodeIndex)_value_cast" -> print } else {
                        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentValue.kind == 5 and assignmentValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand0)" -> print }
                    }
                }
                ", ptr %assign$(request.nodeIndex)_ptr, align " -> print
                assignmentBinding -> arrayElementAlign -> writeDecimalLine
            }
        }
    }
    emitMemberAssignment request: WhileValueRequest -> Unit uses Console {
        context.ir[request.nodeIndex] => assignment
        (assignment.operand0 >= 0 and assignment.operand1 >= 0) -> if {
            assignment.operand1 -> mutableBindingRoot => assignmentRoot
            context.ir[assignmentRoot] => assignmentBinding
            context.ir[assignment.operand0] => assignmentValue
            assignmentBinding.typeModule => assignmentOwnerSource!
            assignmentBinding.typeOrigin == 2 -> if {
                context.modules[assignmentBinding.typeModule].sourceIndex => assignmentOwnerSource!
            }
            context.ranges[assignmentOwnerSource!] => assignmentOwnerRange
            assignment -> sourceNode => assignmentAst
            context.tokens[context.ranges[assignment.sourceModule].tokenStart + assignmentAst.secondaryToken] => assignmentMemberName
            -1 => assignmentFieldOrdinal!
            0 => assignmentCandidateOrdinal!
            0 => assignmentFieldSearch!
            assignmentFieldSearch! < assignmentOwnerRange.symbolCount -> while {
                context.symbols[assignmentOwnerRange.symbolStart + assignmentFieldSearch!] => assignmentField
                (assignmentField.kind == 26 and assignmentField.parent == assignmentBinding.typeSymbol) -> if {
                    context.tokens[assignmentOwnerRange.tokenStart + assignmentField.nameToken] => assignmentFieldName
                    assignmentMemberName.span.length == assignmentFieldName.span.length => assignmentFieldEqual!
                    UIntSize(0) => assignmentFieldByte!
                    (assignmentFieldEqual! and assignmentFieldByte! < assignmentMemberName.span.length) -> while {
                        context.sources[assignment.sourceModule] -> byte(assignmentMemberName.span.start + assignmentFieldByte!) => assignmentMemberByte
                        context.sources[assignmentOwnerSource!] -> byte(assignmentFieldName.span.start + assignmentFieldByte!) => assignmentFieldNameByte
                        assignmentMemberByte != assignmentFieldNameByte -> if { false => assignmentFieldEqual! }
                        assignmentFieldByte! + UIntSize(1) => assignmentFieldByte!
                    }
                    assignmentFieldEqual! -> if { assignmentCandidateOrdinal! => assignmentFieldOrdinal! }
                    assignmentCandidateOrdinal! + 1 => assignmentCandidateOrdinal!
                }
                assignmentFieldSearch! + 1 => assignmentFieldSearch!
            }
            assignmentFieldOrdinal! >= 0 -> if {
                assignmentBinding.typeId => assignmentOwnerTypeId!
                0 => assignmentOwnerTypeSearch!
                assignmentOwnerTypeSearch! < (context.types -> len) -> while {
                    context.types[assignmentOwnerTypeSearch!] => assignmentOwnerTypeCandidate
                    (assignmentOwnerTypeCandidate.status == 0 and assignmentOwnerTypeCandidate.kind == 1 and assignmentOwnerTypeCandidate.origin == assignmentBinding.typeOrigin and assignmentOwnerTypeCandidate.module == assignmentBinding.typeModule and assignmentOwnerTypeCandidate.symbol == assignmentBinding.typeSymbol) -> if { assignmentOwnerTypeSearch! => assignmentOwnerTypeId! }
                    assignmentOwnerTypeSearch! + 1 => assignmentOwnerTypeSearch!
                }
                -1 => assignmentTargetFieldTypeId!
                0 => assignmentTargetFieldOrdinal!
                0 => assignmentTargetFieldSearch!
                assignmentTargetFieldSearch! < (context.fields -> len) -> while {
                    context.fields[assignmentTargetFieldSearch!] => assignmentTargetField
                    (assignmentTargetField.status == 0 and assignmentTargetField.ownerType == assignmentOwnerTypeId!) -> if {
                        assignmentTargetFieldOrdinal! == assignmentFieldOrdinal! -> if { assignmentTargetField.fieldType => assignmentTargetFieldTypeId! }
                        assignmentTargetFieldOrdinal! + 1 => assignmentTargetFieldOrdinal!
                    }
                    assignmentTargetFieldSearch! + 1 => assignmentTargetFieldSearch!
                }
                0 => assignmentSourceWidth!
                0 => assignmentTargetWidth!
                assignmentValue.typeOrigin == 1 -> if { assignmentValue.typeSymbol -> integerWidth => assignmentSourceWidth! }
                assignmentTargetFieldTypeId! >= 0 -> if {
                    context.types[assignmentTargetFieldTypeId!] => assignmentTargetFieldSemanticType
                    assignmentTargetFieldSemanticType.origin == 1 -> if { assignmentTargetFieldSemanticType.symbol -> integerWidth => assignmentTargetWidth! }
                }
                (assignmentSourceWidth! > 0 and assignmentTargetWidth! > 0 and assignmentSourceWidth! != assignmentTargetWidth! and assignmentValue.kind != 3 and assignmentValue.kind != 4) => castAssignmentValue!
                castAssignmentValue! -> if {
                    "  %member_assign$(request.nodeIndex)_value_cast = " -> print
                    assignmentTargetWidth! < assignmentSourceWidth! -> if { "trunc " -> print } else {
                        assignmentValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                    }
                    assignmentValue -> writeIrType
                    " " -> print
                    (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentValue.kind == 5 and assignmentValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand0)" -> print }
                    " to " -> print
                    assignmentTargetFieldTypeId! -> writeSemanticTypeId
                    "" -> println
                }
                "  %member_assign$(request.nodeIndex)_current = load %sl.struct.m$(assignmentOwnerSource!)_s$(assignmentBinding.typeSymbol), ptr %slot$(assignmentRoot), align " -> print
                assignmentBinding -> storageAlign -> writeDecimalLine
                "  %member_assign$(request.nodeIndex)_updated = insertvalue %sl.struct.m$(assignmentOwnerSource!)_s$(assignmentBinding.typeSymbol) %member_assign$(request.nodeIndex)_current, " -> print
                assignmentTargetFieldTypeId! >= 0 -> if { assignmentTargetFieldTypeId! -> writeSemanticTypeId } else { assignmentValue -> writeIrType }
                " " -> print
                (assignmentValue.kind == 3 or assignmentValue.kind == 4) -> if {
                    assignmentValue -> sourceToken => assignmentValueToken
                    assignmentValue.kind == 3 -> if {
                        context.sources[assignmentValue.sourceModule] -> slice(assignmentValueToken.span.start, assignmentValueToken.span.length) -> print
                    } else {
                        ((context.sources[assignmentValue.sourceModule] -> byte(assignmentValueToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                    }
                } else {
                    castAssignmentValue! -> if { "%member_assign$(request.nodeIndex)_value_cast" -> print } else {
                        (request.ownerIndex >= 0 and context.ir[request.ownerIndex].kind == 0 and context.ir[request.ownerIndex].operand1 >= 0 and assignmentValue.kind == 5 and assignmentValue.symbol == context.ir[context.ir[request.ownerIndex].operand1].symbol) -> if { "%arg" -> print } else { "%v$(assignment.operand0)" -> print }
                    }
                }
                ", $(assignmentFieldOrdinal!)" -> println
                "  store %sl.struct.m$(assignmentOwnerSource!)_s$(assignmentBinding.typeSymbol) %member_assign$(request.nodeIndex)_updated, ptr %slot$(assignmentRoot), align " -> print
                assignmentBinding -> storageAlign -> writeDecimalLine
            }
        }
    }
    emitWhileMember request: WhileValueRequest -> Unit uses Console {
        context.ir[request.nodeIndex] => member
        context.ir[member.operand0] => memberBase
        memberBase.typeOrigin => currentOrigin!
        memberBase.typeModule => currentModule!
        memberBase.typeSymbol => currentSymbol!
        member -> sourceNode => memberAst
        memberBase -> sourceNode => memberBaseAst
        memberBaseAst.start + memberBaseAst.length => memberBaseEnd
        memberAst.firstToken => memberTokenIndex!
        true => beforeMemberBaseEnd!
        (beforeMemberBaseEnd! and memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount) -> while {
            context.tokens[context.ranges[member.sourceModule].tokenStart + memberTokenIndex!].span.start < memberBaseEnd -> if {
                memberTokenIndex! + 1 => memberTokenIndex!
            } else { false => beforeMemberBaseEnd! }
        }
        memberTokenIndex! => memberPathTokenStart
        1 => memberIdentifierCount!
        memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
            context.tokens[context.ranges[member.sourceModule].tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                memberIdentifierCount! + 1 => memberIdentifierCount!
            }
            memberTokenIndex! + 1 => memberTokenIndex!
        }
        1 => memberIdentifierOrdinal!
        0 => projectionOrdinal!
        memberPathTokenStart => memberTokenIndex!
        memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
            context.tokens[context.ranges[member.sourceModule].tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                memberIdentifierOrdinal! > 0 -> if {
                    currentModule! => ownerSourceModule!
                    currentOrigin! == 2 -> if { context.modules[currentModule!].sourceIndex => ownerSourceModule! }
                    context.ranges[ownerSourceModule!] => ownerRange
                    -1 => fieldOrdinal!
                    0 => candidateFieldOrdinal!
                    0 => fieldSearch!
                    fieldSearch! < ownerRange.symbolCount -> while {
                        context.symbols[ownerRange.symbolStart + fieldSearch!] => field
                        (field.kind == 26 and field.parent == currentSymbol!) -> if {
                            context.tokens[context.ranges[member.sourceModule].tokenStart + memberTokenIndex!] => memberName
                            context.tokens[ownerRange.tokenStart + field.nameToken] => fieldName
                            memberName.span.length == fieldName.span.length => equal!
                            UIntSize(0) => fieldNameByte!
                            (equal! and fieldNameByte! < memberName.span.length) -> while {
                                context.sources[member.sourceModule] -> byte(memberName.span.start + fieldNameByte!) => memberByte
                                context.sources[ownerSourceModule!] -> byte(fieldName.span.start + fieldNameByte!) => fieldByte
                                memberByte != fieldByte -> if { false => equal! }
                                fieldNameByte! + UIntSize(1) => fieldNameByte!
                            }
                            equal! -> if {
                                candidateFieldOrdinal! => fieldOrdinal!
                                -1 => nextOrigin!
                                -1 => nextModule!
                                -1 => nextSymbol!
                                0 => nominalSearch!
                                nominalSearch! < (context.nominal -> len) -> while {
                                    context.nominal[nominalSearch!] => fieldType
                                    (fieldType.sourceModule == ownerSourceModule! and fieldType.typeAst == field.typeNode) -> if {
                                        fieldType.origin => nextOrigin!
                                        fieldType.targetModule => nextModule!
                                        fieldType.targetSymbol => nextSymbol!
                                    }
                                    nominalSearch! + 1 => nominalSearch!
                                }
                                nextOrigin! < 0 -> if {
                                    0 => compositeSearch!
                                    compositeSearch! < (context.composite -> len) -> while {
                                        context.composite[compositeSearch!] => compositeType
                                        (compositeType.sourceModule == ownerSourceModule! and compositeType.typeAst == field.typeNode) -> if {
                                            10 + compositeType.kind => nextOrigin!
                                            compositeType.kind == 5 -> if {
                                                compositeType.keySymbol => nextModule!
                                                compositeType.valueSymbol => nextSymbol!
                                            } else {
                                                compositeType.elementModule => nextModule!
                                                compositeType.elementSymbol => nextSymbol!
                                            }
                                        }
                                        compositeSearch! + 1 => compositeSearch!
                                    }
                                }
                                "  " -> print
                                memberIdentifierOrdinal! == memberIdentifierCount! - 1 -> if {
                                    "%while$(request.whileIndex)_v$(request.nodeIndex)" -> print
                                } else {
                                    "%while$(request.whileIndex)_v$(request.nodeIndex)_m$(projectionOrdinal!)" -> print
                                }
                                " = extractvalue %sl.struct.m$(ownerSourceModule!)_s$(currentSymbol!) " -> print
                                projectionOrdinal! == 0 -> if {
                                    WhileValueRequest { whileIndex: request.whileIndex, ownerIndex: request.ownerIndex, nodeIndex: member.operand0 } -> writeWhileValue
                                } else {
                                    "%while$(request.whileIndex)_v$(request.nodeIndex)_m" -> print
                                    projectionOrdinal! - 1 -> writeDecimal
                                }
                                ", $(fieldOrdinal!)" -> println
                                nextOrigin! => currentOrigin!
                                nextModule! => currentModule!
                                nextSymbol! => currentSymbol!
                                projectionOrdinal! + 1 => projectionOrdinal!
                            }
                            candidateFieldOrdinal! + 1 => candidateFieldOrdinal!
                        }
                        fieldSearch! + 1 => fieldSearch!
                    }
                }
                memberIdentifierOrdinal! + 1 => memberIdentifierOrdinal!
            }
            memberTokenIndex! + 1 => memberTokenIndex!
        }
    }
    emitWhileBranch request: WhileBranchRequest -> Unit uses Console {
        request.whileIndex => whileIndex
        request.ownerIndex => ownerIndex
        context.ir[whileIndex] => whileNode
        [WhileBoolTask; ~] => boolTasks!
        boolTasks! -> push(WhileBoolTask {
            kind: 0
            nodeIndex: request.conditionIndex
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
                            (valueNode.kind == 6 or valueNode.kind == 7 or valueNode.kind == 8 or valueNode.kind == 9 or valueNode.kind == 13 or valueNode.kind == 15) -> if {
                                ((valueNode.kind == 8 or valueNode.kind == 15 or (valueNode.kind == 9 and (valueNode.opcode == -202 or valueNode.opcode == -203))) and valueNode.operand1 >= 0) -> if {
                                    valueNode.operand1 + 1 => secondValueTask
                                    valueTaskSize! == (valueTasks! -> len) -> if { valueTasks! -> push(secondValueTask) } else { secondValueTask => valueTasks![valueTaskSize!] }
                                    valueTaskSize! + 1 => valueTaskSize!
                                    (valueNode.kind == 9 and valueNode.opcode == -203 and context.ir[valueNode.operand1].nextOperand >= 0) -> if {
                                        context.ir[valueNode.operand1].nextOperand + 1 => sliceLengthTask
                                        valueTaskSize! == (valueTasks! -> len) -> if { valueTasks! -> push(sliceLengthTask) } else { sliceLengthTask => valueTasks![valueTaskSize!] }
                                        valueTaskSize! + 1 => valueTaskSize!
                                    }
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
                                    (valueNode.operand0 >= 0 and context.ir[valueNode.operand0].kind == 17) -> if { valueNode.operand0 => valueBindingIndex! }
                                    valueBindingIndex! >= 0 -> if {
                                        context.ir[valueBindingIndex!] => valueBinding
                                        valueBinding.flags == 1 -> if {
                                            valueBindingIndex! -> mutableBindingRoot => valueRoot
                                            "  %while$(whileIndex)_v$(valueNodeIndex) = load " -> print
                                            valueNode -> writeIrType
                                            ", ptr %slot$(valueRoot), align " -> print
                                            valueNode -> storageAlign -> writeDecimalLine
                                        } else {
                                            context.ir[valueBinding.operand0] => bindingOperand
                                            "  %while$(whileIndex)_v$(valueNodeIndex) = freeze " -> print
                                            valueNode -> writeIrType
                                            " " -> print
                                            (bindingOperand.kind == 3 or bindingOperand.kind == 4) -> if {
                                                bindingOperand -> sourceToken => bindingToken
                                                bindingOperand.kind == 3 -> if { context.sources[bindingOperand.sourceModule] -> slice(bindingToken.span.start, bindingToken.span.length) -> print } else { ((context.sources[bindingOperand.sourceModule] -> byte(bindingToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print } }
                                            } else { "%v$(valueBinding.operand0)" -> print }
                                            "" -> println
                                        }
                                    }
                                }
                            }
                            valueNode.kind == 6 -> if {
                                (valueNode.targetModule < 0 and valueNode.typeOrigin == 1 and (valueNode.typeSymbol -> integerWidth) > 0 and valueNode.operand0 >= 0) -> if {
                                    context.ir[valueNode.operand0] => whileIntegerArgument
                                    valueNode.typeSymbol -> integerWidth => whileTargetWidth
                                    whileIntegerArgument.typeSymbol -> integerWidth => whileInputWidth
                                    "  %while$(whileIndex)_v$(valueNodeIndex) = " -> print
                                    whileTargetWidth < whileInputWidth -> if { "trunc " -> print } else {
                                        whileTargetWidth > whileInputWidth -> if {
                                            whileIntegerArgument -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                                        } else { "add " -> print }
                                    }
                                    whileIntegerArgument -> writeIrType
                                    " " -> print
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                    whileTargetWidth == whileInputWidth -> if { ", 0" -> println } else {
                                        " to " -> print
                                        valueNode -> writeIrType
                                        "" -> println
                                    }
                                } else {
                                    CallEnvironmentRequest { callerIndex: ownerIndex, callIndex: valueNodeIndex, targetModule: valueNode.targetModule, targetSymbol: valueNode.symbol, hasArgument: valueNode.operand0 >= 0 } -> emitCallEnvironmentValues
                                    "  %while$(whileIndex)_v$(valueNodeIndex) = call " -> print
                                    valueNode -> writeIrType
                                    " @sl_m$(valueNode.targetModule)_s$(valueNode.symbol)(" -> print
                                    CallEnvironmentRequest { callerIndex: ownerIndex, callIndex: valueNodeIndex, targetModule: valueNode.targetModule, targetSymbol: valueNode.symbol, hasArgument: valueNode.operand0 >= 0 } -> emitCallEnvironment => whileCallEnvironment
                                    valueNode.operand0 >= 0 -> if {
                                        context.ir[valueNode.operand0] => callArgument
                                        callArgument -> writeIrType
                                        " " -> print
                                        WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                    }
                                    ")" -> println
                                }
                            }
                            ((valueNode.kind == 9 or valueNode.kind == 6) and valueNode.opcode == -201 and valueNode.operand0 >= 0) -> if {
                                context.ir[valueNode.operand0] => whileLengthBase
                                "  %while$(whileIndex)_v$(valueNodeIndex)" -> print
                                valueNode.typeSymbol == 2 -> if { "_raw" -> print }
                                " = extractvalue " -> print
                                whileLengthBase -> writeIrType
                                " " -> print
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                ", " -> print
                                whileLengthBase.typeOrigin == 15 -> if { "2" -> println } else { "1" -> println }
                                valueNode.typeSymbol == 2 -> if {
                                    "  %while$(whileIndex)_v$(valueNodeIndex) = trunc i64 %while$(whileIndex)_v$(valueNodeIndex)_raw to i32" -> println
                                }
                            }
                            ((valueNode.kind == 9 or valueNode.kind == 6) and valueNode.opcode == -202 and valueNode.operand0 >= 0 and valueNode.operand1 >= 0) -> if {
                                context.ir[valueNode.operand0] => whileByteBase
                                context.ir[valueNode.operand1] => whileByteIndex
                                whileByteIndex.typeSymbol -> integerWidth => whileByteIndexWidth
                                "  %while$(whileIndex)_v$(valueNodeIndex)_base = extractvalue " -> print
                                whileByteBase -> writeTextLikeType
                                " " -> print
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                ", 0" -> println
                                (whileByteIndexWidth > 0 and whileByteIndexWidth < 64) -> if {
                                    "  %while$(whileIndex)_v$(valueNodeIndex)_index = " -> print
                                    whileByteIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                                    " i$(whileByteIndexWidth) " -> print
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                    " to i64" -> println
                                }
                                "  %while$(whileIndex)_v$(valueNodeIndex)_address = getelementptr i8, ptr %while$(whileIndex)_v$(valueNodeIndex)_base, i64 " -> print
                                (whileByteIndexWidth > 0 and whileByteIndexWidth < 64) -> if { "%while$(whileIndex)_v$(valueNodeIndex)_index" -> print } else {
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                }
                                "" -> println
                                "  %while$(whileIndex)_v$(valueNodeIndex) = load i8, ptr %while$(whileIndex)_v$(valueNodeIndex)_address, align 1" -> println
                            }
                            ((valueNode.kind == 9 or valueNode.kind == 6) and valueNode.opcode == -203 and valueNode.operand0 >= 0 and valueNode.operand1 >= 0) -> if {
                                context.ir[valueNode.operand0] => whileSliceBase
                                context.ir[valueNode.operand1] => whileSliceIndex
                                whileSliceIndex.typeSymbol -> integerWidth => whileSliceIndexWidth
                                context.ir[valueNode.operand1].nextOperand => whileSliceLength
                                "  %while$(whileIndex)_v$(valueNodeIndex)_base = extractvalue " -> print
                                whileSliceBase -> writeTextLikeType
                                " " -> print
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                ", 0" -> println
                                (whileSliceIndexWidth > 0 and whileSliceIndexWidth < 64) -> if {
                                    "  %while$(whileIndex)_v$(valueNodeIndex)_index = " -> print
                                    whileSliceIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                                    " i$(whileSliceIndexWidth) " -> print
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                    " to i64" -> println
                                }
                                "  %while$(whileIndex)_v$(valueNodeIndex)_start = getelementptr i8, ptr %while$(whileIndex)_v$(valueNodeIndex)_base, i64 " -> print
                                (whileSliceIndexWidth > 0 and whileSliceIndexWidth < 64) -> if { "%while$(whileIndex)_v$(valueNodeIndex)_index" -> print } else {
                                    WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                }
                                "" -> println
                                "  %while$(whileIndex)_v$(valueNodeIndex)_ptr = insertvalue %sl.text poison, ptr %while$(whileIndex)_v$(valueNodeIndex)_start, 0" -> println
                                "  %while$(whileIndex)_v$(valueNodeIndex) = insertvalue %sl.text %while$(whileIndex)_v$(valueNodeIndex)_ptr, i64 " -> print
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: whileSliceLength } -> writeWhileValue
                                ", 1" -> println
                            }
                            (valueNode.kind == 13 and valueNode.operand0 >= 0) -> if {
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNodeIndex } -> emitWhileMember
                            }
                            (valueNode.kind == 15 and valueNode.operand0 >= 0 and valueNode.operand1 >= 0) -> if {
                                context.ir[valueNode.operand0] => whileIndexedArray
                                context.ir[valueNode.operand1] => whileIndexValue
                                "  %while$(whileIndex)_v$(valueNodeIndex)_data = extractvalue " -> print
                                whileIndexedArray -> writeIrType
                                " " -> print
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                ", 0" -> println
                                whileIndexValue.typeSymbol == 13 -> if {
                                    "  %while$(whileIndex)_v$(valueNodeIndex)_index = add i64 0, " -> print
                                } else {
                                    "  %while$(whileIndex)_v$(valueNodeIndex)_index = sext " -> print
                                    whileIndexValue -> writeIrType
                                    " " -> print
                                }
                                WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                whileIndexValue.typeSymbol == 13 -> if { "" -> println } else { " to i64" -> println }
                                "  %while$(whileIndex)_v$(valueNodeIndex)_ptr = getelementptr " -> print
                                valueNode -> writeIrType
                                ", ptr %while$(whileIndex)_v$(valueNodeIndex)_data, i64 %while$(whileIndex)_v$(valueNodeIndex)_index" -> println
                                "  %while$(whileIndex)_v$(valueNodeIndex) = load " -> print
                                valueNode -> writeIrType
                                ", ptr %while$(whileIndex)_v$(valueNodeIndex)_ptr, align " -> print
                                valueNode -> storageAlign -> writeDecimalLine
                            }
                            (valueNode.kind == 7 or valueNode.kind == 8) -> if {
                                context.ir[valueNode.operand0] => left
                                0 => whileBinaryWidth!
                                false => whileLeftPromoted!
                                false => whileRightPromoted!
                                valueNode.kind == 8 -> if {
                                    context.ir[valueNode.operand1] => whileRight
                                    left -> integerNodeWidth => whileLeftWidth!
                                    whileRight -> integerNodeWidth => whileRightWidth!
                                    (whileLeftWidth! > 0 and whileRightWidth! > 0) -> if {
                                        whileLeftWidth! > whileRightWidth! -> if { whileLeftWidth! => whileBinaryWidth! } else { whileRightWidth! => whileBinaryWidth! }
                                        (whileLeftWidth! < whileBinaryWidth! and left.kind != 3 and left.kind != 4) -> if {
                                            "  %while$(whileIndex)_v$(valueNodeIndex)_left_wide = " -> print
                                            left -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                                            " i$(whileLeftWidth!) " -> print
                                            WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                            " to i$(whileBinaryWidth!)" -> println
                                            true => whileLeftPromoted!
                                        }
                                        (whileRightWidth! < whileBinaryWidth! and whileRight.kind != 3 and whileRight.kind != 4) -> if {
                                            "  %while$(whileIndex)_v$(valueNodeIndex)_right_wide = " -> print
                                            whileRight -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                                            " i$(whileRightWidth!) " -> print
                                            WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                            " to i$(whileBinaryWidth!)" -> println
                                            true => whileRightPromoted!
                                        }
                                    }
                                }
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
                                    (valueNode.opcode == grammar.tokenIdLess or valueNode.opcode == grammar.tokenIdLessEqual or valueNode.opcode == grammar.tokenIdGreater or valueNode.opcode == grammar.tokenIdGreaterEqual) -> if {
                                        valueNode -> integerComparisonOperation -> print
                                    }
                                    valueNode.opcode == -24 -> if { "or" -> print }
                                    valueNode.opcode == -25 -> if { "and" -> print }
                                }
                                " " -> print
                                whileBinaryWidth! > 0 -> if { "i$(whileBinaryWidth!)" -> print } else { left -> writeIrType }
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
                                    whileLeftPromoted! -> if { "%while$(whileIndex)_v$(valueNodeIndex)_left_wide" -> print } else {
                                        WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand0 } -> writeWhileValue
                                    }
                                    ", " -> print
                                    whileRightPromoted! -> if { "%while$(whileIndex)_v$(valueNodeIndex)_right_wide" -> print } else {
                                        WhileValueRequest { whileIndex: whileIndex, ownerIndex: ownerIndex, nodeIndex: valueNode.operand1 } -> writeWhileValue
                                    }
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
    emitOwnedDrops request: OwnedDropRequest -> Unit uses Console {
        context.ir[request.regionIndex] => dropRegion
        UIntSize(0) => dropBeforeStart!
        request.beforeAst >= 0 -> if { context.nodes[context.ranges[dropRegion.sourceModule].astStart + request.beforeAst].start => dropBeforeStart! }
        context.ir -> len => ownedDropIndex!
        ownedDropIndex! > 0 -> while {
            ownedDropIndex! - 1 => ownedDropIndex!
            context.ir[ownedDropIndex!] => ownedDropCandidate
            true => ownedDropBeforeEdge!
            request.beforeAst >= 0 -> if {
                (ownedDropCandidate -> sourceStart) >= dropBeforeStart! -> if { false => ownedDropBeforeEdge! }
            }
            false => ownedDropMoved!
            false => ownedDropHasPartialMoves!
            true => ownedDropIsRoot!
            ownedDropIndex! => ownedDropRoot!
            (ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> ownsType)) -> if {
                ownedDropIndex! -> mutableBindingRoot => ownedDropRoot!
                ownedDropRoot! != ownedDropIndex! -> if { false => ownedDropIsRoot! }
            }
            (ownedDropCandidate.kind == 17 and (ownedDropCandidate -> ownsType)) -> if {
                0 => ownedMoveIndex!
                (ownedMoveIndex! < (context.moves -> len) and not ownedDropMoved!) -> while {
                    context.moves[ownedMoveIndex!] => ownedMove
                    (ownedMove.memberIr >= 0 and ownedMove.sourceModule == ownedDropCandidate.sourceModule and ownedMove.symbol == ownedDropCandidate.symbol) -> if { true => ownedDropHasPartialMoves! }
                    (ownedMove.memberIr < 0 and ownedMove.sourceModule == ownedDropCandidate.sourceModule and ownedMove.symbol == ownedDropCandidate.symbol and ownedMove.regionIr == request.regionIndex) -> if {
                        context.ir[ownedMove.siteIr] => ownedMoveCall
                        true => ownedMoveBeforeEdge!
                        request.beforeAst >= 0 -> if {
                            (ownedMoveCall -> sourceStart) >= dropBeforeStart! -> if { false => ownedMoveBeforeEdge! }
                        }
                        ((ownedMoveCall -> sourceStart) > (ownedDropCandidate -> sourceStart) and ownedMoveBeforeEdge!) -> if { true => ownedDropMoved! }
                    }
                    ownedMoveIndex! + 1 => ownedMoveIndex!
                }
            }
            (ownedDropBeforeEdge! and ownedDropIsRoot! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isDynamicArrayType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                ownedDropCandidate.flags == 1 -> if {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_current = load %sl.array.i32, ptr %slot$(ownedDropRoot!), align " -> print
                    ownedDropCandidate -> storageAlign -> writeDecimalLine
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!) = extractvalue %sl.array.i32 %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_current, 0" -> println
                } else {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!) = extractvalue %sl.array.i32 %v$(ownedDropCandidate.operand0), 0" -> println
                }
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!))" -> println
            }
            (ownedDropBeforeEdge! and ownedDropIsRoot! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isDictionaryType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                ownedDropCandidate.flags == 1 -> if {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_current = load %sl.dict, ptr %slot$(ownedDropRoot!), align " -> print
                    ownedDropCandidate -> storageAlign -> writeDecimalLine
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_keys = extractvalue %sl.dict %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_current, 0" -> println
                } else {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_keys = extractvalue %sl.dict %v$(ownedDropCandidate.operand0), 0" -> println
                }
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_keys)" -> println
                ownedDropCandidate.flags == 1 -> if {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_values = extractvalue %sl.dict %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_current, 1" -> println
                } else {
                    "  %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_values = extractvalue %sl.dict %v$(ownedDropCandidate.operand0), 1" -> println
                }
                "  call void @free(ptr %cleanup$(request.edgeIndex)_b$(ownedDropIndex!)_values)" -> println
            }
            (ownedDropBeforeEdge! and ownedDropIsRoot! and not ownedDropMoved! and ownedDropCandidate.kind == 17 and ownedDropCandidate.parent == request.regionIndex and (ownedDropCandidate -> isNominalStructType) and ownedDropCandidate.symbol != request.transferredSymbol) -> if {
                request.edgeIndex * 1000 + ownedDropIndex! => ownedDropNameRoot
                ownedDropCandidate.flags == 1 -> if {
                    "  %dropg$(ownedDropNameRoot)_p0 = load %sl.struct.m$(ownedDropCandidate.typeModule)_s$(ownedDropCandidate.typeSymbol), ptr %slot$(ownedDropRoot!), align " -> print
                    ownedDropCandidate -> storageAlign -> writeDecimalLine
                }
                0 => ownedDropValueKind!
                ownedDropCandidate.flags == 1 -> if { 2 => ownedDropValueKind! }
                DropGlueRequest {
                    typeOrigin: ownedDropCandidate.typeOrigin
                    typeModule: ownedDropCandidate.typeModule
                    typeSymbol: ownedDropCandidate.typeSymbol
                    valueKind: ownedDropValueKind!
                    valueIndex: ownedDropCandidate.operand0
                    nameRoot: ownedDropNameRoot
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
    emitRegion regionIndex: Int -> Unit uses Console {
        context.ir[regionIndex] => region
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
                            false => bindingBelongsViaOperand!
                            (localAncestor! >= 0 and not belongsToLocalRegion! and not belongsToNestedRegion!) -> while {
                                localAncestor! == regionTaskNode -> if { true => belongsToLocalRegion! } else {
                                    (context.ir[localAncestor!].kind == 19 or context.ir[localAncestor!].kind == 20) -> if { true => belongsToNestedRegion! } else { context.ir[localAncestor!].parent => localAncestor! }
                                }
                            }
                            (localCandidate.kind == 17 and localCandidate.operand0 >= 0 and not belongsToLocalRegion! and not belongsToNestedRegion!) -> if {
                                context.ir[localCandidate.operand0].parent => bindingOperandAncestor!
                                (bindingOperandAncestor! >= 0 and not belongsToLocalRegion! and not belongsToNestedRegion!) -> while {
                                    bindingOperandAncestor! == regionTaskNode -> if {
                                        true => belongsToLocalRegion!
                                        true => bindingBelongsViaOperand!
                                    } else {
                                        (context.ir[bindingOperandAncestor!].kind == 19 or context.ir[bindingOperandAncestor!].kind == 20) -> if { true => belongsToNestedRegion! } else { context.ir[bindingOperandAncestor!].parent => bindingOperandAncestor! }
                                    }
                                }
                            }
                            belongsToLocalRegion! -> if {
                                false => localIfCondition!
                                0 => localIfSearch!
                                localIfSearch! < (context.ir -> len) -> while {
                                    context.ir[localIfSearch!] => localIfCandidate
                                    localIfCandidate.kind == 18 -> if {
                                        context.ir[localIfCandidate.operand0] => localIfRoot
                                        (localIfRoot.kind == 8 and (localIfRoot.opcode == -25 or localIfRoot.opcode == -24) or (localIfRoot.kind == 7 and localIfRoot.opcode == -26)) -> if {
                                            NodeAncestorRequest { nodeIndex: localCandidateIndex!, ownerIndex: localIfCandidate.operand0 } => localIfAncestorRequest
                                            localIfAncestorRequest -> irDescendsFrom => localIfDescends
                                            (localCandidateIndex! == localIfCandidate.operand0 or localIfDescends) -> if {
                                                true => localIfCondition!
                                            }
                                        }
                                    }
                                    localIfSearch! + 1 => localIfSearch!
                                }
                                localIfCondition! -> if {
                                    true => localScheduled![localCandidateIndex!]
                                    true => localProgress!
                                }
                                (not localIfCondition! and localCandidate.kind == 19) -> if {
                                    true => localScheduled![localCandidateIndex!]
                                    true => localProgress!
                                }
                                (not localIfCondition! and localCandidate.kind != 19) -> if {
                                    true => localReady!
                                    localCandidateIndex! => localStatementRoot!
                                    localCandidate.parent => localRootParent!
                                    bindingBelongsViaOperand! -> if {
                                        localCandidate.operand0 => localStatementRoot!
                                        context.ir[localCandidate.operand0].parent => localRootParent!
                                    }
                                    (localRootParent! >= 0 and localRootParent! != regionTaskNode) -> while {
                                        localRootParent! => localStatementRoot!
                                        context.ir[localRootParent!].parent => localRootParent!
                                    }
                                    -1 => localPreviousRoot!
                                    UIntSize(0) => localPreviousRootStart!
                                    0 => localRootSearch!
                                    localRootSearch! < (context.ir -> len) -> while {
                                        context.ir[localRootSearch!] => localRootCandidate
                                        (localRootCandidate.parent == regionTaskNode and (localRootCandidate -> sourceStart) < (context.ir[localStatementRoot!] -> sourceStart)) -> if {
                                            localRootCandidate -> sourceStart => localRootCandidateStart
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
                                    (localCandidate.kind != 20 and localCandidate.operand0 >= 0 and (localCandidate.kind != 5 or context.ir[localCandidate.operand0].flags % 2 == 0)) -> if {
                                        context.ir[localCandidate.operand0].parent => localOperandAncestor!
                                        false => localOperandBelongs!
                                        (localOperandAncestor! >= 0 and not localOperandBelongs!) -> while {
                                            localOperandAncestor! == regionTaskNode -> if { true => localOperandBelongs! } else { context.ir[localOperandAncestor!].parent => localOperandAncestor! }
                                        }
                                        (localOperandBelongs! and not localScheduled![localCandidate.operand0]) -> if { false => localReady! }
                                    }
                                    (localCandidate.kind != 6 and localCandidate.kind != 18 and localCandidate.kind != 20 and localCandidate.operand1 >= 0) -> if {
                                        context.ir[localCandidate.operand1].parent => localSecondAncestor!
                                        false => localSecondBelongs!
                                        (localSecondAncestor! >= 0 and not localSecondBelongs!) -> while {
                                            localSecondAncestor! == regionTaskNode -> if { true => localSecondBelongs! } else { context.ir[localSecondAncestor!].parent => localSecondAncestor! }
                                        }
                                        (localSecondBelongs! and not localScheduled![localCandidate.operand1]) -> if { false => localReady! }
                                    }
                                    (localCandidate.kind == 9 and localCandidate.opcode == -203 and localCandidate.operand1 >= 0 and context.ir[localCandidate.operand1].nextOperand >= 0) -> if {
                                        context.ir[localCandidate.operand1].nextOperand => localSliceLength!
                                        context.ir[localSliceLength!].parent => localSliceLengthAncestor!
                                        false => localSliceLengthBelongs!
                                        (localSliceLengthAncestor! >= 0 and not localSliceLengthBelongs!) -> while {
                                            localSliceLengthAncestor! == regionTaskNode -> if { true => localSliceLengthBelongs! } else { context.ir[localSliceLengthAncestor!].parent => localSliceLengthAncestor! }
                                        }
                                        (localSliceLengthBelongs! and not localScheduled![localSliceLength!]) -> if { false => localReady! }
                                    }
                                    (localCandidate.kind == 9 and localCandidate.opcode == -204 and localCandidate.operand1 >= 0) -> if {
                                        localCandidate.operand1 -> aggregateValueIndex => localPushValue!
                                        (localPushValue! >= 0 and localPushValue! != localCandidate.operand1) -> if {
                                            context.ir[localPushValue!].parent => localPushValueAncestor!
                                            false => localPushValueBelongs!
                                            (localPushValueAncestor! >= 0 and not localPushValueBelongs!) -> while {
                                                localPushValueAncestor! == regionTaskNode -> if { true => localPushValueBelongs! } else { context.ir[localPushValueAncestor!].parent => localPushValueAncestor! }
                                            }
                                            (localPushValueBelongs! and not localScheduled![localPushValue!]) -> if { false => localReady! }
                                        }
                                    }
                                    (localCandidate.kind == 12 or localCandidate.kind == 14 or localCandidate.kind == 16) -> if {
                                        localCandidate.operand0 => localAggregateOperand!
                                        localAggregateOperand! >= 0 -> while {
                                            localAggregateOperand! -> aggregateValueIndex => localAggregateValue
                                            not localScheduled![localAggregateValue] -> if { false => localReady! }
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
                            ((returnParameter.typeOrigin == 13 or returnParameter.typeOrigin == 14) and returnParameter.flags % 2 == 1) -> if {
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
                        returnOwner -> writeIrType
                        " " -> print
                        context.ir[regionNode.operand0] => returnedValue
                        (returnedValue.kind == 3 or returnedValue.kind == 4) -> if {
                            returnedValue -> sourceToken => returnValueToken
                            returnedValue.kind == 3 -> if { context.sources[returnedValue.sourceModule] -> slice(returnValueToken.span.start, returnValueToken.span.length) -> print } else {
                                ((context.sources[returnedValue.sourceModule] -> byte(returnValueToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
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
                    guardCondition -> sourceToken => guardConditionToken
                    ((context.sources[guardCondition.sourceModule] -> byte(guardConditionToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
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
            regionNode.kind == 2 -> if {
                regionNode -> sourceToken => regionTextToken
                TextLiteralRequest { sourceModule: regionNode.sourceModule, token: regionTextToken } -> textLiteralLength => regionTextLength
                "  %v$(regionNodeIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(regionNodeIndex!), 0" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.text %v$(regionNodeIndex!)_ptr, i64 $regionTextLength, 1" -> println
            }
            (regionNode.kind == 5 and not (regionNode.typeOrigin == 1 and regionNode.typeSymbol == 16) and ownerIndex! >= 0 and not (context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionNode.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol)) -> if {
                -1 => regionBindingIndex!
                (regionNode.operand0 >= 0 and context.ir[regionNode.operand0].kind == 17) -> if { regionNode.operand0 => regionBindingIndex! }
                regionBindingIndex! >= 0 -> if {
                    context.ir[regionBindingIndex!] => regionBinding
                    regionBinding.flags == 1 -> if {
                        regionBindingIndex! -> mutableBindingRoot => regionBindingRoot
                        "  %v$(regionNodeIndex!) = load " -> print
                        regionNode -> writeIrType
                        ", ptr %slot$(regionBindingRoot), align " -> print
                        regionNode -> storageAlign -> writeDecimalLine
                    } else {
                        context.ir[regionBinding.operand0] => regionBindingValue
                        "  %v$(regionNodeIndex!) = freeze " -> print
                        regionNode -> writeIrType
                        " " -> print
                        (regionBindingValue.kind == 3 or regionBindingValue.kind == 4) -> if {
                            regionBindingValue -> sourceToken => regionBindingToken
                            regionBindingValue.kind == 3 -> if { context.sources[regionBindingValue.sourceModule] -> slice(regionBindingToken.span.start, regionBindingToken.span.length) -> print } else {
                                ((context.sources[regionBindingValue.sourceModule] -> byte(regionBindingToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else { "%v$(regionBinding.operand0)" -> print }
                        "" -> println
                    }
                }
            }
            (regionNode.kind == 17 and regionNode.flags == 1) -> if {
                regionNodeIndex! -> mutableBindingRoot => mutableRegionRoot
                context.ir[mutableRegionRoot] => mutableRegionBinding
                context.ir[regionNode.operand0] => mutableRegionValue
                mutableRegionBinding -> integerNodeWidth => mutableRegionTargetWidth
                mutableRegionValue -> integerNodeWidth => mutableRegionSourceWidth
                (mutableRegionTargetWidth > 0 and mutableRegionSourceWidth > 0 and mutableRegionTargetWidth != mutableRegionSourceWidth and mutableRegionValue.kind != 3 and mutableRegionValue.kind != 4) => castMutableRegionValue!
                castMutableRegionValue! -> if {
                    "  %v$(regionNodeIndex!)_mutable_cast = " -> print
                    mutableRegionTargetWidth < mutableRegionSourceWidth -> if { "trunc " -> print } else { mutableRegionValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print } }
                    mutableRegionValue -> writeIrType
                    " %v$(regionNode.operand0) to " -> print
                    mutableRegionBinding -> writeIrType
                    "" -> println
                }
                "  store " -> print
                mutableRegionBinding -> writeIrType
                " " -> print
                (mutableRegionValue.kind == 3 or mutableRegionValue.kind == 4) -> if {
                    mutableRegionValue -> sourceToken => mutableRegionToken
                    mutableRegionValue.kind == 3 -> if { context.sources[mutableRegionValue.sourceModule] -> slice(mutableRegionToken.span.start, mutableRegionToken.span.length) -> print } else {
                        ((context.sources[mutableRegionValue.sourceModule] -> byte(mutableRegionToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                    }
                } else { castMutableRegionValue! -> if { "%v$(regionNodeIndex!)_mutable_cast" -> print } else { "%v$(regionNode.operand0)" -> print } }
                ", ptr %slot$(mutableRegionRoot), align " -> print
                mutableRegionBinding -> storageAlign -> writeDecimalLine
            }
            regionNode.kind == 24 -> if {
                WhileValueRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex!, nodeIndex: regionNodeIndex! } -> emitIndexAssignment
            }
            regionNode.kind == 25 -> if {
                WhileValueRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex!, nodeIndex: regionNodeIndex! } -> emitMemberAssignment
            }
            regionNode.kind == 12 -> if {
                regionNode.operand0 < 0 -> if {
                    "  %v$(regionNodeIndex!) = freeze " -> print
                    regionNode -> writeIrType
                    " zeroinitializer" -> println
                }
                regionNode.operand0 => regionFieldValueIndex!
                0 => regionFieldPosition!
                regionFieldValueIndex! >= 0 -> while {
                    context.ir[regionFieldValueIndex!] => regionFieldValue
                    regionFieldValueIndex! -> aggregateValueIndex => regionEmittedFieldIndex
                    context.ir[regionEmittedFieldIndex] => regionEmittedFieldValue
                    regionNode.typeId => regionStructOwnerTypeId!
                    0 => regionStructOwnerTypeSearch!
                    regionStructOwnerTypeSearch! < (context.types -> len) -> while {
                        context.types[regionStructOwnerTypeSearch!] => regionStructOwnerTypeCandidate
                        (regionStructOwnerTypeCandidate.status == 0 and regionStructOwnerTypeCandidate.kind == 1 and regionStructOwnerTypeCandidate.origin == regionNode.typeOrigin and regionStructOwnerTypeCandidate.module == regionNode.typeModule and regionStructOwnerTypeCandidate.symbol == regionNode.typeSymbol) -> if { regionStructOwnerTypeSearch! => regionStructOwnerTypeId! }
                        regionStructOwnerTypeSearch! + 1 => regionStructOwnerTypeSearch!
                    }
                    -1 => regionTargetFieldTypeId!
                    0 => regionTargetFieldOrdinal!
                    0 => regionTargetFieldSearch!
                    regionTargetFieldSearch! < (context.fields -> len) -> while {
                        context.fields[regionTargetFieldSearch!] => regionTargetField
                        (regionTargetField.status == 0 and regionTargetField.ownerType == regionStructOwnerTypeId!) -> if {
                            regionTargetFieldOrdinal! == regionFieldPosition! -> if { regionTargetField.fieldType => regionTargetFieldTypeId! }
                            regionTargetFieldOrdinal! + 1 => regionTargetFieldOrdinal!
                        }
                        regionTargetFieldSearch! + 1 => regionTargetFieldSearch!
                    }
                    regionEmittedFieldValue -> integerNodeWidth => regionSourceFieldWidth!
                    0 => regionTargetFieldWidth!
                    regionTargetFieldTypeId! >= 0 -> if {
                        context.types[regionTargetFieldTypeId!] => regionTargetFieldSemanticType
                        regionTargetFieldSemanticType.origin == 1 -> if { regionTargetFieldSemanticType.symbol -> integerWidth => regionTargetFieldWidth! }
                    }
                    false => regionIndexedFieldValue!
                    ((regionEmittedFieldValue -> isDynamicArrayType) and regionFieldValue.nextOperand >= 0 and regionTargetFieldTypeId! >= 0) -> if {
                        regionEmittedFieldValue -> arrayElementTypeId => regionFieldElementTypeId
                        regionFieldElementTypeId == regionTargetFieldTypeId! -> if {
                            true => regionIndexedFieldValue!
                            context.ir[regionFieldValue.nextOperand] => regionFieldIndexValue
                            "  %v$(regionNodeIndex!)_field$(regionFieldPosition!)_data = extractvalue %sl.array.i32 %v$(regionEmittedFieldIndex), 0" -> println
                            regionFieldIndexValue -> integerNodeWidth => regionFieldIndexWidth
                            (regionFieldIndexValue.kind != 3 and regionFieldIndexWidth > 0 and regionFieldIndexWidth < 64) -> if {
                                "  %v$(regionNodeIndex!)_field$(regionFieldPosition!)_index = " -> print
                                regionFieldIndexValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                                "i$(regionFieldIndexWidth) %v$(regionFieldValue.nextOperand) to i64" -> println
                            }
                            "  %v$(regionNodeIndex!)_field$(regionFieldPosition!)_ptr = getelementptr " -> print
                            regionTargetFieldTypeId! -> writeSemanticTypeId
                            ", ptr %v$(regionNodeIndex!)_field$(regionFieldPosition!)_data, i64 " -> print
                            regionFieldIndexValue.kind == 3 -> if {
                                regionFieldIndexValue -> sourceToken => regionFieldIndexToken
                                context.sources[regionFieldIndexValue.sourceModule] -> slice(regionFieldIndexToken.span.start, regionFieldIndexToken.span.length) -> println
                            } else {
                                (regionFieldIndexWidth > 0 and regionFieldIndexWidth < 64) -> if { "%v$(regionNodeIndex!)_field$(regionFieldPosition!)_index" -> println } else { "%v$(regionFieldValue.nextOperand)" -> println }
                            }
                            "  %v$(regionNodeIndex!)_field$(regionFieldPosition!)_indexed = load " -> print
                            regionTargetFieldTypeId! -> writeSemanticTypeId
                            ", ptr %v$(regionNodeIndex!)_field$(regionFieldPosition!)_ptr, align " -> print
                            context.typeAligns[regionTargetFieldTypeId!] -> writeDecimalLine
                            regionTargetFieldWidth! => regionSourceFieldWidth!
                        }
                    }
                    (regionSourceFieldWidth! > 0 and regionTargetFieldWidth! > 0 and regionSourceFieldWidth! != regionTargetFieldWidth! and regionEmittedFieldValue.kind != 3 and regionEmittedFieldValue.kind != 4) => regionCastStructField!
                    regionCastStructField! -> if {
                        "  %v$(regionNodeIndex!)_field$(regionFieldPosition!)_cast = " -> print
                        regionTargetFieldWidth! < regionSourceFieldWidth! -> if { "trunc " -> print } else {
                            regionEmittedFieldValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                        }
                        regionEmittedFieldValue -> writeIrType
                        " " -> print
                        (regionEmittedFieldValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionEmittedFieldValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionEmittedFieldIndex)" -> print }
                        " to " -> print
                        regionTargetFieldTypeId! -> writeSemanticTypeId
                        "" -> println
                    }
                    regionFieldValue.nextOperand => regionNextFieldValue!
                    (regionIndexedFieldValue! and regionNextFieldValue! >= 0) -> if { context.ir[regionNextFieldValue!].nextOperand => regionNextFieldValue! }
                    "  " -> print
                    regionNextFieldValue! < 0 -> if { "%v$(regionNodeIndex!)" -> print } else { "%v$(regionNodeIndex!)_f$(regionFieldPosition!)" -> print }
                    " = insertvalue " -> print
                    regionNode -> writeIrType
                    " " -> print
                    regionFieldPosition! == 0 -> if { "poison" -> print } else {
                        "%v$(regionNodeIndex!)_f" -> print
                        regionFieldPosition! - 1 -> writeDecimal
                    }
                    ", " -> print
                    regionTargetFieldTypeId! >= 0 -> if { regionTargetFieldTypeId! -> writeSemanticTypeId } else { regionEmittedFieldValue -> writeIrType }
                    " " -> print
                    regionEmittedFieldValue.kind == 2 -> if {
                        regionEmittedFieldValue -> sourceToken => regionFieldTextToken
                        Int(TextLiteralRequest { sourceModule: regionEmittedFieldValue.sourceModule, token: regionFieldTextToken } -> textLiteralLength) => regionFieldTextLength
                        "{ ptr @sl_str_$(regionEmittedFieldIndex), i64 $regionFieldTextLength }" -> print
                    } else {
                    (regionEmittedFieldValue.kind == 3 or regionEmittedFieldValue.kind == 4) -> if {
                        regionEmittedFieldValue -> sourceToken => regionFieldToken
                        regionEmittedFieldValue.kind == 3 -> if {
                            context.sources[regionEmittedFieldValue.sourceModule] -> slice(regionFieldToken.span.start, regionFieldToken.span.length) -> print
                        } else {
                            ((context.sources[regionEmittedFieldValue.sourceModule] -> byte(regionFieldToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                        }
                    } else {
                        regionIndexedFieldValue! -> if { "%v$(regionNodeIndex!)_field$(regionFieldPosition!)_indexed" -> print } else {
                            regionCastStructField! -> if { "%v$(regionNodeIndex!)_field$(regionFieldPosition!)_cast" -> print } else {
                                (regionEmittedFieldValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionEmittedFieldValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionEmittedFieldIndex)" -> print }
                            }
                        }
                    }
                    }
                    ", $(regionFieldPosition!)" -> println
                    regionNextFieldValue! => regionFieldValueIndex!
                    regionFieldPosition! + 1 => regionFieldPosition!
                }
            }
            regionNode.kind == 13 -> if {
                (regionNode.typeOrigin == 1 and regionNode.typeSymbol == 16) -> if {
                } else {
                context.ir[regionNode.operand0] => regionMemberBase
                regionMemberBase.typeOrigin => regionMemberCurrentOrigin!
                regionMemberBase.typeModule => regionMemberCurrentModule!
                regionMemberBase.typeSymbol => regionMemberCurrentSymbol!
                regionNode -> sourceNode => regionMemberAst
                regionMemberBase -> sourceNode => regionMemberBaseAst
                regionMemberBaseAst.start + regionMemberBaseAst.length => regionMemberBaseEnd
                1 => regionMemberIdentifierCount!
                regionMemberAst.firstToken => regionMemberTokenIndex!
                true => beforeRegionMemberBaseEnd!
                (beforeRegionMemberBaseEnd! and regionMemberTokenIndex! < regionMemberAst.firstToken + regionMemberAst.tokenCount) -> while {
                    context.tokens[context.ranges[regionNode.sourceModule].tokenStart + regionMemberTokenIndex!].span.start < regionMemberBaseEnd -> if {
                        regionMemberTokenIndex! + 1 => regionMemberTokenIndex!
                    } else { false => beforeRegionMemberBaseEnd! }
                }
                regionMemberTokenIndex! => regionMemberPathTokenStart
                regionMemberTokenIndex! < regionMemberAst.firstToken + regionMemberAst.tokenCount -> while {
                    context.tokens[context.ranges[regionNode.sourceModule].tokenStart + regionMemberTokenIndex!].kind == grammar.tokenIdIdentifier -> if { regionMemberIdentifierCount! + 1 => regionMemberIdentifierCount! }
                    regionMemberTokenIndex! + 1 => regionMemberTokenIndex!
                }
                1 => regionMemberIdentifierOrdinal!
                0 => regionMemberProjectionOrdinal!
                regionMemberPathTokenStart => regionMemberTokenIndex!
                regionMemberTokenIndex! < regionMemberAst.firstToken + regionMemberAst.tokenCount -> while {
                    context.tokens[context.ranges[regionNode.sourceModule].tokenStart + regionMemberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                        regionMemberIdentifierOrdinal! > 0 -> if {
                            regionMemberCurrentModule! => regionOwnerSourceModule!
                            regionMemberCurrentOrigin! == 2 -> if { context.modules[regionMemberCurrentModule!].sourceIndex => regionOwnerSourceModule! }
                            context.ranges[regionOwnerSourceModule!] => regionOwnerRange
                            -1 => regionFieldOrdinal!
                            0 => regionCandidateFieldOrdinal!
                            0 => regionMemberFieldSearch!
                            regionMemberFieldSearch! < regionOwnerRange.symbolCount -> while {
                                context.symbols[regionOwnerRange.symbolStart + regionMemberFieldSearch!] => regionMemberField
                                (regionMemberField.kind == 26 and regionMemberField.parent == regionMemberCurrentSymbol!) -> if {
                                    context.tokens[context.ranges[regionNode.sourceModule].tokenStart + regionMemberTokenIndex!] => regionMemberName
                                    context.tokens[regionOwnerRange.tokenStart + regionMemberField.nameToken] => regionFieldName
                                    regionMemberName.span.length == regionFieldName.span.length => regionFieldEqual!
                                    UIntSize(0) => regionFieldNameByte!
                                    (regionFieldEqual! and regionFieldNameByte! < regionMemberName.span.length) -> while {
                                        context.sources[regionNode.sourceModule] -> byte(regionMemberName.span.start + regionFieldNameByte!) => regionMemberByte
                                        context.sources[regionOwnerSourceModule!] -> byte(regionFieldName.span.start + regionFieldNameByte!) => regionFieldByte
                                        regionMemberByte != regionFieldByte -> if { false => regionFieldEqual! }
                                        regionFieldNameByte! + UIntSize(1) => regionFieldNameByte!
                                    }
                                    regionFieldEqual! -> if {
                                        regionCandidateFieldOrdinal! => regionFieldOrdinal!
                                        -1 => regionMemberFieldTypeIndex!
                                        0 => regionMemberFieldTypeSearch!
                                        regionMemberFieldTypeSearch! < (context.nominal -> len) -> while {
                                            (context.nominal[regionMemberFieldTypeSearch!].sourceModule == regionOwnerSourceModule! and context.nominal[regionMemberFieldTypeSearch!].typeAst == regionMemberField.typeNode) -> if { regionMemberFieldTypeSearch! => regionMemberFieldTypeIndex! }
                                            regionMemberFieldTypeSearch! + 1 => regionMemberFieldTypeSearch!
                                        }
                                        -1 => regionMemberNextOrigin!
                                        -1 => regionMemberNextModule!
                                        -1 => regionMemberNextSymbol!
                                        regionMemberFieldTypeIndex! >= 0 -> if {
                                            context.nominal[regionMemberFieldTypeIndex!] => regionMemberFieldType
                                            regionMemberFieldType.origin => regionMemberNextOrigin!
                                            regionMemberFieldType.targetModule => regionMemberNextModule!
                                            regionMemberFieldType.targetSymbol => regionMemberNextSymbol!
                                        } else {
                                            0 => regionMemberCompositeSearch!
                                            regionMemberCompositeSearch! < (context.composite -> len) -> while {
                                                context.composite[regionMemberCompositeSearch!] => regionMemberCompositeType
                                                (regionMemberCompositeType.sourceModule == regionOwnerSourceModule! and regionMemberCompositeType.typeAst == regionMemberField.typeNode) -> if {
                                                    10 + regionMemberCompositeType.kind => regionMemberNextOrigin!
                                                    regionMemberCompositeType.kind == 5 -> if {
                                                        regionMemberCompositeType.keySymbol => regionMemberNextModule!
                                                        regionMemberCompositeType.valueSymbol => regionMemberNextSymbol!
                                                    } else {
                                                        regionMemberCompositeType.elementModule => regionMemberNextModule!
                                                        regionMemberCompositeType.elementSymbol => regionMemberNextSymbol!
                                                    }
                                                }
                                                regionMemberCompositeSearch! + 1 => regionMemberCompositeSearch!
                                            }
                                        }
                                        "  " -> print
                                        regionMemberIdentifierOrdinal! == regionMemberIdentifierCount! - 1 -> if { "%v$(regionNodeIndex!)" -> print } else { "%v$(regionNodeIndex!)_m$(regionMemberProjectionOrdinal!)" -> print }
                                        " = extractvalue %sl.struct.m$(regionOwnerSourceModule!)_s$(regionMemberCurrentSymbol!) " -> print
                                        regionMemberProjectionOrdinal! == 0 -> if {
                                            (regionMemberBase.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionMemberBase.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                                        } else {
                                            "%v$(regionNodeIndex!)_m" -> print
                                            regionMemberProjectionOrdinal! - 1 -> writeDecimal
                                        }
                                        ", $(regionFieldOrdinal!)" -> println
                                        regionMemberNextOrigin! => regionMemberCurrentOrigin!
                                        regionMemberNextModule! => regionMemberCurrentModule!
                                        regionMemberNextSymbol! => regionMemberCurrentSymbol!
                                        regionMemberProjectionOrdinal! + 1 => regionMemberProjectionOrdinal!
                                    }
                                    regionCandidateFieldOrdinal! + 1 => regionCandidateFieldOrdinal!
                                }
                                regionMemberFieldSearch! + 1 => regionMemberFieldSearch!
                            }
                        }
                        regionMemberIdentifierOrdinal! + 1 => regionMemberIdentifierOrdinal!
                    }
                    regionMemberTokenIndex! + 1 => regionMemberTokenIndex!
                }
                }
            }
            ((regionNode.kind == 9 or regionNode.kind == 6) and regionNode.opcode == -201 and regionNode.operand0 >= 0) -> if {
                context.ir[regionNode.operand0] => regionLengthBase
                (regionLengthBase.typeOrigin == 1 and regionLengthBase.typeSymbol == 16) -> if {
                    "  %v$(regionNodeIndex!) = call i64 @sl_argument_count()" -> println
                } else {
                    "  %v$(regionNodeIndex!)" -> print
                    regionNode.typeSymbol == 2 -> if { "_raw" -> print }
                    " = extractvalue " -> print
                    regionLengthBase -> writeIrType
                    " " -> print
                    (regionLengthBase.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionLengthBase.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                    ", " -> print
                    regionLengthBase.typeOrigin == 15 -> if { "2" -> println } else { "1" -> println }
                    regionNode.typeSymbol == 2 -> if { "  %v$(regionNodeIndex!) = trunc i64 %v$(regionNodeIndex!)_raw to i32" -> println }
                }
            }
            ((regionNode.kind == 9 or regionNode.kind == 6) and regionNode.opcode == -202 and regionNode.operand0 >= 0 and regionNode.operand1 >= 0) -> if {
                context.ir[regionNode.operand0] => regionByteBase
                context.ir[regionNode.operand1] => regionByteIndex
                regionByteIndex.typeSymbol -> integerWidth => regionByteIndexWidth
                "  %v$(regionNodeIndex!)_base = extractvalue " -> print
                regionByteBase -> writeTextLikeType
                " " -> print
                (regionByteBase.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionByteBase.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                ", 0" -> println
                (regionByteIndexWidth > 0 and regionByteIndexWidth < 64) -> if {
                    "  %v$(regionNodeIndex!)_index = " -> print
                    regionByteIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                    " i$(regionByteIndexWidth) %v$(regionNode.operand1) to i64" -> println
                }
                "  %v$(regionNodeIndex!)_address = getelementptr i8, ptr %v$(regionNodeIndex!)_base, i64 " -> print
                (regionByteIndexWidth > 0 and regionByteIndexWidth < 64) -> if { "%v$(regionNodeIndex!)_index" -> println } else { "%v$(regionNode.operand1)" -> println }
                "  %v$(regionNodeIndex!) = load i8, ptr %v$(regionNodeIndex!)_address, align 1" -> println
            }
            ((regionNode.kind == 9 or regionNode.kind == 6) and regionNode.opcode == -203 and regionNode.operand0 >= 0 and regionNode.operand1 >= 0) -> if {
                context.ir[regionNode.operand1].nextOperand => regionSliceLength
                context.ir[regionNode.operand0] => regionSliceBase
                context.ir[regionNode.operand1] => regionSliceIndex
                regionSliceIndex.typeSymbol -> integerWidth => regionSliceIndexWidth
                "  %v$(regionNodeIndex!)_base = extractvalue " -> print
                regionSliceBase -> writeTextLikeType
                " " -> print
                (regionSliceBase.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionSliceBase.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                ", 0" -> println
                (regionSliceIndexWidth > 0 and regionSliceIndexWidth < 64) -> if {
                    "  %v$(regionNodeIndex!)_index = " -> print
                    regionSliceIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                    " i$(regionSliceIndexWidth) %v$(regionNode.operand1) to i64" -> println
                }
                "  %v$(regionNodeIndex!)_start = getelementptr i8, ptr %v$(regionNodeIndex!)_base, i64 " -> print
                (regionSliceIndexWidth > 0 and regionSliceIndexWidth < 64) -> if { "%v$(regionNodeIndex!)_index" -> println } else { "%v$(regionNode.operand1)" -> println }
                "  %v$(regionNodeIndex!)_ptr = insertvalue %sl.text poison, ptr %v$(regionNodeIndex!)_start, 0" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.text %v$(regionNodeIndex!)_ptr, i64 %v$(regionSliceLength), 1" -> println
            }
            (regionNode.kind == 9 and regionNode.opcode == -204 and regionNode.operand0 >= 0 and regionNode.operand1 >= 0) -> if {
                WhileValueRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex!, nodeIndex: 0 } -> emitArrayPush
            }
            ((regionNode.kind == 9 or regionNode.kind == 6) and regionNode.opcode == -205 and regionNode.operand0 >= 0) -> if {
                context.ir[regionNode.operand0] => regionMapPath
                "  %v$(regionNodeIndex!) = call %sl.source_text @sl_runtime_map_text(%sl.text " -> print
                (regionMapPath.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionMapPath.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                ")" -> println
            }
            ((regionNode.kind == 9 or regionNode.kind == 6) and regionNode.opcode == -206 and regionNode.operand0 >= 0) -> if {
                context.ir[regionNode.operand0] => regionBorrowText
                "  %v$(regionNodeIndex!)_data = extractvalue %sl.text " -> print
                (regionBorrowText.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionBorrowText.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                ", 0" -> println
                "  %v$(regionNodeIndex!)_length = extractvalue %sl.text " -> print
                (regionBorrowText.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and regionBorrowText.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                ", 1" -> println
                "  %v$(regionNodeIndex!)_value0 = insertvalue %sl.source_text poison, ptr %v$(regionNodeIndex!)_data, 0" -> println
                "  %v$(regionNodeIndex!)_value1 = insertvalue %sl.source_text %v$(regionNodeIndex!)_value0, i64 %v$(regionNodeIndex!)_length, 1" -> println
                "  %v$(regionNodeIndex!)_value2 = insertvalue %sl.source_text %v$(regionNodeIndex!)_value1, ptr null, 2" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.source_text %v$(regionNodeIndex!)_value2, i64 0, 3" -> println
            }
            (regionNode.kind == 14 and (regionNode.typeOrigin == 13 or regionNode.typeOrigin == 14 or regionNode.typeKind == 3)) -> if {
                0 => regionArrayLength!
                regionNode.operand0 => regionArrayCountIndex!
                regionArrayCountIndex! >= 0 -> while {
                    regionArrayLength! + 1 => regionArrayLength!
                    context.ir[regionArrayCountIndex!].nextOperand => regionArrayCountIndex!
                }
                regionArrayLength! * (regionNode -> arrayElementSize) => regionArrayByteLength
                "  %v$(regionNodeIndex!)_data = call ptr @malloc(i64 $regionArrayByteLength)" -> println
                regionNode.operand0 => regionArrayElementIndex!
                0 => regionArrayElementPosition!
                regionArrayElementIndex! >= 0 -> while {
                    context.ir[regionArrayElementIndex!] => regionArrayElement
                    "  %v$(regionNodeIndex!)_ptr$(regionArrayElementPosition!) = getelementptr " -> print
                    regionNode -> writeArrayElementType
                    ", ptr %v$(regionNodeIndex!)_data, i64 $(regionArrayElementPosition!)" -> println
                    "  store " -> print
                    regionNode -> writeArrayElementType
                    " " -> print
                    regionArrayElement.kind == 3 -> if {
                        regionArrayElement -> sourceToken => regionArrayElementToken
                        context.sources[regionArrayElement.sourceModule] -> slice(regionArrayElementToken.span.start, regionArrayElementToken.span.length) -> print
                    } else { "%v$(regionArrayElementIndex!)" -> print }
                    ", ptr %v$(regionNodeIndex!)_ptr$(regionArrayElementPosition!), align " -> print
                    regionNode -> arrayElementAlign -> writeDecimalLine
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
                        regionDictionaryItem -> sourceToken => regionDictionaryItemToken
                        regionDictionaryItem.kind == 3 -> if {
                            context.sources[regionDictionaryItem.sourceModule] -> slice(regionDictionaryItemToken.span.start, regionDictionaryItemToken.span.length) -> print
                        } else {
                            ((context.sources[regionDictionaryItem.sourceModule] -> byte(regionDictionaryItemToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                        }
                    } else { "%v$(regionDictionaryItemIndex!)" -> print }
                    ", ptr %v$(regionNodeIndex!)_$(regionDictionarySide)_ptr$(regionDictionaryEntryPosition), align " -> print
                    regionDictionaryItemSymbol -> legacyStorageAlign -> writeDecimalLine
                    regionDictionaryItem.nextOperand => regionDictionaryItemIndex!
                    regionDictionaryItemPosition! + 1 => regionDictionaryItemPosition!
                }
                "  %v$(regionNodeIndex!)_0 = insertvalue %sl.dict poison, ptr %v$(regionNodeIndex!)_keys, 0" -> println
                "  %v$(regionNodeIndex!)_1 = insertvalue %sl.dict %v$(regionNodeIndex!)_0, ptr %v$(regionNodeIndex!)_values, 1" -> println
                "  %v$(regionNodeIndex!)_2 = insertvalue %sl.dict %v$(regionNodeIndex!)_1, i64 $(regionDictionaryLength), 2" -> println
                "  %v$(regionNodeIndex!) = insertvalue %sl.dict %v$(regionNodeIndex!)_2, i64 $(regionDictionaryLength), 3" -> println
            }
            regionNode.kind == 15 -> if {
                context.ir[regionNode.operand0] => regionIndexedValue
                context.ir[regionNode.operand1] => regionIndexValue
                (regionIndexedValue.typeOrigin == 1 and regionIndexedValue.typeSymbol == 16) -> if {
                    "  %v$(regionNodeIndex!) = call %sl.text @sl_argument(i64 " -> print
                    regionIndexValue.kind == 3 -> if {
                        regionIndexValue -> sourceToken => regionIndexToken
                        context.sources[regionIndexValue.sourceModule] -> slice(regionIndexToken.span.start, regionIndexToken.span.length) -> print
                    } else {
                        (regionIndexValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionIndexValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand1)" -> print }
                    }
                    ")" -> println
                } else {
                    "  %v$(regionNodeIndex!)_data = extractvalue %sl.array.i32 " -> print
                    (regionIndexedValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionIndexedValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                    ", 0" -> println
                    false => regionNestedIndex!
                    regionIndexValue -> integerNodeWidth => regionIndexWidth!
                    regionIndexValue -> integerNodeSigned => regionIndexSigned!
                    ((regionIndexValue -> isDynamicArrayType) and regionIndexValue.nextOperand >= 0) -> if {
                        true => regionNestedIndex!
                        context.ir[regionIndexValue.nextOperand] => regionNestedSubscript
                        "  %v$(regionNodeIndex!)_nested_data = extractvalue %sl.array.i32 %v$(regionNode.operand1), 0" -> println
                        regionNestedSubscript -> integerNodeWidth => regionNestedSubscriptWidth
                        (regionNestedSubscript.kind != 3 and regionNestedSubscriptWidth > 0 and regionNestedSubscriptWidth < 64) -> if {
                            "  %v$(regionNodeIndex!)_nested_index = " -> print
                            regionNestedSubscript -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                            "i$(regionNestedSubscriptWidth) %v$(regionIndexValue.nextOperand) to i64" -> println
                        }
                        "  %v$(regionNodeIndex!)_nested_ptr = getelementptr " -> print
                        regionIndexValue -> writeArrayElementType
                        ", ptr %v$(regionNodeIndex!)_nested_data, i64 " -> print
                        regionNestedSubscript.kind == 3 -> if {
                            regionNestedSubscript -> sourceToken => regionNestedSubscriptToken
                            context.sources[regionNestedSubscript.sourceModule] -> slice(regionNestedSubscriptToken.span.start, regionNestedSubscriptToken.span.length) -> println
                        } else {
                            (regionNestedSubscriptWidth > 0 and regionNestedSubscriptWidth < 64) -> if { "%v$(regionNodeIndex!)_nested_index" -> println } else { "%v$(regionIndexValue.nextOperand)" -> println }
                        }
                        regionIndexValue -> arrayElementTypeId => regionNestedElementTypeId
                        "  %v$(regionNodeIndex!)_nested_value = load " -> print
                        regionIndexValue -> writeArrayElementType
                        ", ptr %v$(regionNodeIndex!)_nested_ptr, align " -> print
                        regionIndexValue -> arrayElementAlign -> writeDecimalLine
                        regionNestedElementTypeId >= 0 -> if {
                            context.types[regionNestedElementTypeId] => regionNestedElementType
                            regionNestedElementType.origin == 1 -> if {
                                regionNestedElementType.symbol -> integerWidth => regionIndexWidth!
                                regionNestedElementType.symbol -> integerSigned => regionIndexSigned!
                            }
                        }
                    }
                    (regionIndexValue.kind != 3 and regionIndexWidth! > 0 and regionIndexWidth! < 64) -> if {
                        "  %v$(regionNodeIndex!)_index = " -> print
                        regionIndexSigned! -> if { "sext " -> print } else { "zext " -> print }
                        "i$(regionIndexWidth!) " -> print
                        regionNestedIndex! -> if { "%v$(regionNodeIndex!)_nested_value" -> print } else {
                            (regionIndexValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionIndexValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand1)" -> print }
                        }
                        " to i64" -> println
                    }
                    "  %v$(regionNodeIndex!)_ptr = getelementptr " -> print
                    regionNode -> writeIrType
                    ", ptr %v$(regionNodeIndex!)_data, i64 " -> print
                    regionIndexValue.kind == 3 -> if {
                        regionIndexValue -> sourceToken => regionIndexToken
                        context.sources[regionIndexValue.sourceModule] -> slice(regionIndexToken.span.start, regionIndexToken.span.length) -> println
                    } else {
                        (regionIndexWidth! > 0 and regionIndexWidth! < 64) -> if { "%v$(regionNodeIndex!)_index" -> println } else {
                            regionNestedIndex! -> if { "%v$(regionNodeIndex!)_nested_value" -> println } else {
                                (regionIndexValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionIndexValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> println } else { "%v$(regionNode.operand1)" -> println }
                            }
                        }
                    }
                    "  %v$(regionNodeIndex!) = load " -> print
                    regionNode -> writeIrType
                    ", ptr %v$(regionNodeIndex!)_ptr, align " -> print
                    regionNode -> storageAlign -> writeDecimalLine
                }
            }
            (regionNode.kind == 7 or regionNode.kind == 8) -> if {
                context.ir[regionNode.operand0] => regionLeft
                0 => regionBinaryWidth!
                false => regionLeftPromoted!
                false => regionRightPromoted!
                regionNode.kind == 8 -> if {
                    context.ir[regionNode.operand1] => regionRight
                    regionLeft -> integerNodeWidth => regionLeftWidth!
                    regionRight -> integerNodeWidth => regionRightWidth!
                    (regionLeftWidth! > 0 and regionRightWidth! > 0) -> if {
                        regionLeftWidth! > regionRightWidth! -> if { regionLeftWidth! => regionBinaryWidth! } else { regionRightWidth! => regionBinaryWidth! }
                        IntegerPromotionRequest { expressionIndex: regionNodeIndex!, operandIndex: regionNode.operand0, targetWidth: regionBinaryWidth!, ownerIndex: ownerIndex!, right: false } -> emitIntegerPromotion => regionLeftPromoted!
                        IntegerPromotionRequest { expressionIndex: regionNodeIndex!, operandIndex: regionNode.operand1, targetWidth: regionBinaryWidth!, ownerIndex: ownerIndex!, right: true } -> emitIntegerPromotion => regionRightPromoted!
                    }
                }
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
                    (regionNode.opcode == grammar.tokenIdLess or regionNode.opcode == grammar.tokenIdLessEqual or regionNode.opcode == grammar.tokenIdGreater or regionNode.opcode == grammar.tokenIdGreaterEqual) -> if {
                        regionNode -> integerComparisonOperation => regionOperation!
                    }
                    regionNode.opcode == -24 -> if { "or" => regionOperation! }
                    regionNode.opcode == -25 -> if { "and" => regionOperation! }
                }
                "  %v$(regionNodeIndex!) = $(regionOperation!) " -> print
                regionBinaryWidth! > 0 -> if { "i$(regionBinaryWidth!)" -> print } else { regionLeft -> writeIrType }
                " " -> print
                (regionNode.kind == 7 and regionNode.opcode != -26) -> if { "0" -> print } else {
                    (regionLeft.kind == 3 or regionLeft.kind == 4) -> if {
                        regionLeft -> sourceToken => regionLeftToken
                        regionLeft.kind == 3 -> if { context.sources[regionLeft.sourceModule] -> slice(regionLeftToken.span.start, regionLeftToken.span.length) -> print } else {
                            ((context.sources[regionLeft.sourceModule] -> byte(regionLeftToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                        }
                    } else {
                        regionLeftPromoted! -> if { "%v$(regionNodeIndex!)_left_wide" -> print } else {
                            (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionLeft.kind == 5 and regionLeft.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                        }
                    }
                }
                regionNode.kind == 7 -> if {
                    regionNode.opcode == -26 -> if { ", true" -> println } else {
                        ", " -> print
                        (regionLeft.kind == 3 or regionLeft.kind == 4) -> if {
                            regionLeft -> sourceToken => regionUnaryToken
                            context.sources[regionLeft.sourceModule] -> slice(regionUnaryToken.span.start, regionUnaryToken.span.length) -> println
                        } else { "%v$(regionNode.operand0)" -> println }
                    }
                } else {
                    ", " -> print
                    context.ir[regionNode.operand1] => regionRight
                    (regionRight.kind == 3 or regionRight.kind == 4) -> if {
                        regionRight -> sourceToken => regionRightToken
                        regionRight.kind == 3 -> if { context.sources[regionRight.sourceModule] -> slice(regionRightToken.span.start, regionRightToken.span.length) -> println } else {
                            ((context.sources[regionRight.sourceModule] -> byte(regionRightToken.span.start)) == UInt8(116)) -> if { "1" -> println } else { "0" -> println }
                        }
                    } else {
                        regionRightPromoted! -> if { "%v$(regionNodeIndex!)_right_wide" -> println } else {
                            (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionRight.kind == 5 and regionRight.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> println } else { "%v$(regionNode.operand1)" -> println }
                        }
                    }
                }
            }
            (regionNode.kind == 6 and regionNode.opcode != -205) -> if {
                (regionNode.symbol == -101 or regionNode.symbol == -102) -> if {
                    context.ir[regionNode.operand0] => regionArgument
                    regionArgument.kind == 2 -> if {
                        regionArgument -> sourceToken => regionArgumentToken
                        Int(TextLiteralRequest { sourceModule: regionArgument.sourceModule, token: regionArgumentToken } -> textLiteralLength) => regionArgumentLength
                        regionArgument -> sourceInterpolations => regionExpressionInterpolation!
                        false => regionHasExpressionInterpolation!
                        0 => regionExpressionSearch!
                        regionExpressionSearch! < (regionExpressionInterpolation! -> len) -> while {
                            (regionExpressionInterpolation![regionExpressionSearch!].sourceToken == regionArgument.payloadToken and regionExpressionInterpolation![regionExpressionSearch!].parent < 0) -> if { true => regionHasExpressionInterpolation! }
                            regionExpressionSearch! + 1 => regionExpressionSearch!
                        }
                        regionHasExpressionInterpolation! -> if {
                            ownerIndex! + 1 => regionOwnerStart
                            regionOwnerStart => regionOwnerEnd!
                            (regionOwnerEnd! < (context.ir -> len) and context.ir[regionOwnerEnd!].kind != 0 and context.ir[regionOwnerEnd!].kind != 11) -> while {
                                regionOwnerEnd! + 1 => regionOwnerEnd!
                            }
                            0 => regionExpressionSegment!
                            UIntSize(0) => regionExpressionSuffixStart!
                            0 => regionExpressionRootSearch!
                            regionExpressionRootSearch! < (regionExpressionInterpolation! -> len) -> while {
                                regionExpressionInterpolation![regionExpressionRootSearch!] => regionExpressionRoot
                                (regionExpressionRoot.sourceToken == regionArgument.payloadToken and regionExpressionRoot.parent < 0) -> if {
                                    "  %v$(regionNodeIndex!)_expression_literal$(regionExpressionSegment!) = getelementptr i8, ptr @sl_str_$(regionNode.operand0), i64 $(regionExpressionRoot.literalStart)" -> println
                                    "  call void @sl_runtime_print(ptr %v$(regionNodeIndex!)_expression_literal$(regionExpressionSegment!), i64 $(regionExpressionRoot.literalLength), i1 false)" -> println
                                    regionExpressionRoot.typeSymbol => regionExpressionRootTypeSymbol!
                                    -1 => regionExpressionRootBinding!
                                    regionExpressionRoot.kind == 1 -> if {
                                        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and context.ir[context.ir[ownerIndex!].operand1].symbol == regionExpressionRoot.symbol) -> if {
                                            context.ir[context.ir[ownerIndex!].operand1].typeSymbol => regionExpressionRootTypeSymbol!
                                        } else {
                                            regionOwnerStart => regionExpressionBindingSearch!
                                            regionExpressionBindingSearch! < regionOwnerEnd! -> while {
                                                (context.ir[regionExpressionBindingSearch!].kind == 17 and context.ir[regionExpressionBindingSearch!].symbol == regionExpressionRoot.symbol) -> if { regionExpressionBindingSearch! => regionExpressionRootBinding! }
                                                regionExpressionBindingSearch! + 1 => regionExpressionBindingSearch!
                                            }
                                            regionExpressionRootBinding! >= 0 -> if { context.ir[regionExpressionRootBinding!].typeSymbol => regionExpressionRootTypeSymbol! }
                                        }
                                    }
                                    false => regionExpressionRootMutable!
                                    regionExpressionRootBinding! >= 0 -> if {
                                        context.ir[regionExpressionRootBinding!] => regionExpressionRootBindingNode
                                        regionExpressionRootBindingNode.flags == 1 -> if {
                                            regionExpressionRootBinding! -> mutableBindingRoot => regionExpressionRootMutableRoot
                                            "  %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_current = load " -> print
                                            regionExpressionRootBindingNode -> writeIrType
                                            ", ptr %slot$(regionExpressionRootMutableRoot), align " -> print
                                            regionExpressionRootBindingNode -> storageAlign -> writeDecimalLine
                                            true => regionExpressionRootMutable!
                                        }
                                    }
                                    false => regionExpressionRootMember!
                                    regionExpressionRoot.kind == 5 -> if {
                                        regionExpressionInterpolation![regionExpressionRoot.operand0] => regionExpressionMemberBase
                                        -1 => regionExpressionMemberBaseIr!
                                        -1 => regionExpressionMemberBinding!
                                        false => regionExpressionMemberBaseParameter!
                                        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and context.ir[context.ir[ownerIndex!].operand1].symbol == regionExpressionMemberBase.symbol) -> if {
                                            context.ir[ownerIndex!].operand1 => regionExpressionMemberBaseIr!
                                            true => regionExpressionMemberBaseParameter!
                                        } else {
                                            regionOwnerStart => regionExpressionMemberBindingSearch!
                                            regionExpressionMemberBindingSearch! < regionOwnerEnd! -> while {
                                                (context.ir[regionExpressionMemberBindingSearch!].kind == 17 and context.ir[regionExpressionMemberBindingSearch!].symbol == regionExpressionMemberBase.symbol) -> if {
                                                    regionExpressionMemberBindingSearch! => regionExpressionMemberBinding!
                                                    context.ir[regionExpressionMemberBindingSearch!].operand0 => regionExpressionMemberBaseIr!
                                                }
                                                regionExpressionMemberBindingSearch! + 1 => regionExpressionMemberBindingSearch!
                                            }
                                        }
                                        regionExpressionMemberBaseIr! >= 0 -> if {
                                            context.ir[regionExpressionMemberBaseIr!] => regionExpressionMemberBaseValue
                                            false => regionExpressionMemberBaseMutable!
                                            regionExpressionMemberBinding! >= 0 -> if {
                                                context.ir[regionExpressionMemberBinding!] => regionExpressionMemberBindingNode
                                                regionExpressionMemberBindingNode.flags == 1 -> if {
                                                    regionExpressionMemberBinding! -> mutableBindingRoot => regionExpressionMemberMutableRoot
                                                    "  %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_member_current = load " -> print
                                                    regionExpressionMemberBaseValue -> writeIrType
                                                    ", ptr %slot$(regionExpressionMemberMutableRoot), align " -> print
                                                    regionExpressionMemberBindingNode -> storageAlign -> writeDecimalLine
                                                    true => regionExpressionMemberBaseMutable!
                                                }
                                            }
                                            regionExpressionMemberBaseValue.typeModule => regionExpressionMemberOwnerSource!
                                            regionExpressionMemberBaseValue.typeOrigin == 2 -> if {
                                                context.modules[regionExpressionMemberBaseValue.typeModule].sourceIndex => regionExpressionMemberOwnerSource!
                                            }
                                            context.ranges[regionExpressionMemberOwnerSource!] => regionExpressionMemberOwnerRange
                                            regionExpressionRoot.payloadStart => regionExpressionMemberDot!
                                            regionExpressionRoot.payloadStart + regionExpressionRoot.payloadLength => regionExpressionMemberEnd
                                            (regionExpressionMemberDot! < regionExpressionMemberEnd and (context.sources[regionArgument.sourceModule] -> byte(regionExpressionMemberDot!)) != UInt8(46)) -> while {
                                                regionExpressionMemberDot! + UIntSize(1) => regionExpressionMemberDot!
                                            }
                                            regionExpressionMemberDot! + UIntSize(1) => regionExpressionMemberNameStart
                                            regionExpressionMemberEnd - regionExpressionMemberNameStart => regionExpressionMemberNameLength
                                            -1 => regionExpressionMemberFieldOrdinal!
                                            -1 => regionExpressionMemberFieldTypeAst!
                                            0 => regionExpressionMemberCandidateOrdinal!
                                            0 => regionExpressionMemberFieldSearch!
                                            regionExpressionMemberFieldSearch! < regionExpressionMemberOwnerRange.symbolCount -> while {
                                                context.symbols[regionExpressionMemberOwnerRange.symbolStart + regionExpressionMemberFieldSearch!] => regionExpressionMemberField
                                                (regionExpressionMemberField.kind == 26 and regionExpressionMemberField.parent == regionExpressionMemberBaseValue.typeSymbol) -> if {
                                                    context.tokens[regionExpressionMemberOwnerRange.tokenStart + regionExpressionMemberField.nameToken] => regionExpressionMemberFieldName
                                                    regionExpressionMemberFieldName.span.length == regionExpressionMemberNameLength => regionExpressionMemberNameEqual!
                                                    UIntSize(0) => regionExpressionMemberNameByte!
                                                    (regionExpressionMemberNameEqual! and regionExpressionMemberNameByte! < regionExpressionMemberNameLength) -> while {
                                                        (context.sources[regionArgument.sourceModule] -> byte(regionExpressionMemberNameStart + regionExpressionMemberNameByte!)) != (context.sources[regionExpressionMemberOwnerSource!] -> byte(regionExpressionMemberFieldName.span.start + regionExpressionMemberNameByte!)) -> if { false => regionExpressionMemberNameEqual! }
                                                        regionExpressionMemberNameByte! + UIntSize(1) => regionExpressionMemberNameByte!
                                                    }
                                                    regionExpressionMemberNameEqual! -> if {
                                                        regionExpressionMemberCandidateOrdinal! => regionExpressionMemberFieldOrdinal!
                                                        regionExpressionMemberField.typeNode => regionExpressionMemberFieldTypeAst!
                                                    }
                                                    regionExpressionMemberCandidateOrdinal! + 1 => regionExpressionMemberCandidateOrdinal!
                                                }
                                                regionExpressionMemberFieldSearch! + 1 => regionExpressionMemberFieldSearch!
                                            }
                                            regionExpressionMemberFieldTypeAst! >= 0 -> if {
                                                0 => regionExpressionMemberTypeSearch!
                                                regionExpressionMemberTypeSearch! < (context.nominal -> len) -> while {
                                                    context.nominal[regionExpressionMemberTypeSearch!] => regionExpressionMemberType
                                                    (regionExpressionMemberType.sourceModule == regionExpressionMemberOwnerSource! and regionExpressionMemberType.typeAst == regionExpressionMemberFieldTypeAst! and regionExpressionMemberType.status == 0) -> if {
                                                        regionExpressionMemberType.targetSymbol => regionExpressionRootTypeSymbol!
                                                    }
                                                    regionExpressionMemberTypeSearch! + 1 => regionExpressionMemberTypeSearch!
                                                }
                                            }
                                            regionExpressionMemberFieldOrdinal! >= 0 -> if {
                                                "  %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!) = extractvalue " -> print
                                                regionExpressionMemberBaseValue -> writeIrType
                                                " " -> print
                                                regionExpressionMemberBaseParameter! -> if { "%arg" -> print } else {
                                                    regionExpressionMemberBaseMutable! -> if { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_member_current" -> print } else { "%v$(regionExpressionMemberBaseIr!)" -> print }
                                                }
                                                ", $(regionExpressionMemberFieldOrdinal!)" -> println
                                                true => regionExpressionRootMember!
                                            }
                                        }
                                    }
                                    regionExpressionRootTypeSymbol! -> integerWidth => regionExpressionRootWidth!
                                    (regionExpressionRootBinding! >= 0 and not regionExpressionRootMutable! and context.ir[regionExpressionRootBinding!].operand0 >= 0) -> if {
                                        context.ir[context.ir[regionExpressionRootBinding!].operand0] -> integerNodeWidth => regionExpressionBoundWidth
                                        regionExpressionBoundWidth > 0 -> if { regionExpressionBoundWidth => regionExpressionRootWidth! }
                                    }
                                    regionExpressionRootTypeSymbol! == 1 -> if {
                                        "  %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_text_ptr = extractvalue %sl.text " -> print
                                        regionExpressionRoot.kind == 5 -> if {
                                            "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)" -> print
                                        } else {
                                            (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and context.ir[context.ir[ownerIndex!].operand1].symbol == regionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                regionExpressionRootMutable! -> if { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_current" -> print } else {
                                                    "%v" -> print
                                                    context.ir[regionExpressionRootBinding!].operand0 -> writeDecimal
                                                }
                                            }
                                        }
                                        ", 0" -> println
                                        "  %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_text_len = extractvalue %sl.text " -> print
                                        regionExpressionRoot.kind == 5 -> if {
                                            "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)" -> print
                                        } else {
                                            (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and context.ir[context.ir[ownerIndex!].operand1].symbol == regionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                regionExpressionRootMutable! -> if { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_current" -> print } else {
                                                    "%v" -> print
                                                    context.ir[regionExpressionRootBinding!].operand0 -> writeDecimal
                                                }
                                            }
                                        }
                                        ", 1" -> println
                                        "  call void @sl_runtime_print(ptr %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_text_ptr, i64 %v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_text_len, i1 false)" -> println
                                    } else {
                                        regionExpressionRootTypeSymbol! == 23 -> if { "  call void @sl_runtime_print_i1(i1 " -> print } else {
                                            regionExpressionRootWidth! == 64 -> if { "  call void @sl_runtime_print_i64(i64 " -> print } else { "  call void @sl_runtime_print_i32(i32 " -> print }
                                        }
                                        (regionExpressionRoot.kind == 0 or regionExpressionRoot.kind == 4) -> if {
                                            regionExpressionRoot.kind == 4 -> if {
                                                (context.sources[regionArgument.sourceModule] -> byte(regionExpressionRoot.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                            } else { context.sources[regionArgument.sourceModule] -> slice(regionExpressionRoot.payloadStart, regionExpressionRoot.payloadLength) -> print }
                                        } else {
                                            regionExpressionRoot.kind == 5 -> if {
                                                regionExpressionRootMember! -> if { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)" -> print }
                                            } else {
                                                regionExpressionRoot.kind == 1 -> if {
                                                    (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and context.ir[context.ir[ownerIndex!].operand1].symbol == regionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                        regionExpressionRootBinding! >= 0 -> if {
                                                            context.ir[context.ir[regionExpressionRootBinding!].operand0] => regionExpressionRootValue
                                                            regionExpressionRootMutable! -> if { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)_current" -> print } else {
                                                            (regionExpressionRootValue.kind == 3 or regionExpressionRootValue.kind == 4) -> if {
                                                                regionExpressionRootValue -> sourceToken => regionExpressionRootToken
                                                                regionExpressionRootValue.kind == 4 -> if {
                                                                    (context.sources[regionExpressionRootValue.sourceModule] -> byte(regionExpressionRootToken.span.start)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                                } else { context.sources[regionExpressionRootValue.sourceModule] -> slice(regionExpressionRootToken.span.start, regionExpressionRootToken.span.length) -> print }
                                                            } else {
                                                                "%v" -> print
                                                                context.ir[regionExpressionRootBinding!].operand0 -> writeDecimal
                                                            }
                                                            }
                                                        }
                                                    }
                                                } else { "%v$(regionNodeIndex!)_expression$(regionExpressionRootSearch!)" -> print }
                                            }
                                        }
                                        regionExpressionRootWidth! == 64 -> if {
                                            ", i1 " -> print
                                            regionExpressionRootTypeSymbol! -> integerSigned -> if { "true" -> print } else { "false" -> print }
                                        }
                                        ", i1 false)" -> println
                                    }
                                    regionExpressionRoot.expressionStart + regionExpressionRoot.expressionLength + UIntSize(1) => regionExpressionSuffixStart!
                                    regionExpressionSegment! + 1 => regionExpressionSegment!
                                }
                                regionExpressionRootSearch! + 1 => regionExpressionRootSearch!
                            }
                            UIntSize(regionArgumentLength) - regionExpressionSuffixStart! => regionExpressionSuffixLength
                            "  %v$(regionNodeIndex!)_expression_suffix = getelementptr i8, ptr @sl_str_$(regionNode.operand0), i64 $(regionExpressionSuffixStart!)" -> println
                            "  call void @sl_runtime_print(ptr %v$(regionNodeIndex!)_expression_suffix, i64 $regionExpressionSuffixLength, i1 " -> print
                        } else {
                            "  call void @sl_runtime_print(ptr @sl_str_$(regionNode.operand0), i64 $regionArgumentLength, i1 " -> print
                        }
                        regionNode.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                    } else {
                        (regionNode.opcode == -203 or (regionArgument.typeOrigin == 1 and regionArgument.typeSymbol == 1)) -> if {
                            "  %v$(regionNodeIndex!)_runtime_ptr = extractvalue %sl.text " -> print
                            regionNode.opcode == -203 -> if { "%v$(regionNodeIndex!)" -> print } else {
                                (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionArgument.kind == 5 and regionArgument.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                            }
                            ", 0" -> println
                            "  %v$(regionNodeIndex!)_runtime_len = extractvalue %sl.text " -> print
                            regionNode.opcode == -203 -> if { "%v$(regionNodeIndex!)" -> print } else {
                                (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionArgument.kind == 5 and regionArgument.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                            }
                            ", 1" -> println
                            "  call void @sl_runtime_print(ptr %v$(regionNodeIndex!)_runtime_ptr, i64 %v$(regionNodeIndex!)_runtime_len, i1 " -> print
                            regionNode.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                        } else {
                        (regionArgument.typeOrigin == 1 and regionArgument.typeSymbol == 2) -> if {
                            "  call void @sl_runtime_print_i32(i32 " -> print
                            regionArgument.kind == 3 -> if {
                                regionArgument -> sourceToken => regionIntegerPrintToken
                                context.sources[regionArgument.sourceModule] -> slice(regionIntegerPrintToken.span.start, regionIntegerPrintToken.span.length) -> print
                            } else {
                                (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionArgument.kind == 5 and regionArgument.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                            }
                            regionNode.symbol == -102 -> if { ", i1 true)" -> println } else { ", i1 false)" -> println }
                        }
                        }
                    }
                } else {
                (regionNode.targetModule < 0 and regionNode.typeOrigin == 1 and (regionNode.typeSymbol -> integerWidth) > 0 and regionNode.operand0 >= 0) -> if {
                    context.ir[regionNode.operand0] => regionIntegerArgument
                    regionNode.typeSymbol -> integerWidth => regionTargetWidth
                    regionIntegerArgument.typeSymbol -> integerWidth => regionInputWidth
                    "  %v$(regionNodeIndex!) = " -> print
                    regionTargetWidth < regionInputWidth -> if { "trunc " -> print } else {
                        regionTargetWidth > regionInputWidth -> if {
                            regionIntegerArgument -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                        } else { "add " -> print }
                    }
                    regionIntegerArgument -> writeIrType
                    " " -> print
                    regionIntegerArgument.kind == 3 -> if {
                        regionIntegerArgument -> sourceToken => regionIntegerToken
                        context.sources[regionIntegerArgument.sourceModule] -> slice(regionIntegerToken.span.start, regionIntegerToken.span.length) -> print
                    } else {
                        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and regionIntegerArgument.kind == 5 and regionIntegerArgument.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(regionNode.operand0)" -> print }
                    }
                    regionTargetWidth == regionInputWidth -> if { ", 0" -> println } else {
                        " to " -> print
                        regionNode -> writeIrType
                        "" -> println
                    }
                } else {
                    CallEnvironmentRequest { callerIndex: ownerIndex!, callIndex: regionNodeIndex!, targetModule: regionNode.targetModule, targetSymbol: regionNode.symbol, hasArgument: regionNode.operand0 >= 0 } -> emitCallEnvironmentValues
                    (regionNode.typeOrigin == 1 and regionNode.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(regionNodeIndex!) = call " -> print }
                    regionNode -> writeIrType
                    " @sl_m$(regionNode.targetModule)_s$(regionNode.symbol)(" -> print
                    CallEnvironmentRequest { callerIndex: ownerIndex!, callIndex: regionNodeIndex!, targetModule: regionNode.targetModule, targetSymbol: regionNode.symbol, hasArgument: regionNode.operand0 >= 0 } -> emitCallEnvironment => regionCallEnvironment
                    regionNode.operand0 >= 0 -> if {
                        context.ir[regionNode.operand0] => regionCallArgument
                        regionCallArgument -> writeIrType
                        " " -> print
                        (regionCallArgument.kind == 3 or regionCallArgument.kind == 4) -> if {
                            regionCallArgument -> sourceToken => regionCallToken
                            regionCallArgument.kind == 3 -> if { context.sources[regionCallArgument.sourceModule] -> slice(regionCallToken.span.start, regionCallToken.span.length) -> print } else {
                                ((context.sources[regionCallArgument.sourceModule] -> byte(regionCallToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
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
            }
            regionEventKind == 2 -> if {
                not regionTerminated! -> if {
                true => ifActive![regionNodeIndex!]
                regionNode.operand0 => nestedConditionIndex!
                true => nestedConditionUnwrap!
                (nestedConditionIndex! >= 0 and nestedConditionUnwrap! and context.ir[nestedConditionIndex!].kind == 9) -> while {
                    -1 => nestedConditionChild!
                    UIntSize(0) => nestedConditionChildStart!
                    0 => nestedConditionChildSearch!
                    nestedConditionChildSearch! < (context.ir -> len) -> while {
                        context.ir[nestedConditionChildSearch!].parent == nestedConditionIndex! -> if {
                            context.ir[nestedConditionChildSearch!] -> sourceNode => nestedConditionChildAst
                            (nestedConditionChild! < 0 or nestedConditionChildAst.start > nestedConditionChildStart!) -> if {
                                nestedConditionChildSearch! => nestedConditionChild!
                                nestedConditionChildAst.start => nestedConditionChildStart!
                            }
                        }
                        nestedConditionChildSearch! + 1 => nestedConditionChildSearch!
                    }
                    nestedConditionChild! >= 0 -> if { nestedConditionChild! => nestedConditionIndex! } else { false => nestedConditionUnwrap! }
                }
                context.ir[nestedConditionIndex!] => nestedCondition
                (regionNode.typeSymbol != 0 and regionNode.nextOperand >= 0) -> if {
                    "  %if$(regionNodeIndex!)_result = alloca " -> print
                    regionNode -> writeIrType
                    ", align " -> print
                    regionNode -> storageAlign -> writeDecimalLine
                }
                (nestedCondition.kind == 8 and (nestedCondition.opcode == -25 or nestedCondition.opcode == -24) or (nestedCondition.kind == 7 and nestedCondition.opcode == -26)) => nestedShortCircuit
                nestedShortCircuit -> if {
                    WhileBranchRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex!, conditionIndex: nestedConditionIndex! } -> emitWhileBranch
                    "while$(regionNodeIndex!)_body:" -> println
                    "  br label %if$(regionNodeIndex!)_then" -> println
                    "while$(regionNodeIndex!)_exit:" -> println
                    regionNode.nextOperand >= 0 -> if { "  br label %if$(regionNodeIndex!)_else" -> println } else { "  br label %if$(regionNodeIndex!)_merge" -> println }
                } else {
                    "  br i1 " -> print
                    nestedCondition.kind == 4 -> if {
                        nestedCondition -> sourceToken => nestedConditionToken
                        ((context.sources[nestedCondition.sourceModule] -> byte(nestedConditionToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                    } else {
                        (ownerIndex! >= 0 and context.ir[ownerIndex!].kind == 0 and context.ir[ownerIndex!].operand1 >= 0 and nestedCondition.kind == 5 and nestedCondition.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(nestedConditionIndex!)" -> print }
                    }
                    regionNode.nextOperand >= 0 -> if { ", label %if$(regionNodeIndex!)_then, label %if$(regionNodeIndex!)_else" -> println } else { ", label %if$(regionNodeIndex!)_then, label %if$(regionNodeIndex!)_merge" -> println }
                }
                "if$(regionNodeIndex!)_then:" -> println
                false => regionTerminated!
                }
            }
            regionEventKind == 3 -> if {
                ifActive![regionNodeIndex!] -> if {
                    not regionTerminated! -> if {
                        (regionNode.typeSymbol != 0 and regionNode.nextOperand >= 0) -> if {
                            regionNode.operand1 -> regionResultValueIndex => nestedThenStoreIndex
                            context.ir[nestedThenStoreIndex] => nestedThenStoreValue
                            "  store " -> print
                            regionNode -> writeIrType
                            " " -> print
                            (nestedThenStoreValue.kind == 3 or nestedThenStoreValue.kind == 4) -> if {
                                nestedThenStoreValue -> sourceToken => nestedThenStoreToken
                                nestedThenStoreValue.kind == 3 -> if { context.sources[nestedThenStoreValue.sourceModule] -> slice(nestedThenStoreToken.span.start, nestedThenStoreToken.span.length) -> print } else {
                                    ((context.sources[nestedThenStoreValue.sourceModule] -> byte(nestedThenStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else {
                                (nestedThenStoreValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and nestedThenStoreValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(nestedThenStoreIndex)" -> print }
                            }
                            ", ptr %if$(regionNodeIndex!)_result, align " -> print
                            regionNode -> storageAlign -> writeDecimalLine
                        }
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
                        regionNode.typeSymbol != 0 -> if {
                            regionNode.nextOperand -> regionResultValueIndex => nestedElseStoreIndex
                            context.ir[nestedElseStoreIndex] => nestedElseStoreValue
                            "  store " -> print
                            regionNode -> writeIrType
                            " " -> print
                            (nestedElseStoreValue.kind == 3 or nestedElseStoreValue.kind == 4) -> if {
                                nestedElseStoreValue -> sourceToken => nestedElseStoreToken
                                nestedElseStoreValue.kind == 3 -> if { context.sources[nestedElseStoreValue.sourceModule] -> slice(nestedElseStoreToken.span.start, nestedElseStoreToken.span.length) -> print } else {
                                    ((context.sources[nestedElseStoreValue.sourceModule] -> byte(nestedElseStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else {
                                (nestedElseStoreValue.kind == 5 and ownerIndex! >= 0 and context.ir[ownerIndex!].operand1 >= 0 and nestedElseStoreValue.symbol == context.ir[context.ir[ownerIndex!].operand1].symbol) -> if { "%arg" -> print } else { "%v$(nestedElseStoreIndex)" -> print }
                            }
                            ", ptr %if$(regionNodeIndex!)_result, align " -> print
                            regionNode -> storageAlign -> writeDecimalLine
                        }
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
                    (regionNode.typeSymbol != 0 and regionNode.nextOperand >= 0) -> if {
                        "  %v$(regionNodeIndex!) = load " -> print
                        regionNode -> writeIrType
                        ", ptr %if$(regionNodeIndex!)_result, align " -> print
                        regionNode -> storageAlign -> writeDecimalLine
                    }
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
                    WhileBranchRequest { whileIndex: regionNodeIndex!, ownerIndex: ownerIndex!, conditionIndex: regionNode.operand0 } -> emitWhileBranch
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
    -1 => intrinsicRuntimeModule!
    0 => intrinsicRuntimeSearch!
    intrinsicRuntimeSearch! < (context.modules -> len) -> while {
        context.modules[intrinsicRuntimeSearch!] => intrinsicModuleIdentity
        intrinsicModuleIdentity.pathHash == UInt64(12254170256457402476) -> if { intrinsicModuleIdentity.sourceIndex => intrinsicRuntimeModule! }
        intrinsicRuntimeSearch! + 1 => intrinsicRuntimeSearch!
    }
    false => usesSourceText!
    0 => sourceTextTypeSearch!
    sourceTextTypeSearch! < (context.ir -> len) -> while {
        context.ir[sourceTextTypeSearch!] -> isSourceTextType -> if { true => usesSourceText! }
        sourceTextTypeSearch! + 1 => sourceTextTypeSearch!
    }
    usesSourceText! -> if { "%sl.source_text = type { ptr, i64, ptr, i64 }" -> println }
    0 => structTypeIndex!
    structTypeIndex! < (context.types -> len) -> while {
        context.types[structTypeIndex!] => structType
        (structType.status == 0 and structType.kind == 1 and (structType.origin == 0 or structType.origin == 2)) -> if {
            "%sl.struct.m$(structType.module)_s$(structType.symbol) = type { " -> print
            true => firstField!
            0 => canonicalFieldIndex!
            canonicalFieldIndex! < (context.fields -> len) -> while {
                context.fields[canonicalFieldIndex!] => canonicalField
                (canonicalField.status == 0 and canonicalField.ownerType == structTypeIndex!) -> if {
                    not firstField! -> if { ", " -> print }
                    canonicalField.fieldType -> writeSemanticTypeId
                    false => firstField!
                }
                canonicalFieldIndex! + 1 => canonicalFieldIndex!
            }
            " }" -> println
        }
        structTypeIndex! + 1 => structTypeIndex!
    }
    false => usesDynamicArray!
    false => usesArrayPush!
    0 => arrayTypeSearch!
    arrayTypeSearch! < (context.ir -> len) -> while {
        context.ir[arrayTypeSearch!] -> isDynamicArrayType -> if { true => usesDynamicArray! }
        context.ir[arrayTypeSearch!].opcode == -204 -> if { true => usesArrayPush! }
        arrayTypeSearch! + 1 => arrayTypeSearch!
    }
    0 => arrayCompositeSearch!
    arrayCompositeSearch! < (context.composite -> len) -> while {
        context.composite[arrayCompositeSearch!].kind == 3 -> if { true => usesDynamicArray! }
        arrayCompositeSearch! + 1 => arrayCompositeSearch!
    }
    usesDynamicArray! -> if {
        "%sl.array.i32 = type { ptr, i64, i64 }" -> println
    }
    (usesDynamicArray! or usesSourceText! or intrinsicRuntimeModule! >= 0) -> if {
        "declare ptr @malloc(i64)" -> println
        usesArrayPush! -> if { "declare ptr @realloc(ptr, i64)" -> println }
        "declare void @free(ptr)" -> println
    }
    false => usesDictionary!
    0 => dictionaryTypeSearch!
    dictionaryTypeSearch! < (context.ir -> len) -> while {
        context.ir[dictionaryTypeSearch!] -> isDictionaryType -> if { true => usesDictionary! }
        dictionaryTypeSearch! + 1 => dictionaryTypeSearch!
    }
    0 => dictionaryCompositeSearch!
    dictionaryCompositeSearch! < (context.composite -> len) -> while {
        context.composite[dictionaryCompositeSearch!].kind == 5 -> if { true => usesDictionary! }
        dictionaryCompositeSearch! + 1 => dictionaryCompositeSearch!
    }
    usesDictionary! -> if {
        "%sl.dict = type { ptr, ptr, i64, i64 }" -> println
        intrinsicRuntimeModule! < 0 -> if { "declare void @llvm.trap()" -> println }
        (not usesDynamicArray! and intrinsicRuntimeModule! < 0) -> if {
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
    usesSourceText! -> if { true => usesText! }
    usesText! -> if {
        "%sl.text = type { ptr, i64 }" -> println
        0 => textGlobalIndex!
        textGlobalIndex! < (context.ir -> len) -> while {
            context.ir[textGlobalIndex!] => textConstant
            textConstant.kind == 2 -> if {
                textConstant -> sourceToken => textToken
                TextLiteralRequest { sourceModule: textConstant.sourceModule, token: textToken } => textLiteralRequest
                textLiteralRequest -> textLiteralLength => textLength
                "@sl_str_$(textGlobalIndex!) = private unnamed_addr constant [$textLength x i8] " -> print
                textLiteralRequest -> emitTextLiteralConstant
            }
            textGlobalIndex! + 1 => textGlobalIndex!
        }
    }
    false => usesArguments!
    0 => argumentsTypeSearch!
    argumentsTypeSearch! < (context.ir -> len) -> while {
        (context.ir[argumentsTypeSearch!].typeOrigin == 1 and context.ir[argumentsTypeSearch!].typeSymbol == 16) -> if { true => usesArguments! }
        argumentsTypeSearch! + 1 => argumentsTypeSearch!
    }
    usesArguments! -> if {
        "@sl_argc = internal global i64 0" -> println
        "@sl_argv = internal global ptr null" -> println
        "define i64 @sl_argument_count() {" -> println
        "entry:" -> println
        "  %count = load i64, ptr @sl_argc, align 8" -> println
        "  ret i64 %count" -> println
        "}" -> println
        "define %sl.text @sl_argument(i64 %index) {" -> println
        "entry:" -> println
        "  %argv = load ptr, ptr @sl_argv, align 8" -> println
        "  %slot = getelementptr ptr, ptr %argv, i64 %index" -> println
        "  %value = load ptr, ptr %slot, align 8" -> println
        "  br label %length_loop" -> println
        "length_loop:" -> println
        "  %length = phi i64 [ 0, %entry ], [ %next, %length_body ]" -> println
        "  %byte_ptr = getelementptr i8, ptr %value, i64 %length" -> println
        "  %byte = load i8, ptr %byte_ptr, align 1" -> println
        "  %done = icmp eq i8 %byte, 0" -> println
        "  br i1 %done, label %length_done, label %length_body" -> println
        "length_body:" -> println
        "  %next = add i64 %length, 1" -> println
        "  br label %length_loop" -> println
        "length_done:" -> println
        "  %text_ptr = insertvalue %sl.text poison, ptr %value, 0" -> println
        "  %text = insertvalue %sl.text %text_ptr, i64 %length, 1" -> println
        "  ret %sl.text %text" -> println
        "}" -> println
    }
    [Int; ~] => declaredExternalModules!
    [Int; ~] => declaredExternalSymbols!
    0 => externalCallIndex!
    externalCallIndex! < (context.ir -> len) -> while {
        context.ir[externalCallIndex!] => externalCall
        (externalCall.kind == 6 and externalCall.opcode != -205 and externalCall.targetModule >= 0 and externalCall.symbol >= 0) -> if {
            false => externalDefined!
            0 => externalDefinitionSearch!
            externalDefinitionSearch! < (context.ir -> len) -> while {
                (context.ir[externalDefinitionSearch!].kind == 0 and context.ir[externalDefinitionSearch!].sourceModule == externalCall.targetModule and context.ir[externalDefinitionSearch!].symbol == externalCall.symbol) -> if { true => externalDefined! }
                externalDefinitionSearch! + 1 => externalDefinitionSearch!
            }
            false => externalDeclared!
            0 => externalDeclaredSearch!
            externalDeclaredSearch! < (declaredExternalModules! -> len) -> while {
                (declaredExternalModules![externalDeclaredSearch!] == externalCall.targetModule and declaredExternalSymbols![externalDeclaredSearch!] == externalCall.symbol) -> if { true => externalDeclared! }
                externalDeclaredSearch! + 1 => externalDeclaredSearch!
            }
            (externalCall.targetModule != intrinsicRuntimeModule! and not externalDefined! and not externalDeclared!) -> if {
                "declare " -> print
                externalCall -> writeIrType
                " @sl_m$(externalCall.targetModule)_s$(externalCall.symbol)(" -> print
                externalCall.operand0 >= 0 -> if { context.ir[externalCall.operand0] -> writeIrType }
                ")" -> println
                declaredExternalModules! -> push(externalCall.targetModule)
                declaredExternalSymbols! -> push(externalCall.symbol)
            }
        }
        externalCallIndex! + 1 => externalCallIndex!
    }
    0 => functionIndex!
    functionIndex! < (context.ir -> len) -> while {
        context.ir[functionIndex!] => function
        function.kind == 0 -> if {
            functionIndex! + 1 => functionEnd!
            (functionEnd! < (context.ir -> len) and context.ir[functionEnd!].kind != 0 and context.ir[functionEnd!].kind != 11) -> while {
                functionEnd! + 1 => functionEnd!
            }
            functionIndex! -> functionCaptures => currentFunctionCaptures!
            "define " -> print
            function -> writeIrType
            " @sl_m" -> print
            function.sourceModule -> writeDecimal
            "_s" -> print
            function.symbol -> writeDecimal
            "(" -> print
            0 => functionCaptureParameterIndex!
            functionCaptureParameterIndex! < (currentFunctionCaptures! -> len) -> while {
                context.ir[currentFunctionCaptures![functionCaptureParameterIndex!]] -> writeIrType
                " %capture_$(functionCaptureParameterIndex!)" -> print
                (functionCaptureParameterIndex! + 1 < (currentFunctionCaptures! -> len) or function.operand1 >= 0) -> if { ", " -> print }
                functionCaptureParameterIndex! + 1 => functionCaptureParameterIndex!
            }
            function.operand1 >= 0 -> if {
                context.ir[function.operand1] => parameter
                parameter -> writeIrType
                " %arg" -> print
            }
            ") {" -> println
            "entry:" -> println
            function.operand0 + 1 => expressionStart
            expressionStart => functionCaptureUseIndex!
            functionCaptureUseIndex! < functionEnd! -> while {
                context.ir[functionCaptureUseIndex!] => functionCaptureUse
                (functionCaptureUse.kind == 5 and functionCaptureUse.operand0 < 0) -> if {
                    0 => functionCaptureMatchIndex!
                    functionCaptureMatchIndex! < (currentFunctionCaptures! -> len) -> while {
                        context.ir[currentFunctionCaptures![functionCaptureMatchIndex!]].symbol == functionCaptureUse.symbol -> if {
                            "  %v$(functionCaptureUseIndex!) = freeze " -> print
                            functionCaptureUse -> writeIrType
                            " %capture_$(functionCaptureMatchIndex!)" -> println
                        }
                        functionCaptureMatchIndex! + 1 => functionCaptureMatchIndex!
                    }
                }
                functionCaptureUseIndex! + 1 => functionCaptureUseIndex!
            }
            expressionStart => mutableSlotIndex!
            mutableSlotIndex! < functionEnd! -> while {
                context.ir[mutableSlotIndex!] => mutableSlotCandidate
                (mutableSlotCandidate.kind == 17 and mutableSlotCandidate.flags == 1) -> if {
                    mutableSlotIndex! -> mutableBindingRoot => mutableSlotRoot
                    mutableSlotRoot == mutableSlotIndex! -> if {
                        "  %slot$(mutableSlotRoot) = alloca " -> print
                        mutableSlotCandidate -> writeIrType
                        ", align " -> print
                        mutableSlotCandidate -> storageAlign -> writeDecimalLine
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
                -1 => pendingEach!
                Int(expressionOrder! -> len) - 1 => pendingEachOrderIndex!
                (pendingEachOrderIndex! >= 0 and pendingEach! < 0) -> while {
                    expressionOrder![pendingEachOrderIndex!] => orderedEachCandidate
                    context.ir[orderedEachCandidate] => orderedEachNode
                    (orderedEachNode.kind == 6 and orderedEachNode.opcode == -208) -> if {
                        false => orderedEachHasPendingBody!
                        expressionStart => orderedEachBodySearch!
                        (orderedEachBodySearch! < functionEnd! and not orderedEachHasPendingBody!) -> while {
                            NodeAncestorRequest { nodeIndex: orderedEachBodySearch!, ownerIndex: orderedEachCandidate } => orderedEachAncestorRequest
                            orderedEachAncestorRequest -> irDescendsFrom => orderedEachInside
                            (not expressionScheduled![orderedEachBodySearch!] and orderedEachInside) -> if { true => orderedEachHasPendingBody! }
                            orderedEachBodySearch! + 1 => orderedEachBodySearch!
                        }
                        orderedEachHasPendingBody! -> if { orderedEachCandidate => pendingEach! }
                    }
                    pendingEachOrderIndex! - 1 => pendingEachOrderIndex!
                }
                expressionStart => scheduleCandidate!
                scheduleCandidate! < functionEnd! -> while {
                    not expressionScheduled![scheduleCandidate!] -> if {
                        context.ir[scheduleCandidate!] => scheduleNode
                        scheduleNode.parent => scheduleAncestor!
                        scheduleNode.kind == 19 => insideControlRegion!
                        (scheduleAncestor! >= expressionStart and not insideControlRegion!) -> while {
                            (context.ir[scheduleAncestor!].kind == 19 or context.ir[scheduleAncestor!].kind == 20) -> if { true => insideControlRegion! } else { context.ir[scheduleAncestor!].parent => scheduleAncestor! }
                        }
                        false => insideIfCondition!
                        expressionStart => ifConditionSearch!
                        ifConditionSearch! < functionEnd! -> while {
                            context.ir[ifConditionSearch!] => ifConditionCandidate
                            ifConditionCandidate.kind == 18 -> if {
                                context.ir[ifConditionCandidate.operand0] => ifConditionRoot
                                (ifConditionRoot.kind == 8 and (ifConditionRoot.opcode == -25 or ifConditionRoot.opcode == -24) or (ifConditionRoot.kind == 7 and ifConditionRoot.opcode == -26)) -> if {
                                    NodeAncestorRequest { nodeIndex: scheduleCandidate!, ownerIndex: ifConditionCandidate.operand0 } => ifAncestorRequest
                                    ifAncestorRequest -> irDescendsFrom => ifDescends
                                    (scheduleCandidate! == ifConditionCandidate.operand0 or ifDescends) -> if {
                                        true => insideIfCondition!
                                    }
                                }
                            }
                            ifConditionSearch! + 1 => ifConditionSearch!
                        }
                        insideIfCondition! -> if { true => insideControlRegion! }
                        (scheduleNode.kind == 17 and scheduleNode.operand0 >= expressionStart and scheduleNode.operand0 < functionEnd! and not insideControlRegion!) -> if {
                            context.ir[scheduleNode.operand0].parent => bindingScheduleAncestor!
                            (bindingScheduleAncestor! >= expressionStart and not insideControlRegion!) -> while {
                                context.ir[bindingScheduleAncestor!].kind == 19 -> if { true => insideControlRegion! } else { context.ir[bindingScheduleAncestor!].parent => bindingScheduleAncestor! }
                            }
                        }
                        (insideControlRegion! or scheduleNode.kind == 19) -> if {
                            true => expressionScheduled![scheduleCandidate!]
                            true => scheduleProgress!
                        }
                        true => scheduleReady!
                        (insideControlRegion! or scheduleNode.kind == 19) -> if { false => scheduleReady! }
                        false => scheduleInsidePendingEach!
                        pendingEach! >= 0 -> if {
                            NodeAncestorRequest { nodeIndex: scheduleCandidate!, ownerIndex: pendingEach! } => pendingEachAncestorRequest
                            pendingEachAncestorRequest -> irDescendsFrom => scheduleInsidePendingEach!
                        }
                        (scheduleReady! and pendingEach! >= 0 and not scheduleInsidePendingEach!) -> if { false => scheduleReady! }
                        (scheduleReady! and scheduleNode.parent >= expressionStart) -> if {
                            scheduleCandidate! => eachBranchChild!
                            scheduleNode.parent => eachScheduleAncestor!
                            (eachScheduleAncestor! >= expressionStart and scheduleReady!) -> while {
                                context.ir[eachScheduleAncestor!] => eachScheduleOwner
                                (eachScheduleOwner.kind == 6 and eachScheduleOwner.opcode == -208 and eachBranchChild! != eachScheduleOwner.operand0 and not expressionScheduled![eachScheduleAncestor!]) -> if { false => scheduleReady! }
                                eachScheduleAncestor! => eachBranchChild!
                                context.ir[eachScheduleAncestor!].parent => eachScheduleAncestor!
                            }
                        }
                        (scheduleReady! and scheduleNode.operand0 >= expressionStart and scheduleNode.operand0 < functionEnd! and (scheduleNode.kind != 5 or context.ir[scheduleNode.operand0].flags % 2 == 0) and not expressionScheduled![scheduleNode.operand0]) -> if { false => scheduleReady! }
                        (scheduleReady! and scheduleNode.kind != 6 and scheduleNode.kind != 18 and scheduleNode.kind != 20 and scheduleNode.operand1 >= expressionStart and scheduleNode.operand1 < functionEnd! and not expressionScheduled![scheduleNode.operand1]) -> if { false => scheduleReady! }
                        (scheduleReady! and (scheduleNode.kind == 9 or scheduleNode.kind == 6) and scheduleNode.opcode == -203 and scheduleNode.operand1 >= 0 and context.ir[scheduleNode.operand1].nextOperand >= expressionStart and context.ir[scheduleNode.operand1].nextOperand < functionEnd! and not expressionScheduled![context.ir[scheduleNode.operand1].nextOperand]) -> if { false => scheduleReady! }
                        (scheduleReady! and scheduleNode.kind == 9 and scheduleNode.opcode == -204 and scheduleNode.operand1 >= 0) -> if {
                            scheduleNode.operand1 -> aggregateValueIndex => schedulePushValue
                            (schedulePushValue >= expressionStart and schedulePushValue < functionEnd! and not expressionScheduled![schedulePushValue]) -> if { false => scheduleReady! }
                        }
                        (scheduleReady! and (scheduleNode.kind == 12 or scheduleNode.kind == 14 or scheduleNode.kind == 16)) -> if {
                            scheduleNode.operand0 => scheduleSibling!
                            scheduleSibling! >= 0 -> while {
                                scheduleSibling! -> aggregateValueIndex => scheduleAggregateValue
                                not expressionScheduled![scheduleAggregateValue] -> if { false => scheduleReady! }
                                context.ir[scheduleSibling!].nextOperand => scheduleSibling!
                            }
                        }
                        (scheduleReady! and (scheduleNode.kind == 18 or scheduleNode.kind == 20)) -> if {
                            expressionStart => controlDependencySearch!
                            controlDependencySearch! < functionEnd! -> while {
                                context.ir[controlDependencySearch!] => controlDependencyUse
                                controlDependencyUse.parent => controlDependencyAncestor!
                                false => controlDependencyInside!
                                (controlDependencyAncestor! >= expressionStart and not controlDependencyInside!) -> while {
                                    (controlDependencyAncestor! == scheduleNode.operand1 or (scheduleNode.kind == 20 and controlDependencyAncestor! == scheduleNode.operand0) or (scheduleNode.kind == 18 and scheduleNode.nextOperand >= 0 and controlDependencyAncestor! == scheduleNode.nextOperand)) -> if { true => controlDependencyInside! } else { context.ir[controlDependencyAncestor!].parent => controlDependencyAncestor! }
                                }
                                (controlDependencyInside! and controlDependencyUse.kind == 5 and controlDependencyUse.operand0 >= expressionStart and controlDependencyUse.operand0 < functionEnd! and context.ir[controlDependencyUse.operand0].kind == 17) -> if {
                                    context.ir[controlDependencyUse.operand0].operand0 => controlDependencyValue!
                                    (controlDependencyValue! >= expressionStart and controlDependencyValue! < functionEnd!) -> if {
                                        context.ir[controlDependencyValue!].parent => controlDependencyValueAncestor!
                                        false => controlDependencyValueInside!
                                        (controlDependencyValueAncestor! >= expressionStart and not controlDependencyValueInside!) -> while {
                                            (controlDependencyValueAncestor! == scheduleNode.operand1 or (scheduleNode.kind == 20 and controlDependencyValueAncestor! == scheduleNode.operand0) or (scheduleNode.kind == 18 and scheduleNode.nextOperand >= 0 and controlDependencyValueAncestor! == scheduleNode.nextOperand)) -> if { true => controlDependencyValueInside! } else { context.ir[controlDependencyValueAncestor!].parent => controlDependencyValueAncestor! }
                                        }
                                        (not controlDependencyValueInside! and not expressionScheduled![controlDependencyValue!]) -> if { false => scheduleReady! }
                                    }
                                }
                                controlDependencyInside! -> if {
                                    0 => controlDirectOperandIndex!
                                    controlDirectOperandIndex! < 2 -> while {
                                        controlDependencyUse.operand0 => controlDirectOperand!
                                        controlDirectOperandIndex! == 1 -> if { controlDependencyUse.operand1 => controlDirectOperand! }
                                        (controlDirectOperand! >= expressionStart and controlDirectOperand! < functionEnd!) -> if {
                                            context.ir[controlDirectOperand!].parent => controlDirectOperandAncestor!
                                            false => controlDirectOperandInside!
                                            controlDirectOperand! == scheduleNode.operand1 -> if { true => controlDirectOperandInside! }
                                            (scheduleNode.kind == 20 and controlDirectOperand! == scheduleNode.operand0) -> if { true => controlDirectOperandInside! }
                                            (scheduleNode.kind == 18 and scheduleNode.nextOperand >= 0 and controlDirectOperand! == scheduleNode.nextOperand) -> if { true => controlDirectOperandInside! }
                                            (controlDirectOperandAncestor! >= expressionStart and not controlDirectOperandInside!) -> while {
                                                (controlDirectOperandAncestor! == scheduleNode.operand1 or (scheduleNode.kind == 20 and controlDirectOperandAncestor! == scheduleNode.operand0) or (scheduleNode.kind == 18 and scheduleNode.nextOperand >= 0 and controlDirectOperandAncestor! == scheduleNode.nextOperand)) -> if { true => controlDirectOperandInside! } else { context.ir[controlDirectOperandAncestor!].parent => controlDirectOperandAncestor! }
                                            }
                                            (not controlDirectOperandInside! and not expressionScheduled![controlDirectOperand!]) -> if { false => scheduleReady! }
                                        }
                                        controlDirectOperandIndex! + 1 => controlDirectOperandIndex!
                                    }
                                }
                                controlDependencySearch! + 1 => controlDependencySearch!
                            }
                        }
                        (scheduleReady! and (scheduleNode.kind == 18 or scheduleNode.kind == 20)) -> if {
                            expressionStart => priorControlValueSearch!
                            priorControlValueSearch! < functionEnd! -> while {
                                (not expressionScheduled![priorControlValueSearch!] and (context.ir[priorControlValueSearch!] -> sourceStart) < (scheduleNode -> sourceStart)) -> if {
                                    false => scheduleReady!
                                }
                                priorControlValueSearch! + 1 => priorControlValueSearch!
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
                                expressionStart => mutableReadBarrierSearch!
                                mutableReadBarrierSearch! < functionEnd! -> while {
                                    context.ir[mutableReadBarrierSearch!] => mutableReadBarrier
                                    false => mutableReadInsideBarrier!
                                    scheduleNode.parent => mutableReadBarrierAncestor!
                                    (mutableReadBarrierAncestor! >= expressionStart and not mutableReadInsideBarrier!) -> while {
                                        mutableReadBarrierAncestor! == mutableReadBarrierSearch! -> if { true => mutableReadInsideBarrier! } else {
                                            context.ir[mutableReadBarrierAncestor!].parent => mutableReadBarrierAncestor!
                                        }
                                    }
                                    (not mutableReadInsideBarrier! and not expressionScheduled![mutableReadBarrierSearch!] and (mutableReadBarrier.kind == 18 or mutableReadBarrier.kind == 20 or mutableReadBarrier.kind == 24 or mutableReadBarrier.kind == 25 or (mutableReadBarrier.kind == 17 and mutableReadBarrier.flags == 1) or (mutableReadBarrier.kind == 9 and mutableReadBarrier.opcode == -204)) and (mutableReadBarrier -> sourceStart) < (scheduleNode -> sourceStart)) -> if {
                                        false => scheduleReady!
                                    }
                                    mutableReadBarrierSearch! + 1 => mutableReadBarrierSearch!
                                }
                            }
                        }
                        (scheduleReady! and (scheduleNode.kind == 6 or scheduleNode.kind == 18 or scheduleNode.kind == 20 or scheduleNode.kind == 24 or scheduleNode.kind == 25 or (scheduleNode.kind == 17 and scheduleNode.flags == 1) or (scheduleNode.kind == 9 and (scheduleNode.opcode == -204 or scheduleNode.opcode == -205)))) -> if {
                            true => rootEffect!
                            scheduleNode.parent => effectAncestor!
                            (effectAncestor! >= expressionStart and rootEffect!) -> while {
                                (context.ir[effectAncestor!].kind == 6 or context.ir[effectAncestor!].kind == 18 or context.ir[effectAncestor!].kind == 20 or context.ir[effectAncestor!].kind == 24 or context.ir[effectAncestor!].kind == 25 or (context.ir[effectAncestor!].kind == 17 and context.ir[effectAncestor!].flags == 1) or (context.ir[effectAncestor!].kind == 9 and (context.ir[effectAncestor!].opcode == -204 or context.ir[effectAncestor!].opcode == -205))) -> if { false => rootEffect! } else { context.ir[effectAncestor!].parent => effectAncestor! }
                            }
                            rootEffect! -> if {
                                expressionStart => earlierEffectSearch!
                                earlierEffectSearch! < functionEnd! -> while {
                                    context.ir[earlierEffectSearch!] => earlierEffect
                                (not expressionScheduled![earlierEffectSearch!] and (earlierEffect.kind == 6 or earlierEffect.kind == 18 or earlierEffect.kind == 20 or earlierEffect.kind == 24 or earlierEffect.kind == 25 or (earlierEffect.kind == 17 and earlierEffect.flags == 1) or (earlierEffect.kind == 9 and (earlierEffect.opcode == -204 or earlierEffect.opcode == -205))) and (earlierEffect -> sourceStart) < (scheduleNode -> sourceStart)) -> if {
                                        earlierEffect.parent => earlierEffectAncestor!
                                        true => earlierRootEffect!
                                        false => earlierInsideRegion!
                                        (earlierEffectAncestor! >= expressionStart and earlierRootEffect! and not earlierInsideRegion!) -> while {
                                            (context.ir[earlierEffectAncestor!].kind == 19 or context.ir[earlierEffectAncestor!].kind == 20) -> if { true => earlierInsideRegion! } else {
                                            (context.ir[earlierEffectAncestor!].kind == 6 or context.ir[earlierEffectAncestor!].kind == 18 or context.ir[earlierEffectAncestor!].kind == 20 or context.ir[earlierEffectAncestor!].kind == 24 or context.ir[earlierEffectAncestor!].kind == 25 or (context.ir[earlierEffectAncestor!].kind == 17 and context.ir[earlierEffectAncestor!].flags == 1) or (context.ir[earlierEffectAncestor!].kind == 9 and (context.ir[earlierEffectAncestor!].opcode == -204 or context.ir[earlierEffectAncestor!].opcode == -205))) -> if { false => earlierRootEffect! } else { context.ir[earlierEffectAncestor!].parent => earlierEffectAncestor! }
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
                            (scheduleNode.kind == 6 and scheduleNode.opcode == -208) -> if { scheduleCandidate! => pendingEach! }
                            true => scheduleProgress!
                        }
                    }
                    scheduleCandidate! + 1 => scheduleCandidate!
                }
            }
            [Int; ~] => activeEach!
            0 => activeEachSize!
            0 => expressionOrderIndex!
            expressionOrderIndex! < (expressionOrder! -> len) -> while {
                expressionOrder![expressionOrderIndex!] => expressionIndex!
                context.ir[expressionIndex!] => expression
                (expression.kind == 5 and not (expression.typeOrigin == 1 and expression.typeSymbol == 16) and not (function.operand1 >= 0 and expression.symbol == context.ir[function.operand1].symbol)) -> if {
                    -1 => bindingValueIr!
                    (expression.operand0 >= 0 and context.ir[expression.operand0].kind == 17) -> if { expression.operand0 => bindingValueIr! }
                    bindingValueIr! >= 0 -> if {
                        context.ir[bindingValueIr!] => bindingValue
                        bindingValue.flags == 1 -> if {
                            bindingValueIr! -> mutableBindingRoot => bindingRoot
                            "  %v$(expressionIndex!) = load " -> print
                            expression -> writeIrType
                            ", ptr %slot$(bindingRoot), align " -> print
                            expression -> storageAlign -> writeDecimalLine
                        } else {
                            context.ir[bindingValue.operand0] => bindingOperand
                            "  %v$(expressionIndex!) = freeze " -> print
                            expression -> writeIrType
                            " " -> print
                            (bindingOperand.kind == 3 or bindingOperand.kind == 4) -> if {
                                bindingOperand -> sourceToken => bindingOperandToken
                                bindingOperand.kind == 3 -> if { context.sources[bindingOperand.sourceModule] -> slice(bindingOperandToken.span.start, bindingOperandToken.span.length) -> print } else {
                                    ((context.sources[bindingOperand.sourceModule] -> byte(bindingOperandToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else { "%v$(bindingValue.operand0)" -> print }
                            "" -> println
                        }
                    }
                }
                ((expression.kind == 9 or expression.kind == 6) and expression.opcode == -201 and expression.operand0 >= 0) -> if {
                    context.ir[expression.operand0] => lengthBase
                    (lengthBase.typeOrigin == 1 and lengthBase.typeSymbol == 16) -> if {
                        "  %v$(expressionIndex!) = call i64 @sl_argument_count()" -> println
                    } else {
                        "  %v$(expressionIndex!)" -> print
                        expression.typeSymbol == 2 -> if { "_raw" -> print }
                        " = extractvalue " -> print
                        lengthBase -> writeIrType
                        " " -> print
                        (lengthBase.kind == 5 and function.operand1 >= 0 and lengthBase.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        ", " -> print
                        lengthBase.typeOrigin == 15 -> if { "2" -> println } else { "1" -> println }
                        expression.typeSymbol == 2 -> if { "  %v$(expressionIndex!) = trunc i64 %v$(expressionIndex!)_raw to i32" -> println }
                    }
                }
                ((expression.kind == 9 or expression.kind == 6) and expression.opcode == -202 and expression.operand0 >= 0 and expression.operand1 >= 0) -> if {
                    context.ir[expression.operand0] => byteBase
                    context.ir[expression.operand1] => byteIndex
                    byteIndex.typeSymbol -> integerWidth => byteIndexWidth
                    "  %v$(expressionIndex!)_base = extractvalue " -> print
                    byteBase -> writeTextLikeType
                    " " -> print
                    (byteBase.kind == 5 and function.operand1 >= 0 and byteBase.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    (byteIndexWidth > 0 and byteIndexWidth < 64) -> if {
                        "  %v$(expressionIndex!)_index = " -> print
                        byteIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                        " i$(byteIndexWidth) %v$(expression.operand1) to i64" -> println
                    }
                    "  %v$(expressionIndex!)_address = getelementptr i8, ptr %v$(expressionIndex!)_base, i64 " -> print
                    (byteIndexWidth > 0 and byteIndexWidth < 64) -> if { "%v$(expressionIndex!)_index" -> println } else { "%v$(expression.operand1)" -> println }
                    "  %v$(expressionIndex!) = load i8, ptr %v$(expressionIndex!)_address, align 1" -> println
                }
                ((expression.kind == 9 or expression.kind == 6) and expression.opcode == -203 and expression.operand0 >= 0 and expression.operand1 >= 0) -> if {
                    context.ir[expression.operand1].nextOperand => sliceLength
                    context.ir[expression.operand0] => sliceBase
                    context.ir[expression.operand1] => sliceIndex
                    sliceIndex.typeSymbol -> integerWidth => sliceIndexWidth
                    "  %v$(expressionIndex!)_base = extractvalue " -> print
                    sliceBase -> writeTextLikeType
                    " " -> print
                    (sliceBase.kind == 5 and function.operand1 >= 0 and sliceBase.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    (sliceIndexWidth > 0 and sliceIndexWidth < 64) -> if {
                        "  %v$(expressionIndex!)_index = " -> print
                        sliceIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                        " i$(sliceIndexWidth) %v$(expression.operand1) to i64" -> println
                    }
                    "  %v$(expressionIndex!)_start = getelementptr i8, ptr %v$(expressionIndex!)_base, i64 " -> print
                    (sliceIndexWidth > 0 and sliceIndexWidth < 64) -> if { "%v$(expressionIndex!)_index" -> println } else { "%v$(expression.operand1)" -> println }
                    "  %v$(expressionIndex!)_ptr = insertvalue %sl.text poison, ptr %v$(expressionIndex!)_start, 0" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.text %v$(expressionIndex!)_ptr, i64 %v$(sliceLength), 1" -> println
                }
                (expression.kind == 9 and expression.opcode == -204 and expression.operand0 >= 0 and expression.operand1 >= 0) -> if {
                    WhileValueRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex!, nodeIndex: 0 } -> emitArrayPush
                }
                ((expression.kind == 9 or expression.kind == 6) and expression.opcode == -205 and expression.operand0 >= 0) -> if {
                    context.ir[expression.operand0] => mapPath
                    "  %v$(expressionIndex!) = call %sl.source_text @sl_runtime_map_text(%sl.text " -> print
                    (mapPath.kind == 5 and function.operand1 >= 0 and mapPath.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ")" -> println
                }
                ((expression.kind == 9 or expression.kind == 6) and expression.opcode == -206 and expression.operand0 >= 0) -> if {
                    context.ir[expression.operand0] => borrowText
                    "  %v$(expressionIndex!)_data = extractvalue %sl.text " -> print
                    (borrowText.kind == 5 and function.operand1 >= 0 and borrowText.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    "  %v$(expressionIndex!)_length = extractvalue %sl.text " -> print
                    (borrowText.kind == 5 and function.operand1 >= 0 and borrowText.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 1" -> println
                    "  %v$(expressionIndex!)_value0 = insertvalue %sl.source_text poison, ptr %v$(expressionIndex!)_data, 0" -> println
                    "  %v$(expressionIndex!)_value1 = insertvalue %sl.source_text %v$(expressionIndex!)_value0, i64 %v$(expressionIndex!)_length, 1" -> println
                    "  %v$(expressionIndex!)_value2 = insertvalue %sl.source_text %v$(expressionIndex!)_value1, ptr null, 2" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.source_text %v$(expressionIndex!)_value2, i64 0, 3" -> println
                }
                (expression.kind == 6 and expression.opcode == -208 and expression.operand0 >= 0 and expression.operand1 >= 0) -> if {
                    context.ir[expression.operand0] => eachSource
                    "  %each$(expressionIndex!)_data = extractvalue %sl.array.i32 " -> print
                    (eachSource.kind == 5 and function.operand1 >= 0 and eachSource.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    "  %each$(expressionIndex!)_length = extractvalue %sl.array.i32 " -> print
                    (eachSource.kind == 5 and function.operand1 >= 0 and eachSource.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 1" -> println
                    "  %each$(expressionIndex!)_index_slot = alloca i64, align 8" -> println
                    "  store i64 0, ptr %each$(expressionIndex!)_index_slot, align 8" -> println
                    "  br label %each$(expressionIndex!)_header" -> println
                    "each$(expressionIndex!)_header:" -> println
                    "  %each$(expressionIndex!)_index = load i64, ptr %each$(expressionIndex!)_index_slot, align 8" -> println
                    "  %each$(expressionIndex!)_done = icmp eq i64 %each$(expressionIndex!)_index, %each$(expressionIndex!)_length" -> println
                    "  br i1 %each$(expressionIndex!)_done, label %each$(expressionIndex!)_exit, label %each$(expressionIndex!)_body" -> println
                    "each$(expressionIndex!)_body:" -> println
                    "  %each$(expressionIndex!)_item_ptr = getelementptr " -> print
                    eachSource -> writeArrayElementType
                    ", ptr %each$(expressionIndex!)_data, i64 %each$(expressionIndex!)_index" -> println
                    "  %each$(expressionIndex!)_item = load " -> print
                    eachSource -> writeArrayElementType
                    ", ptr %each$(expressionIndex!)_item_ptr, align " -> print
                    eachSource -> arrayElementAlign -> writeDecimalLine
                    expressionStart => eachRoleUseIndex!
                    eachRoleUseIndex! < functionEnd! -> while {
                        context.ir[eachRoleUseIndex!] => eachRoleUse
                        NodeAncestorRequest { nodeIndex: eachRoleUseIndex!, ownerIndex: expressionIndex! } => eachRoleAncestorRequest
                        eachRoleAncestorRequest -> nodeDescendsFrom => eachRoleInside
                        (eachRoleUse.kind == 5 and eachRoleUse.operand0 < 0 and eachRoleUse.symbol >= 0 and eachRoleInside) -> if {
                            context.ranges[eachRoleUse.sourceModule] => eachRoleRange
                            false => eachRoleIsItem!
                            eachRoleUse.symbol < eachRoleRange.symbolCount -> if {
                                context.symbols[eachRoleRange.symbolStart + eachRoleUse.symbol] => eachRoleSymbol
                                (eachRoleSymbol.kind == 35 and eachRoleSymbol.astNode == expression.astNode) -> if { true => eachRoleIsItem! }
                            }
                            eachRoleIsItem! -> if {
                                "  %v$(eachRoleUseIndex!) = freeze " -> print
                                eachSource -> writeArrayElementType
                                " %each$(expressionIndex!)_item" -> println
                            }
                        }
                        eachRoleUseIndex! + 1 => eachRoleUseIndex!
                    }
                    activeEachSize! == (activeEach! -> len) -> if { activeEach! -> push(expressionIndex!) } else { expressionIndex! => activeEach![activeEachSize!] }
                    activeEachSize! + 1 => activeEachSize!
                }
                (expression.kind == 17 and expression.flags == 1) -> if {
                    expressionIndex! -> mutableBindingRoot => mutableRoot
                    context.ir[mutableRoot] => mutableBinding
                    context.ir[expression.operand0] => mutableValue
                    mutableBinding -> integerNodeWidth => mutableTargetWidth
                    mutableValue -> integerNodeWidth => mutableSourceWidth
                    (mutableTargetWidth > 0 and mutableSourceWidth > 0 and mutableTargetWidth != mutableSourceWidth and mutableValue.kind != 3 and mutableValue.kind != 4) => castMutableValue!
                    castMutableValue! -> if {
                        "  %v$(expressionIndex!)_mutable_cast = " -> print
                        mutableTargetWidth < mutableSourceWidth -> if { "trunc " -> print } else { mutableValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print } }
                        mutableValue -> writeIrType
                        " " -> print
                        (mutableValue.kind == 5 and function.operand1 >= 0 and mutableValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        " to " -> print
                        mutableBinding -> writeIrType
                        "" -> println
                    }
                    "  store " -> print
                    mutableBinding -> writeIrType
                    " " -> print
                    (mutableValue.kind == 3 or mutableValue.kind == 4) -> if {
                        mutableValue -> sourceToken => mutableToken
                        mutableValue.kind == 3 -> if { context.sources[mutableValue.sourceModule] -> slice(mutableToken.span.start, mutableToken.span.length) -> print } else {
                            ((context.sources[mutableValue.sourceModule] -> byte(mutableToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                        }
                    } else {
                        castMutableValue! -> if { "%v$(expressionIndex!)_mutable_cast" -> print } else {
                            (mutableValue.kind == 5 and function.operand1 >= 0 and mutableValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ", ptr %slot$(mutableRoot), align " -> print
                    mutableBinding -> storageAlign -> writeDecimalLine
                }
                expression.kind == 24 -> if {
                    WhileValueRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex!, nodeIndex: expressionIndex! } -> emitIndexAssignment
                }
                expression.kind == 25 -> if {
                    WhileValueRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex!, nodeIndex: expressionIndex! } -> emitMemberAssignment
                }
                expression.kind == 2 -> if {
                    expression -> sourceToken => expressionToken
                    TextLiteralRequest { sourceModule: expression.sourceModule, token: expressionToken } -> textLiteralLength => expressionLength
                    "  %v$(expressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(expressionIndex!), 0" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.text %v$(expressionIndex!)_ptr, i64 $expressionLength, 1" -> println
                }
                expression.kind == 12 -> if {
                    expression.operand0 < 0 -> if {
                        "  %v$(expressionIndex!) = freeze " -> print
                        expression -> writeIrType
                        " zeroinitializer" -> println
                    }
                    expression.operand0 => fieldValueIndex!
                    0 => fieldPosition!
                    fieldValueIndex! >= 0 -> while {
                    context.ir[fieldValueIndex!] => fieldValue
                    fieldValueIndex! -> aggregateValueIndex => emittedFieldIndex
                    context.ir[emittedFieldIndex] => emittedFieldValue
                        expression.typeId => structOwnerTypeId!
                        0 => structOwnerTypeSearch!
                        structOwnerTypeSearch! < (context.types -> len) -> while {
                            context.types[structOwnerTypeSearch!] => structOwnerTypeCandidate
                            (structOwnerTypeCandidate.status == 0 and structOwnerTypeCandidate.kind == 1 and structOwnerTypeCandidate.origin == expression.typeOrigin and structOwnerTypeCandidate.module == expression.typeModule and structOwnerTypeCandidate.symbol == expression.typeSymbol) -> if { structOwnerTypeSearch! => structOwnerTypeId! }
                            structOwnerTypeSearch! + 1 => structOwnerTypeSearch!
                        }
                        -1 => targetFieldTypeId!
                        0 => targetFieldOrdinal!
                        0 => targetFieldSearch!
                        targetFieldSearch! < (context.fields -> len) -> while {
                            context.fields[targetFieldSearch!] => targetField
                            (targetField.status == 0 and targetField.ownerType == structOwnerTypeId!) -> if {
                                targetFieldOrdinal! == fieldPosition! -> if { targetField.fieldType => targetFieldTypeId! }
                                targetFieldOrdinal! + 1 => targetFieldOrdinal!
                            }
                            targetFieldSearch! + 1 => targetFieldSearch!
                        }
                        emittedFieldValue -> integerNodeWidth => sourceFieldWidth!
                        0 => targetFieldWidth!
                        targetFieldTypeId! >= 0 -> if {
                            context.types[targetFieldTypeId!] => targetFieldSemanticType
                            targetFieldSemanticType.origin == 1 -> if { targetFieldSemanticType.symbol -> integerWidth => targetFieldWidth! }
                        }
                        (sourceFieldWidth! > 0 and targetFieldWidth! > 0 and sourceFieldWidth! != targetFieldWidth! and emittedFieldValue.kind != 3 and emittedFieldValue.kind != 4) => castStructField!
                        castStructField! -> if {
                            "  %v$(expressionIndex!)_field$(fieldPosition!)_cast = " -> print
                            targetFieldWidth! < sourceFieldWidth! -> if { "trunc " -> print } else {
                                emittedFieldValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                            }
                            emittedFieldValue -> writeIrType
                            " " -> print
                            (emittedFieldValue.kind == 5 and function.operand1 >= 0 and emittedFieldValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(emittedFieldIndex)" -> print }
                            " to " -> print
                            targetFieldTypeId! -> writeSemanticTypeId
                            "" -> println
                        }
                        "  " -> print
                        fieldValue.nextOperand < 0 -> if { "%v$(expressionIndex!)" -> print } else { "%v$(expressionIndex!)_f$(fieldPosition!)" -> print }
                        " = insertvalue " -> print
                        expression -> writeIrType
                        " " -> print
                        fieldPosition! == 0 -> if { "poison" -> print } else {
                            "%v$(expressionIndex!)_f" -> print
                            fieldPosition! - 1 -> writeDecimal
                        }
                        ", " -> print
                        targetFieldTypeId! >= 0 -> if { targetFieldTypeId! -> writeSemanticTypeId } else { emittedFieldValue -> writeIrType }
                        " " -> print
                        emittedFieldValue.kind == 2 -> if {
                            emittedFieldValue -> sourceToken => fieldTextToken
                            Int(TextLiteralRequest { sourceModule: emittedFieldValue.sourceModule, token: fieldTextToken } -> textLiteralLength) => fieldTextLength
                            "{ ptr @sl_str_$(emittedFieldIndex), i64 $fieldTextLength }" -> print
                        } else {
                        (emittedFieldValue.kind == 3 or emittedFieldValue.kind == 4) -> if {
                            emittedFieldValue -> sourceToken => fieldToken
                            emittedFieldValue.kind == 3 -> if {
                                context.sources[emittedFieldValue.sourceModule] -> slice(fieldToken.span.start, fieldToken.span.length) -> print
                            } else {
                                ((context.sources[emittedFieldValue.sourceModule] -> byte(fieldToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else {
                            castStructField! -> if { "%v$(expressionIndex!)_field$(fieldPosition!)_cast" -> print } else {
                                (emittedFieldValue.kind == 5 and function.operand1 >= 0 and emittedFieldValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(emittedFieldIndex)" -> print }
                            }
                        }
                        }
                        ", $(fieldPosition!)" -> println
                        fieldValue.nextOperand => fieldValueIndex!
                        fieldPosition! + 1 => fieldPosition!
                    }
                }
                expression.kind == 13 -> if {
                    (expression.typeOrigin == 1 and expression.typeSymbol == 16) -> if {
                    } else {
                    context.ir[expression.operand0] => memberBase
                    memberBase.typeOrigin => memberCurrentOrigin!
                    memberBase.typeModule => memberCurrentModule!
                    memberBase.typeSymbol => memberCurrentSymbol!
                    expression -> sourceNode => memberAst
                    memberBase -> sourceNode => memberBaseAst
                    memberBaseAst.start + memberBaseAst.length => memberBaseEnd
                    1 => memberIdentifierCount!
                    memberAst.firstToken => memberTokenIndex!
                    true => beforeMemberBaseEnd!
                    (beforeMemberBaseEnd! and memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount) -> while {
                        context.tokens[context.ranges[expression.sourceModule].tokenStart + memberTokenIndex!].span.start < memberBaseEnd -> if {
                            memberTokenIndex! + 1 => memberTokenIndex!
                        } else { false => beforeMemberBaseEnd! }
                    }
                    memberTokenIndex! => memberPathTokenStart
                    memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                        context.tokens[context.ranges[expression.sourceModule].tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if { memberIdentifierCount! + 1 => memberIdentifierCount! }
                        memberTokenIndex! + 1 => memberTokenIndex!
                    }
                    1 => memberIdentifierOrdinal!
                    0 => memberProjectionOrdinal!
                    memberPathTokenStart => memberTokenIndex!
                    memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                        context.tokens[context.ranges[expression.sourceModule].tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                            memberIdentifierOrdinal! > 0 -> if {
                                memberCurrentModule! => ownerSourceModule!
                                memberCurrentOrigin! == 2 -> if { context.modules[memberCurrentModule!].sourceIndex => ownerSourceModule! }
                                context.ranges[ownerSourceModule!] => ownerRange
                                -1 => fieldOrdinal!
                                0 => candidateFieldOrdinal!
                                0 => memberFieldSearch!
                                memberFieldSearch! < ownerRange.symbolCount -> while {
                                    context.symbols[ownerRange.symbolStart + memberFieldSearch!] => memberField
                                    (memberField.kind == 26 and memberField.parent == memberCurrentSymbol!) -> if {
                                        context.tokens[context.ranges[expression.sourceModule].tokenStart + memberTokenIndex!] => memberName
                                        context.tokens[ownerRange.tokenStart + memberField.nameToken] => fieldName
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
                                            } else {
                                                "%v$(expressionIndex!)_m" -> print
                                                memberProjectionOrdinal! - 1 -> writeDecimal
                                            }
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
                }
                (expression.kind == 14 and (expression.typeOrigin == 13 or expression.typeOrigin == 14 or expression.typeKind == 3)) -> if {
                    0 => arrayLength!
                    expression.operand0 => arrayCountIndex!
                    arrayCountIndex! >= 0 -> while {
                        arrayLength! + 1 => arrayLength!
                        context.ir[arrayCountIndex!].nextOperand => arrayCountIndex!
                    }
                    arrayLength! * (expression -> arrayElementSize) => arrayByteLength
                    "  %v$(expressionIndex!)_data = call ptr @malloc(i64 $arrayByteLength)" -> println
                    expression.operand0 => arrayElementIndex!
                    0 => arrayElementPosition!
                    arrayElementIndex! >= 0 -> while {
                        context.ir[arrayElementIndex!] => arrayElement
                        "  %v$(expressionIndex!)_ptr$(arrayElementPosition!) = getelementptr " -> print
                        expression -> writeArrayElementType
                        ", ptr %v$(expressionIndex!)_data, i64 $(arrayElementPosition!)" -> println
                        "  store " -> print
                        expression -> writeArrayElementType
                        " " -> print
                        arrayElement.kind == 3 -> if {
                            arrayElement -> sourceToken => arrayElementToken
                            context.sources[arrayElement.sourceModule] -> slice(arrayElementToken.span.start, arrayElementToken.span.length) -> print
                        } else {
                            (arrayElement.kind == 5 and function.operand1 >= 0 and arrayElement.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(arrayElementIndex!)" -> print }
                        }
                        ", ptr %v$(expressionIndex!)_ptr$(arrayElementPosition!), align " -> print
                        expression -> arrayElementAlign -> writeDecimalLine
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
                            dictionaryItem -> sourceToken => dictionaryItemToken
                            dictionaryItem.kind == 3 -> if {
                                context.sources[dictionaryItem.sourceModule] -> slice(dictionaryItemToken.span.start, dictionaryItemToken.span.length) -> print
                            } else {
                                ((context.sources[dictionaryItem.sourceModule] -> byte(dictionaryItemToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else {
                            (dictionaryItem.kind == 5 and function.operand1 >= 0 and dictionaryItem.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(dictionaryItemIndex!)" -> print }
                        }
                        ", ptr %v$(expressionIndex!)_$(dictionarySide)_ptr$(dictionaryEntryPosition), align " -> print
                        dictionaryItemSymbol -> legacyStorageAlign -> writeDecimalLine
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
                    (indexedArray.typeOrigin == 1 and indexedArray.typeSymbol == 16) -> if {
                        arrayIndex.typeSymbol -> integerWidth => argumentIndexWidth
                        (arrayIndex.kind != 3 and argumentIndexWidth > 0 and argumentIndexWidth < 64) -> if {
                            "  %v$(expressionIndex!)_argument_index = " -> print
                            arrayIndex.typeSymbol -> integerSigned -> if { "sext " -> print } else { "zext " -> print }
                            arrayIndex -> writeIrType
                            " " -> print
                            (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand1)" -> print }
                            " to i64" -> println
                        }
                        "  %v$(expressionIndex!) = call %sl.text @sl_argument(i64 " -> print
                        arrayIndex.kind == 3 -> if {
                            arrayIndex -> sourceToken => argumentIndexToken
                            context.sources[arrayIndex.sourceModule] -> slice(argumentIndexToken.span.start, argumentIndexToken.span.length) -> print
                        } else {
                            (argumentIndexWidth > 0 and argumentIndexWidth < 64) -> if { "%v$(expressionIndex!)_argument_index" -> print } else {
                                (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand1)" -> print }
                            }
                        }
                        ")" -> println
                    } else {
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
                        ", ptr %v$(expressionIndex!)_key_ptr, align " -> print
                        indexedArray.typeModule -> legacyStorageAlign -> writeDecimalLine
                        "  %v$(expressionIndex!)_found = icmp eq " -> print
                        indexedArray.typeModule -> llvmType -> print
                        " %v$(expressionIndex!)_key, " -> print
                        (arrayIndex.kind == 3 or arrayIndex.kind == 4) -> if {
                            arrayIndex -> sourceToken => dictionaryIndexToken
                            arrayIndex.kind == 3 -> if {
                                context.sources[arrayIndex.sourceModule] -> slice(dictionaryIndexToken.span.start, dictionaryIndexToken.span.length) -> println
                            } else {
                                ((context.sources[arrayIndex.sourceModule] -> byte(dictionaryIndexToken.span.start)) == UInt8(116)) -> if { "1" -> println } else { "0" -> println }
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
                        ", ptr %v$(expressionIndex!)_value_ptr, align " -> print
                        indexedArray.typeSymbol -> legacyStorageAlign -> writeDecimalLine
                    } else {
                    "  %v$(expressionIndex!)_data = extractvalue %sl.array.i32 " -> print
                    (indexedArray.kind == 5 and function.operand1 >= 0 and indexedArray.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    arrayIndex -> integerNodeWidth => arrayIndexWidth
                    (arrayIndex.kind != 3 and arrayIndexWidth > 0 and arrayIndexWidth < 64) -> if {
                        "  %v$(expressionIndex!)_index = " -> print
                        arrayIndex -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                        "i$(arrayIndexWidth) " -> print
                        (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand1)" -> print }
                        " to i64" -> println
                    }
                    "  %v$(expressionIndex!)_ptr = getelementptr " -> print
                    expression -> writeIrType
                    ", ptr %v$(expressionIndex!)_data, i64 " -> print
                    arrayIndex.kind == 3 -> if {
                        arrayIndex -> sourceToken => indexToken
                        context.sources[arrayIndex.sourceModule] -> slice(indexToken.span.start, indexToken.span.length) -> println
                    } else {
                        (arrayIndexWidth > 0 and arrayIndexWidth < 64) -> if { "%v$(expressionIndex!)_index" -> println } else {
                            (arrayIndex.kind == 5 and function.operand1 >= 0 and arrayIndex.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                        }
                    }
                    "  %v$(expressionIndex!) = load " -> print
                    expression -> writeIrType
                    ", ptr %v$(expressionIndex!)_ptr, align " -> print
                    expression -> storageAlign -> writeDecimalLine
                    }
                    }
                }
                (expression.kind == 7 or expression.kind == 8) -> if {
                    context.ir[expression.operand0] => leftOperand
                    0 => binaryWidth!
                    false => leftPromoted!
                    false => rightPromoted!
                    expression.kind == 8 -> if {
                        context.ir[expression.operand1] => rightOperand
                        leftOperand -> integerNodeWidth => leftWidth!
                        rightOperand -> integerNodeWidth => rightWidth!
                        (leftWidth! > 0 and rightWidth! > 0) -> if {
                            leftWidth! > rightWidth! -> if { leftWidth! => binaryWidth! } else { rightWidth! => binaryWidth! }
                            IntegerPromotionRequest { expressionIndex: expressionIndex!, operandIndex: expression.operand0, targetWidth: binaryWidth!, ownerIndex: functionIndex!, right: false } -> emitIntegerPromotion => leftPromoted!
                            IntegerPromotionRequest { expressionIndex: expressionIndex!, operandIndex: expression.operand1, targetWidth: binaryWidth!, ownerIndex: functionIndex!, right: true } -> emitIntegerPromotion => rightPromoted!
                        }
                    }
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
                        (expression.opcode == grammar.tokenIdLess or expression.opcode == grammar.tokenIdLessEqual or expression.opcode == grammar.tokenIdGreater or expression.opcode == grammar.tokenIdGreaterEqual) -> if {
                            expression -> integerComparisonOperation => operation!
                        }
                        expression.opcode == -24 -> if { "or" => operation! }
                        expression.opcode == -25 -> if { "and" => operation! }
                    }
                    "  %v$(expressionIndex!) = $(operation!) " -> print
                    binaryWidth! > 0 -> if { "i$(binaryWidth!)" -> print } else { leftOperand -> writeIrType }
                    " " -> print
                    (expression.kind == 7 and expression.opcode != -26) -> if { "0" -> print } else {
                        (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                            leftOperand -> sourceToken => leftToken
                            leftOperand.kind == 3 -> if {
                                context.sources[leftOperand.sourceModule] -> slice(leftToken.span.start, leftToken.span.length) -> print
                            } else {
                                ((context.sources[leftOperand.sourceModule] -> byte(leftToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else {
                            leftPromoted! -> if { "%v$(expressionIndex!)_left_wide" -> print } else {
                                (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            }
                        }
                    }
                    ", " -> print
                    expression.kind == 7 -> if {
                        expression.opcode == -26 -> if { "true" -> println } else {
                            (leftOperand.kind == 3 or leftOperand.kind == 4) -> if {
                                leftOperand -> sourceToken => unaryToken
                                context.sources[leftOperand.sourceModule] -> slice(unaryToken.span.start, unaryToken.span.length) -> println
                            } else {
                                (leftOperand.kind == 5 and function.operand1 >= 0 and leftOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand0)" -> println }
                            }
                        }
                    } else {
                        context.ir[expression.operand1] => rightOperand
                        (rightOperand.kind == 3 or rightOperand.kind == 4) -> if {
                            rightOperand -> sourceToken => rightToken
                            rightOperand.kind == 3 -> if {
                                context.sources[rightOperand.sourceModule] -> slice(rightToken.span.start, rightToken.span.length) -> println
                            } else {
                                ((context.sources[rightOperand.sourceModule] -> byte(rightToken.span.start)) == UInt8(116)) -> if { "1" -> println } else { "0" -> println }
                            }
                        } else {
                            rightPromoted! -> if { "%v$(expressionIndex!)_right_wide" -> println } else {
                                (rightOperand.kind == 5 and function.operand1 >= 0 and rightOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else { "%v$(expression.operand1)" -> println }
                            }
                        }
                    }
                }
                (expression.kind == 6 and expression.opcode == -207 and expression.operand0 >= 0 and expression.operand1 >= 0) -> if {
                    context.ir[expression.operand0] => parallelSource
                    context.ir[expression.operand1] => parallelBodyCall
                    "  %v$(expressionIndex!)_input = extractvalue %sl.array.i32 " -> print
                    (parallelSource.kind == 5 and function.operand1 >= 0 and parallelSource.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 0" -> println
                    "  %v$(expressionIndex!)_length = extractvalue %sl.array.i32 " -> print
                    (parallelSource.kind == 5 and function.operand1 >= 0 and parallelSource.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                    ", 1" -> println
                    "  %v$(expressionIndex!)_bytes = mul i64 %v$(expressionIndex!)_length, " -> print
                    expression -> arrayElementSize -> writeDecimalLine
                    "  %v$(expressionIndex!)_data = call ptr @malloc(i64 %v$(expressionIndex!)_bytes)" -> println
                    "  %v$(expressionIndex!)_index_slot = alloca i64, align 8" -> println
                    "  store i64 0, ptr %v$(expressionIndex!)_index_slot, align 8" -> println
                    "  br label %parallel$(expressionIndex!)_header" -> println
                    "parallel$(expressionIndex!)_header:" -> println
                    "  %v$(expressionIndex!)_index = load i64, ptr %v$(expressionIndex!)_index_slot, align 8" -> println
                    "  %v$(expressionIndex!)_done = icmp eq i64 %v$(expressionIndex!)_index, %v$(expressionIndex!)_length" -> println
                    "  br i1 %v$(expressionIndex!)_done, label %parallel$(expressionIndex!)_exit, label %parallel$(expressionIndex!)_body" -> println
                    "parallel$(expressionIndex!)_body:" -> println
                    "  %v$(expressionIndex!)_input_ptr = getelementptr " -> print
                    parallelSource -> writeArrayElementType
                    ", ptr %v$(expressionIndex!)_input, i64 %v$(expressionIndex!)_index" -> println
                    "  %v$(expressionIndex!)_item = load " -> print
                    parallelSource -> writeArrayElementType
                    ", ptr %v$(expressionIndex!)_input_ptr, align " -> print
                    parallelSource -> arrayElementAlign -> writeDecimalLine
                    CallEnvironmentRequest { callerIndex: functionIndex!, callIndex: expressionIndex!, targetModule: parallelBodyCall.targetModule, targetSymbol: parallelBodyCall.symbol, hasArgument: true } -> emitCallEnvironmentValues
                    "  %v$(expressionIndex!)_mapped = call " -> print
                    parallelBodyCall -> writeIrType
                    " @sl_m$(parallelBodyCall.targetModule)_s$(parallelBodyCall.symbol)(" -> print
                    CallEnvironmentRequest { callerIndex: functionIndex!, callIndex: expressionIndex!, targetModule: parallelBodyCall.targetModule, targetSymbol: parallelBodyCall.symbol, hasArgument: true } -> emitCallEnvironment => parallelCallCaptures
                    parallelSource -> writeArrayElementType
                    " %v$(expressionIndex!)_item)" -> println
                    "  %v$(expressionIndex!)_output_ptr = getelementptr " -> print
                    expression -> writeArrayElementType
                    ", ptr %v$(expressionIndex!)_data, i64 %v$(expressionIndex!)_index" -> println
                    "  store " -> print
                    expression -> writeArrayElementType
                    " %v$(expressionIndex!)_mapped, ptr %v$(expressionIndex!)_output_ptr, align " -> print
                    expression -> arrayElementAlign -> writeDecimalLine
                    "  %v$(expressionIndex!)_next = add i64 %v$(expressionIndex!)_index, 1" -> println
                    "  store i64 %v$(expressionIndex!)_next, ptr %v$(expressionIndex!)_index_slot, align 8" -> println
                    "  br label %parallel$(expressionIndex!)_header" -> println
                    "parallel$(expressionIndex!)_exit:" -> println
                    "  %v$(expressionIndex!)_0 = insertvalue %sl.array.i32 poison, ptr %v$(expressionIndex!)_data, 0" -> println
                    "  %v$(expressionIndex!)_1 = insertvalue %sl.array.i32 %v$(expressionIndex!)_0, i64 %v$(expressionIndex!)_length, 1" -> println
                    "  %v$(expressionIndex!) = insertvalue %sl.array.i32 %v$(expressionIndex!)_1, i64 %v$(expressionIndex!)_length, 2" -> println
                }
                (expression.kind == 6 and expression.opcode != -205 and expression.opcode != -207 and expression.opcode != -208 and not (expression.parent >= 0 and context.ir[expression.parent].kind == 6 and context.ir[expression.parent].opcode == -207 and context.ir[expression.parent].operand1 == expressionIndex!)) -> if {
                    (expression.symbol == -101 or expression.symbol == -102) -> if {
                        context.ir[expression.operand0] => runtimeArgument
                        runtimeArgument.kind == 2 -> if {
                            runtimeArgument -> sourceToken => runtimeArgumentToken
                            Int(TextLiteralRequest { sourceModule: runtimeArgument.sourceModule, token: runtimeArgumentToken } -> textLiteralLength) => runtimeArgumentLength
                            runtimeArgument -> sourceInterpolations => functionExpressionInterpolation!
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
                                        Int(functionExpressionInterpolation! -> len) - 1 => functionExpressionNodeIndex!
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
                                                                    functionExpressionLeftValue -> sourceToken => functionExpressionLeftToken
                                                                    context.sources[functionExpressionLeftValue.sourceModule] -> slice(functionExpressionLeftToken.span.start, functionExpressionLeftToken.span.length) -> print
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[functionExpressionLeftBinding!].operand0 -> writeDecimal
                                                                }
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
                                                                    functionExpressionUnaryValue -> sourceToken => functionExpressionUnaryToken
                                                                    context.sources[functionExpressionUnaryValue.sourceModule] -> slice(functionExpressionUnaryToken.span.start, functionExpressionUnaryToken.span.length) -> println
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[functionExpressionUnaryBinding!].operand0 -> writeDecimalLine
                                                                }
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
                                                                    functionExpressionRightValue -> sourceToken => functionExpressionRightToken
                                                                    context.sources[functionExpressionRightValue.sourceModule] -> slice(functionExpressionRightToken.span.start, functionExpressionRightToken.span.length) -> println
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[functionExpressionRightBinding!].operand0 -> writeDecimalLine
                                                                }
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
                                        false => functionExpressionRootMember!
                                        functionExpressionRoot.kind == 5 -> if {
                                            functionExpressionInterpolation![functionExpressionRoot.operand0] => functionExpressionMemberBase
                                            -1 => functionExpressionMemberBaseIr!
                                            false => functionExpressionMemberBaseParameter!
                                            (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionMemberBase.symbol) -> if {
                                                function.operand1 => functionExpressionMemberBaseIr!
                                                true => functionExpressionMemberBaseParameter!
                                            } else {
                                                expressionStart => functionExpressionMemberBindingSearch!
                                                functionExpressionMemberBindingSearch! < functionEnd! -> while {
                                                    (context.ir[functionExpressionMemberBindingSearch!].kind == 17 and context.ir[functionExpressionMemberBindingSearch!].symbol == functionExpressionMemberBase.symbol) -> if {
                                                        context.ir[functionExpressionMemberBindingSearch!].operand0 => functionExpressionMemberBaseIr!
                                                    }
                                                    functionExpressionMemberBindingSearch! + 1 => functionExpressionMemberBindingSearch!
                                                }
                                            }
                                            functionExpressionMemberBaseIr! >= 0 -> if {
                                                context.ir[functionExpressionMemberBaseIr!] => functionExpressionMemberBaseValue
                                                functionExpressionMemberBaseValue.typeModule => functionExpressionMemberOwnerSource!
                                                functionExpressionMemberBaseValue.typeOrigin == 2 -> if {
                                                    context.modules[functionExpressionMemberBaseValue.typeModule].sourceIndex => functionExpressionMemberOwnerSource!
                                                }
                                                context.ranges[functionExpressionMemberOwnerSource!] => functionExpressionMemberOwnerRange
                                                functionExpressionRoot.payloadStart => functionExpressionMemberDot!
                                                functionExpressionRoot.payloadStart + functionExpressionRoot.payloadLength => functionExpressionMemberEnd
                                                (functionExpressionMemberDot! < functionExpressionMemberEnd and (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionMemberDot!)) != UInt8(46)) -> while {
                                                    functionExpressionMemberDot! + UIntSize(1) => functionExpressionMemberDot!
                                                }
                                                functionExpressionMemberDot! + UIntSize(1) => functionExpressionMemberNameStart
                                                functionExpressionMemberEnd - functionExpressionMemberNameStart => functionExpressionMemberNameLength
                                                -1 => functionExpressionMemberFieldOrdinal!
                                                -1 => functionExpressionMemberFieldTypeAst!
                                                0 => functionExpressionMemberCandidateOrdinal!
                                                0 => functionExpressionMemberFieldSearch!
                                                functionExpressionMemberFieldSearch! < functionExpressionMemberOwnerRange.symbolCount -> while {
                                                    context.symbols[functionExpressionMemberOwnerRange.symbolStart + functionExpressionMemberFieldSearch!] => functionExpressionMemberField
                                                    (functionExpressionMemberField.kind == 26 and functionExpressionMemberField.parent == functionExpressionMemberBaseValue.typeSymbol) -> if {
                                                        context.tokens[functionExpressionMemberOwnerRange.tokenStart + functionExpressionMemberField.nameToken] => functionExpressionMemberFieldName
                                                        functionExpressionMemberFieldName.span.length == functionExpressionMemberNameLength => functionExpressionMemberNameEqual!
                                                        UIntSize(0) => functionExpressionMemberNameByte!
                                                        (functionExpressionMemberNameEqual! and functionExpressionMemberNameByte! < functionExpressionMemberNameLength) -> while {
                                                            (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionMemberNameStart + functionExpressionMemberNameByte!)) != (context.sources[functionExpressionMemberOwnerSource!] -> byte(functionExpressionMemberFieldName.span.start + functionExpressionMemberNameByte!)) -> if {
                                                                false => functionExpressionMemberNameEqual!
                                                            }
                                                            functionExpressionMemberNameByte! + UIntSize(1) => functionExpressionMemberNameByte!
                                                        }
                                                        functionExpressionMemberNameEqual! -> if {
                                                            functionExpressionMemberCandidateOrdinal! => functionExpressionMemberFieldOrdinal!
                                                            functionExpressionMemberField.typeNode => functionExpressionMemberFieldTypeAst!
                                                        }
                                                        functionExpressionMemberCandidateOrdinal! + 1 => functionExpressionMemberCandidateOrdinal!
                                                    }
                                                    functionExpressionMemberFieldSearch! + 1 => functionExpressionMemberFieldSearch!
                                                }
                                                functionExpressionMemberFieldTypeAst! >= 0 -> if {
                                                    0 => functionExpressionMemberTypeSearch!
                                                    functionExpressionMemberTypeSearch! < (context.nominal -> len) -> while {
                                                        context.nominal[functionExpressionMemberTypeSearch!] => functionExpressionMemberType
                                                        (functionExpressionMemberType.sourceModule == functionExpressionMemberOwnerSource! and functionExpressionMemberType.typeAst == functionExpressionMemberFieldTypeAst! and functionExpressionMemberType.status == 0) -> if {
                                                            functionExpressionMemberType.targetSymbol => functionExpressionRootTypeSymbol!
                                                        }
                                                        functionExpressionMemberTypeSearch! + 1 => functionExpressionMemberTypeSearch!
                                                    }
                                                }
                                                functionExpressionMemberFieldOrdinal! >= 0 -> if {
                                                    "  %v$(expressionIndex!)_expression$(functionExpressionRootSearch!) = extractvalue " -> print
                                                    functionExpressionMemberBaseValue -> writeIrType
                                                    " " -> print
                                                    functionExpressionMemberBaseParameter! -> if { "%arg" -> print } else { "%v$(functionExpressionMemberBaseIr!)" -> print }
                                                    ", $(functionExpressionMemberFieldOrdinal!)" -> println
                                                    true => functionExpressionRootMember!
                                                }
                                            }
                                        }
                                        functionExpressionRootTypeSymbol! -> integerWidth => functionExpressionRootWidth!
                                        (functionExpressionRootBinding! >= 0 and context.ir[functionExpressionRootBinding!].operand0 >= 0) -> if {
                                            context.ir[context.ir[functionExpressionRootBinding!].operand0] -> integerNodeWidth => functionExpressionBoundWidth
                                            functionExpressionBoundWidth > 0 -> if { functionExpressionBoundWidth => functionExpressionRootWidth! }
                                        }
                                        functionExpressionRootTypeSymbol! == 1 -> if {
                                            "  %v$(expressionIndex!)_expression$(functionExpressionRootSearch!)_text_ptr = extractvalue %sl.text " -> print
                                            functionExpressionRoot.kind == 5 -> if {
                                                "%v$(expressionIndex!)_expression$(functionExpressionRootSearch!)" -> print
                                            } else {
                                                (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                    "%v" -> print
                                                    context.ir[functionExpressionRootBinding!].operand0 -> writeDecimal
                                                }
                                            }
                                            ", 0" -> println
                                            "  %v$(expressionIndex!)_expression$(functionExpressionRootSearch!)_text_len = extractvalue %sl.text " -> print
                                            functionExpressionRoot.kind == 5 -> if {
                                                "%v$(expressionIndex!)_expression$(functionExpressionRootSearch!)" -> print
                                            } else {
                                                (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                    "%v" -> print
                                                    context.ir[functionExpressionRootBinding!].operand0 -> writeDecimal
                                                }
                                            }
                                            ", 1" -> println
                                            "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_expression$(functionExpressionRootSearch!)_text_ptr, i64 %v$(expressionIndex!)_expression$(functionExpressionRootSearch!)_text_len, i1 false)" -> println
                                        } else {
                                        functionExpressionRootTypeSymbol! == 23 -> if { "  call void @sl_runtime_print_i1(i1 " -> print } else {
                                            functionExpressionRootWidth! == 64 -> if { "  call void @sl_runtime_print_i64(i64 " -> print } else { "  call void @sl_runtime_print_i32(i32 " -> print }
                                        }
                                        (functionExpressionRoot.kind == 0 or functionExpressionRoot.kind == 4) -> if {
                                            functionExpressionRoot.kind == 4 -> if {
                                                (context.sources[runtimeArgument.sourceModule] -> byte(functionExpressionRoot.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                            } else { context.sources[runtimeArgument.sourceModule] -> slice(functionExpressionRoot.payloadStart, functionExpressionRoot.payloadLength) -> print }
                                        } else {
                                        functionExpressionRoot.kind == 5 -> if {
                                            functionExpressionRootMember! -> if { "%v$(expressionIndex!)_expression$(functionExpressionRootSearch!)" -> print }
                                        } else {
                                        functionExpressionRoot.kind == 1 -> if {
                                            (function.operand1 >= 0 and context.ir[function.operand1].symbol == functionExpressionRoot.symbol) -> if { "%arg" -> print } else {
                                                functionExpressionRootBinding! >= 0 -> if {
                                                    context.ir[context.ir[functionExpressionRootBinding!].operand0] => functionExpressionRootValue
                                                    (functionExpressionRootValue.kind == 3 or functionExpressionRootValue.kind == 4) -> if {
                                                        functionExpressionRootValue -> sourceToken => functionExpressionRootToken
                                                        functionExpressionRootValue.kind == 4 -> if {
                                                            (context.sources[functionExpressionRootValue.sourceModule] -> byte(functionExpressionRootToken.span.start)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                        } else { context.sources[functionExpressionRootValue.sourceModule] -> slice(functionExpressionRootToken.span.start, functionExpressionRootToken.span.length) -> print }
                                                    } else {
                                                        "%v" -> print
                                                        context.ir[functionExpressionRootBinding!].operand0 -> writeDecimal
                                                    }
                                                }
                                            }
                                        } else { "%v$(expressionIndex!)_expression$(functionExpressionRootSearch!)" -> print }
                                        }
                                        }
                                        functionExpressionRootWidth! == 64 -> if {
                                            ", i1 " -> print
                                            functionExpressionRootTypeSymbol! -> integerSigned -> if { "true" -> print } else { "false" -> print }
                                        }
                                        ", i1 false)" -> println
                                        }
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
                            UIntSize(functionInterpolationContentStart) => functionInterpolationSegmentStart!
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
                                            context.ranges[runtimeArgument.sourceModule] => functionInterpolationRange
                                            0 => functionInterpolationSymbolIndex!
                                            functionInterpolationSymbolIndex! < functionInterpolationRange.symbolCount -> while {
                                                context.symbols[functionInterpolationRange.symbolStart + functionInterpolationSymbolIndex!] => functionInterpolationSymbol
                                                (functionInterpolationSymbol.kind == 9 or functionInterpolationSymbol.kind == 35) -> if {
                                                    context.tokens[functionInterpolationRange.tokenStart + functionInterpolationSymbol.nameToken] => functionInterpolationSymbolToken
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
                                            functionInterpolationValue -> sourceToken => functionInterpolationValueToken
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
                            expression.opcode == -203 -> if { "%v$(expressionIndex!)" -> print } else {
                                (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            }
                            ", 0" -> println
                            "  %v$(expressionIndex!)_runtime_len = extractvalue %sl.text " -> print
                            expression.opcode == -203 -> if { "%v$(expressionIndex!)" -> print } else {
                                (runtimeArgument.kind == 5 and function.operand1 >= 0 and runtimeArgument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                            }
                            ", 1" -> println
                            "  call void @sl_runtime_print(ptr %v$(expressionIndex!)_runtime_ptr, i64 %v$(expressionIndex!)_runtime_len, i1 " -> print
                        }
                        expression.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                    } else {
                    (expression.targetModule < 0 and expression.typeOrigin == 1 and (expression.typeSymbol -> integerWidth) > 0 and expression.operand0 >= 0) -> if {
                        context.ir[expression.operand0] => functionIntegerArgument
                        expression.typeSymbol -> integerWidth => functionTargetWidth
                        functionIntegerArgument.typeSymbol -> integerWidth => functionInputWidth
                        "  %v$(expressionIndex!) = " -> print
                        functionTargetWidth < functionInputWidth -> if { "trunc " -> print } else {
                            functionTargetWidth > functionInputWidth -> if {
                                functionIntegerArgument -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                            } else { "add " -> print }
                        }
                        functionIntegerArgument -> writeIrType
                        " " -> print
                        functionIntegerArgument.kind == 3 -> if {
                            functionIntegerArgument -> sourceToken => functionIntegerToken
                            context.sources[functionIntegerArgument.sourceModule] -> slice(functionIntegerToken.span.start, functionIntegerToken.span.length) -> print
                        } else {
                            (functionIntegerArgument.kind == 5 and function.operand1 >= 0 and functionIntegerArgument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                        functionTargetWidth == functionInputWidth -> if { ", 0" -> println } else {
                            " to " -> print
                            expression -> writeIrType
                            "" -> println
                        }
                    } else {
                    CallEnvironmentRequest { callerIndex: functionIndex!, callIndex: expressionIndex!, targetModule: expression.targetModule, targetSymbol: expression.symbol, hasArgument: expression.operand0 >= 0 } -> emitCallEnvironmentValues
                    (expression.typeOrigin == 1 and expression.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(expressionIndex!) = call " -> print }
                    expression -> writeIrType
                    " @sl_m$(expression.targetModule)_s$(expression.symbol)(" -> print
                    CallEnvironmentRequest { callerIndex: functionIndex!, callIndex: expressionIndex!, targetModule: expression.targetModule, targetSymbol: expression.symbol, hasArgument: expression.operand0 >= 0 } -> emitCallEnvironment => functionCallEnvironment
                    expression.operand0 >= 0 -> if {
                        context.ir[expression.operand0] => argument
                        argument -> writeIrType
                        " " -> print
                        (argument.kind == 3 or argument.kind == 4) -> if {
                            argument -> sourceToken => argumentToken
                            argument.kind == 3 -> if {
                                context.sources[argument.sourceModule] -> slice(argumentToken.span.start, argumentToken.span.length) -> print
                            } else {
                                ((context.sources[argument.sourceModule] -> byte(argumentToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else {
                            (argument.kind == 5 and function.operand1 >= 0 and argument.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                    }
                    ")" -> println
                    }
                    }
                }
                expression.kind == 18 -> if {
                    context.ir[expression.operand0] => ifCondition
                    (expression.typeSymbol != 0 and expression.nextOperand >= 0) -> if {
                        "  %if$(expressionIndex!)_result = alloca " -> print
                        expression -> writeIrType
                        ", align " -> print
                        expression -> storageAlign -> writeDecimalLine
                    }
                    (ifCondition.kind == 8 and (ifCondition.opcode == -25 or ifCondition.opcode == -24) or (ifCondition.kind == 7 and ifCondition.opcode == -26)) => ifShortCircuit
                    ifShortCircuit -> if {
                        WhileBranchRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex!, conditionIndex: expression.operand0 } -> emitWhileBranch
                        "while$(expressionIndex!)_body:" -> println
                        "  br label %if$(expressionIndex!)_then" -> println
                        "while$(expressionIndex!)_exit:" -> println
                        expression.nextOperand >= 0 -> if { "  br label %if$(expressionIndex!)_else" -> println } else { "  br label %if$(expressionIndex!)_merge" -> println }
                    } else {
                        "  br i1 " -> print
                        ifCondition.kind == 4 -> if {
                            ifCondition -> sourceToken => ifConditionToken
                            ((context.sources[ifCondition.sourceModule] -> byte(ifConditionToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                        } else {
                            (ifCondition.kind == 5 and function.operand1 >= 0 and ifCondition.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(expression.operand0)" -> print }
                        }
                        expression.nextOperand >= 0 -> if { ", label %if$(expressionIndex!)_then, label %if$(expressionIndex!)_else" -> println } else { ", label %if$(expressionIndex!)_then, label %if$(expressionIndex!)_merge" -> println }
                    }
                    "if$(expressionIndex!)_then:" -> println
                    expression.operand1 -> regionReturns => thenReturns!
                    expression.operand1 -> emitRegion
                    not thenReturns! -> if {
                        (expression.typeSymbol != 0 and expression.nextOperand >= 0) -> if {
                            expression.operand1 -> regionResultValueIndex => thenStoreIndex
                            context.ir[thenStoreIndex] => thenStoreValue
                            "  store " -> print
                            expression -> writeIrType
                            " " -> print
                            (thenStoreValue.kind == 3 or thenStoreValue.kind == 4) -> if {
                                thenStoreValue -> sourceToken => thenStoreToken
                                thenStoreValue.kind == 3 -> if { context.sources[thenStoreValue.sourceModule] -> slice(thenStoreToken.span.start, thenStoreToken.span.length) -> print } else {
                                    ((context.sources[thenStoreValue.sourceModule] -> byte(thenStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else {
                                (thenStoreValue.kind == 5 and function.operand1 >= 0 and thenStoreValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(thenStoreIndex)" -> print }
                            }
                            ", ptr %if$(expressionIndex!)_result, align " -> print
                            expression -> storageAlign -> writeDecimalLine
                        }
                        "  br label %if$(expressionIndex!)_merge" -> println
                    }
                    false => elseReturns!
                    expression.nextOperand >= 0 -> if {
                        "if$(expressionIndex!)_else:" -> println
                        expression.nextOperand -> regionReturns => elseReturns!
                        expression.nextOperand -> emitRegion
                        not elseReturns! -> if {
                            expression.typeSymbol != 0 -> if {
                                expression.nextOperand -> regionResultValueIndex => elseStoreIndex
                                context.ir[elseStoreIndex] => elseStoreValue
                                "  store " -> print
                                expression -> writeIrType
                                " " -> print
                                (elseStoreValue.kind == 3 or elseStoreValue.kind == 4) -> if {
                                    elseStoreValue -> sourceToken => elseStoreToken
                                    elseStoreValue.kind == 3 -> if { context.sources[elseStoreValue.sourceModule] -> slice(elseStoreToken.span.start, elseStoreToken.span.length) -> print } else {
                                        ((context.sources[elseStoreValue.sourceModule] -> byte(elseStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                    }
                                } else {
                                    (elseStoreValue.kind == 5 and function.operand1 >= 0 and elseStoreValue.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> print } else { "%v$(elseStoreIndex)" -> print }
                                }
                                ", ptr %if$(expressionIndex!)_result, align " -> print
                                expression -> storageAlign -> writeDecimalLine
                            }
                            "  br label %if$(expressionIndex!)_merge" -> println
                        }
                    }
                    not (thenReturns! and elseReturns!) -> if { "if$(expressionIndex!)_merge:" -> println }
                    (expression.typeSymbol != 0 and expression.nextOperand >= 0 and not (thenReturns! and elseReturns!)) -> if {
                        "  %v$(expressionIndex!) = load " -> print
                        expression -> writeIrType
                        ", ptr %if$(expressionIndex!)_result, align " -> print
                        expression -> storageAlign -> writeDecimalLine
                    }
                }
                expression.kind == 20 -> if {
                    "  br label %while$(expressionIndex!)_header" -> println
                    "while$(expressionIndex!)_header:" -> println
                    WhileBranchRequest { whileIndex: expressionIndex!, ownerIndex: functionIndex!, conditionIndex: expression.operand0 } -> emitWhileBranch
                    "while$(expressionIndex!)_body:" -> println
                    expression.operand1 -> emitRegion
                    OwnedDropRequest { regionIndex: expression.operand1, beforeAst: -1, edgeIndex: expressionIndex! * 10 + 3, transferredSymbol: -1 } -> emitOwnedDrops
                    "  br label %while$(expressionIndex!)_header" -> println
                    "while$(expressionIndex!)_exit:" -> println
                }
                -1 => nextExpressionIndex!
                expressionOrderIndex! + 1 < (expressionOrder! -> len) -> if { expressionOrder![expressionOrderIndex! + 1] => nextExpressionIndex! }
                true => closeEachProgress!
                (activeEachSize! > 0 and closeEachProgress!) -> while {
                    activeEach![activeEachSize! - 1] => activeEachIndex
                    false => nextInsideEach!
                    nextExpressionIndex! >= 0 -> if { NodeAncestorRequest { nodeIndex: nextExpressionIndex!, ownerIndex: activeEachIndex } -> nodeDescendsFrom => nextInsideEach! }
                    nextInsideEach! -> if { false => closeEachProgress! } else {
                        "  br label %each$(activeEachIndex)_continue" -> println
                        "each$(activeEachIndex)_continue:" -> println
                        "  %each$(activeEachIndex)_next = add i64 %each$(activeEachIndex)_index, 1" -> println
                        "  store i64 %each$(activeEachIndex)_next, ptr %each$(activeEachIndex)_index_slot, align 8" -> println
                        "  br label %each$(activeEachIndex)_header" -> println
                        "each$(activeEachIndex)_exit:" -> println
                        activeEachSize! - 1 => activeEachSize!
                    }
                }
                expressionOrderIndex! + 1 => expressionOrderIndex!
            }
            function.operand0 => returnValueIndex!
            context.ir[function.operand0] => returnNode
            (returnNode.kind == 1 and returnNode.operand0 >= 0) -> if {
                returnNode.operand0 => returnValueIndex!
            }
            (returnNode.kind == 1 and returnNode.operand0 < 0) -> if {
                function.operand0 + 1 => returnChildSearch!
                (returnChildSearch! < functionEnd! and returnValueIndex! == function.operand0) -> while {
                    context.ir[returnChildSearch!].parent == function.operand0 -> if {
                        returnChildSearch! => returnValueIndex!
                    }
                    returnChildSearch! + 1 => returnChildSearch!
                }
            }
            returnValueIndex! => returnValueIndex
            context.ir[returnValueIndex] => returnOperand
            expressionStart => dropIndex!
            dropIndex! < functionEnd! -> while {
                context.ir[dropIndex!] => dropCandidate
                false => dropCandidateMoved!
                false => dropCandidateHasPartialMoves!
                true => dropCandidateIsRoot!
                dropIndex! => dropCandidateRoot!
                (dropCandidate.kind == 17 and dropCandidate.parent == function.operand0 and (dropCandidate -> ownsType)) -> if {
                    dropIndex! -> mutableBindingRoot => dropCandidateRoot!
                    dropCandidateRoot! != dropIndex! -> if { false => dropCandidateIsRoot! }
                }
                false => dropCandidateReturned!
                (dropCandidate.kind == 17 and dropCandidateIsRoot! and returnOperand.kind == 5 and returnOperand.operand0 >= 0) -> if {
                    context.ir[returnOperand.operand0] => returnedBindingCandidate
                    returnedBindingCandidate.kind == 17 -> if {
                        returnOperand.operand0 -> mutableBindingRoot => returnedBindingRoot
                        returnedBindingRoot == dropCandidateRoot! -> if { true => dropCandidateReturned! }
                    }
                }
                (dropCandidate.kind == 17 and (dropCandidate.typeOrigin == 13 or dropCandidate.typeOrigin == 14 or dropCandidate.typeOrigin == 15 or dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2)) -> if {
                    0 => dropMoveIndex!
                    (dropMoveIndex! < (context.moves -> len) and not dropCandidateMoved!) -> while {
                        context.moves[dropMoveIndex!] => dropMove
                        (dropMove.memberIr >= 0 and dropMove.sourceModule == dropCandidate.sourceModule and dropMove.symbol == dropCandidate.symbol) -> if { true => dropCandidateHasPartialMoves! }
                        (dropMove.memberIr < 0 and dropMove.sourceModule == dropCandidate.sourceModule and dropMove.symbol == dropCandidate.symbol and dropMove.regionIr == function.operand0) -> if {
                            context.ir[dropMove.siteIr] => dropMoveCall
                            (dropMoveCall -> sourceStart) > (dropCandidate -> sourceStart) -> if { true => dropCandidateMoved! }
                        }
                        dropMoveIndex! + 1 => dropMoveIndex!
                    }
                }
                (dropCandidate.kind == 14 and (dropCandidate.typeOrigin == 13 or dropCandidate.typeOrigin == 14) and dropCandidate.parent == function.operand0 and dropIndex! != returnValueIndex) -> if {
                    "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %v$(dropIndex!), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!))" -> println
                }
                (dropCandidate.kind == 16 and dropCandidate.typeOrigin == 15 and dropCandidate.parent == function.operand0 and dropIndex! != returnValueIndex) -> if {
                    "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %v$(dropIndex!), 0" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_keys)" -> println
                    "  %drop$(dropIndex!)_values = extractvalue %sl.dict %v$(dropIndex!), 1" -> println
                    "  call void @free(ptr %drop$(dropIndex!)_values)" -> println
                }
                (dropCandidate.kind == 12 and (dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2) and dropCandidate.parent == function.operand0 and dropIndex! != returnValueIndex) -> if {
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
                (dropCandidate.kind == 17 and dropCandidateIsRoot! and (dropCandidate.typeOrigin == 13 or dropCandidate.typeOrigin == 14) and dropCandidate.parent == function.operand0 and not dropCandidateMoved! and not dropCandidateReturned!) -> if {
                    dropCandidate.flags == 1 -> if {
                        "  %drop$(dropIndex!)_current = load %sl.array.i32, ptr %slot$(dropCandidateRoot!), align " -> print
                        dropCandidate -> storageAlign -> writeDecimalLine
                        "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %drop$(dropIndex!)_current, 0" -> println
                    } else {
                        "  %drop$(dropIndex!) = extractvalue %sl.array.i32 %v$(dropCandidate.operand0), 0" -> println
                    }
                    "  call void @free(ptr %drop$(dropIndex!))" -> println
                }
                (dropCandidate.kind == 17 and dropCandidateIsRoot! and dropCandidate.typeOrigin == 15 and dropCandidate.parent == function.operand0 and not dropCandidateMoved! and not dropCandidateReturned!) -> if {
                    dropCandidate.flags == 1 -> if {
                        "  %drop$(dropIndex!)_current = load %sl.dict, ptr %slot$(dropCandidateRoot!), align " -> print
                        dropCandidate -> storageAlign -> writeDecimalLine
                        "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %drop$(dropIndex!)_current, 0" -> println
                    } else {
                        "  %drop$(dropIndex!)_keys = extractvalue %sl.dict %v$(dropCandidate.operand0), 0" -> println
                    }
                    "  call void @free(ptr %drop$(dropIndex!)_keys)" -> println
                    dropCandidate.flags == 1 -> if {
                        "  %drop$(dropIndex!)_values = extractvalue %sl.dict %drop$(dropIndex!)_current, 1" -> println
                    } else {
                        "  %drop$(dropIndex!)_values = extractvalue %sl.dict %v$(dropCandidate.operand0), 1" -> println
                    }
                    "  call void @free(ptr %drop$(dropIndex!)_values)" -> println
                }
                (dropCandidate.kind == 17 and dropCandidateIsRoot! and (dropCandidate.typeOrigin == 0 or dropCandidate.typeOrigin == 2) and dropCandidate.parent == function.operand0 and not dropCandidateMoved! and not dropCandidateReturned!) -> if {
                    dropCandidate.flags == 1 -> if {
                        "  %dropg$(dropIndex!)_p0 = load %sl.struct.m$(dropCandidate.typeModule)_s$(dropCandidate.typeSymbol), ptr %slot$(dropCandidateRoot!), align " -> print
                        dropCandidate -> storageAlign -> writeDecimalLine
                    }
                    0 => dropCandidateValueKind!
                    dropCandidate.flags == 1 -> if { 2 => dropCandidateValueKind! }
                    DropGlueRequest {
                        typeOrigin: dropCandidate.typeOrigin
                        typeModule: dropCandidate.typeModule
                        typeSymbol: dropCandidate.typeSymbol
                        valueKind: dropCandidateValueKind!
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
                false => ownedParameterMoved!
                0 => ownedParameterMoveIndex!
                ownedParameterMoveIndex! < (context.moves -> len) -> while {
                    context.moves[ownedParameterMoveIndex!] => ownedParameterMove
                    (ownedParameterMove.memberIr >= 0 and ownedParameterMove.sourceModule == ownedParameter.sourceModule and ownedParameterMove.symbol == ownedParameter.symbol) -> if { true => ownedParameterHasPartialMoves! }
                    (ownedParameterMove.memberIr < 0 and ownedParameterMove.sourceModule == ownedParameter.sourceModule and ownedParameterMove.symbol == ownedParameter.symbol and ownedParameterMove.regionIr == function.operand0) -> if { true => ownedParameterMoved! }
                    ownedParameterMoveIndex! + 1 => ownedParameterMoveIndex!
                }
                ((ownedParameter.typeOrigin == 13 or ownedParameter.typeOrigin == 14) and ownedParameter.flags % 2 == 1 and not ownedParameterMoved!) -> if {
                    not (returnOperand.kind == 5 and returnOperand.symbol == ownedParameter.symbol) -> if {
                        "  %drop_arg = extractvalue %sl.array.i32 %arg, 0" -> println
                        "  call void @free(ptr %drop_arg)" -> println
                    }
                }
                (ownedParameter.typeOrigin == 15 and ownedParameter.flags % 2 == 1 and not ownedParameterMoved!) -> if {
                    not (returnOperand.kind == 5 and returnOperand.symbol == ownedParameter.symbol) -> if {
                        "  %drop_arg_keys = extractvalue %sl.dict %arg, 0" -> println
                        "  call void @free(ptr %drop_arg_keys)" -> println
                        "  %drop_arg_values = extractvalue %sl.dict %arg, 1" -> println
                        "  call void @free(ptr %drop_arg_values)" -> println
                    }
                }
                ((ownedParameter.typeOrigin == 0 or ownedParameter.typeOrigin == 2) and ownedParameter.flags % 2 == 1 and not ownedParameterMoved!) -> if {
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
            function -> writeIrType
            " " -> print
            (returnOperand.kind == 3 or returnOperand.kind == 4) -> if {
                returnOperand -> sourceToken => returnToken
                returnOperand.kind == 3 -> if {
                    context.sources[returnOperand.sourceModule] -> slice(returnToken.span.start, returnToken.span.length) -> println
                } else {
                    ((context.sources[returnOperand.sourceModule] -> byte(returnToken.span.start)) == UInt8(116)) -> if { "1" -> println } else { "0" -> println }
                }
            } else {
                (returnOperand.kind == 5 and function.operand1 >= 0 and returnOperand.symbol == context.ir[function.operand1].symbol) -> if { "%arg" -> println } else {
                    "%v" -> print
                    returnValueIndex -> writeDecimalLine
                }
            }
            }
            "}" -> println
            functionEnd! => functionIndex!
        } else {
            function.kind == 11 -> if {
                functionIndex! + 1 => entryEnd!
                (entryEnd! < (context.ir -> len) and context.ir[entryEnd!].kind != 0 and context.ir[entryEnd!].kind != 11) -> while { entryEnd! + 1 => entryEnd! }
                usesArguments! -> if { "define i32 @main(i32 %argc, ptr %argv) {" -> println } else { "define i32 @main() {" -> println }
                "entry:" -> println
                usesArguments! -> if {
                    "  %argc64 = zext i32 %argc to i64" -> println
                    "  store i64 %argc64, ptr @sl_argc, align 8" -> println
                    "  store ptr %argv, ptr @sl_argv, align 8" -> println
                }
                functionIndex! + 1 => entryMutableSlotIndex!
                entryMutableSlotIndex! < entryEnd! -> while {
                    context.ir[entryMutableSlotIndex!] => entryMutableSlotCandidate
                    (entryMutableSlotCandidate.kind == 17 and entryMutableSlotCandidate.flags == 1) -> if {
                        entryMutableSlotIndex! -> mutableBindingRoot => entryMutableSlotRoot
                        entryMutableSlotRoot == entryMutableSlotIndex! -> if {
                            "  %slot$(entryMutableSlotRoot) = alloca " -> print
                            entryMutableSlotCandidate -> writeIrType
                            ", align " -> print
                            entryMutableSlotCandidate -> storageAlign -> writeDecimalLine
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
                            false => entryInsideIfCondition!
                            functionIndex! + 1 => entryIfConditionSearch!
                            entryIfConditionSearch! < entryEnd! -> while {
                                context.ir[entryIfConditionSearch!] => entryIfConditionCandidate
                                entryIfConditionCandidate.kind == 18 -> if {
                                    context.ir[entryIfConditionCandidate.operand0] => entryIfConditionRoot
                                    (entryIfConditionRoot.kind == 8 and (entryIfConditionRoot.opcode == -25 or entryIfConditionRoot.opcode == -24) or (entryIfConditionRoot.kind == 7 and entryIfConditionRoot.opcode == -26)) -> if {
                                        NodeAncestorRequest { nodeIndex: entryScheduleCandidate!, ownerIndex: entryIfConditionCandidate.operand0 } => entryIfAncestorRequest
                                        entryIfAncestorRequest -> irDescendsFrom => entryIfDescends
                                        (entryScheduleCandidate! == entryIfConditionCandidate.operand0 or entryIfDescends) -> if {
                                            true => entryInsideIfCondition!
                                        }
                                    }
                                }
                                entryIfConditionSearch! + 1 => entryIfConditionSearch!
                            }
                            entryInsideIfCondition! -> if { true => entryInsideControlRegion! }
                            (entryScheduleNode.kind == 17 and entryScheduleNode.operand0 > functionIndex! and entryScheduleNode.operand0 < entryEnd! and not entryInsideControlRegion!) -> if {
                                context.ir[entryScheduleNode.operand0].parent => entryBindingScheduleAncestor!
                                (entryBindingScheduleAncestor! > functionIndex! and not entryInsideControlRegion!) -> while {
                                    context.ir[entryBindingScheduleAncestor!].kind == 19 -> if { true => entryInsideControlRegion! } else { context.ir[entryBindingScheduleAncestor!].parent => entryBindingScheduleAncestor! }
                                }
                            }
                            (entryInsideControlRegion! or entryScheduleNode.kind == 19) -> if {
                                true => entryScheduled![entryScheduleCandidate!]
                                true => entryScheduleProgress!
                            }
                            true => entryScheduleReady!
                            (entryInsideControlRegion! or entryScheduleNode.kind == 19) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and entryScheduleNode.operand0 > functionIndex! and entryScheduleNode.operand0 < entryEnd! and (entryScheduleNode.kind != 5 or context.ir[entryScheduleNode.operand0].flags % 2 == 0) and not entryScheduled![entryScheduleNode.operand0]) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and entryScheduleNode.kind != 6 and entryScheduleNode.kind != 18 and entryScheduleNode.kind != 20 and entryScheduleNode.operand1 > functionIndex! and entryScheduleNode.operand1 < entryEnd! and not entryScheduled![entryScheduleNode.operand1]) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and (entryScheduleNode.kind == 9 or entryScheduleNode.kind == 6) and entryScheduleNode.opcode == -203 and entryScheduleNode.operand1 >= 0 and context.ir[entryScheduleNode.operand1].nextOperand > functionIndex! and context.ir[entryScheduleNode.operand1].nextOperand < entryEnd! and not entryScheduled![context.ir[entryScheduleNode.operand1].nextOperand]) -> if { false => entryScheduleReady! }
                            (entryScheduleReady! and entryScheduleNode.kind == 9 and entryScheduleNode.opcode == -204 and entryScheduleNode.operand1 >= 0) -> if {
                                entryScheduleNode.operand1 -> aggregateValueIndex => entrySchedulePushValue
                                (entrySchedulePushValue > functionIndex! and entrySchedulePushValue < entryEnd! and not entryScheduled![entrySchedulePushValue]) -> if { false => entryScheduleReady! }
                            }
                            (entryScheduleReady! and (entryScheduleNode.kind == 12 or entryScheduleNode.kind == 14 or entryScheduleNode.kind == 16)) -> if {
                                entryScheduleNode.operand0 => entryScheduleSibling!
                                entryScheduleSibling! >= 0 -> while {
                                    entryScheduleSibling! -> aggregateValueIndex => entryScheduleAggregateValue
                                    not entryScheduled![entryScheduleAggregateValue] -> if { false => entryScheduleReady! }
                                    context.ir[entryScheduleSibling!].nextOperand => entryScheduleSibling!
                                }
                            }
                            (entryScheduleReady! and (entryScheduleNode.kind == 18 or entryScheduleNode.kind == 20)) -> if {
                                functionIndex! + 1 => entryPriorControlValueSearch!
                                entryPriorControlValueSearch! < entryEnd! -> while {
                                    (not entryScheduled![entryPriorControlValueSearch!] and (context.ir[entryPriorControlValueSearch!] -> sourceStart) < (entryScheduleNode -> sourceStart)) -> if {
                                        false => entryScheduleReady!
                                    }
                                    entryPriorControlValueSearch! + 1 => entryPriorControlValueSearch!
                                }
                            }
                            (entryScheduleReady! and entryScheduleNode.kind == 5) -> if {
                                false => entryMutableRead!
                                functionIndex! + 1 => entryMutableReadBindingSearch!
                                entryMutableReadBindingSearch! < entryEnd! -> while {
                                    (context.ir[entryMutableReadBindingSearch!].kind == 17 and context.ir[entryMutableReadBindingSearch!].symbol == entryScheduleNode.symbol and context.ir[entryMutableReadBindingSearch!].flags == 1) -> if { true => entryMutableRead! }
                                    entryMutableReadBindingSearch! + 1 => entryMutableReadBindingSearch!
                                }
                                entryMutableRead! -> if {
                                    functionIndex! + 1 => entryMutableReadBarrierSearch!
                                    entryMutableReadBarrierSearch! < entryEnd! -> while {
                                        context.ir[entryMutableReadBarrierSearch!] => entryMutableReadBarrier
                                        false => entryMutableReadInsideBarrier!
                                        entryScheduleNode.parent => entryMutableReadBarrierAncestor!
                                        (entryMutableReadBarrierAncestor! > functionIndex! and not entryMutableReadInsideBarrier!) -> while {
                                            entryMutableReadBarrierAncestor! == entryMutableReadBarrierSearch! -> if { true => entryMutableReadInsideBarrier! } else {
                                                context.ir[entryMutableReadBarrierAncestor!].parent => entryMutableReadBarrierAncestor!
                                            }
                                        }
                                        (not entryMutableReadInsideBarrier! and not entryScheduled![entryMutableReadBarrierSearch!] and (entryMutableReadBarrier.kind == 18 or entryMutableReadBarrier.kind == 20 or entryMutableReadBarrier.kind == 24 or entryMutableReadBarrier.kind == 25 or (entryMutableReadBarrier.kind == 17 and entryMutableReadBarrier.flags == 1) or (entryMutableReadBarrier.kind == 9 and entryMutableReadBarrier.opcode == -204)) and (entryMutableReadBarrier -> sourceStart) < (entryScheduleNode -> sourceStart)) -> if {
                                            false => entryScheduleReady!
                                        }
                                        entryMutableReadBarrierSearch! + 1 => entryMutableReadBarrierSearch!
                                    }
                                }
                            }
                            (entryScheduleReady! and (entryScheduleNode.kind == 6 or entryScheduleNode.kind == 18 or entryScheduleNode.kind == 20 or entryScheduleNode.kind == 24 or entryScheduleNode.kind == 25 or (entryScheduleNode.kind == 17 and entryScheduleNode.flags == 1) or (entryScheduleNode.kind == 9 and (entryScheduleNode.opcode == -204 or entryScheduleNode.opcode == -205)))) -> if {
                                true => entryRootEffect!
                                entryScheduleNode.parent => entryEffectAncestor!
                                (entryEffectAncestor! > functionIndex! and entryRootEffect!) -> while {
                                    (context.ir[entryEffectAncestor!].kind == 6 or context.ir[entryEffectAncestor!].kind == 18 or context.ir[entryEffectAncestor!].kind == 20 or context.ir[entryEffectAncestor!].kind == 24 or context.ir[entryEffectAncestor!].kind == 25 or (context.ir[entryEffectAncestor!].kind == 17 and context.ir[entryEffectAncestor!].flags == 1) or (context.ir[entryEffectAncestor!].kind == 9 and (context.ir[entryEffectAncestor!].opcode == -204 or context.ir[entryEffectAncestor!].opcode == -205))) -> if { false => entryRootEffect! } else { context.ir[entryEffectAncestor!].parent => entryEffectAncestor! }
                                }
                                entryRootEffect! -> if {
                                    functionIndex! + 1 => entryEarlierEffectSearch!
                                    entryEarlierEffectSearch! < entryEnd! -> while {
                                        context.ir[entryEarlierEffectSearch!] => entryEarlierEffect
                                        (not entryScheduled![entryEarlierEffectSearch!] and (entryEarlierEffect.kind == 6 or entryEarlierEffect.kind == 18 or entryEarlierEffect.kind == 20 or entryEarlierEffect.kind == 24 or entryEarlierEffect.kind == 25 or (entryEarlierEffect.kind == 17 and entryEarlierEffect.flags == 1) or (entryEarlierEffect.kind == 9 and (entryEarlierEffect.opcode == -204 or entryEarlierEffect.opcode == -205))) and (entryEarlierEffect -> sourceStart) < (entryScheduleNode -> sourceStart)) -> if {
                                            entryEarlierEffect.parent => entryEarlierEffectAncestor!
                                            true => entryEarlierRootEffect!
                                            false => entryEarlierInsideRegion!
                                            (entryEarlierEffectAncestor! > functionIndex! and entryEarlierRootEffect! and not entryEarlierInsideRegion!) -> while {
                                                (context.ir[entryEarlierEffectAncestor!].kind == 19 or context.ir[entryEarlierEffectAncestor!].kind == 20) -> if { true => entryEarlierInsideRegion! } else {
                                                    (context.ir[entryEarlierEffectAncestor!].kind == 6 or context.ir[entryEarlierEffectAncestor!].kind == 18 or context.ir[entryEarlierEffectAncestor!].kind == 20 or context.ir[entryEarlierEffectAncestor!].kind == 24 or context.ir[entryEarlierEffectAncestor!].kind == 25 or (context.ir[entryEarlierEffectAncestor!].kind == 17 and context.ir[entryEarlierEffectAncestor!].flags == 1) or (context.ir[entryEarlierEffectAncestor!].kind == 9 and (context.ir[entryEarlierEffectAncestor!].opcode == -204 or context.ir[entryEarlierEffectAncestor!].opcode == -205))) -> if { false => entryEarlierRootEffect! } else { context.ir[entryEarlierEffectAncestor!].parent => entryEarlierEffectAncestor! }
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
                    entryExpression.kind == 2 -> if {
                        entryExpression -> sourceToken => entryExpressionToken
                        TextLiteralRequest { sourceModule: entryExpression.sourceModule, token: entryExpressionToken } -> textLiteralLength => entryExpressionLength
                        "  %v$(entryExpressionIndex!)_ptr = insertvalue %sl.text poison, ptr @sl_str_$(entryExpressionIndex!), 0" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.text %v$(entryExpressionIndex!)_ptr, i64 $entryExpressionLength, 1" -> println
                    }
                    (entryExpression.kind == 5 and not (entryExpression.typeOrigin == 1 and entryExpression.typeSymbol == 16)) -> if {
                        -1 => entryBindingValueIr!
                        (entryExpression.operand0 >= 0 and context.ir[entryExpression.operand0].kind == 17) -> if { entryExpression.operand0 => entryBindingValueIr! }
                        entryBindingValueIr! >= 0 -> if {
                            context.ir[entryBindingValueIr!] => entryBindingValue
                            entryBindingValue.flags == 1 -> if {
                                entryBindingValueIr! -> mutableBindingRoot => entryBindingRoot
                                "  %v$(entryExpressionIndex!) = load " -> print
                                entryExpression -> writeIrType
                                ", ptr %slot$(entryBindingRoot), align " -> print
                                entryExpression -> storageAlign -> writeDecimalLine
                            } else {
                                context.ir[entryBindingValue.operand0] => entryBindingOperand
                                "  %v$(entryExpressionIndex!) = freeze " -> print
                                entryExpression -> writeIrType
                                " " -> print
                                (entryBindingOperand.kind == 3 or entryBindingOperand.kind == 4) -> if {
                                    entryBindingOperand -> sourceToken => entryBindingOperandToken
                                    entryBindingOperand.kind == 3 -> if { context.sources[entryBindingOperand.sourceModule] -> slice(entryBindingOperandToken.span.start, entryBindingOperandToken.span.length) -> print } else {
                                        ((context.sources[entryBindingOperand.sourceModule] -> byte(entryBindingOperandToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                    }
                                } else { "%v$(entryBindingValue.operand0)" -> print }
                                "" -> println
                            }
                        }
                    }
                    ((entryExpression.kind == 9 or entryExpression.kind == 6) and entryExpression.opcode == -201 and entryExpression.operand0 >= 0) -> if {
                        context.ir[entryExpression.operand0] => entryLengthBase
                        (entryLengthBase.typeOrigin == 1 and entryLengthBase.typeSymbol == 16) -> if {
                            "  %v$(entryExpressionIndex!) = call i64 @sl_argument_count()" -> println
                        } else {
                            "  %v$(entryExpressionIndex!)" -> print
                            entryExpression.typeSymbol == 2 -> if { "_raw" -> print }
                            " = extractvalue " -> print
                            entryLengthBase -> writeIrType
                            " %v$(entryExpression.operand0), " -> print
                            entryLengthBase.typeOrigin == 15 -> if { "2" -> println } else { "1" -> println }
                            entryExpression.typeSymbol == 2 -> if { "  %v$(entryExpressionIndex!) = trunc i64 %v$(entryExpressionIndex!)_raw to i32" -> println }
                        }
                    }
                    ((entryExpression.kind == 9 or entryExpression.kind == 6) and entryExpression.opcode == -202 and entryExpression.operand0 >= 0 and entryExpression.operand1 >= 0) -> if {
                        context.ir[entryExpression.operand0] => entryByteBase
                        context.ir[entryExpression.operand1] => entryByteIndex
                        entryByteIndex.typeSymbol -> integerWidth => entryByteIndexWidth
                        "  %v$(entryExpressionIndex!)_base = extractvalue " -> print
                        entryByteBase -> writeTextLikeType
                        " %v$(entryExpression.operand0), 0" -> println
                        (entryByteIndexWidth > 0 and entryByteIndexWidth < 64) -> if {
                            "  %v$(entryExpressionIndex!)_index = " -> print
                            entryByteIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                            " i$(entryByteIndexWidth) %v$(entryExpression.operand1) to i64" -> println
                        }
                        "  %v$(entryExpressionIndex!)_address = getelementptr i8, ptr %v$(entryExpressionIndex!)_base, i64 " -> print
                        (entryByteIndexWidth > 0 and entryByteIndexWidth < 64) -> if { "%v$(entryExpressionIndex!)_index" -> println } else { "%v$(entryExpression.operand1)" -> println }
                        "  %v$(entryExpressionIndex!) = load i8, ptr %v$(entryExpressionIndex!)_address, align 1" -> println
                    }
                    ((entryExpression.kind == 9 or entryExpression.kind == 6) and entryExpression.opcode == -203 and entryExpression.operand0 >= 0 and entryExpression.operand1 >= 0) -> if {
                        context.ir[entryExpression.operand1].nextOperand => entrySliceLength
                        context.ir[entryExpression.operand0] => entrySliceBase
                        context.ir[entryExpression.operand1] => entrySliceIndex
                        entrySliceIndex.typeSymbol -> integerWidth => entrySliceIndexWidth
                        "  %v$(entryExpressionIndex!)_base = extractvalue " -> print
                        entrySliceBase -> writeTextLikeType
                        " %v$(entryExpression.operand0), 0" -> println
                        (entrySliceIndexWidth > 0 and entrySliceIndexWidth < 64) -> if {
                            "  %v$(entryExpressionIndex!)_index = " -> print
                            entrySliceIndex -> integerNodeSigned -> if { "sext" -> print } else { "zext" -> print }
                            " i$(entrySliceIndexWidth) %v$(entryExpression.operand1) to i64" -> println
                        }
                        "  %v$(entryExpressionIndex!)_start = getelementptr i8, ptr %v$(entryExpressionIndex!)_base, i64 " -> print
                        (entrySliceIndexWidth > 0 and entrySliceIndexWidth < 64) -> if { "%v$(entryExpressionIndex!)_index" -> println } else { "%v$(entryExpression.operand1)" -> println }
                        "  %v$(entryExpressionIndex!)_ptr = insertvalue %sl.text poison, ptr %v$(entryExpressionIndex!)_start, 0" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.text %v$(entryExpressionIndex!)_ptr, i64 %v$(entrySliceLength), 1" -> println
                    }
                    (entryExpression.kind == 9 and entryExpression.opcode == -204 and entryExpression.operand0 >= 0 and entryExpression.operand1 >= 0) -> if {
                        WhileValueRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex!, nodeIndex: 0 } -> emitArrayPush
                    }
                    ((entryExpression.kind == 9 or entryExpression.kind == 6) and entryExpression.opcode == -205 and entryExpression.operand0 >= 0) -> if {
                        "  %v$(entryExpressionIndex!) = call %sl.source_text @sl_runtime_map_text(%sl.text %v$(entryExpression.operand0))" -> println
                    }
                    ((entryExpression.kind == 9 or entryExpression.kind == 6) and entryExpression.opcode == -206 and entryExpression.operand0 >= 0) -> if {
                        "  %v$(entryExpressionIndex!)_data = extractvalue %sl.text %v$(entryExpression.operand0), 0" -> println
                        "  %v$(entryExpressionIndex!)_length = extractvalue %sl.text %v$(entryExpression.operand0), 1" -> println
                        "  %v$(entryExpressionIndex!)_value0 = insertvalue %sl.source_text poison, ptr %v$(entryExpressionIndex!)_data, 0" -> println
                        "  %v$(entryExpressionIndex!)_value1 = insertvalue %sl.source_text %v$(entryExpressionIndex!)_value0, i64 %v$(entryExpressionIndex!)_length, 1" -> println
                        "  %v$(entryExpressionIndex!)_value2 = insertvalue %sl.source_text %v$(entryExpressionIndex!)_value1, ptr null, 2" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.source_text %v$(entryExpressionIndex!)_value2, i64 0, 3" -> println
                    }
                    (entryExpression.kind == 14 and (entryExpression.typeOrigin == 13 or entryExpression.typeOrigin == 14 or entryExpression.typeKind == 3)) -> if {
                        0 => entryArrayLength!
                        entryExpression.operand0 => entryArrayCountIndex!
                        entryArrayCountIndex! >= 0 -> while {
                            entryArrayLength! + 1 => entryArrayLength!
                            context.ir[entryArrayCountIndex!].nextOperand => entryArrayCountIndex!
                        }
                        entryArrayLength! * (entryExpression -> arrayElementSize) => entryArrayByteLength
                        "  %v$(entryExpressionIndex!)_data = call ptr @malloc(i64 $entryArrayByteLength)" -> println
                        entryExpression.operand0 => entryArrayElementIndex!
                        0 => entryArrayElementPosition!
                        entryArrayElementIndex! >= 0 -> while {
                            context.ir[entryArrayElementIndex!] => entryArrayElement
                            "  %v$(entryExpressionIndex!)_ptr$(entryArrayElementPosition!) = getelementptr " -> print
                            entryExpression -> writeArrayElementType
                            ", ptr %v$(entryExpressionIndex!)_data, i64 $(entryArrayElementPosition!)" -> println
                            "  store " -> print
                            entryExpression -> writeArrayElementType
                            " " -> print
                            (entryArrayElement.kind == 3 or entryArrayElement.kind == 4) -> if {
                                entryArrayElement -> sourceToken => entryArrayElementToken
                                entryArrayElement.kind == 3 -> if { context.sources[entryArrayElement.sourceModule] -> slice(entryArrayElementToken.span.start, entryArrayElementToken.span.length) -> print } else {
                                    ((context.sources[entryArrayElement.sourceModule] -> byte(entryArrayElementToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else { "%v$(entryArrayElementIndex!)" -> print }
                            ", ptr %v$(entryExpressionIndex!)_ptr$(entryArrayElementPosition!), align " -> print
                            entryExpression -> arrayElementAlign -> writeDecimalLine
                            entryArrayElement.nextOperand => entryArrayElementIndex!
                            entryArrayElementPosition! + 1 => entryArrayElementPosition!
                        }
                        "  %v$(entryExpressionIndex!)_0 = insertvalue %sl.array.i32 poison, ptr %v$(entryExpressionIndex!)_data, 0" -> println
                        "  %v$(entryExpressionIndex!)_1 = insertvalue %sl.array.i32 %v$(entryExpressionIndex!)_0, i64 $(entryArrayLength!), 1" -> println
                        "  %v$(entryExpressionIndex!) = insertvalue %sl.array.i32 %v$(entryExpressionIndex!)_1, i64 $(entryArrayLength!), 2" -> println
                    }
                    (entryExpression.kind == 17 and entryExpression.flags == 1) -> if {
                        entryExpressionIndex! -> mutableBindingRoot => entryMutableRoot
                        context.ir[entryMutableRoot] => entryMutableBinding
                        context.ir[entryExpression.operand0] => entryMutableValue
                        entryMutableBinding -> integerNodeWidth => entryMutableTargetWidth
                        entryMutableValue -> integerNodeWidth => entryMutableSourceWidth
                        (entryMutableTargetWidth > 0 and entryMutableSourceWidth > 0 and entryMutableTargetWidth != entryMutableSourceWidth and entryMutableValue.kind != 3 and entryMutableValue.kind != 4) => castEntryMutableValue!
                        castEntryMutableValue! -> if {
                            "  %v$(entryExpressionIndex!)_mutable_cast = " -> print
                            entryMutableTargetWidth < entryMutableSourceWidth -> if { "trunc " -> print } else { entryMutableValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print } }
                            entryMutableValue -> writeIrType
                            " %v$(entryExpression.operand0) to " -> print
                            entryMutableBinding -> writeIrType
                            "" -> println
                        }
                        "  store " -> print
                        entryMutableBinding -> writeIrType
                        " " -> print
                        (entryMutableValue.kind == 3 or entryMutableValue.kind == 4) -> if {
                            entryMutableValue -> sourceToken => entryMutableToken
                            entryMutableValue.kind == 3 -> if { context.sources[entryMutableValue.sourceModule] -> slice(entryMutableToken.span.start, entryMutableToken.span.length) -> print } else {
                                ((context.sources[entryMutableValue.sourceModule] -> byte(entryMutableToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            }
                        } else { castEntryMutableValue! -> if { "%v$(entryExpressionIndex!)_mutable_cast" -> print } else { "%v$(entryExpression.operand0)" -> print } }
                        ", ptr %slot$(entryMutableRoot), align " -> print
                        entryMutableBinding -> storageAlign -> writeDecimalLine
                    }
                    entryExpression.kind == 24 -> if {
                        WhileValueRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex!, nodeIndex: entryExpressionIndex! } -> emitIndexAssignment
                    }
                    entryExpression.kind == 25 -> if {
                        WhileValueRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex!, nodeIndex: entryExpressionIndex! } -> emitMemberAssignment
                    }
                    entryExpression.kind == 15 -> if {
                        context.ir[entryExpression.operand0] => entryIndexedValue
                        context.ir[entryExpression.operand1] => entryIndexValue
                        (entryIndexedValue.typeOrigin == 1 and entryIndexedValue.typeSymbol == 16) -> if {
                            entryIndexValue -> integerNodeWidth => entryArgumentIndexWidth
                            (entryIndexValue.kind != 3 and entryArgumentIndexWidth > 0 and entryArgumentIndexWidth < 64) -> if {
                                "  %v$(entryExpressionIndex!)_argument_index = " -> print
                                entryIndexValue -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                                entryIndexValue -> writeIrType
                                " %v$(entryExpression.operand1) to i64" -> println
                            }
                            "  %v$(entryExpressionIndex!) = call %sl.text @sl_argument(i64 " -> print
                            entryIndexValue.kind == 3 -> if {
                                entryIndexValue -> sourceToken => entryIndexToken
                                context.sources[entryIndexValue.sourceModule] -> slice(entryIndexToken.span.start, entryIndexToken.span.length) -> print
                            } else {
                                (entryArgumentIndexWidth > 0 and entryArgumentIndexWidth < 64) -> if { "%v$(entryExpressionIndex!)_argument_index" -> print } else { "%v$(entryExpression.operand1)" -> print }
                            }
                            ")" -> println
                        }
                    }
                    (entryExpression.kind == 7 or entryExpression.kind == 8) -> if {
                        context.ir[entryExpression.operand0] => entryLeft
                        0 => entryBinaryWidth!
                        false => entryLeftPromoted!
                        false => entryRightPromoted!
                        entryExpression.kind == 8 -> if {
                            context.ir[entryExpression.operand1] => entryRight
                            entryLeft -> integerNodeWidth => entryLeftWidth!
                            entryRight -> integerNodeWidth => entryRightWidth!
                            (entryLeftWidth! > 0 and entryRightWidth! > 0) -> if {
                                entryLeftWidth! > entryRightWidth! -> if { entryLeftWidth! => entryBinaryWidth! } else { entryRightWidth! => entryBinaryWidth! }
                                IntegerPromotionRequest { expressionIndex: entryExpressionIndex!, operandIndex: entryExpression.operand0, targetWidth: entryBinaryWidth!, ownerIndex: functionIndex!, right: false } -> emitIntegerPromotion => entryLeftPromoted!
                                IntegerPromotionRequest { expressionIndex: entryExpressionIndex!, operandIndex: entryExpression.operand1, targetWidth: entryBinaryWidth!, ownerIndex: functionIndex!, right: true } -> emitIntegerPromotion => entryRightPromoted!
                            }
                        }
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
                            (entryExpression.opcode == grammar.tokenIdLess or entryExpression.opcode == grammar.tokenIdLessEqual or entryExpression.opcode == grammar.tokenIdGreater or entryExpression.opcode == grammar.tokenIdGreaterEqual) -> if {
                                entryExpression -> integerComparisonOperation => entryOperation!
                            }
                            entryExpression.opcode == -24 -> if { "or" => entryOperation! }
                            entryExpression.opcode == -25 -> if { "and" => entryOperation! }
                        }
                        "  %v$(entryExpressionIndex!) = $(entryOperation!) " -> print
                        entryBinaryWidth! > 0 -> if { "i$(entryBinaryWidth!)" -> print } else { entryLeft -> writeIrType }
                        " " -> print
                        (entryExpression.kind == 7 and entryExpression.opcode != -26) -> if { "0" -> print } else {
                            (entryLeft.kind == 3 or entryLeft.kind == 4) -> if {
                                entryLeft -> sourceToken => entryLeftToken
                                entryLeft.kind == 3 -> if { context.sources[entryLeft.sourceModule] -> slice(entryLeftToken.span.start, entryLeftToken.span.length) -> print } else {
                                    ((context.sources[entryLeft.sourceModule] -> byte(entryLeftToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else {
                                entryLeftPromoted! -> if { "%v$(entryExpressionIndex!)_left_wide" -> print } else { "%v$(entryExpression.operand0)" -> print }
                            }
                        }
                        ", " -> print
                        entryExpression.kind == 7 -> if {
                            entryExpression.opcode == -26 -> if { "true" -> println } else {
                                (entryLeft.kind == 3 or entryLeft.kind == 4) -> if {
                                    entryLeft -> sourceToken => entryUnaryToken
                                    context.sources[entryLeft.sourceModule] -> slice(entryUnaryToken.span.start, entryUnaryToken.span.length) -> println
                                } else { "%v$(entryExpression.operand0)" -> println }
                            }
                        } else {
                            context.ir[entryExpression.operand1] => entryRight
                            (entryRight.kind == 3 or entryRight.kind == 4) -> if {
                                entryRight -> sourceToken => entryRightToken
                                entryRight.kind == 3 -> if { context.sources[entryRight.sourceModule] -> slice(entryRightToken.span.start, entryRightToken.span.length) -> println } else {
                                    ((context.sources[entryRight.sourceModule] -> byte(entryRightToken.span.start)) == UInt8(116)) -> if { "1" -> println } else { "0" -> println }
                                }
                            } else {
                                entryRightPromoted! -> if { "%v$(entryExpressionIndex!)_right_wide" -> println } else { "%v$(entryExpression.operand1)" -> println }
                            }
                        }
                    }
                    (entryExpression.kind == 6 and entryExpression.opcode != -205) -> if {
                        (entryExpression.symbol == -101 or entryExpression.symbol == -102) -> if {
                            context.ir[entryExpression.operand0] => runtimeArgument
                            runtimeArgument.kind == 2 -> if {
                                runtimeArgument -> sourceToken => runtimeArgumentToken
                                Int(TextLiteralRequest { sourceModule: runtimeArgument.sourceModule, token: runtimeArgumentToken } -> textLiteralLength) => runtimeArgumentLength
                                runtimeArgument -> sourceInterpolations => entryExpressionInterpolation!
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
                                            Int(entryExpressionInterpolation! -> len) - 1 => entryExpressionNodeIndex!
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
                                                                    entryExpressionLeftValue -> sourceToken => entryExpressionLeftToken
                                                                    context.sources[entryExpressionLeftValue.sourceModule] -> slice(entryExpressionLeftToken.span.start, entryExpressionLeftToken.span.length) -> print
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[entryExpressionLeftBinding!].operand0 -> writeDecimal
                                                                }
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
                                                                    entryExpressionUnaryValue -> sourceToken => entryExpressionUnaryToken
                                                                    context.sources[entryExpressionUnaryValue.sourceModule] -> slice(entryExpressionUnaryToken.span.start, entryExpressionUnaryToken.span.length) -> println
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[entryExpressionUnaryBinding!].operand0 -> writeDecimalLine
                                                                }
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
                                                                    entryExpressionRightValue -> sourceToken => entryExpressionRightToken
                                                                    context.sources[entryExpressionRightValue.sourceModule] -> slice(entryExpressionRightToken.span.start, entryExpressionRightToken.span.length) -> println
                                                                } else {
                                                                    "%v" -> print
                                                                    context.ir[entryExpressionRightBinding!].operand0 -> writeDecimalLine
                                                                }
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
                                            entryExpressionRootTypeSymbol! -> integerWidth => entryExpressionRootWidth!
                                            (entryExpressionRootBinding! >= 0 and context.ir[entryExpressionRootBinding!].operand0 >= 0) -> if {
                                                context.ir[context.ir[entryExpressionRootBinding!].operand0] -> integerNodeWidth => entryExpressionBoundWidth
                                                entryExpressionBoundWidth > 0 -> if { entryExpressionBoundWidth => entryExpressionRootWidth! }
                                            }
                                            entryExpressionRootTypeSymbol! == 1 -> if {
                                                "  %v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)_text_ptr = extractvalue %sl.text " -> print
                                                entryExpressionRoot.kind == 1 -> if {
                                                    "%v" -> print
                                                    context.ir[entryExpressionRootBinding!].operand0 -> writeDecimal
                                                } else { "%v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)" -> print }
                                                ", 0" -> println
                                                "  %v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)_text_len = extractvalue %sl.text " -> print
                                                entryExpressionRoot.kind == 1 -> if {
                                                    "%v" -> print
                                                    context.ir[entryExpressionRootBinding!].operand0 -> writeDecimal
                                                } else { "%v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)" -> print }
                                                ", 1" -> println
                                                "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)_text_ptr, i64 %v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)_text_len, i1 false)" -> println
                                            } else {
                                            entryExpressionRootTypeSymbol! == 23 -> if { "  call void @sl_runtime_print_i1(i1 " -> print } else {
                                                entryExpressionRootWidth! == 64 -> if { "  call void @sl_runtime_print_i64(i64 " -> print } else { "  call void @sl_runtime_print_i32(i32 " -> print }
                                            }
                                            (entryExpressionRoot.kind == 0 or entryExpressionRoot.kind == 4) -> if {
                                                entryExpressionRoot.kind == 4 -> if {
                                                    (context.sources[runtimeArgument.sourceModule] -> byte(entryExpressionRoot.payloadStart)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                } else { context.sources[runtimeArgument.sourceModule] -> slice(entryExpressionRoot.payloadStart, entryExpressionRoot.payloadLength) -> print }
                                            } else {
                                            entryExpressionRoot.kind == 1 -> if {
                                                entryExpressionRootBinding! >= 0 -> if {
                                                    context.ir[context.ir[entryExpressionRootBinding!].operand0] => entryExpressionRootValue
                                                    (entryExpressionRootValue.kind == 3 or entryExpressionRootValue.kind == 4) -> if {
                                                        entryExpressionRootValue -> sourceToken => entryExpressionRootToken
                                                        entryExpressionRootValue.kind == 4 -> if {
                                                            (context.sources[entryExpressionRootValue.sourceModule] -> byte(entryExpressionRootToken.span.start)) == UInt8(116) -> if { "1" -> print } else { "0" -> print }
                                                        } else { context.sources[entryExpressionRootValue.sourceModule] -> slice(entryExpressionRootToken.span.start, entryExpressionRootToken.span.length) -> print }
                                                    } else {
                                                        "%v" -> print
                                                        context.ir[entryExpressionRootBinding!].operand0 -> writeDecimal
                                                    }
                                                }
                                            } else { "%v$(entryExpressionIndex!)_expression$(entryExpressionRootSearch!)" -> print }
                                            }
                                            entryExpressionRootWidth! == 64 -> if {
                                                ", i1 " -> print
                                                entryExpressionRootTypeSymbol! -> integerSigned -> if { "true" -> print } else { "false" -> print }
                                            }
                                            ", i1 false)" -> println
                                            }
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
                                UIntSize(interpolationContentStart) => interpolationSegmentStart!
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
                                                context.ranges[runtimeArgument.sourceModule] => interpolationRange
                                                0 => interpolationSymbolIndex!
                                                interpolationSymbolIndex! < interpolationRange.symbolCount -> while {
                                                    context.symbols[interpolationRange.symbolStart + interpolationSymbolIndex!] => interpolationSymbol
                                                    interpolationSymbol.kind == 9 -> if {
                                                        context.tokens[interpolationRange.tokenStart + interpolationSymbol.nameToken] => interpolationSymbolToken
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
                                            interpolationValue -> sourceToken => interpolationValueToken
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
                                entryExpression.opcode == -203 -> if {
                                    "  %v$(entryExpressionIndex!)_runtime_ptr = extractvalue %sl.text %v$(entryExpressionIndex!), 0" -> println
                                    "  %v$(entryExpressionIndex!)_runtime_len = extractvalue %sl.text %v$(entryExpressionIndex!), 1" -> println
                                } else {
                                    "  %v$(entryExpressionIndex!)_runtime_ptr = extractvalue %sl.text %v$(entryExpression.operand0), 0" -> println
                                    "  %v$(entryExpressionIndex!)_runtime_len = extractvalue %sl.text %v$(entryExpression.operand0), 1" -> println
                                }
                                "  call void @sl_runtime_print(ptr %v$(entryExpressionIndex!)_runtime_ptr, i64 %v$(entryExpressionIndex!)_runtime_len, i1 " -> print
                            }
                            entryExpression.symbol == -102 -> if { "true)" -> println } else { "false)" -> println }
                        } else {
                        (entryExpression.targetModule < 0 and entryExpression.typeOrigin == 1 and (entryExpression.typeSymbol -> integerWidth) > 0 and entryExpression.operand0 >= 0) -> if {
                            context.ir[entryExpression.operand0] => entryIntegerArgument
                            entryExpression.typeSymbol -> integerWidth => entryTargetWidth
                            entryIntegerArgument.typeSymbol -> integerWidth => entryInputWidth
                            "  %v$(entryExpressionIndex!) = " -> print
                            entryTargetWidth < entryInputWidth -> if { "trunc " -> print } else {
                                entryTargetWidth > entryInputWidth -> if {
                                    entryIntegerArgument -> integerNodeSigned -> if { "sext " -> print } else { "zext " -> print }
                                } else { "add " -> print }
                            }
                            entryIntegerArgument -> writeIrType
                            " " -> print
                            entryIntegerArgument.kind == 3 -> if {
                                entryIntegerArgument -> sourceToken => entryIntegerToken
                                context.sources[entryIntegerArgument.sourceModule] -> slice(entryIntegerToken.span.start, entryIntegerToken.span.length) -> print
                            } else { "%v$(entryExpression.operand0)" -> print }
                            entryTargetWidth == entryInputWidth -> if { ", 0" -> println } else {
                                " to " -> print
                                entryExpression -> writeIrType
                                "" -> println
                            }
                        } else {
                        (entryExpression.typeOrigin == 1 and entryExpression.typeSymbol == 0) -> if { "  call " -> print } else { "  %v$(entryExpressionIndex!) = call " -> print }
                        entryExpression -> writeIrType
                        " @sl_m$(entryExpression.targetModule)_s$(entryExpression.symbol)(" -> print
                        entryExpression.operand0 >= 0 -> if {
                            context.ir[entryExpression.operand0] => entryArgument
                            entryArgument -> writeIrType
                            " " -> print
                            entryArgument.kind == 2 -> if {
                                entryArgument -> sourceToken => entryArgumentToken
                                Int(TextLiteralRequest { sourceModule: entryArgument.sourceModule, token: entryArgumentToken } -> textLiteralLength) => entryArgumentLength
                                "{ ptr @sl_str_$(entryExpression.operand0), i64 $entryArgumentLength }" -> print
                            } else {
                            (entryArgument.kind == 3 or entryArgument.kind == 4) -> if {
                                entryArgument -> sourceToken => entryArgumentToken
                                entryArgument.kind == 3 -> if {
                                    context.sources[entryArgument.sourceModule] -> slice(entryArgumentToken.span.start, entryArgumentToken.span.length) -> print
                                } else {
                                    ((context.sources[entryArgument.sourceModule] -> byte(entryArgumentToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else { "%v$(entryExpression.operand0)" -> print }
                            }
                        }
                        ")" -> println
                        }
                        }
                    }
                    entryExpression.kind == 18 -> if {
                        context.ir[entryExpression.operand0] => entryIfCondition
                        (entryExpression.typeSymbol != 0 and entryExpression.nextOperand >= 0) -> if {
                            "  %if$(entryExpressionIndex!)_result = alloca " -> print
                            entryExpression -> writeIrType
                            ", align " -> print
                            entryExpression -> storageAlign -> writeDecimalLine
                        }
                        (entryIfCondition.kind == 8 and (entryIfCondition.opcode == -25 or entryIfCondition.opcode == -24) or (entryIfCondition.kind == 7 and entryIfCondition.opcode == -26)) => entryIfShortCircuit
                        entryIfShortCircuit -> if {
                            WhileBranchRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex!, conditionIndex: entryExpression.operand0 } -> emitWhileBranch
                            "while$(entryExpressionIndex!)_body:" -> println
                            "  br label %if$(entryExpressionIndex!)_then" -> println
                            "while$(entryExpressionIndex!)_exit:" -> println
                            entryExpression.nextOperand >= 0 -> if { "  br label %if$(entryExpressionIndex!)_else" -> println } else { "  br label %if$(entryExpressionIndex!)_merge" -> println }
                        } else {
                            "  br i1 " -> print
                            entryIfCondition.kind == 4 -> if {
                                entryIfCondition -> sourceToken => entryIfConditionToken
                                ((context.sources[entryIfCondition.sourceModule] -> byte(entryIfConditionToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                            } else { "%v$(entryExpression.operand0)" -> print }
                            entryExpression.nextOperand >= 0 -> if { ", label %if$(entryExpressionIndex!)_then, label %if$(entryExpressionIndex!)_else" -> println } else { ", label %if$(entryExpressionIndex!)_then, label %if$(entryExpressionIndex!)_merge" -> println }
                        }
                        "if$(entryExpressionIndex!)_then:" -> println
                        entryExpression.operand1 -> emitRegion
                        (entryExpression.typeSymbol != 0 and entryExpression.nextOperand >= 0) -> if {
                            entryExpression.operand1 -> regionResultValueIndex => entryThenStoreIndex
                            context.ir[entryThenStoreIndex] => entryThenStoreValue
                            "  store " -> print
                            entryExpression -> writeIrType
                            " " -> print
                            (entryThenStoreValue.kind == 3 or entryThenStoreValue.kind == 4) -> if {
                                entryThenStoreValue -> sourceToken => entryThenStoreToken
                                entryThenStoreValue.kind == 3 -> if { context.sources[entryThenStoreValue.sourceModule] -> slice(entryThenStoreToken.span.start, entryThenStoreToken.span.length) -> print } else {
                                    ((context.sources[entryThenStoreValue.sourceModule] -> byte(entryThenStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                }
                            } else { "%v$(entryThenStoreIndex)" -> print }
                            ", ptr %if$(entryExpressionIndex!)_result, align " -> print
                            entryExpression -> storageAlign -> writeDecimalLine
                        }
                        "  br label %if$(entryExpressionIndex!)_merge" -> println
                        entryExpression.nextOperand >= 0 -> if {
                            "if$(entryExpressionIndex!)_else:" -> println
                            entryExpression.nextOperand -> emitRegion
                            entryExpression.typeSymbol != 0 -> if {
                                entryExpression.nextOperand -> regionResultValueIndex => entryElseStoreIndex
                                context.ir[entryElseStoreIndex] => entryElseStoreValue
                                "  store " -> print
                                entryExpression -> writeIrType
                                " " -> print
                                (entryElseStoreValue.kind == 3 or entryElseStoreValue.kind == 4) -> if {
                                    entryElseStoreValue -> sourceToken => entryElseStoreToken
                                    entryElseStoreValue.kind == 3 -> if { context.sources[entryElseStoreValue.sourceModule] -> slice(entryElseStoreToken.span.start, entryElseStoreToken.span.length) -> print } else {
                                        ((context.sources[entryElseStoreValue.sourceModule] -> byte(entryElseStoreToken.span.start)) == UInt8(116)) -> if { "1" -> print } else { "0" -> print }
                                    }
                                } else { "%v$(entryElseStoreIndex)" -> print }
                                ", ptr %if$(entryExpressionIndex!)_result, align " -> print
                                entryExpression -> storageAlign -> writeDecimalLine
                            }
                            "  br label %if$(entryExpressionIndex!)_merge" -> println
                        }
                        "if$(entryExpressionIndex!)_merge:" -> println
                        (entryExpression.typeSymbol != 0 and entryExpression.nextOperand >= 0) -> if {
                            "  %v$(entryExpressionIndex!) = load " -> print
                            entryExpression -> writeIrType
                            ", ptr %if$(entryExpressionIndex!)_result, align " -> print
                            entryExpression -> storageAlign -> writeDecimalLine
                        }
                    }
                    entryExpression.kind == 20 -> if {
                        "  br label %while$(entryExpressionIndex!)_header" -> println
                        "while$(entryExpressionIndex!)_header:" -> println
                        WhileBranchRequest { whileIndex: entryExpressionIndex!, ownerIndex: functionIndex!, conditionIndex: entryExpression.operand0 } -> emitWhileBranch
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

usesSourceTextRuntime context: EmitContext -> Bool {
    false => usesRuntime!
    0 => nodeIndex!
    nodeIndex! < (context.ir -> len) -> while {
        context.ir[nodeIndex!] -> isSourceTextType -> if { true => usesRuntime! }
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
            context.tokens[context.ranges[argument.sourceModule].tokenStart + argument.payloadToken] => token
            token.span.start + UIntSize(1) => byteIndex!
            token.span.start + token.span.length - UIntSize(1) => byteEnd
            byteIndex! < byteEnd -> while {
                ((context.sources[argument.sourceModule] -> byte(byteIndex!)) == UInt8(36) and byteIndex! + UIntSize(1) < byteEnd) -> if {
                    context.sources[argument.sourceModule] -> byte(byteIndex! + UIntSize(1)) => interpolationNextByte
                    (interpolationNextByte != UInt8(40) and ((interpolationNextByte >= UInt8(65) and interpolationNextByte <= UInt8(90)) or (interpolationNextByte >= UInt8(97) and interpolationNextByte <= UInt8(122)) or interpolationNextByte == UInt8(95) or interpolationNextByte >= UInt8(128))) -> if { true => usesInterpolation! }
                }
                byteIndex! + UIntSize(1) => byteIndex!
            }
            context.interpolationRanges[argument.sourceModule] => interpolationRange
            0 => interpolationIndex!
            interpolationIndex! < interpolationRange.nodeCount -> while {
                context.interpolations[interpolationRange.nodeStart + interpolationIndex!] => interpolationNode
                (interpolationNode.sourceToken == argument.payloadToken and interpolationNode.parent < 0) -> if {
                    (interpolationNode.typeSymbol >= 2 and interpolationNode.typeSymbol <= 14) -> if { true => usesInterpolation! }
                    # Member result types are resolved against the typed base during
                    # emission, after interpolation lowering. Keep the integer
                    # formatter available for either direct or nested members.
                    interpolationNode.kind == 5 -> if { true => usesInterpolation! }
                    interpolationNode.kind == 1 -> if {
                        0 => valueSearch!
                        valueSearch! < (context.ir -> len) -> while {
                            context.ir[valueSearch!] => valueNode
                            ((valueNode.kind == 10 or valueNode.kind == 17) and valueNode.sourceModule == argument.sourceModule and valueNode.symbol == interpolationNode.symbol and valueNode.typeSymbol >= 2 and valueNode.typeSymbol <= 14) -> if { true => usesInterpolation! }
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
            context.interpolationRanges[argument.sourceModule] => interpolationRange
            0 => interpolationIndex!
            interpolationIndex! < interpolationRange.nodeCount -> while {
                context.interpolations[interpolationRange.nodeStart + interpolationIndex!] => interpolationNode
                (interpolationNode.sourceToken == argument.payloadToken and interpolationNode.parent < 0) -> if {
                    interpolationNode.typeSymbol == 23 -> if { true => usesInterpolation! }
                    # See usesIntInterpolation: the concrete member field type is
                    # known only when the surrounding typed function is emitted.
                    interpolationNode.kind == 5 -> if { true => usesInterpolation! }
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

emitIntTextRuntime: -> Unit uses Console {
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
    """
    define internal void @sl_runtime_print_i64(i64 %value, i1 %signed, i1 %newline) {
    entry:
      %buffer = alloca [21 x i8], align 1
      %end = getelementptr [21 x i8], ptr %buffer, i64 0, i64 21
      %negative_value = icmp slt i64 %value, 0
      %negative = and i1 %signed, %negative_value
      %negated = sub i64 0, %value
      %magnitude = select i1 %negative, i64 %negated, i64 %value
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

emitBoolTextRuntime: -> Unit uses Console {
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

emitWindowsTextRuntime: -> Unit uses Console {
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

emitLinuxTextRuntime: -> Unit uses Console {
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

emitWasmTextRuntime: -> Unit uses Console {
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

public emit sources: move [Text; ~] -> Unit uses Console {
    llvmTarget.windowsX64 => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    PrepareRequest { sources: sources, pointerBitWidth: target.pointerBitWidth } => prepareRequest!
    prepareRequest! -> prepare => context!
    context! -> usesTextRuntime -> if { emitWindowsTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> usesSourceTextRuntime => needsSourceTextRuntime
    -1 => runtimeModule!
    0 => runtimeModuleSearch!
    runtimeModuleSearch! < (context!.modules -> len) -> while {
        (context!.modules)[runtimeModuleSearch!] => moduleIdentity
        moduleIdentity.pathHash == UInt64(12254170256457402476) -> if { moduleIdentity.sourceIndex => runtimeModule! }
        runtimeModuleSearch! + 1 => runtimeModuleSearch!
    }
    context! -> emitCore
    runtimeModule! >= 0 -> if {
        runtimeModule! -> llvmRuntime.emitWindows
    }
    needsSourceTextRuntime -> if {
        runtimeModule! < 0 -> if { llvmRuntime.emitWindowsSourceTextDeclarations }
        llvmRuntime.emitWindowsSourceText
    }
}

public emitLinux sources: move [Text; ~] -> Unit uses Console {
    llvmTarget.linuxX64 => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    PrepareRequest { sources: sources, pointerBitWidth: target.pointerBitWidth } => prepareRequest!
    prepareRequest! -> prepare => context!
    context! -> usesTextRuntime -> if { emitLinuxTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> emitCore
}

public emitWasm sources: move [Text; ~] -> Unit uses Console {
    llvmTarget.wasm32Browser => target
    target.dataLayoutLine -> println
    target.tripleLine -> println
    PrepareRequest { sources: sources, pointerBitWidth: target.pointerBitWidth } => prepareRequest!
    prepareRequest! -> prepare => context!
    context! -> usesTextRuntime -> if { emitWasmTextRuntime }
    context! -> usesIntInterpolation -> if { emitIntTextRuntime }
    context! -> usesBoolInterpolation -> if { emitBoolTextRuntime }
    context! -> emitCore
}

public emitFiles paths: [Text; ~] -> Unit uses Console, File {
    [file.SourceText; ~] => owners!
    0 => pathIndex!
    pathIndex! < (paths -> len) -> while {
        paths[pathIndex!] => path
        path -> file.mapText => owner!
        owners! -> push(owner!)
        pathIndex! + 1 => pathIndex!
    }
    [Text; ~] => sources!
    0 => sourceIndex!
    sourceIndex! < (owners! -> len) -> while {
        owners![sourceIndex!] -> len => sourceLength
        owners![sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        sources! -> push(source)
        sourceIndex! + 1 => sourceIndex!
    }
    sources! -> emit
    owners! -> len => ownerCount
}

public emitLinuxFiles paths: [Text; ~] -> Unit uses Console, File {
    [file.SourceText; ~] => owners!
    0 => pathIndex!
    pathIndex! < (paths -> len) -> while {
        paths[pathIndex!] => path
        path -> file.mapText => owner!
        owners! -> push(owner!)
        pathIndex! + 1 => pathIndex!
    }
    [Text; ~] => sources!
    0 => sourceIndex!
    sourceIndex! < (owners! -> len) -> while {
        owners![sourceIndex!] -> len => sourceLength
        owners![sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        sources! -> push(source)
        sourceIndex! + 1 => sourceIndex!
    }
    sources! -> emitLinux
    owners! -> len => ownerCount
}

public emitWasmFiles paths: [Text; ~] -> Unit uses Console, File {
    [file.SourceText; ~] => owners!
    0 => pathIndex!
    pathIndex! < (paths -> len) -> while {
        paths[pathIndex!] => path
        path -> file.mapText => owner!
        owners! -> push(owner!)
        pathIndex! + 1 => pathIndex!
    }
    [Text; ~] => sources!
    0 => sourceIndex!
    sourceIndex! < (owners! -> len) -> while {
        owners![sourceIndex!] -> len => sourceLength
        owners![sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        sources! -> push(source)
        sourceIndex! + 1 => sourceIndex!
    }
    sources! -> emitWasm
    owners! -> len => ownerCount
}
