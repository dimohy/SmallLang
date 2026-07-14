namespace smalllang.compiler.ir.typed

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.analysis as analysis
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.semantic.expression_type_ids as expressionTypeIds
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.modules as modules
import smalllang.compiler.semantic.module_resolve as moduleResolve
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.qualified as qualified
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.semantic.type_ids as typeIds
import smalllang.compiler.semantic.type_terms as typeTerms
import smalllang.compiler.semantic.types as semanticTypes
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar
import sys.file as file

# Stable, flat typed IR. Indexes are relocatable array offsets so later LLVM
# lowering can consume the table without allocating an object graph.
# Kinds: 0 function, 1 return, 2 Text constant, 3 Int constant,
# 4 Bool constant, 5 name, 6 call, 7 unary, 8 binary, 9 other expression,
# 10 parameter, 11 entry point, 12 struct literal, 13 member access,
# 14 array literal, 15 index access, 16 dictionary literal, 17 binding,
# 18 structured if, 19 control-flow region, 20 structured while,
# 21 loop exit, 22 guarded loop exit (opcode 0 break, 1 continue;
# operand0 targets the while, guarded operand1 is the Bool condition),
# 23 explicit return (operand0 is the optional returned value). AST kind 48
# lowers to an ordinary call node; its child operations preserve the role body.
public struct TypedIrNode {
    kind: Int
    parent: Int
    sourceModule: Int
    astNode: Int
    symbol: Int
    targetModule: Int
    typeOrigin: Int
    typeModule: Int
    typeSymbol: Int
    typeId: Int
    typeKind: Int
    typeFlags: Int
    payloadToken: Int
    opcode: Int
    operand0: Int
    operand1: Int
    nextOperand: Int
    flags: Int
}

# A move event is a consuming call whose argument is a resolved local owner.
# Cleanup lowering uses this side table instead of overloading value/type flags.
public struct MoveEvent {
    siteIr: Int
    sourceModule: Int
    symbol: Int
    regionIr: Int
    memberIr: Int
}

# A suspension point belongs to one async function and receives a stable,
# one-based resume state in source order. Kind distinguishes child await from
# cooperative yield. awaitIr is -1 until a corresponding node is available;
# awaitAst is the stable source-site AST index for either kind.
public struct CoroutineSuspendPoint {
    functionIr: Int
    sourceModule: Int
    awaitAst: Int
    awaitIr: Int
    state: Int
    # 0 awaits a child Task; 1 cooperatively yields the current Task.
    kind: Int
}

# One frame slot records a binding that is defined before a suspension and
# referenced after it inside the same async function. Parameters stay in the
# function context and therefore do not appear in this side table. In flags,
# bit 0 marks mutable storage, bit 1 marks an obvious heap owner, and bit 2
# marks an affine Task whose handle and context must move into the frame.
public struct CoroutineFrameSlot {
    functionIr: Int
    state: Int
    sourceModule: Int
    symbol: Int
    typeOrigin: Int
    typeModule: Int
    typeSymbol: Int
    typeId: Int
    typeKind: Int
    typeFlags: Int
    flags: Int
}

# One lowering produces every coroutine side table. Consumers that need more
# than one table keep this aggregate and avoid rebuilding typed IR.
public struct CoroutinePlan {
    ir: [TypedIrNode; ~]
    points: [CoroutineSuspendPoint; ~]
    slots: [CoroutineFrameSlot; ~]
    destroys: [CoroutineFrameSlot; ~]
}

public struct TypedIrRequest {
    sources: [Text; ~]
    types: [typeIds.SemanticType; ~]
    references: [typeIds.TypeReference; ~]
    fields: [typeIds.NominalField; ~]
    nominal: [nominalTypes.NominalType; ~]
    composite: [compositeTypes.CompositeType; ~]
    modules: [modules.ModuleIdentity; ~]
    qualified: [qualified.QualifiedResolution; ~]
    calls: [calls.ModuleCallResolution; ~]
    analysisRanges: [analysis.SourceAnalysisRange; ~]
    analysisNodes: [ast.AstNode; ~]
    analysisTokens: [syntax.SyntaxToken; ~]
    analysisSymbols: [symbols.Symbol; ~]
    analysisNames: [resolution.ResolvedName; ~]
}

struct IntrinsicNameRequest {
    source: Text
    token: syntax.SyntaxToken
}

intrinsicOpcode request: IntrinsicNameRequest -> Int {
    -1 => opcode!
    request.token.span.length == UIntSize(3) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(110)) -> if { -201 => opcode! }
    }
    request.token.span.length == UIntSize(4) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(98) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(121) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(116) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(101)) -> if { -202 => opcode! }
    }
    request.token.span.length == UIntSize(5) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(115) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(105) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(99) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(101)) -> if { -203 => opcode! }
    }
    opcode!
}

public lowerContext prepared: semanticContext.CompilationContext -> [TypedIrNode; ~] {
    prepared -> expressionTypeIds.resolveContext => recursiveTypes
    [typeIds.SemanticType; ~] => recursiveSemanticTypes!
    recursiveTypes.types -> each recursiveSemanticType {
        recursiveSemanticTypes! -> push(recursiveSemanticType)
    }
    [typeIds.SemanticType; ~] => classificationTypes!
    recursiveSemanticTypes! -> each classificationType { classificationTypes! -> push(classificationType) }
    [typeIds.NominalField; ~] => classificationFields!
    recursiveTypes.fields -> each classificationField { classificationFields! -> push(classificationField) }
    typeIds.TypeClassificationRequest { types: classificationTypes!, fields: classificationFields! } => classificationRequest!
    classificationRequest! -> typeIds.classify => recursiveTypeFlags!
    [typeIds.TypeReference; ~] => recursiveReferences!
    recursiveTypes.references -> each recursiveReference {
        recursiveReferences! -> push(recursiveReference)
    }
    [expressionTypeIds.ExpressionTypeId; ~] => recursiveExpressions!
    recursiveTypes.expressions -> each recursiveExpression {
        recursiveExpressions! -> push(recursiveExpression)
    }
    prepared -> expressionTypes.inferContext => inferred!
    [TypedIrNode; ~] => results!
    0 => sourceIndex!
    sourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[sourceIndex!] -> len => sourceLength
        prepared.sources[sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        prepared.ranges[sourceIndex!] => sourceRange
        [resolution.ResolvedName; ~] => resolvedNames!
        0 => sourceNameOffset!
        sourceNameOffset! < sourceRange.nameCount -> while {
            resolvedNames! -> push(prepared.names[sourceRange.nameStart + sourceNameOffset!])
            sourceNameOffset! + 1 => sourceNameOffset!
        }
        0 => symbolIndex!
        symbolIndex! < sourceRange.symbolCount -> while {
            prepared.symbols[sourceRange.symbolStart + symbolIndex!] => function
            function.kind == 7 -> if {
                -1 => resultTypeIndex!
                1000000 => resultDistance!
                UIntSize(0) => resultTopLevelStart!
                0 => typeSearch!
                typeSearch! < (inferred! -> len) -> while {
                    inferred![typeSearch!] => candidateType
                    candidateType.sourceModule == sourceIndex! -> if {
                        prepared.nodes[sourceRange.astStart + candidateType.astNode].parent => ancestor!
                        candidateType.astNode => functionChildAst!
                        1 => distance!
                        false => belongsToFunction!
                        (ancestor! >= 0 and not belongsToFunction!) -> while {
                            ancestor! == function.astNode -> if { true => belongsToFunction! } else {
                                ancestor! => functionChildAst!
                                prepared.nodes[sourceRange.astStart + ancestor!].parent => ancestor!
                                distance! + 1 => distance!
                            }
                        }
                        belongsToFunction! -> if {
                            prepared.nodes[sourceRange.astStart + functionChildAst!].start => candidateTopLevelStart
                            (resultTypeIndex! < 0 or candidateTopLevelStart > resultTopLevelStart! or (candidateTopLevelStart == resultTopLevelStart! and distance! < resultDistance!)) -> if {
                                typeSearch! => resultTypeIndex!
                                distance! => resultDistance!
                                candidateTopLevelStart => resultTopLevelStart!
                            }
                        }
                    }
                    typeSearch! + 1 => typeSearch!
                }
                resultTypeIndex! >= 0 -> if {
                    inferred![resultTypeIndex!] => resultType
                    resultType.origin => functionResultOrigin!
                    resultType.targetModule => functionResultModule!
                    resultType.targetSymbol => functionResultSymbol!
                    function.typeNode => declaredResultAst!
                    function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode => declaredResultAst! }
                    0 => declaredResultNominalSearch!
                    declaredResultNominalSearch! < (prepared.nominal -> len) -> while {
                        prepared.nominal[declaredResultNominalSearch!] => declaredResultNominal
                        (declaredResultNominal.sourceModule == sourceIndex! and declaredResultNominal.typeAst == declaredResultAst!) -> if {
                            declaredResultNominal.origin => functionResultOrigin!
                            declaredResultNominal.targetModule => functionResultModule!
                            declaredResultNominal.targetSymbol => functionResultSymbol!
                        }
                        declaredResultNominalSearch! + 1 => declaredResultNominalSearch!
                    }
                    0 => declaredResultCompositeSearch!
                    declaredResultCompositeSearch! < (prepared.composite -> len) -> while {
                        prepared.composite[declaredResultCompositeSearch!] => declaredResultComposite
                        (declaredResultComposite.sourceModule == sourceIndex! and declaredResultComposite.typeAst == declaredResultAst!) -> if {
                            10 + declaredResultComposite.kind => functionResultOrigin!
                            declaredResultComposite.elementModule => functionResultModule!
                            declaredResultComposite.elementSymbol => functionResultSymbol!
                        }
                        declaredResultCompositeSearch! + 1 => declaredResultCompositeSearch!
                    }
                    -1 => parameterSymbol!
                    -1 => parameterTypeIndex!
                    0 => parameterSearch!
                    parameterSearch! < sourceRange.symbolCount -> while {
                        prepared.symbols[sourceRange.symbolStart + parameterSearch!] => parameterCandidate
                        (parameterCandidate.kind == 35 and parameterCandidate.parent == symbolIndex!) -> if { parameterSearch! => parameterSymbol! }
                        parameterSearch! + 1 => parameterSearch!
                    }
                    parameterSymbol! >= 0 -> if {
                        0 => parameterNameSearch!
                        parameterNameSearch! < (resolvedNames! -> len) -> while {
                            resolvedNames![parameterNameSearch!] => parameterName
                            parameterName.symbol == parameterSymbol! -> if {
                                0 => parameterInferredSearch!
                                parameterInferredSearch! < (inferred! -> len) -> while {
                                    inferred![parameterInferredSearch!] => parameterInferred
                                    (parameterInferred.sourceModule == sourceIndex! and parameterInferred.astNode == parameterName.astNode) -> if { parameterInferredSearch! => parameterTypeIndex! }
                                    parameterInferredSearch! + 1 => parameterInferredSearch!
                                }
                            }
                            parameterNameSearch! + 1 => parameterNameSearch!
                        }
                    }
                    -1 => parameterOrigin!
                    -1 => parameterModule!
                    -1 => parameterTypeSymbol!
                    parameterTypeIndex! >= 0 -> if {
                        inferred![parameterTypeIndex!] => inferredParameterType
                        inferredParameterType.origin => parameterOrigin!
                        inferredParameterType.targetModule => parameterModule!
                        inferredParameterType.targetSymbol => parameterTypeSymbol!
                    }
                    (parameterSymbol! >= 0 and parameterOrigin! < 0) -> if {
                        prepared.symbols[sourceRange.symbolStart + parameterSymbol!] => declaredParameter
                        0 => declaredNominalSearch!
                        declaredNominalSearch! < (prepared.nominal -> len) -> while {
                            prepared.nominal[declaredNominalSearch!] => declaredNominal
                            (declaredNominal.sourceModule == sourceIndex! and declaredNominal.typeAst == declaredParameter.typeNode) -> if {
                                declaredNominal.origin => parameterOrigin!
                                declaredNominal.targetModule => parameterModule!
                                declaredNominal.targetSymbol => parameterTypeSymbol!
                            }
                            declaredNominalSearch! + 1 => declaredNominalSearch!
                        }
                        parameterOrigin! < 0 -> if {
                            0 => declaredCompositeSearch!
                            declaredCompositeSearch! < (prepared.composite -> len) -> while {
                                prepared.composite[declaredCompositeSearch!] => declaredComposite
                                (declaredComposite.sourceModule == sourceIndex! and declaredComposite.typeAst == declaredParameter.typeNode) -> if {
                                    10 + declaredComposite.kind => parameterOrigin!
                                    declaredComposite.elementModule => parameterModule!
                                    declaredComposite.elementSymbol => parameterTypeSymbol!
                                }
                                declaredCompositeSearch! + 1 => declaredCompositeSearch!
                            }
                        }
                    }
                    results! -> len => functionIr!
                    functionIr! + 1 => returnIr
                    -1 => parameterIr!
                    (parameterSymbol! >= 0 and parameterOrigin! >= 0) -> if { returnIr + 1 => parameterIr! }
                    results! -> push(TypedIrNode {
                        kind: 0
                        parent: -1
                        sourceModule: sourceIndex!
                        astNode: function.astNode
                        symbol: symbolIndex!
                        targetModule: sourceIndex!
                        typeOrigin: functionResultOrigin!
                        typeModule: functionResultModule!
                        typeSymbol: functionResultSymbol!
                        typeId: -1
                        typeKind: -1
                        typeFlags: 0
                        payloadToken: function.nameToken
                        opcode: -1
                        operand0: returnIr
                        operand1: parameterIr!
                        nextOperand: -1
                        flags: function.flags
                    })
                    results! -> push(TypedIrNode {
                        kind: 1
                        parent: functionIr!
                        sourceModule: sourceIndex!
                        astNode: resultType.astNode
                        symbol: symbolIndex!
                        targetModule: -1
                        typeOrigin: functionResultOrigin!
                        typeModule: functionResultModule!
                        typeSymbol: functionResultSymbol!
                        typeId: -1
                        typeKind: -1
                        typeFlags: 0
                        payloadToken: -1
                        opcode: -1
                        operand0: -1
                        operand1: -1
                        nextOperand: -1
                        flags: 0
                    })
                    parameterIr! >= 0 -> if {
                        prepared.symbols[sourceRange.symbolStart + parameterSymbol!] => parameter
                        results! -> push(TypedIrNode {
                            kind: 10
                            parent: functionIr!
                            sourceModule: sourceIndex!
                            astNode: function.astNode
                            symbol: parameterSymbol!
                            targetModule: sourceIndex!
                            typeOrigin: parameterOrigin!
                            typeModule: parameterModule!
                            typeSymbol: parameterTypeSymbol!
                            typeId: -1
                            typeKind: -1
                            typeFlags: 0
                            payloadToken: parameter.nameToken
                            opcode: -1
                            operand0: -1
                            operand1: -1
                            nextOperand: -1
                            flags: parameter.flags
                        })
                    }

                    [Int; ~] => astToIr!
                    0 => astMapIndex!
                    astMapIndex! < sourceRange.astCount -> while {
                        astToIr! -> push(-1)
                        astMapIndex! + 1 => astMapIndex!
                    }
                    results! -> len => expressionIrStart
                    0 => bindingAstIndex!
                    bindingAstIndex! < sourceRange.astCount -> while {
                        prepared.nodes[sourceRange.astStart + bindingAstIndex!] => bindingAst
                        (bindingAst.kind == 9 or (bindingAst.kind == 48 and bindingAst.secondaryToken >= 0)) -> if {
                            bindingAst.parent => bindingAncestor!
                            false => bindingBelongsToFunction!
                            (bindingAncestor! >= 0 and not bindingBelongsToFunction!) -> while {
                                bindingAncestor! == function.astNode -> if { true => bindingBelongsToFunction! } else { prepared.nodes[sourceRange.astStart + bindingAncestor!].parent => bindingAncestor! }
                            }
                            bindingBelongsToFunction! -> if {
                                -1 => bindingTypeIndex!
                                1000000 => bindingTypeDistance!
                                0 => bindingTypeSearch!
                                bindingTypeSearch! < (inferred! -> len) -> while {
                                    inferred![bindingTypeSearch!] => bindingTypeCandidate
                                    bindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                        prepared.nodes[sourceRange.astStart + bindingTypeCandidate.astNode].parent => bindingTypeAncestor!
                                        1 => bindingDistance!
                                        bindingTypeCandidate.astNode == bindingAstIndex! => belongsToBinding!
                                        belongsToBinding! -> if { 0 => bindingDistance! }
                                        (bindingTypeAncestor! >= 0 and not belongsToBinding!) -> while {
                                            bindingTypeAncestor! == bindingAstIndex! -> if { true => belongsToBinding! } else {
                                                prepared.nodes[sourceRange.astStart + bindingTypeAncestor!].parent => bindingTypeAncestor!
                                                bindingDistance! + 1 => bindingDistance!
                                            }
                                        }
                                        (belongsToBinding! and bindingDistance! < bindingTypeDistance!) -> if {
                                            bindingTypeSearch! => bindingTypeIndex!
                                            bindingDistance! => bindingTypeDistance!
                                        }
                                    }
                                    bindingTypeSearch! + 1 => bindingTypeSearch!
                                }
                                bindingTypeIndex! >= 0 -> if {
                                    -1 => bindingSymbol!
                                    0 => bindingSymbolSearch!
                                    bindingSymbolSearch! < sourceRange.symbolCount -> while {
                                        (prepared.symbols[sourceRange.symbolStart + bindingSymbolSearch!].kind == 9 and prepared.symbols[sourceRange.symbolStart + bindingSymbolSearch!].astNode == bindingAstIndex!) -> if { bindingSymbolSearch! => bindingSymbol! }
                                        bindingSymbolSearch! + 1 => bindingSymbolSearch!
                                    }
                                    inferred![bindingTypeIndex!] => bindingType
                                    results! -> len => bindingIr
                                    bindingIr => astToIr![bindingAstIndex!]
                                    results! -> push(TypedIrNode {
                                        kind: 17
                                        parent: returnIr
                                        sourceModule: sourceIndex!
                                        astNode: bindingAstIndex!
                                        symbol: bindingSymbol!
                                        targetModule: sourceIndex!
                                        typeOrigin: bindingType.origin
                                        typeModule: bindingType.targetModule
                                        typeSymbol: bindingType.targetSymbol
                                        typeId: -1
                                        typeKind: -1
                                        typeFlags: 0
                                        payloadToken: bindingAst.kind == 48 -> if { bindingAst.secondaryToken } else { bindingAst.payloadToken }
                                        opcode: -1
                                        operand0: -1
                                        operand1: -1
                                        nextOperand: -1
                                        flags: bindingAst.flags
                                    })
                                }
                            }
                        }
                        bindingAstIndex! + 1 => bindingAstIndex!
                    }
                    0 => expressionAstIndex!
                    expressionAstIndex! < sourceRange.astCount -> while {
                        prepared.nodes[sourceRange.astStart + expressionAstIndex!] => expression
                        expression.parent => expressionAncestor!
                        false => expressionBelongsToFunction!
                        (expressionAncestor! >= 0 and not expressionBelongsToFunction!) -> while {
                            expressionAncestor! == function.astNode -> if { true => expressionBelongsToFunction! } else {
                                prepared.nodes[sourceRange.astStart + expressionAncestor!].parent => expressionAncestor!
                            }
                        }
                        -1 => expressionTypeIndex!
                        0 => expressionTypeSearch!
                        expressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![expressionTypeSearch!] => candidateExpressionType
                            (candidateExpressionType.sourceModule == sourceIndex! and candidateExpressionType.astNode == expressionAstIndex!) -> if { expressionTypeSearch! => expressionTypeIndex! }
                            expressionTypeSearch! + 1 => expressionTypeSearch!
                        }
                        -1 => recursiveExpressionTypeId!
                        0 => recursiveExpressionSearch!
                        recursiveExpressionSearch! < (recursiveExpressions! -> len) -> while {
                            recursiveExpressions![recursiveExpressionSearch!] => recursiveExpressionCandidate
                            (recursiveExpressionCandidate.status == 0 and recursiveExpressionCandidate.sourceModule == sourceIndex! and recursiveExpressionCandidate.astNode == expressionAstIndex!) -> if {
                                recursiveExpressionCandidate.typeId => recursiveExpressionTypeId!
                            }
                            recursiveExpressionSearch! + 1 => recursiveExpressionSearch!
                        }
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        0 => expressionTypeSymbol!
                        expressionTypeIndex! >= 0 -> if {
                            inferred![expressionTypeIndex!] => legacyExpressionType
                            legacyExpressionType.origin => expressionTypeOrigin!
                            legacyExpressionType.targetModule => expressionTypeModule!
                            legacyExpressionType.targetSymbol => expressionTypeSymbol!
                        }
                        recursiveExpressionTypeId! >= 0 -> if {
                            recursiveSemanticTypes![recursiveExpressionTypeId!] => recursiveExpressionType
                            recursiveExpressionType.origin => expressionTypeOrigin!
                            recursiveExpressionType.module => expressionTypeModule!
                            recursiveExpressionType.symbol => expressionTypeSymbol!
                            (recursiveExpressionType.kind >= 2 and recursiveExpressionType.kind <= 6) -> if {
                                10 + recursiveExpressionType.kind => expressionTypeOrigin!
                            }
                            (recursiveExpressionType.kind == 2 or recursiveExpressionType.kind == 3 or recursiveExpressionType.kind == 4 or recursiveExpressionType.kind == 6) -> if {
                                recursiveExpressionType.first >= 0 -> if {
                                    recursiveSemanticTypes![recursiveExpressionType.first].module => expressionTypeModule!
                                    recursiveSemanticTypes![recursiveExpressionType.first].symbol => expressionTypeSymbol!
                                }
                            }
                            recursiveExpressionType.kind == 5 -> if {
                                recursiveExpressionType.first >= 0 -> if { recursiveSemanticTypes![recursiveExpressionType.first].symbol => expressionTypeModule! }
                                recursiveExpressionType.second >= 0 -> if { recursiveSemanticTypes![recursiveExpressionType.second].symbol => expressionTypeSymbol! }
                            }
                        }
                        expressionBelongsToFunction! -> if {
                            (expression.kind == 42 or expression.kind == 43 or expression.kind == 44 or expression.kind == 45 or expression.kind == 46 or expression.kind == 47) -> if {
                                results! -> len => controlIr
                                controlIr => astToIr![expressionAstIndex!]
                                18 => controlKind!
                                expression.kind == 43 -> if { 19 => controlKind! }
                                expression.kind == 44 -> if { 20 => controlKind! }
                                expression.kind == 45 -> if { 21 => controlKind! }
                                expression.kind == 46 -> if { 22 => controlKind! }
                                expression.kind == 47 -> if { 23 => controlKind! }
                                -1 => controlOpcode!
                                expression.kind == 45 -> if {
                                    0 => controlOpcode!
                                    (source -> byte(expression.start)) == UInt8(99) -> if { 1 => controlOpcode! }
                                }
                                expression.kind == 46 -> if { expression.operatorKind => controlOpcode! }
                                1 => controlTypeOrigin!
                                -1 => controlTypeModule!
                                0 => controlTypeSymbol!
                                expressionTypeIndex! >= 0 -> if {
                                    inferred![expressionTypeIndex!] => inferredControlType
                                    inferredControlType.origin => controlTypeOrigin!
                                    inferredControlType.targetModule => controlTypeModule!
                                    inferredControlType.targetSymbol => controlTypeSymbol!
                                }
                                results! -> push(TypedIrNode {
                                    kind: controlKind!
                                    parent: returnIr
                                    sourceModule: sourceIndex!
                                    astNode: expressionAstIndex!
                                    symbol: -1
                                    targetModule: sourceIndex!
                                    typeOrigin: controlTypeOrigin!
                                    typeModule: controlTypeModule!
                                    typeSymbol: controlTypeSymbol!
                                    typeId: -1
                                    typeKind: -1
                                    typeFlags: 0
                                    payloadToken: expression.payloadToken
                                    opcode: controlOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: expression.flags
                                })
                            } else {
                                (expressionTypeIndex! >= 0 or recursiveExpressionTypeId! >= 0) -> if {
                                9 => expressionKind!
                                expression.kind == 13 -> if { 2 => expressionKind! }
                                expression.kind == 14 -> if { 3 => expressionKind! }
                                expression.kind == 15 -> if {
                                    5 => expressionKind!
                                    (expressionTypeOrigin! == 1 and expressionTypeSymbol! == 23) -> if {
                                        true => expressionIsBoolLiteral!
                                        0 => expressionBoolNameSearch!
                                        expressionBoolNameSearch! < (resolvedNames! -> len) -> while {
                                            resolvedNames![expressionBoolNameSearch!].astNode == expressionAstIndex! -> if { false => expressionIsBoolLiteral! }
                                            expressionBoolNameSearch! + 1 => expressionBoolNameSearch!
                                        }
                                        expressionIsBoolLiteral! -> if { 4 => expressionKind! }
                                    }
                                }
                                (expression.kind == 11 or expression.kind == 48) -> if { 6 => expressionKind! }
                                expression.kind == 22 -> if { 7 => expressionKind! }
                                (expression.kind >= 18 and expression.kind <= 21) -> if { 8 => expressionKind! }
                                (expression.kind == 24 or expression.kind == 25) -> if { 8 => expressionKind! }
                                expression.kind == 39 -> if { 12 => expressionKind! }
                                expression.kind == 36 -> if { 13 => expressionKind! }
                                expression.kind == 37 -> if { 14 => expressionKind! }
                                expression.kind == 38 -> if { 16 => expressionKind! }
                                expression.kind == 41 -> if { 15 => expressionKind! }
                                0 => propertyCallSearch!
                                propertyCallSearch! < (prepared.calls -> len) -> while {
                                    prepared.calls[propertyCallSearch!] => propertyCall
                                    (propertyCall.sourceModule == sourceIndex! and propertyCall.callAst == expressionAstIndex! and propertyCall.status == 0) -> if { 6 => expressionKind! }
                                    propertyCallSearch! + 1 => propertyCallSearch!
                                }
                                results! -> len => expressionIr
                                expressionIr => astToIr![expressionAstIndex!]
                                -1 => expressionSymbol!
                                -1 => expressionTargetModule!
                                expression.flags => expressionFlags!
                                expression.operatorKind => expressionOpcode!
                                expressionKind! == 13 -> if {
                                    0 => valuePathSearch!
                                    valuePathSearch! < (prepared.qualified -> len) -> while {
                                        prepared.qualified[valuePathSearch!] => valuePath
                                        (valuePath.sourceModule == sourceIndex! and valuePath.pathAst == expressionAstIndex! and valuePath.status == 0) -> if {
                                            prepared.modules[valuePath.targetModule].sourceIndex => valueTargetSource
                                            prepared.ranges[valueTargetSource] => valueTargetRange
                                            prepared.symbols[valueTargetRange.symbolStart + valuePath.targetSymbol] => valueTarget
                                            (valueTarget.kind == 7 and valueTarget.secondaryTypeNode < 0) -> if {
                                                6 => expressionKind!
                                                valuePath.targetSymbol => expressionSymbol!
                                                valueTargetSource => expressionTargetModule!
                                            }
                                        }
                                        valuePathSearch! + 1 => valuePathSearch!
                                    }
                                }
                                expression.kind == 10 -> if {
                                    0 => intrinsicChildSearch!
                                    intrinsicChildSearch! < sourceRange.astCount -> while {
                                        prepared.nodes[sourceRange.astStart + intrinsicChildSearch!] => intrinsicChild
                                        (intrinsicChild.parent == expressionAstIndex! and intrinsicChild.kind == 16) -> if {
                                            IntrinsicNameRequest { source: source, token: prepared.tokens[sourceRange.tokenStart + intrinsicChild.payloadToken] } -> intrinsicOpcode => expressionOpcode!
                                        }
                                        intrinsicChildSearch! + 1 => intrinsicChildSearch!
                                    }
                                }
                                expressionKind! == 5 -> if {
                                    0 => nameResolutionSearch!
                                    nameResolutionSearch! < (resolvedNames! -> len) -> while {
                                        resolvedNames![nameResolutionSearch!] => resolvedName
                                        resolvedName.astNode == expressionAstIndex! -> if { resolvedName.symbol => expressionSymbol! }
                                        nameResolutionSearch! + 1 => nameResolutionSearch!
                                    }
                                }
                                expressionKind! == 6 -> if {
                                    0 => callSearch!
                                    callSearch! < (prepared.calls -> len) -> while {
                                        prepared.calls[callSearch!] => resolvedCall
                                        (resolvedCall.sourceModule == sourceIndex! and resolvedCall.callAst == expressionAstIndex! and resolvedCall.status == 0) -> if {
                                            resolvedCall.functionSymbol => expressionSymbol!
                                            resolvedCall.targetSourceModule => expressionTargetModule!
                                            (resolvedCall.functionSymbol >= 0 and resolvedCall.targetSourceModule >= 0) -> if {
                                                prepared.sources[resolvedCall.targetSourceModule] -> symbols.collectSource => expressionTargetTable!
                                                ((expressionTargetTable![resolvedCall.functionSymbol].flags / 8) % 2 == 1 and (expressionFlags! / 8) % 2 == 0) -> if {
                                                    expressionFlags! + 8 => expressionFlags!
                                                }
                                            }
                                        }
                                        callSearch! + 1 => callSearch!
                                    }
                                }
                                results! -> push(TypedIrNode {
                                    kind: expressionKind!
                                    parent: returnIr
                                    sourceModule: sourceIndex!
                                    astNode: expressionAstIndex!
                                    symbol: expressionSymbol!
                                    targetModule: expressionTargetModule!
                                    typeOrigin: expressionTypeOrigin!
                                    typeModule: expressionTypeModule!
                                    typeSymbol: expressionTypeSymbol!
                                    typeId: recursiveExpressionTypeId!
                                    typeKind: recursiveExpressionTypeId! < 0 -> if { -1 } else { recursiveSemanticTypes![recursiveExpressionTypeId!].kind }
                                    typeFlags: recursiveExpressionTypeId! < 0 -> if { 0 } else { recursiveTypeFlags![recursiveExpressionTypeId!] }
                                    payloadToken: expression.payloadToken
                                    opcode: expressionOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: expressionFlags!
                                })
                                }
                            }
                        }
                        expressionAstIndex! + 1 => expressionAstIndex!
                    }

                    results! -> len => expressionIrEnd
                    expressionIrStart => parentIrIndex!
                    parentIrIndex! < expressionIrEnd -> while {
                        results![parentIrIndex!] => expressionIrNode!
                        prepared.nodes[sourceRange.astStart + expressionIrNode!.astNode].parent => parentAst!
                        -1 => semanticParentIr!
                        (parentAst! >= 0 and parentAst! != function.astNode and semanticParentIr! < 0) -> while {
                            astToIr![parentAst!] >= 0 -> if { astToIr![parentAst!] => semanticParentIr! } else {
                                prepared.nodes[sourceRange.astStart + parentAst!].parent => parentAst!
                            }
                        }
                        semanticParentIr! >= 0 -> if { semanticParentIr! => expressionIrNode!.parent }
                        expressionIrNode! => results![parentIrIndex!]
                        parentIrIndex! + 1 => parentIrIndex!
                    }

                    expressionIrStart => operandIrIndex!
                    operandIrIndex! < expressionIrEnd -> while {
                        results![operandIrIndex!] => operatorIr!
                        (operatorIr!.kind == 6 or operatorIr!.kind == 7 or operatorIr!.kind == 8 or (operatorIr!.kind == 9 and operatorIr!.opcode <= -201 and operatorIr!.opcode >= -203) or operatorIr!.kind == 13 or operatorIr!.kind == 15 or operatorIr!.kind == 17 or operatorIr!.kind == 22 or operatorIr!.kind == 23) -> if {
                            -1 => firstOperand!
                            -1 => secondOperand!
                            UIntSize(0) => firstStart!
                            0 => childIrIndex!
                            childIrIndex! < expressionIrEnd -> while {
                                results![childIrIndex!] => childIr
                                childIr.parent == operandIrIndex! -> if {
                                    prepared.nodes[sourceRange.astStart + childIr.astNode].start => childStart
                                    firstOperand! < 0 -> if {
                                        childIrIndex! => firstOperand!
                                        childStart => firstStart!
                                    } else {
                                        childStart < firstStart! -> if {
                                            firstOperand! => secondOperand!
                                            childIrIndex! => firstOperand!
                                            childStart => firstStart!
                                        } else {
                                            secondOperand! < 0 -> if { childIrIndex! => secondOperand! }
                                        }
                                    }
                                }
                                childIrIndex! + 1 => childIrIndex!
                            }
                            firstOperand! => operatorIr!.operand0
                            (operatorIr!.kind == 6 or operatorIr!.kind == 8 or (operatorIr!.kind == 9 and operatorIr!.opcode <= -201 and operatorIr!.opcode >= -203) or operatorIr!.kind == 15) -> if { secondOperand! => operatorIr!.operand1 }
                            operatorIr!.kind == 22 -> if {
                                -1 => operatorIr!.operand0
                                firstOperand! => operatorIr!.operand1
                            }
                            operatorIr! => results![operandIrIndex!]
                        }
                        operandIrIndex! + 1 => operandIrIndex!
                    }

                    expressionIrStart => siblingIrIndex!
                    siblingIrIndex! < expressionIrEnd -> while {
                        results![siblingIrIndex!] => sibling!
                        -1 => nextSibling!
                        UIntSize(0) => nextSiblingStart!
                        expressionIrStart => siblingSearch!
                        siblingSearch! < expressionIrEnd -> while {
                            results![siblingSearch!] => siblingCandidate
                            (siblingCandidate.parent == sibling!.parent and prepared.nodes[sourceRange.astStart + siblingCandidate.astNode].start > prepared.nodes[sourceRange.astStart + sibling!.astNode].start) -> if {
                                prepared.nodes[sourceRange.astStart + siblingCandidate.astNode].start => siblingCandidateStart
                                (nextSibling! < 0 or siblingCandidateStart < nextSiblingStart!) -> if {
                                    siblingSearch! => nextSibling!
                                    siblingCandidateStart => nextSiblingStart!
                                }
                            }
                            siblingSearch! + 1 => siblingSearch!
                        }
                        nextSibling! => sibling!.nextOperand
                        sibling! => results![siblingIrIndex!]
                        siblingIrIndex! + 1 => siblingIrIndex!
                    }

                    expressionIrStart => controlIrIndex!
                    controlIrIndex! < expressionIrEnd -> while {
                        results![controlIrIndex!] => control!
                        control!.kind == 19 -> if {
                            -1 => firstRegionChild!
                            -1 => lastRegionChild!
                            UIntSize(0) => firstRegionChildStart!
                            UIntSize(0) => lastRegionChildStart!
                            expressionIrStart => regionChildSearch!
                            regionChildSearch! < expressionIrEnd -> while {
                                results![regionChildSearch!].parent == controlIrIndex! -> if {
                                    prepared.nodes[sourceRange.astStart + results![regionChildSearch!].astNode].start => regionChildStart
                                    (firstRegionChild! < 0 or regionChildStart < firstRegionChildStart!) -> if {
                                        regionChildSearch! => firstRegionChild!
                                        regionChildStart => firstRegionChildStart!
                                    }
                                    (lastRegionChild! < 0 or regionChildStart > lastRegionChildStart!) -> if {
                                        regionChildSearch! => lastRegionChild!
                                        regionChildStart => lastRegionChildStart!
                                    }
                                }
                                regionChildSearch! + 1 => regionChildSearch!
                            }
                            firstRegionChild! => control!.operand0
                            (lastRegionChild! >= 0 and results![lastRegionChild!].kind == 9) -> while {
                                -1 => nestedRegionResult!
                                UIntSize(0) => nestedRegionResultStart!
                                expressionIrStart => nestedResultSearch!
                                nestedResultSearch! < expressionIrEnd -> while {
                                    results![nestedResultSearch!].parent == lastRegionChild! -> if {
                                        prepared.nodes[sourceRange.astStart + results![nestedResultSearch!].astNode].start => nestedResultStart
                                        (nestedRegionResult! < 0 or nestedResultStart > nestedRegionResultStart!) -> if {
                                            nestedResultSearch! => nestedRegionResult!
                                            nestedResultStart => nestedRegionResultStart!
                                        }
                                    }
                                    nestedResultSearch! + 1 => nestedResultSearch!
                                }
                                nestedRegionResult! >= 0 -> if { nestedRegionResult! => lastRegionChild! } else { -1 => lastRegionChild! }
                            }
                            lastRegionChild! => control!.operand1
                            control! => results![controlIrIndex!]
                        }
                        (control!.kind == 18 or control!.kind == 20) -> if {
                            prepared.nodes[sourceRange.astStart + control!.astNode].parent => controlFlowAst
                            -1 => conditionIr!
                            UIntSize(0) => conditionStart!
                            1000000 => conditionDistance!
                            expressionIrStart => conditionSearch!
                            conditionSearch! < expressionIrEnd -> while {
                                results![conditionSearch!] => conditionCandidate
                                prepared.nodes[sourceRange.astStart + conditionCandidate.astNode].parent => conditionAncestor!
                                1 => candidateConditionDistance!
                                false => conditionBelongsToFlow!
                                (conditionAncestor! >= 0 and not conditionBelongsToFlow!) -> while {
                                    conditionAncestor! == controlFlowAst -> if { true => conditionBelongsToFlow! } else {
                                        prepared.nodes[sourceRange.astStart + conditionAncestor!].parent => conditionAncestor!
                                        candidateConditionDistance! + 1 => candidateConditionDistance!
                                    }
                                }
                                (conditionBelongsToFlow! and prepared.nodes[sourceRange.astStart + conditionCandidate.astNode].start < prepared.nodes[sourceRange.astStart + control!.astNode].start) -> if {
                                    prepared.nodes[sourceRange.astStart + conditionCandidate.astNode].start => candidateStart
                                    (conditionIr! < 0 or candidateConditionDistance! < conditionDistance! or (candidateConditionDistance! == conditionDistance! and candidateStart > conditionStart!)) -> if {
                                        conditionSearch! => conditionIr!
                                        candidateStart => conditionStart!
                                        candidateConditionDistance! => conditionDistance!
                                    }
                                }
                            conditionSearch! + 1 => conditionSearch!
                        }
                        (conditionIr! < 0 and control!.parent >= expressionIrStart and results![control!.parent].kind != 19) -> if {
                            control!.parent => enclosingConditionIr!
                            results![enclosingConditionIr!].parent => control!.parent
                            enclosingConditionIr! => conditionIr!
                        }
                        (conditionIr! >= 0 and results![conditionIr!].kind == 9) -> while {
                            -1 => nestedConditionResult!
                            UIntSize(0) => nestedConditionStart!
                            expressionIrStart => nestedConditionSearch!
                            nestedConditionSearch! < expressionIrEnd -> while {
                                results![nestedConditionSearch!].parent == conditionIr! -> if {
                                    prepared.nodes[sourceRange.astStart + results![nestedConditionSearch!].astNode].start => nestedCandidateStart
                                    (nestedConditionResult! < 0 or nestedCandidateStart > nestedConditionStart!) -> if {
                                        nestedConditionSearch! => nestedConditionResult!
                                        nestedCandidateStart => nestedConditionStart!
                                    }
                                }
                                nestedConditionSearch! + 1 => nestedConditionSearch!
                            }
                            nestedConditionResult! >= 0 -> if { nestedConditionResult! => conditionIr! } else { -1 => conditionIr! }
                        }
                        -1 => thenRegion!
                            -1 => elseRegion!
                            expressionIrStart => regionSearch!
                            regionSearch! < expressionIrEnd -> while {
                                results![regionSearch!] => regionCandidate
                                (regionCandidate.kind == 19 and regionCandidate.parent == controlIrIndex!) -> if {
                                    thenRegion! < 0 -> if { regionSearch! => thenRegion! } else { regionSearch! => elseRegion! }
                                }
                                regionSearch! + 1 => regionSearch!
                            }
                            conditionIr! => control!.operand0
                            thenRegion! => control!.operand1
                            control!.kind == 18 -> if { elseRegion! => control!.nextOperand } else { -1 => control!.nextOperand }
                            control! => results![controlIrIndex!]
                            (control!.kind == 20 and conditionIr! >= 0) -> if {
                                results![conditionIr!] => loopCondition!
                                controlIrIndex! => loopCondition!.parent
                                loopCondition! => results![conditionIr!]
                            }
                        }
                        controlIrIndex! + 1 => controlIrIndex!
                    }

                    expressionIrStart => bindingNameIrIndex!
                    bindingNameIrIndex! < expressionIrEnd -> while {
                        results![bindingNameIrIndex!] => bindingName!
                        bindingName!.kind == 5 -> if {
                            expressionIrStart => bindingDefinitionSearch!
                            bindingDefinitionSearch! < expressionIrEnd -> while {
                                (results![bindingDefinitionSearch!].kind == 17 and results![bindingDefinitionSearch!].symbol == bindingName!.symbol) -> if {
                                    bindingDefinitionSearch! => bindingName!.operand0
                                }
                                bindingDefinitionSearch! + 1 => bindingDefinitionSearch!
                            }
                            bindingName! => results![bindingNameIrIndex!]
                        }
                        bindingNameIrIndex! + 1 => bindingNameIrIndex!
                    }

                    expressionIrStart => aggregateIrIndex!
                    aggregateIrIndex! < expressionIrEnd -> while {
                        results![aggregateIrIndex!] => aggregate!
                        (aggregate!.kind == 12 or aggregate!.kind == 14 or aggregate!.kind == 16) -> if {
                            -1 => firstFieldOperand!
                            expressionIrStart => fieldOperandSearch!
                            fieldOperandSearch! < expressionIrEnd -> while {
                                results![fieldOperandSearch!].parent == aggregateIrIndex! -> if {
                                    (firstFieldOperand! < 0 or prepared.nodes[sourceRange.astStart + results![fieldOperandSearch!].astNode].start < prepared.nodes[sourceRange.astStart + results![firstFieldOperand!].astNode].start) -> if { fieldOperandSearch! => firstFieldOperand! }
                                }
                                fieldOperandSearch! + 1 => fieldOperandSearch!
                            }
                            firstFieldOperand! => aggregate!.operand0
                            aggregate! => results![aggregateIrIndex!]
                        }
                        aggregateIrIndex! + 1 => aggregateIrIndex!
                    }

                    results![returnIr] => returnNode!
                    astToIr![resultType.astNode] => returnOperandIr!
                    (returnOperandIr! >= 0 and results![returnOperandIr!].kind == 9) -> if {
                        expressionIrStart => returnControlSearch!
                        returnControlSearch! < expressionIrEnd -> while {
                            ((results![returnControlSearch!].kind == 18 or results![returnControlSearch!].kind == 20) and results![returnControlSearch!].parent == returnOperandIr!) -> if { returnControlSearch! => returnOperandIr! }
                            returnControlSearch! + 1 => returnControlSearch!
                        }
                    }
                    returnOperandIr! => returnNode!.operand0
                    returnNode! => results![returnIr]
                }
            }
            symbolIndex! + 1 => symbolIndex!
        }
        0 => entryAstIndex!
        entryAstIndex! < sourceRange.astCount -> while {
            prepared.nodes[sourceRange.astStart + entryAstIndex!] => entryAst
            entryAst.kind == 8 -> if {
                -1 => entryResultTypeIndex!
                1000000 => entryResultDistance!
                0 => entryTypeSearch!
                entryTypeSearch! < (inferred! -> len) -> while {
                    inferred![entryTypeSearch!] => entryCandidateType
                    entryCandidateType.sourceModule == sourceIndex! -> if {
                        prepared.nodes[sourceRange.astStart + entryCandidateType.astNode].parent => entryAncestor!
                        1 => entryDistance!
                        false => belongsToEntry!
                        (entryAncestor! >= 0 and not belongsToEntry!) -> while {
                            entryAncestor! == entryAstIndex! -> if { true => belongsToEntry! } else {
                                prepared.nodes[sourceRange.astStart + entryAncestor!].parent => entryAncestor!
                                entryDistance! + 1 => entryDistance!
                            }
                        }
                        (belongsToEntry! and (entryDistance! < entryResultDistance! or (entryDistance! == entryResultDistance! and (entryResultTypeIndex! < 0 or prepared.nodes[sourceRange.astStart + entryCandidateType.astNode].start > prepared.nodes[sourceRange.astStart + inferred![entryResultTypeIndex!].astNode].start)))) -> if {
                            entryTypeSearch! => entryResultTypeIndex!
                            entryDistance! => entryResultDistance!
                        }
                    }
                    entryTypeSearch! + 1 => entryTypeSearch!
                }
                results! -> len => entryIr!
                results! -> push(TypedIrNode {
                    kind: 11
                    parent: -1
                    sourceModule: sourceIndex!
                    astNode: entryAstIndex!
                    symbol: -1
                    targetModule: sourceIndex!
                    typeOrigin: 1
                    typeModule: -1
                    typeSymbol: 2
                    typeId: -1
                    typeKind: -1
                    typeFlags: 0
                    payloadToken: -1
                    opcode: -1
                    operand0: -1
                    operand1: -1
                    nextOperand: -1
                    flags: 0
                })
                entryResultTypeIndex! >= 0 -> if {
                    [Int; ~] => entryAstToIr!
                    0 => entryMapIndex!
                    entryMapIndex! < sourceRange.astCount -> while {
                        entryAstToIr! -> push(-1)
                        entryMapIndex! + 1 => entryMapIndex!
                    }
                    results! -> len => entryExpressionStart
                    0 => entryBindingAstIndex!
                    entryBindingAstIndex! < sourceRange.astCount -> while {
                        prepared.nodes[sourceRange.astStart + entryBindingAstIndex!] => entryBindingAst
                        (entryBindingAst.kind == 9 or (entryBindingAst.kind == 48 and entryBindingAst.secondaryToken >= 0)) -> if {
                            entryBindingAst.parent => entryBindingAncestor!
                            false => entryBindingBelongs!
                            (entryBindingAncestor! >= 0 and not entryBindingBelongs!) -> while {
                                entryBindingAncestor! == entryAstIndex! -> if { true => entryBindingBelongs! } else { prepared.nodes[sourceRange.astStart + entryBindingAncestor!].parent => entryBindingAncestor! }
                            }
                            entryBindingBelongs! -> if {
                                -1 => entryBindingTypeIndex!
                                1000000 => entryBindingTypeDistance!
                                0 => entryBindingTypeSearch!
                                entryBindingTypeSearch! < (inferred! -> len) -> while {
                                    inferred![entryBindingTypeSearch!] => entryBindingTypeCandidate
                                    entryBindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                        prepared.nodes[sourceRange.astStart + entryBindingTypeCandidate.astNode].parent => entryBindingTypeAncestor!
                                        1 => entryBindingDistance!
                                        entryBindingTypeCandidate.astNode == entryBindingAstIndex! => entryBelongsToBinding!
                                        entryBelongsToBinding! -> if { 0 => entryBindingDistance! }
                                        (entryBindingTypeAncestor! >= 0 and not entryBelongsToBinding!) -> while {
                                            entryBindingTypeAncestor! == entryBindingAstIndex! -> if { true => entryBelongsToBinding! } else {
                                                prepared.nodes[sourceRange.astStart + entryBindingTypeAncestor!].parent => entryBindingTypeAncestor!
                                                entryBindingDistance! + 1 => entryBindingDistance!
                                            }
                                        }
                                        (entryBelongsToBinding! and entryBindingDistance! < entryBindingTypeDistance!) -> if {
                                            entryBindingTypeSearch! => entryBindingTypeIndex!
                                            entryBindingDistance! => entryBindingTypeDistance!
                                        }
                                    }
                                    entryBindingTypeSearch! + 1 => entryBindingTypeSearch!
                                }
                                entryBindingTypeIndex! >= 0 -> if {
                                    -1 => entryBindingSymbol!
                                    0 => entryBindingSymbolSearch!
                                    entryBindingSymbolSearch! < sourceRange.symbolCount -> while {
                                        (prepared.symbols[sourceRange.symbolStart + entryBindingSymbolSearch!].kind == 9 and prepared.symbols[sourceRange.symbolStart + entryBindingSymbolSearch!].astNode == entryBindingAstIndex!) -> if { entryBindingSymbolSearch! => entryBindingSymbol! }
                                        entryBindingSymbolSearch! + 1 => entryBindingSymbolSearch!
                                    }
                                    inferred![entryBindingTypeIndex!] => entryBindingType
                                    results! -> len => entryBindingIr
                                    entryBindingIr => entryAstToIr![entryBindingAstIndex!]
                                    results! -> push(TypedIrNode {
                                        kind: 17
                                        parent: entryIr!
                                        sourceModule: sourceIndex!
                                        astNode: entryBindingAstIndex!
                                        symbol: entryBindingSymbol!
                                        targetModule: sourceIndex!
                                        typeOrigin: entryBindingType.origin
                                        typeModule: entryBindingType.targetModule
                                        typeSymbol: entryBindingType.targetSymbol
                                        typeId: -1
                                        typeKind: -1
                                        typeFlags: 0
                                        payloadToken: entryBindingAst.kind == 48 -> if { entryBindingAst.secondaryToken } else { entryBindingAst.payloadToken }
                                        opcode: -1
                                        operand0: -1
                                        operand1: -1
                                        nextOperand: -1
                                        flags: entryBindingAst.flags
                                    })
                                }
                            }
                        }
                        entryBindingAstIndex! + 1 => entryBindingAstIndex!
                    }
                    0 => entryExpressionAst!
                    entryExpressionAst! < sourceRange.astCount -> while {
                        prepared.nodes[sourceRange.astStart + entryExpressionAst!] => entryExpression
                        entryExpression.parent => entryExpressionAncestor!
                        false => entryExpressionBelongs!
                        (entryExpressionAncestor! >= 0 and not entryExpressionBelongs!) -> while {
                            entryExpressionAncestor! == entryAstIndex! -> if { true => entryExpressionBelongs! } else { prepared.nodes[sourceRange.astStart + entryExpressionAncestor!].parent => entryExpressionAncestor! }
                        }
                        -1 => entryExpressionTypeIndex!
                        0 => entryExpressionTypeSearch!
                        entryExpressionTypeSearch! < (inferred! -> len) -> while {
                            inferred![entryExpressionTypeSearch!] => entryExpressionTypeCandidate
                            (entryExpressionTypeCandidate.sourceModule == sourceIndex! and entryExpressionTypeCandidate.astNode == entryExpressionAst!) -> if { entryExpressionTypeSearch! => entryExpressionTypeIndex! }
                            entryExpressionTypeSearch! + 1 => entryExpressionTypeSearch!
                        }
                        -1 => recursiveEntryExpressionTypeId!
                        0 => recursiveEntryExpressionSearch!
                        recursiveEntryExpressionSearch! < (recursiveExpressions! -> len) -> while {
                            recursiveExpressions![recursiveEntryExpressionSearch!] => recursiveEntryExpressionCandidate
                            (recursiveEntryExpressionCandidate.status == 0 and recursiveEntryExpressionCandidate.sourceModule == sourceIndex! and recursiveEntryExpressionCandidate.astNode == entryExpressionAst!) -> if {
                                recursiveEntryExpressionCandidate.typeId => recursiveEntryExpressionTypeId!
                            }
                            recursiveEntryExpressionSearch! + 1 => recursiveEntryExpressionSearch!
                        }
                        1 => entryExpressionTypeOrigin!
                        -1 => entryExpressionTypeModule!
                        0 => entryExpressionTypeSymbol!
                        entryExpressionTypeIndex! >= 0 -> if {
                            inferred![entryExpressionTypeIndex!] => legacyEntryExpressionType
                            legacyEntryExpressionType.origin => entryExpressionTypeOrigin!
                            legacyEntryExpressionType.targetModule => entryExpressionTypeModule!
                            legacyEntryExpressionType.targetSymbol => entryExpressionTypeSymbol!
                        }
                        recursiveEntryExpressionTypeId! >= 0 -> if {
                            recursiveSemanticTypes![recursiveEntryExpressionTypeId!] => recursiveEntryExpressionType
                            recursiveEntryExpressionType.origin => entryExpressionTypeOrigin!
                            recursiveEntryExpressionType.module => entryExpressionTypeModule!
                            recursiveEntryExpressionType.symbol => entryExpressionTypeSymbol!
                            (recursiveEntryExpressionType.kind >= 2 and recursiveEntryExpressionType.kind <= 6) -> if {
                                10 + recursiveEntryExpressionType.kind => entryExpressionTypeOrigin!
                            }
                            (recursiveEntryExpressionType.kind == 2 or recursiveEntryExpressionType.kind == 3 or recursiveEntryExpressionType.kind == 4 or recursiveEntryExpressionType.kind == 6) -> if {
                                recursiveEntryExpressionType.first >= 0 -> if {
                                    recursiveSemanticTypes![recursiveEntryExpressionType.first].module => entryExpressionTypeModule!
                                    recursiveSemanticTypes![recursiveEntryExpressionType.first].symbol => entryExpressionTypeSymbol!
                                }
                            }
                            recursiveEntryExpressionType.kind == 5 -> if {
                                recursiveEntryExpressionType.first >= 0 -> if { recursiveSemanticTypes![recursiveEntryExpressionType.first].symbol => entryExpressionTypeModule! }
                                recursiveEntryExpressionType.second >= 0 -> if { recursiveSemanticTypes![recursiveEntryExpressionType.second].symbol => entryExpressionTypeSymbol! }
                            }
                        }
                        entryExpressionBelongs! -> if {
                            (entryExpression.kind == 42 or entryExpression.kind == 43 or entryExpression.kind == 44 or entryExpression.kind == 45 or entryExpression.kind == 46 or entryExpression.kind == 47) -> if {
                                results! -> len => entryControlIr
                                entryControlIr => entryAstToIr![entryExpressionAst!]
                                18 => entryControlKind!
                                entryExpression.kind == 43 -> if { 19 => entryControlKind! }
                                entryExpression.kind == 44 -> if { 20 => entryControlKind! }
                                entryExpression.kind == 45 -> if { 21 => entryControlKind! }
                                entryExpression.kind == 46 -> if { 22 => entryControlKind! }
                                entryExpression.kind == 47 -> if { 23 => entryControlKind! }
                                -1 => entryControlOpcode!
                                entryExpression.kind == 45 -> if {
                                    0 => entryControlOpcode!
                                    (source -> byte(entryExpression.start)) == UInt8(99) -> if { 1 => entryControlOpcode! }
                                }
                                entryExpression.kind == 46 -> if { entryExpression.operatorKind => entryControlOpcode! }
                                1 => entryControlTypeOrigin!
                                -1 => entryControlTypeModule!
                                0 => entryControlTypeSymbol!
                                entryExpressionTypeIndex! >= 0 -> if {
                                    inferred![entryExpressionTypeIndex!] => inferredEntryControlType
                                    inferredEntryControlType.origin => entryControlTypeOrigin!
                                    inferredEntryControlType.targetModule => entryControlTypeModule!
                                    inferredEntryControlType.targetSymbol => entryControlTypeSymbol!
                                }
                                results! -> push(TypedIrNode {
                                    kind: entryControlKind!
                                    parent: entryIr!
                                    sourceModule: sourceIndex!
                                    astNode: entryExpressionAst!
                                    symbol: -1
                                    targetModule: sourceIndex!
                                    typeOrigin: entryControlTypeOrigin!
                                    typeModule: entryControlTypeModule!
                                    typeSymbol: entryControlTypeSymbol!
                                    typeId: -1
                                    typeKind: -1
                                    typeFlags: 0
                                    payloadToken: entryExpression.payloadToken
                                    opcode: entryControlOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: entryExpression.flags
                                })
                            } else {
                                (entryExpressionTypeIndex! >= 0 or recursiveEntryExpressionTypeId! >= 0) -> if {
                                9 => entryExpressionKind!
                                entryExpression.kind == 13 -> if { 2 => entryExpressionKind! }
                                entryExpression.kind == 14 -> if { 3 => entryExpressionKind! }
                                entryExpression.kind == 15 -> if {
                                    5 => entryExpressionKind!
                                    (entryExpressionTypeOrigin! == 1 and entryExpressionTypeSymbol! == 23) -> if {
                                        true => entryExpressionIsBoolLiteral!
                                        0 => entryBoolNameSearch!
                                        entryBoolNameSearch! < (resolvedNames! -> len) -> while {
                                            resolvedNames![entryBoolNameSearch!].astNode == entryExpressionAst! -> if { false => entryExpressionIsBoolLiteral! }
                                            entryBoolNameSearch! + 1 => entryBoolNameSearch!
                                        }
                                        entryExpressionIsBoolLiteral! -> if { 4 => entryExpressionKind! }
                                    }
                                }
                                (entryExpression.kind == 11 or entryExpression.kind == 48) -> if { 6 => entryExpressionKind! }
                                entryExpression.kind == 22 -> if { 7 => entryExpressionKind! }
                                (entryExpression.kind >= 18 and entryExpression.kind <= 21) -> if { 8 => entryExpressionKind! }
                                (entryExpression.kind == 24 or entryExpression.kind == 25) -> if { 8 => entryExpressionKind! }
                                entryExpression.kind == 39 -> if { 12 => entryExpressionKind! }
                                entryExpression.kind == 36 -> if { 13 => entryExpressionKind! }
                                entryExpression.kind == 37 -> if { 14 => entryExpressionKind! }
                                entryExpression.kind == 38 -> if { 16 => entryExpressionKind! }
                                entryExpression.kind == 41 -> if { 15 => entryExpressionKind! }
                                0 => entryPropertyCallSearch!
                                entryPropertyCallSearch! < (prepared.calls -> len) -> while {
                                    prepared.calls[entryPropertyCallSearch!] => entryPropertyCall
                                    (entryPropertyCall.sourceModule == sourceIndex! and entryPropertyCall.callAst == entryExpressionAst! and entryPropertyCall.status == 0) -> if { 6 => entryExpressionKind! }
                                    entryPropertyCallSearch! + 1 => entryPropertyCallSearch!
                                }
                                results! -> len => entryExpressionIr
                                entryExpressionIr => entryAstToIr![entryExpressionAst!]
                                -1 => entryExpressionSymbol!
                                -1 => entryExpressionTargetModule!
                                entryExpression.flags => entryExpressionFlags!
                                entryExpression.operatorKind => entryExpressionOpcode!
                                entryExpressionKind! == 13 -> if {
                                    0 => entryValuePathSearch!
                                    entryValuePathSearch! < (prepared.qualified -> len) -> while {
                                        prepared.qualified[entryValuePathSearch!] => entryValuePath
                                        (entryValuePath.sourceModule == sourceIndex! and entryValuePath.pathAst == entryExpressionAst! and entryValuePath.status == 0) -> if {
                                            prepared.modules[entryValuePath.targetModule].sourceIndex => entryValueTargetSource
                                            prepared.ranges[entryValueTargetSource] => entryValueTargetRange
                                            prepared.symbols[entryValueTargetRange.symbolStart + entryValuePath.targetSymbol] => entryValueTarget
                                            (entryValueTarget.kind == 7 and entryValueTarget.secondaryTypeNode < 0) -> if {
                                                6 => entryExpressionKind!
                                                entryValuePath.targetSymbol => entryExpressionSymbol!
                                                entryValueTargetSource => entryExpressionTargetModule!
                                            }
                                        }
                                        entryValuePathSearch! + 1 => entryValuePathSearch!
                                    }
                                }
                                entryExpression.kind == 10 -> if {
                                    0 => entryIntrinsicChildSearch!
                                    entryIntrinsicChildSearch! < sourceRange.astCount -> while {
                                        prepared.nodes[sourceRange.astStart + entryIntrinsicChildSearch!] => entryIntrinsicChild
                                        (entryIntrinsicChild.parent == entryExpressionAst! and entryIntrinsicChild.kind == 16) -> if {
                                            IntrinsicNameRequest { source: source, token: prepared.tokens[sourceRange.tokenStart + entryIntrinsicChild.payloadToken] } -> intrinsicOpcode => entryExpressionOpcode!
                                        }
                                        entryIntrinsicChildSearch! + 1 => entryIntrinsicChildSearch!
                                    }
                                }
                                entryExpressionKind! == 5 -> if {
                                    0 => entryNameSearch!
                                    entryNameSearch! < (resolvedNames! -> len) -> while {
                                        resolvedNames![entryNameSearch!] => entryResolvedName
                                        entryResolvedName.astNode == entryExpressionAst! -> if { entryResolvedName.symbol => entryExpressionSymbol! }
                                        entryNameSearch! + 1 => entryNameSearch!
                                    }
                                }
                                entryExpressionKind! == 6 -> if {
                                    0 => entryCallSearch!
                                    entryCallSearch! < (prepared.calls -> len) -> while {
                                        prepared.calls[entryCallSearch!] => entryResolvedCall
                                        (entryResolvedCall.sourceModule == sourceIndex! and entryResolvedCall.callAst == entryExpressionAst! and entryResolvedCall.status == 0) -> if {
                                            entryResolvedCall.functionSymbol => entryExpressionSymbol!
                                            entryResolvedCall.targetSourceModule => entryExpressionTargetModule!
                                            (entryResolvedCall.functionSymbol >= 0 and entryResolvedCall.targetSourceModule >= 0) -> if {
                                                prepared.sources[entryResolvedCall.targetSourceModule] -> symbols.collectSource => entryExpressionTargetTable!
                                                ((entryExpressionTargetTable![entryResolvedCall.functionSymbol].flags / 8) % 2 == 1 and (entryExpressionFlags! / 8) % 2 == 0) -> if {
                                                    entryExpressionFlags! + 8 => entryExpressionFlags!
                                                }
                                            }
                                        }
                                        entryCallSearch! + 1 => entryCallSearch!
                                    }
                                }
                                results! -> push(TypedIrNode {
                                    kind: entryExpressionKind!
                                    parent: entryIr!
                                    sourceModule: sourceIndex!
                                    astNode: entryExpressionAst!
                                    symbol: entryExpressionSymbol!
                                    targetModule: entryExpressionTargetModule!
                                    typeOrigin: entryExpressionTypeOrigin!
                                    typeModule: entryExpressionTypeModule!
                                    typeSymbol: entryExpressionTypeSymbol!
                                    typeId: recursiveEntryExpressionTypeId!
                                    typeKind: recursiveEntryExpressionTypeId! < 0 -> if { -1 } else { recursiveSemanticTypes![recursiveEntryExpressionTypeId!].kind }
                                    typeFlags: recursiveEntryExpressionTypeId! < 0 -> if { 0 } else { recursiveTypeFlags![recursiveEntryExpressionTypeId!] }
                                    payloadToken: entryExpression.payloadToken
                                    opcode: entryExpressionOpcode!
                                    operand0: -1
                                    operand1: -1
                                    nextOperand: -1
                                    flags: entryExpressionFlags!
                                })
                                }
                            }
                        }
                        entryExpressionAst! + 1 => entryExpressionAst!
                    }
                    results! -> len => entryExpressionEnd
                    entryExpressionStart => entryParentIr!
                    entryParentIr! < entryExpressionEnd -> while {
                        results![entryParentIr!] => entryIrNode!
                        prepared.nodes[sourceRange.astStart + entryIrNode!.astNode].parent => entryParentAst!
                        -1 => entrySemanticParent!
                        (entryParentAst! >= 0 and entryParentAst! != entryAstIndex! and entrySemanticParent! < 0) -> while {
                            entryAstToIr![entryParentAst!] >= 0 -> if { entryAstToIr![entryParentAst!] => entrySemanticParent! } else { prepared.nodes[sourceRange.astStart + entryParentAst!].parent => entryParentAst! }
                        }
                        entrySemanticParent! >= 0 -> if { entrySemanticParent! => entryIrNode!.parent }
                        entryIrNode! => results![entryParentIr!]
                        entryParentIr! + 1 => entryParentIr!
                    }
                    entryExpressionStart => entryOperandIr!
                    entryOperandIr! < entryExpressionEnd -> while {
                        results![entryOperandIr!] => entryOperator!
                        (entryOperator!.kind == 6 or entryOperator!.kind == 7 or entryOperator!.kind == 8 or (entryOperator!.kind == 9 and entryOperator!.opcode <= -201 and entryOperator!.opcode >= -203) or entryOperator!.kind == 17 or entryOperator!.kind == 22 or entryOperator!.kind == 23) -> if {
                            -1 => entryFirstOperand!
                            -1 => entrySecondOperand!
                            UIntSize(0) => entryFirstStart!
                            entryExpressionStart => entryChildIr!
                            entryChildIr! < entryExpressionEnd -> while {
                                results![entryChildIr!] => entryChild
                                entryChild.parent == entryOperandIr! -> if {
                                    prepared.nodes[sourceRange.astStart + entryChild.astNode].start => entryChildStart
                                    entryFirstOperand! < 0 -> if {
                                        entryChildIr! => entryFirstOperand!
                                        entryChildStart => entryFirstStart!
                                    } else {
                                        entryChildStart < entryFirstStart! -> if {
                                            entryFirstOperand! => entrySecondOperand!
                                            entryChildIr! => entryFirstOperand!
                                            entryChildStart => entryFirstStart!
                                        } else { entrySecondOperand! < 0 -> if { entryChildIr! => entrySecondOperand! } }
                                    }
                                }
                                entryChildIr! + 1 => entryChildIr!
                            }
                            entryFirstOperand! => entryOperator!.operand0
                            (entryOperator!.kind == 6 or entryOperator!.kind == 8 or (entryOperator!.kind == 9 and entryOperator!.opcode <= -201 and entryOperator!.opcode >= -203)) -> if { entrySecondOperand! => entryOperator!.operand1 }
                            entryOperator!.kind == 22 -> if {
                                -1 => entryOperator!.operand0
                                entryFirstOperand! => entryOperator!.operand1
                            }
                            entryOperator! => results![entryOperandIr!]
                        }
                        entryOperandIr! + 1 => entryOperandIr!
                    }
                    entryExpressionStart => entrySiblingIr!
                    entrySiblingIr! < entryExpressionEnd -> while {
                        results![entrySiblingIr!] => entrySibling!
                        -1 => entryNextSibling!
                        UIntSize(0) => entryNextSiblingStart!
                        entryExpressionStart => entrySiblingSearch!
                        entrySiblingSearch! < entryExpressionEnd -> while {
                            results![entrySiblingSearch!] => entrySiblingCandidate
                            (entrySiblingCandidate.parent == entrySibling!.parent and prepared.nodes[sourceRange.astStart + entrySiblingCandidate.astNode].start > prepared.nodes[sourceRange.astStart + entrySibling!.astNode].start) -> if {
                                prepared.nodes[sourceRange.astStart + entrySiblingCandidate.astNode].start => entrySiblingCandidateStart
                                (entryNextSibling! < 0 or entrySiblingCandidateStart < entryNextSiblingStart!) -> if {
                                    entrySiblingSearch! => entryNextSibling!
                                    entrySiblingCandidateStart => entryNextSiblingStart!
                                }
                            }
                            entrySiblingSearch! + 1 => entrySiblingSearch!
                        }
                        entryNextSibling! => entrySibling!.nextOperand
                        entrySibling! => results![entrySiblingIr!]
                        entrySiblingIr! + 1 => entrySiblingIr!
                    }
                    entryExpressionStart => entryControlIrIndex!
                    entryControlIrIndex! < entryExpressionEnd -> while {
                        results![entryControlIrIndex!] => entryControl!
                        entryControl!.kind == 19 -> if {
                            -1 => entryFirstRegionChild!
                            -1 => entryLastRegionChild!
                            UIntSize(0) => entryFirstRegionChildStart!
                            UIntSize(0) => entryLastRegionChildStart!
                            entryExpressionStart => entryRegionChildSearch!
                            entryRegionChildSearch! < entryExpressionEnd -> while {
                                results![entryRegionChildSearch!].parent == entryControlIrIndex! -> if {
                                    prepared.nodes[sourceRange.astStart + results![entryRegionChildSearch!].astNode].start => entryRegionChildStart
                                    (entryFirstRegionChild! < 0 or entryRegionChildStart < entryFirstRegionChildStart!) -> if {
                                        entryRegionChildSearch! => entryFirstRegionChild!
                                        entryRegionChildStart => entryFirstRegionChildStart!
                                    }
                                    (entryLastRegionChild! < 0 or entryRegionChildStart > entryLastRegionChildStart!) -> if {
                                        entryRegionChildSearch! => entryLastRegionChild!
                                        entryRegionChildStart => entryLastRegionChildStart!
                                    }
                                }
                                entryRegionChildSearch! + 1 => entryRegionChildSearch!
                            }
                            entryFirstRegionChild! => entryControl!.operand0
                            (entryLastRegionChild! >= 0 and results![entryLastRegionChild!].kind == 9) -> while {
                                -1 => entryNestedRegionResult!
                                UIntSize(0) => entryNestedRegionResultStart!
                                entryExpressionStart => entryNestedResultSearch!
                                entryNestedResultSearch! < entryExpressionEnd -> while {
                                    results![entryNestedResultSearch!].parent == entryLastRegionChild! -> if {
                                        prepared.nodes[sourceRange.astStart + results![entryNestedResultSearch!].astNode].start => entryNestedResultStart
                                        (entryNestedRegionResult! < 0 or entryNestedResultStart > entryNestedRegionResultStart!) -> if {
                                            entryNestedResultSearch! => entryNestedRegionResult!
                                            entryNestedResultStart => entryNestedRegionResultStart!
                                        }
                                    }
                                    entryNestedResultSearch! + 1 => entryNestedResultSearch!
                                }
                                entryNestedRegionResult! >= 0 -> if { entryNestedRegionResult! => entryLastRegionChild! } else { -1 => entryLastRegionChild! }
                            }
                            entryLastRegionChild! => entryControl!.operand1
                            entryControl! => results![entryControlIrIndex!]
                        }
                        (entryControl!.kind == 18 or entryControl!.kind == 20) -> if {
                            prepared.nodes[sourceRange.astStart + entryControl!.astNode].parent => entryControlFlowAst
                            -1 => entryConditionIr!
                            UIntSize(0) => entryConditionStart!
                            1000000 => entryConditionDistance!
                            entryExpressionStart => entryConditionSearch!
                            entryConditionSearch! < entryExpressionEnd -> while {
                                results![entryConditionSearch!] => entryConditionCandidate
                                prepared.nodes[sourceRange.astStart + entryConditionCandidate.astNode].parent => entryConditionAncestor!
                                1 => entryCandidateConditionDistance!
                                false => entryConditionBelongsToFlow!
                                (entryConditionAncestor! >= 0 and not entryConditionBelongsToFlow!) -> while {
                                    entryConditionAncestor! == entryControlFlowAst -> if { true => entryConditionBelongsToFlow! } else {
                                        prepared.nodes[sourceRange.astStart + entryConditionAncestor!].parent => entryConditionAncestor!
                                        entryCandidateConditionDistance! + 1 => entryCandidateConditionDistance!
                                    }
                                }
                                (entryConditionBelongsToFlow! and prepared.nodes[sourceRange.astStart + entryConditionCandidate.astNode].start < prepared.nodes[sourceRange.astStart + entryControl!.astNode].start) -> if {
                                    prepared.nodes[sourceRange.astStart + entryConditionCandidate.astNode].start => entryCandidateStart
                                    (entryConditionIr! < 0 or entryCandidateConditionDistance! < entryConditionDistance! or (entryCandidateConditionDistance! == entryConditionDistance! and entryCandidateStart > entryConditionStart!)) -> if {
                                        entryConditionSearch! => entryConditionIr!
                                        entryCandidateStart => entryConditionStart!
                                        entryCandidateConditionDistance! => entryConditionDistance!
                                    }
                                }
                                entryConditionSearch! + 1 => entryConditionSearch!
                            }
                            (entryConditionIr! < 0 and entryControl!.parent >= entryExpressionStart and results![entryControl!.parent].kind != 19) -> if {
                                entryControl!.parent => entryEnclosingConditionIr!
                                results![entryEnclosingConditionIr!].parent => entryControl!.parent
                                entryEnclosingConditionIr! => entryConditionIr!
                            }
                            (entryConditionIr! >= 0 and results![entryConditionIr!].kind == 9) -> while {
                                -1 => entryNestedConditionResult!
                                UIntSize(0) => entryNestedConditionStart!
                                entryExpressionStart => entryNestedConditionSearch!
                                entryNestedConditionSearch! < entryExpressionEnd -> while {
                                    results![entryNestedConditionSearch!].parent == entryConditionIr! -> if {
                                        prepared.nodes[sourceRange.astStart + results![entryNestedConditionSearch!].astNode].start => entryNestedCandidateStart
                                        (entryNestedConditionResult! < 0 or entryNestedCandidateStart > entryNestedConditionStart!) -> if {
                                            entryNestedConditionSearch! => entryNestedConditionResult!
                                            entryNestedCandidateStart => entryNestedConditionStart!
                                        }
                                    }
                                    entryNestedConditionSearch! + 1 => entryNestedConditionSearch!
                                }
                                entryNestedConditionResult! >= 0 -> if { entryNestedConditionResult! => entryConditionIr! } else { -1 => entryConditionIr! }
                            }
                            -1 => entryThenRegion!
                            -1 => entryElseRegion!
                            entryExpressionStart => entryRegionSearch!
                            entryRegionSearch! < entryExpressionEnd -> while {
                                results![entryRegionSearch!] => entryRegionCandidate
                                (entryRegionCandidate.kind == 19 and entryRegionCandidate.parent == entryControlIrIndex!) -> if {
                                    entryThenRegion! < 0 -> if { entryRegionSearch! => entryThenRegion! } else { entryRegionSearch! => entryElseRegion! }
                                }
                                entryRegionSearch! + 1 => entryRegionSearch!
                            }
                            entryConditionIr! => entryControl!.operand0
                            entryThenRegion! => entryControl!.operand1
                            entryControl!.kind == 18 -> if { entryElseRegion! => entryControl!.nextOperand } else { -1 => entryControl!.nextOperand }
                            entryControl! => results![entryControlIrIndex!]
                            (entryControl!.kind == 20 and entryConditionIr! >= 0) -> if {
                                results![entryConditionIr!] => entryLoopCondition!
                                entryControlIrIndex! => entryLoopCondition!.parent
                                entryLoopCondition! => results![entryConditionIr!]
                            }
                        }
                        entryControlIrIndex! + 1 => entryControlIrIndex!
                    }
                    entryExpressionStart => entryBindingNameIr!
                    entryBindingNameIr! < entryExpressionEnd -> while {
                        results![entryBindingNameIr!] => entryBindingName!
                        entryBindingName!.kind == 5 -> if {
                            entryExpressionStart => entryBindingDefinitionSearch!
                            entryBindingDefinitionSearch! < entryExpressionEnd -> while {
                                (results![entryBindingDefinitionSearch!].kind == 17 and results![entryBindingDefinitionSearch!].symbol == entryBindingName!.symbol) -> if {
                                    entryBindingDefinitionSearch! => entryBindingName!.operand0
                                }
                                entryBindingDefinitionSearch! + 1 => entryBindingDefinitionSearch!
                            }
                            entryBindingName! => results![entryBindingNameIr!]
                        }
                        entryBindingNameIr! + 1 => entryBindingNameIr!
                    }
                    entryExpressionStart => entryAggregateIrIndex!
                    entryAggregateIrIndex! < entryExpressionEnd -> while {
                        results![entryAggregateIrIndex!] => entryAggregate!
                        (entryAggregate!.kind == 12 or entryAggregate!.kind == 14 or entryAggregate!.kind == 16) -> if {
                            -1 => entryFirstFieldOperand!
                            entryExpressionStart => entryFieldOperandSearch!
                            entryFieldOperandSearch! < entryExpressionEnd -> while {
                                results![entryFieldOperandSearch!].parent == entryAggregateIrIndex! -> if {
                                    (entryFirstFieldOperand! < 0 or prepared.nodes[sourceRange.astStart + results![entryFieldOperandSearch!].astNode].start < prepared.nodes[sourceRange.astStart + results![entryFirstFieldOperand!].astNode].start) -> if { entryFieldOperandSearch! => entryFirstFieldOperand! }
                                }
                                entryFieldOperandSearch! + 1 => entryFieldOperandSearch!
                            }
                            entryFirstFieldOperand! => entryAggregate!.operand0
                            entryAggregate! => results![entryAggregateIrIndex!]
                        }
                        entryAggregateIrIndex! + 1 => entryAggregateIrIndex!
                    }
                    results![entryIr!] => entryNode!
                    entryAstToIr![inferred![entryResultTypeIndex!].astNode] => entryResultIr!
                    (entryResultIr! >= 0 and results![entryResultIr!].kind == 9) -> if {
                        entryExpressionStart => entryResultControlSearch!
                        entryResultControlSearch! < entryExpressionEnd -> while {
                            ((results![entryResultControlSearch!].kind == 18 or results![entryResultControlSearch!].kind == 20) and results![entryResultControlSearch!].parent == entryResultIr!) -> if { entryResultControlSearch! => entryResultIr! }
                            entryResultControlSearch! + 1 => entryResultControlSearch!
                        }
                    }
                    entryResultIr! => entryNode!.operand0
                    entryNode! => results![entryIr!]
                }
            }
            entryAstIndex! + 1 => entryAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    0 => ownedBindingIndex!
    ownedBindingIndex! < (results! -> len) -> while {
        results![ownedBindingIndex!] => ownedBinding!
        (ownedBinding!.kind == 17 and (ownedBinding!.typeOrigin == 13 or ownedBinding!.typeOrigin == 15)) -> if {
            -1 => ownedBindingValue!
            0 => ownedBindingValueSearch!
            ownedBindingValueSearch! < (results! -> len) -> while {
                results![ownedBindingValueSearch!] => ownedValueCandidate
                ((ownedBinding!.typeOrigin == 13 and ownedValueCandidate.kind == 14) or (ownedBinding!.typeOrigin == 15 and ownedValueCandidate.kind == 16)) -> if {
                    ownedValueCandidate.parent => ownedValueAncestor!
                    false => ownedValueBelongs!
                    (ownedValueAncestor! >= 0 and not ownedValueBelongs!) -> while {
                        ownedValueAncestor! == ownedBindingIndex! -> if { true => ownedValueBelongs! } else { results![ownedValueAncestor!].parent => ownedValueAncestor! }
                    }
                    (ownedValueBelongs! and ownedBindingValue! < 0) -> if { ownedBindingValueSearch! => ownedBindingValue! }
                }
                ownedBindingValueSearch! + 1 => ownedBindingValueSearch!
            }
            ownedBindingValue! >= 0 -> if {
                ownedBindingValue! => ownedBinding!.operand0
                ownedBinding! => results![ownedBindingIndex!]
            }
        }
        ownedBindingIndex! + 1 => ownedBindingIndex!
    }
    0 => memberIndex!
    memberIndex! < (results! -> len) -> while {
        results![memberIndex!] => member!
        (member!.kind == 13 and member!.operand0 >= 0) -> if {
            results![member!.operand0] => memberBase
            memberBase.typeOrigin => memberCurrentOrigin!
            memberBase.typeModule => memberCurrentModule!
            memberBase.typeSymbol => memberCurrentSymbol!
            prepared.sources[member!.sourceModule] -> ast.lowerSource => memberNodes!
            prepared.sources[member!.sourceModule] -> lexer.lexSource => memberTokens!
            memberNodes![member!.astNode] => memberAst
            # The operand already represents the complete typed base. Resolve
            # only the trailing path, so an identifier inside values[index]
            # cannot be mistaken for a field in values[index].field.
            1 => memberIdentifierOrdinal!
            memberNodes![memberBase.astNode] => memberBaseAst
            memberBaseAst.start + memberBaseAst.length => memberBaseEnd
            memberAst.firstToken => memberTokenIndex!
            true => beforeMemberBaseEnd!
            (beforeMemberBaseEnd! and memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount) -> while {
                memberTokens![memberTokenIndex!].span.start < memberBaseEnd -> if {
                    memberTokenIndex! + 1 => memberTokenIndex!
                } else {
                    false => beforeMemberBaseEnd!
                }
            }
            memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                memberTokens![memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                    memberIdentifierOrdinal! > 0 -> if {
                        memberCurrentModule! => memberOwnerSource!
                        memberCurrentOrigin! == 2 -> if { prepared.modules[memberCurrentModule!].sourceIndex => memberOwnerSource! }
                        prepared.sources[memberOwnerSource!] -> symbols.collectSource => memberOwnerTable!
                        prepared.sources[memberOwnerSource!] -> lexer.lexSource => memberOwnerTokens!
                        0 => memberFieldOrdinal!
                        0 => memberFieldIndex!
                        memberFieldIndex! < (memberOwnerTable! -> len) -> while {
                            memberOwnerTable![memberFieldIndex!] => memberField
                            (memberField.kind == 26 and memberField.parent == memberCurrentSymbol!) -> if {
                                memberTokens![memberTokenIndex!] => memberName
                                memberOwnerTokens![memberField.nameToken] => memberFieldName
                                memberName.span.length == memberFieldName.span.length => memberEqual!
                                UIntSize(0) => memberNameByte!
                                (memberEqual! and memberNameByte! < memberName.span.length) -> while {
                                    prepared.sources[member!.sourceModule] -> byte(memberName.span.start + memberNameByte!) => memberByte
                                    prepared.sources[memberOwnerSource!] -> byte(memberFieldName.span.start + memberNameByte!) => memberFieldByte
                                    memberByte != memberFieldByte -> if { false => memberEqual! }
                                    memberNameByte! + UIntSize(1) => memberNameByte!
                                }
                                memberEqual! -> if {
                                    memberFieldOrdinal! => member!.symbol
                                    memberOwnerSource! => member!.targetModule
                                    -1 => memberFieldTypeIndex!
                                    0 => memberFieldTypeSearch!
                                    memberFieldTypeSearch! < (prepared.nominal -> len) -> while {
                                        (prepared.nominal[memberFieldTypeSearch!].sourceModule == memberOwnerSource! and prepared.nominal[memberFieldTypeSearch!].typeAst == memberField.typeNode) -> if { memberFieldTypeSearch! => memberFieldTypeIndex! }
                                        memberFieldTypeSearch! + 1 => memberFieldTypeSearch!
                                    }
                                    memberFieldTypeIndex! >= 0 -> if {
                                        prepared.nominal[memberFieldTypeIndex!] => memberFieldType
                                        memberFieldType.origin => memberCurrentOrigin!
                                        memberFieldType.targetModule => memberCurrentModule!
                                        memberFieldType.targetSymbol => memberCurrentSymbol!
                                    } else {
                                        0 => memberCompositeSearch!
                                        memberCompositeSearch! < (prepared.composite -> len) -> while {
                                            prepared.composite[memberCompositeSearch!] => memberCompositeType
                                            (memberCompositeType.sourceModule == memberOwnerSource! and memberCompositeType.typeAst == memberField.typeNode) -> if {
                                                10 + memberCompositeType.kind => memberCurrentOrigin!
                                                memberCompositeType.kind == 5 -> if {
                                                    memberCompositeType.keySymbol => memberCurrentModule!
                                                    memberCompositeType.valueSymbol => memberCurrentSymbol!
                                                } else {
                                                    memberCompositeType.elementModule => memberCurrentModule!
                                                    memberCompositeType.elementSymbol => memberCurrentSymbol!
                                                }
                                            }
                                            memberCompositeSearch! + 1 => memberCompositeSearch!
                                        }
                                    }
                                    memberCurrentOrigin! => member!.typeOrigin
                                    memberCurrentModule! => member!.typeModule
                                    memberCurrentSymbol! => member!.typeSymbol
                                    0 => memberTypeReferenceIndex!
                                    memberTypeReferenceIndex! < (recursiveReferences! -> len) -> while {
                                        recursiveReferences![memberTypeReferenceIndex!] => memberTypeReference
                                        (memberTypeReference.status == 0 and memberTypeReference.sourceModule == memberOwnerSource! and memberTypeReference.typeAst == memberField.typeNode) -> if {
                                            memberTypeReference.typeId => member!.typeId
                                            recursiveSemanticTypes![memberTypeReference.typeId].kind => member!.typeKind
                                            recursiveTypeFlags![memberTypeReference.typeId] => member!.typeFlags
                                        }
                                        memberTypeReferenceIndex! + 1 => memberTypeReferenceIndex!
                                    }
                                }
                                memberFieldOrdinal! + 1 => memberFieldOrdinal!
                            }
                            memberFieldIndex! + 1 => memberFieldIndex!
                        }
                    }
                    memberIdentifierOrdinal! + 1 => memberIdentifierOrdinal!
                }
                memberTokenIndex! + 1 => memberTokenIndex!
            }
            member! => results![memberIndex!]
        }
        memberIndex! + 1 => memberIndex!
    }
    0 => loopExitIndex!
    loopExitIndex! < (results! -> len) -> while {
        results![loopExitIndex!] => loopExit!
        (loopExit!.kind == 21 or loopExit!.kind == 22) -> if {
            loopExit!.parent => targetLoop!
            (targetLoop! >= 0 and results![targetLoop!].kind != 20) -> while {
                results![targetLoop!].parent => targetLoop!
            }
            targetLoop! => loopExit!.operand0
            loopExit! => results![loopExitIndex!]
        }
        loopExitIndex! + 1 => loopExitIndex!
    }
    0 => recursiveIrIndex!
    recursiveIrIndex! < (results! -> len) -> while {
        results![recursiveIrIndex!] => recursiveIr!
        recursiveExpressions! -> each recursiveExpression {
            (recursiveExpression.status == 0 and recursiveExpression.sourceModule == recursiveIr!.sourceModule and recursiveExpression.astNode == recursiveIr!.astNode) -> if {
                recursiveExpression.typeId => recursiveIr!.typeId
                recursiveSemanticTypes![recursiveExpression.typeId].kind => recursiveIr!.typeKind
                recursiveTypeFlags![recursiveExpression.typeId] => recursiveIr!.typeFlags
            }
        }
        recursiveIr! => results![recursiveIrIndex!]
        recursiveIrIndex! + 1 => recursiveIrIndex!
    }
    0 => canonicalBindingIndex!
    canonicalBindingIndex! < (results! -> len) -> while {
        results![canonicalBindingIndex!] => canonicalBinding!
        (canonicalBinding!.kind == 17 and canonicalBinding!.operand0 >= 0 and results![canonicalBinding!.operand0].typeId >= 0) -> if {
            results![canonicalBinding!.operand0].typeId => canonicalBinding!.typeId
            results![canonicalBinding!.operand0].typeKind => canonicalBinding!.typeKind
            results![canonicalBinding!.operand0].typeFlags => canonicalBinding!.typeFlags
            canonicalBinding! => results![canonicalBindingIndex!]
        }
        canonicalBindingIndex! + 1 => canonicalBindingIndex!
    }
    results!
}

public lowerPrepared request: move TypedIrRequest -> [TypedIrNode; ~] {
    [file.SourceText; ~] => sources!
    request.sources -> each source {
        source -> file.borrowText => ownedSource!
        sources! -> push(ownedSource!)
    }
    [typeIds.SemanticType; ~] => types!
    request.types -> each semanticType { types! -> push(semanticType) }
    [typeIds.TypeReference; ~] => references!
    request.references -> each reference { references! -> push(reference) }
    [typeIds.NominalField; ~] => fields!
    request.fields -> each field { fields! -> push(field) }
    [nominalTypes.NominalType; ~] => nominal!
    request.nominal -> each nominalType { nominal! -> push(nominalType) }
    [compositeTypes.CompositeType; ~] => composite!
    request.composite -> each compositeType { composite! -> push(compositeType) }
    [modules.ModuleIdentity; ~] => moduleIdentities!
    request.modules -> each moduleIdentity { moduleIdentities! -> push(moduleIdentity) }
    [qualified.QualifiedResolution; ~] => qualifiedResults!
    request.qualified -> each qualifiedResult { qualifiedResults! -> push(qualifiedResult) }
    [calls.ModuleCallResolution; ~] => moduleCalls!
    request.calls -> each moduleCall { moduleCalls! -> push(moduleCall) }
    [analysis.SourceAnalysisRange; ~] => ranges!
    request.analysisRanges -> each sourceRange { ranges! -> push(sourceRange) }
    [ast.AstNode; ~] => nodes!
    request.analysisNodes -> each node { nodes! -> push(node) }
    [syntax.SyntaxToken; ~] => tokens!
    request.analysisTokens -> each token { tokens! -> push(token) }
    [symbols.Symbol; ~] => symbolTable!
    request.analysisSymbols -> each symbol { symbolTable! -> push(symbol) }
    [resolution.ResolvedName; ~] => names!
    request.analysisNames -> each name { names! -> push(name) }
    [typeTerms.TypeTerm; ~] => terms!
    [semanticTypes.TypeUse; ~] => typeUses!
    semanticContext.CompilationContext {
        sources: sources!
        types: types!
        references: references!
        fields: fields!
        nominal: nominal!
        composite: composite!
        modules: moduleIdentities!
        imports: [modules.ImportEdge; ~]
        resolvedImports: [moduleResolve.ResolvedImport; ~]
        qualified: qualifiedResults!
        calls: moduleCalls!
        ranges: ranges!
        nodes: nodes!
        tokens: tokens!
        symbols: symbolTable!
        names: names!
        terms: terms!
        typeUses: typeUses!
    } => prepared!
    prepared! -> lowerContext => result!
    result!
}

public lower sources: [Text; ~] -> [TypedIrNode; ~] {
    sources -> semanticContext.prepare => prepared!
    prepared! -> lowerContext => result!
    result!
}
public movesFrom ir: [TypedIrNode; ~] -> [MoveEvent; ~] {
    [MoveEvent; ~] => events!
    0 => siteIndex!
    siteIndex! < (ir -> len) -> while {
        ir[siteIndex!] => site
        -1 => movedValueIr!
        (site.kind == 6 and site.symbol >= 0 and site.operand0 >= 0) -> if {
            false => consumes!
            0 => functionIndex!
            functionIndex! < (ir -> len) -> while {
                ir[functionIndex!] => function
                (function.kind == 0 and function.sourceModule == site.targetModule and function.symbol == site.symbol and function.operand1 >= 0) -> if {
                    ir[function.operand1] => parameter
                    parameter.flags % 2 == 1 -> if { true => consumes! }
                }
                functionIndex! + 1 => functionIndex!
            }
            consumes! -> if { site.operand0 => movedValueIr! }
        }
        (site.kind == 17 and site.operand0 >= 0 and ir[site.operand0].kind == 13 and ir[site.operand0].typeFlags % 2 == 1) -> if {
            site.operand0 => movedValueIr!
        }
        movedValueIr! >= 0 -> if {
            movedValueIr! => moveRootIr!
            -1 => moveMemberIr!
            ir[moveRootIr!].kind == 13 -> if { moveRootIr! => moveMemberIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 13) -> while { ir[moveRootIr!].operand0 => moveRootIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 5 and ir[moveRootIr!].symbol >= 0) -> if {
                site.parent => moveRegion!
                (moveRegion! >= 0 and ir[moveRegion!].kind != 1 and ir[moveRegion!].kind != 19 and ir[moveRegion!].kind != 20) -> while {
                    ir[moveRegion!].parent => moveRegion!
                }
                events! -> push(MoveEvent {
                    siteIr: siteIndex!
                    sourceModule: ir[moveRootIr!].sourceModule
                    symbol: ir[moveRootIr!].symbol
                    regionIr: moveRegion!
                    memberIr: moveMemberIr!
                })
            }
        }
        siteIndex! + 1 => siteIndex!
    }
    events!
}

public moves sources: [Text; ~] -> [MoveEvent; ~] {
    sources -> lower -> movesFrom
}

public coroutinePlanContext prepared: semanticContext.CompilationContext -> CoroutinePlan {
    prepared -> lowerContext => ir!
    [CoroutineSuspendPoint; ~] => points!
    0 => sourceIndex!
    sourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[sourceIndex!] -> len => sourceLength
        prepared.sources[sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        prepared.ranges[sourceIndex!] => sourceRange
        0 => awaitAstIndex!
        awaitAstIndex! < sourceRange.astCount -> while {
            prepared.nodes[sourceRange.astStart + awaitAstIndex!] => awaitAst
            false => isAwait!
            false => isYield!
            (awaitAst.kind == 16 and awaitAst.parent >= 0 and prepared.nodes[sourceRange.astStart + awaitAst.parent].kind == 10 and awaitAst.payloadToken >= 0) -> if {
                prepared.tokens[sourceRange.tokenStart + awaitAst.payloadToken] => awaitToken
                awaitToken.span.length == UIntSize(5) -> if {
                    source -> byte(awaitToken.span.start) => awaitByte0
                    source -> byte(awaitToken.span.start + UIntSize(1)) => awaitByte1
                    source -> byte(awaitToken.span.start + UIntSize(2)) => awaitByte2
                    source -> byte(awaitToken.span.start + UIntSize(3)) => awaitByte3
                    source -> byte(awaitToken.span.start + UIntSize(4)) => awaitByte4
                    (awaitByte0 == UInt8(97) and awaitByte1 == UInt8(119) and awaitByte2 == UInt8(97) and awaitByte3 == UInt8(105) and awaitByte4 == UInt8(116)) -> if {
                        true => isAwait!
                    }
                }
            }
            (awaitAst.kind == 15 and awaitAst.payloadToken >= 0) -> if {
                prepared.tokens[sourceRange.tokenStart + awaitAst.payloadToken] => yieldToken
                yieldToken.span.length == UIntSize(5) -> if {
                    source -> byte(yieldToken.span.start) => yieldByte0
                    source -> byte(yieldToken.span.start + UIntSize(1)) => yieldByte1
                    source -> byte(yieldToken.span.start + UIntSize(2)) => yieldByte2
                    source -> byte(yieldToken.span.start + UIntSize(3)) => yieldByte3
                    source -> byte(yieldToken.span.start + UIntSize(4)) => yieldByte4
                    (yieldByte0 == UInt8(121) and yieldByte1 == UInt8(105) and yieldByte2 == UInt8(101) and yieldByte3 == UInt8(108) and yieldByte4 == UInt8(100)) -> if {
                        true => isYield!
                    }
                }
            }
            (isAwait! or isYield!) -> if {
                awaitAst.parent => functionAstIndex!
                (functionAstIndex! >= 0 and prepared.nodes[sourceRange.astStart + functionAstIndex!].kind != 7) -> while {
                    prepared.nodes[sourceRange.astStart + functionAstIndex!].parent => functionAstIndex!
                }
                (functionAstIndex! >= 0 and (prepared.nodes[sourceRange.astStart + functionAstIndex!].flags / 8) % 2 == 1) -> if {
                    -1 => functionIr!
                    -1 => awaitIr!
                    0 => irIndex!
                    irIndex! < (ir! -> len) -> while {
                        ir![irIndex!] => node
                        (node.sourceModule == sourceIndex! and node.astNode == functionAstIndex! and node.kind == 0) -> if {
                            irIndex! => functionIr!
                        }
                        (node.sourceModule == sourceIndex! and node.astNode == awaitAstIndex!) -> if {
                            irIndex! => awaitIr!
                        }
                        irIndex! + 1 => irIndex!
                    }
                    functionIr! >= 0 -> if {
                        1 => state!
                        0 => pointSearch!
                        pointSearch! < (points! -> len) -> while {
                            points![pointSearch!].functionIr == functionIr! -> if { state! + 1 => state! }
                            pointSearch! + 1 => pointSearch!
                        }
                        points! -> push(CoroutineSuspendPoint {
                            functionIr: functionIr!
                            sourceModule: sourceIndex!
                            awaitAst: awaitAstIndex!
                            awaitIr: awaitIr!
                            state: state!
                            kind: isYield! -> if { 1 } else { 0 }
                        })
                    }
                }
            }
            awaitAstIndex! + 1 => awaitAstIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    [CoroutineFrameSlot; ~] => slots!
    0 => pointIndex!
    pointIndex! < (points! -> len) -> while {
        points![pointIndex!] => point
        prepared.ranges[point.sourceModule] => sourceRange
        ir![point.functionIr].astNode => functionAst
        prepared.nodes[sourceRange.astStart + point.awaitAst].start => awaitStart
        point.awaitAst => awaitBindingAst!
        (awaitBindingAst! >= 0 and prepared.nodes[sourceRange.astStart + awaitBindingAst!].kind != 9) -> while {
            prepared.nodes[sourceRange.astStart + awaitBindingAst!].parent => awaitBindingAst!
        }
        0 => definitionIndex!
        definitionIndex! < (ir! -> len) -> while {
            ir![definitionIndex!] => definition
            false => belongsToFunction!
            definition.astNode => definitionAncestor!
            (definitionAncestor! >= 0 and not belongsToFunction!) -> while {
                definitionAncestor! == functionAst -> if { true => belongsToFunction! } else {
                    prepared.nodes[sourceRange.astStart + definitionAncestor!].parent => definitionAncestor!
                }
            }
            (definition.kind == 17 and definition.sourceModule == point.sourceModule and definition.symbol >= 0 and definition.astNode != awaitBindingAst! and belongsToFunction! and prepared.nodes[sourceRange.astStart + definition.astNode].start < awaitStart) -> if {
                false => usedAfterAwait!
                0 => useIndex!
                (useIndex! < (ir! -> len) and not usedAfterAwait!) -> while {
                    ir![useIndex!] => use
                    (use.kind == 5 and use.sourceModule == point.sourceModule and use.symbol == definition.symbol and use.astNode >= 0 and prepared.nodes[sourceRange.astStart + use.astNode].start > awaitStart) -> if {
                        false => useBelongsToFunction!
                        use.astNode => useAncestor!
                        (useAncestor! >= 0 and not useBelongsToFunction!) -> while {
                            useAncestor! == functionAst -> if { true => useBelongsToFunction! } else {
                                prepared.nodes[sourceRange.astStart + useAncestor!].parent => useAncestor!
                            }
                        }
                        useBelongsToFunction! -> if { true => usedAfterAwait! }
                    }
                    useIndex! + 1 => useIndex!
                }
                usedAfterAwait! -> if {
                    definition.flags => frameFlags!
                    (definition.typeFlags / 2) % 2 == 1 -> if {
                        frameFlags! + 2 => frameFlags!
                    }
                    false => isTaskSlot!
                    0 => taskSearch!
                    (taskSearch! < (ir! -> len) and not isTaskSlot!) -> while {
                        ir![taskSearch!] => taskCandidate
                        ((taskCandidate.flags / 8) % 2 == 1 and taskCandidate.sourceModule == point.sourceModule and taskCandidate.astNode >= 0) -> if {
                            taskCandidate.astNode => taskAncestor!
                            false => belongsToDefinition!
                            (taskAncestor! >= 0 and not belongsToDefinition!) -> while {
                                taskAncestor! == definition.astNode -> if { true => belongsToDefinition! } else {
                                    prepared.nodes[sourceRange.astStart + taskAncestor!].parent => taskAncestor!
                                }
                            }
                            belongsToDefinition! -> if { true => isTaskSlot! }
                        }
                        taskSearch! + 1 => taskSearch!
                    }
                    isTaskSlot! -> if { frameFlags! + 4 => frameFlags! }
                    slots! -> push(CoroutineFrameSlot {
                        functionIr: point.functionIr
                        state: point.state
                        sourceModule: point.sourceModule
                        symbol: definition.symbol
                        typeOrigin: definition.typeOrigin
                        typeModule: definition.typeModule
                        typeSymbol: definition.typeSymbol
                        typeId: definition.typeId
                        typeKind: definition.typeKind
                        typeFlags: definition.typeFlags
                        flags: frameFlags!
                    })
                }
            }
            definitionIndex! + 1 => definitionIndex!
        }
        pointIndex! + 1 => pointIndex!
    }
    [CoroutineFrameSlot; ~] => destroys!
    0 => destroyPointIndex!
    destroyPointIndex! < (points! -> len) -> while {
        points![destroyPointIndex!] => point
        destroys! -> push(CoroutineFrameSlot {
            functionIr: point.functionIr
            state: point.state
            sourceModule: point.sourceModule
            symbol: -1
            typeOrigin: -1
            typeModule: -1
            typeSymbol: -1
            typeId: -1
            typeKind: -1
            typeFlags: 0
            flags: 4
        })
        slots! -> len => slotCursor!
        slotCursor! > 0 -> while {
            slotCursor! - 1 => slotCursor!
            slots![slotCursor!] => slot
            (slot.functionIr == point.functionIr and slot.state == point.state and ((slot.flags / 2) % 2 == 1 or (slot.flags / 4) % 2 == 1)) -> if {
                destroys! -> push(slot)
            }
        }
        destroyPointIndex! + 1 => destroyPointIndex!
    }
    CoroutinePlan { ir: ir!, points: points!, slots: slots!, destroys: destroys! } => plan!
    plan!
}

public coroutinePlan sources: [Text; ~] -> CoroutinePlan {
    sources -> semanticContext.prepare => prepared
    prepared -> coroutinePlanContext
}

public suspensions sources: [Text; ~] -> [CoroutineSuspendPoint; ~] {
    sources -> coroutinePlan => plan
    [CoroutineSuspendPoint; ~] => points!
    plan.points -> each point { points! -> push(point) }
    points!
}

public frameSlots sources: [Text; ~] -> [CoroutineFrameSlot; ~] {
    sources -> coroutinePlan => plan
    [CoroutineFrameSlot; ~] => slots!
    plan.slots -> each slot { slots! -> push(slot) }
    slots!
}

# Destruction order is explicit and deterministic. Each state starts with a
# synthetic active-child entry (symbol -1, Task bit set), followed by initialized
# owned frame slots in reverse definition order. Scalar-only slots need no drop.
public destroySlots sources: [Text; ~] -> [CoroutineFrameSlot; ~] {
    sources -> coroutinePlan => plan
    [CoroutineFrameSlot; ~] => destroys!
    plan.destroys -> each slot { destroys! -> push(slot) }
    destroys!
}
