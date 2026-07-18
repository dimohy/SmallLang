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
# 23 explicit return (operand0 is the optional returned value), 24 index
# assignment (operand0 is the value and operand1 is the index), 25 mutable
# struct member assignment (operand0 is the value and operand1 is the owner
# binding), 26 enum constructor (operand0 is the optional payload), 27 enum
# match (operand0 subject, operand1 first arm), 29 enum payload binding. Enum
# arms reuse kind 19 regions and carry their variant tag in opcode. AST kind 48
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

struct SourceTypedIr {
    nodes: [TypedIrNode; ~]
}

struct FunctionLowerRequest {
    sourceIndex: Int
    symbolIndex: Int
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

struct FlowIntrinsicRequest {
    source: Text
    tokens: [syntax.SyntaxToken; ~]
    tokenStart: Int
    node: ast.AstNode
}

intrinsicOpcode request: IntrinsicNameRequest -> Int {
    -1 => opcode!
    request.token.span.length == UIntSize(3) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(110)) -> if { -201 => opcode! }
    }
    request.token.span.length == UIntSize(4) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(98) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(121) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(116) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(101)) -> if { -202 => opcode! }
        ((request.source -> byte(request.token.span.start)) == UInt8(112) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(117) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(115) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(104)) -> if { -204 => opcode! }
        ((request.source -> byte(request.token.span.start)) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(99) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(104)) -> if { -208 => opcode! }
        ((request.source -> byte(request.token.span.start)) == UInt8(116) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(107) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(101)) -> if { -213 => opcode! }
    }
    request.token.span.length == UIntSize(5) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(115) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(105) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(99) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(101)) -> if { -203 => opcode! }
    }
    request.token.span.length == UIntSize(7) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(109) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(112) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(84) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(5))) == UInt8(120) and (request.source -> byte(request.token.span.start + UIntSize(6))) == UInt8(116)) -> if { -205 => opcode! }
    }
    request.token.span.length == UIntSize(8) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(112) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(114) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(5))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(6))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(7))) == UInt8(108)) -> if { -207 => opcode! }
    }
    request.token.span.length == UIntSize(10) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(98) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(111) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(114) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(114) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(111) and (request.source -> byte(request.token.span.start + UIntSize(5))) == UInt8(119) and (request.source -> byte(request.token.span.start + UIntSize(6))) == UInt8(84) and (request.source -> byte(request.token.span.start + UIntSize(7))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(8))) == UInt8(120) and (request.source -> byte(request.token.span.start + UIntSize(9))) == UInt8(116)) -> if { -206 => opcode! }
    }
    request.token.span.length == UIntSize(11) -> if {
        ((request.source -> byte(request.token.span.start)) == UInt8(116) and (request.source -> byte(request.token.span.start + UIntSize(1))) == UInt8(114) and (request.source -> byte(request.token.span.start + UIntSize(2))) == UInt8(121) and (request.source -> byte(request.token.span.start + UIntSize(3))) == UInt8(80) and (request.source -> byte(request.token.span.start + UIntSize(4))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(5))) == UInt8(114) and (request.source -> byte(request.token.span.start + UIntSize(6))) == UInt8(97) and (request.source -> byte(request.token.span.start + UIntSize(7))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(8))) == UInt8(108) and (request.source -> byte(request.token.span.start + UIntSize(9))) == UInt8(101) and (request.source -> byte(request.token.span.start + UIntSize(10))) == UInt8(108)) -> if { -209 => opcode! }
    }
    opcode!
}

flowIntrinsicOpcode request: FlowIntrinsicRequest -> Int {
    -1 => nameToken!
    request.node.firstToken => tokenIndex!
    false => inTypeArguments!
    false => inCallArguments!
    true => beforeBlock!
    (beforeBlock! and tokenIndex! < request.node.firstToken + request.node.tokenCount) -> while {
        request.tokens[request.tokenStart + tokenIndex!] => token
        token.kind == grammar.tokenIdLeftBrace -> if { false => beforeBlock! } else {
            token.kind == grammar.tokenIdLess -> if { true => inTypeArguments! }
            token.kind == grammar.tokenIdGreater -> if { false => inTypeArguments! }
            token.kind == grammar.tokenIdLeftParen -> if { true => inCallArguments! }
            token.kind == grammar.tokenIdRightParen -> if { false => inCallArguments! }
            (token.kind == grammar.tokenIdIdentifier and not inTypeArguments! and not inCallArguments!) -> if { tokenIndex! => nameToken! }
            tokenIndex! + 1 => tokenIndex!
        }
    }
    -1 => opcode!
    nameToken! >= 0 -> if {
        IntrinsicNameRequest { source: request.source, token: request.tokens[request.tokenStart + nameToken!] } -> intrinsicOpcode => opcode!
    }
    opcode!
}

# The canonical recursive expression pass is authoritative. Older typed-IR
# consumers still read the shallow ExpressionType shape, so project it once in
# linear time instead of rerunning the legacy whole-AST inference pass.
legacyTypes recursive: expressionTypeIds.ExpressionTypeIdSet -> [expressionTypes.ExpressionType; ~] {
    [expressionTypes.ExpressionType; ~] => projected!
    0 => expressionIndex!
    expressionIndex! < (recursive.expressions -> len) -> while {
        recursive.expressions[expressionIndex!] => expression
        (expression.status == 0 and expression.typeId >= 0 and expression.typeId < (recursive.types -> len)) -> if {
            recursive.types[expression.typeId] => current
            current.origin => origin!
            current.module => targetModule!
            current.symbol => targetSymbol!
            -1 => keyOrigin!
            -1 => keyModule!
            -1 => valueOrigin!
            -1 => valueModule!
            current.kind != 1 -> if {
                10 + current.kind => origin!
                current.first >= 0 -> if {
                    recursive.types[current.first] => first
                    first.module => targetModule!
                    first.symbol => targetSymbol!
                }
                current.kind == 5 -> if {
                    current.first >= 0 -> if {
                        recursive.types[current.first] => key
                        key.symbol => targetModule!
                        key.origin => keyOrigin!
                        key.module => keyModule!
                    }
                    current.second >= 0 -> if {
                        recursive.types[current.second] => value
                        value.symbol => targetSymbol!
                        value.origin => valueOrigin!
                        value.module => valueModule!
                    }
                }
            }
            projected! -> push(expressionTypes.ExpressionType {
                sourceModule: expression.sourceModule
                astNode: expression.astNode
                origin: origin!
                targetModule: targetModule!
                targetSymbol: targetSymbol!
                keyOrigin: keyOrigin!
                keyModule: keyModule!
                valueOrigin: valueOrigin!
                valueModule: valueModule!
            })
        }
        expressionIndex! + 1 => expressionIndex!
    }
    projected!
}

public lowerContext prepared: semanticContext.SemanticSnapshot -> [TypedIrNode; ~] {
    lowerFunction request: FunctionLowerRequest -> SourceTypedIr {
        [TypedIrNode; ~] => results!
        request.sourceIndex => sourceIndex!
        prepared.package.sources[sourceIndex!] -> len => sourceLength
        prepared.package.sources[sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        prepared.package.ranges[sourceIndex!] => sourceRange
        [resolution.ResolvedName; ~] => resolvedNames!
        0 => sourceNameOffset!
        sourceNameOffset! < sourceRange.nameCount -> while {
            resolvedNames! -> push(prepared.package.names[sourceRange.nameStart + sourceNameOffset!])
            sourceNameOffset! + 1 => sourceNameOffset!
        }
        request.symbolIndex => symbolIndex!
        prepared.package.symbols[sourceRange.symbolStart + symbolIndex!] => function
        function.kind == 7 -> if {
            -1 => resultTypeIndex!
            -1 => resultRootAst!
            1000000 => resultDistance!
            UIntSize(0) => resultTopLevelStart!
            0 => typeSearch!
            typeSearch! < (inferred! -> len) -> while {
                inferred![typeSearch!] => candidateType
                candidateType.sourceModule == sourceIndex! -> if {
                    prepared.package.nodes[sourceRange.astStart + candidateType.astNode].parent => ancestor!
                    candidateType.astNode => functionChildAst!
                    1 => distance!
                    false => belongsToFunction!
                    false => blockedByNestedFunction!
                    (ancestor! >= 0 and not belongsToFunction! and not blockedByNestedFunction!) -> while {
                        ancestor! == function.astNode -> if { true => belongsToFunction! } else {
                            (prepared.package.nodes[sourceRange.astStart + ancestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + ancestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + ancestor!].kind == 31) -> if { true => blockedByNestedFunction! } else {
                                ancestor! => functionChildAst!
                                prepared.package.nodes[sourceRange.astStart + ancestor!].parent => ancestor!
                                distance! + 1 => distance!
                            }
                        }
                    }
                    belongsToFunction! -> if {
                        prepared.package.nodes[sourceRange.astStart + functionChildAst!].start => candidateTopLevelStart
                        (resultTypeIndex! < 0 or candidateTopLevelStart > resultTopLevelStart! or (candidateTopLevelStart == resultTopLevelStart! and distance! < resultDistance!)) -> if {
                            typeSearch! => resultTypeIndex!
                            functionChildAst! => resultRootAst!
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
                -1 => functionResultTypeId!
                0 => functionResultReferenceSearch!
                functionResultReferenceSearch! < (recursiveTypes.references -> len) -> while {
                    recursiveTypes.references[functionResultReferenceSearch!] => functionResultReference
                    (functionResultReference.status == 0 and functionResultReference.sourceModule == sourceIndex! and functionResultReference.typeAst == declaredResultAst!) -> if {
                        functionResultReference.typeId => functionResultTypeId!
                    }
                    functionResultReferenceSearch! + 1 => functionResultReferenceSearch!
                }
                -1 => functionResultTypeKind!
                0 => functionResultTypeFlags!
                functionResultTypeId! >= 0 -> if {
                    recursiveSemanticTypes![functionResultTypeId!] => canonicalFunctionResult
                    canonicalFunctionResult.origin => functionResultOrigin!
                    canonicalFunctionResult.module => functionResultModule!
                    canonicalFunctionResult.symbol => functionResultSymbol!
                    canonicalFunctionResult.kind => functionResultTypeKind!
                    recursiveTypeFlags![functionResultTypeId!] => functionResultTypeFlags!
                    canonicalFunctionResult.kind != 1 -> if {
                        10 + canonicalFunctionResult.kind => functionResultOrigin!
                        canonicalFunctionResult.first >= 0 -> if {
                            recursiveSemanticTypes![canonicalFunctionResult.first].module => functionResultModule!
                            recursiveSemanticTypes![canonicalFunctionResult.first].symbol => functionResultSymbol!
                        }
                    }
                }
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
                    prepared.package.symbols[sourceRange.symbolStart + parameterSearch!] => parameterCandidate
                    (parameterSymbol! < 0 and parameterCandidate.kind == 35 and parameterCandidate.parent == symbolIndex! and parameterCandidate.astNode == function.astNode) -> if { parameterSearch! => parameterSymbol! }
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
                -1 => parameterTypeId!
                parameterTypeIndex! >= 0 -> if {
                    inferred![parameterTypeIndex!] => inferredParameterType
                    inferredParameterType.origin => parameterOrigin!
                    inferredParameterType.targetModule => parameterModule!
                    inferredParameterType.targetSymbol => parameterTypeSymbol!
                }
                (parameterSymbol! >= 0 and parameterOrigin! < 0) -> if {
                    prepared.package.symbols[sourceRange.symbolStart + parameterSymbol!] => declaredParameter
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
                parameterSymbol! >= 0 -> if {
                    prepared.package.symbols[sourceRange.symbolStart + parameterSymbol!] => canonicalParameter
                    0 => parameterReferenceSearch!
                    parameterReferenceSearch! < (recursiveTypes.references -> len) -> while {
                        recursiveTypes.references[parameterReferenceSearch!] => parameterReference
                        (parameterReference.status == 0 and parameterReference.sourceModule == sourceIndex! and parameterReference.typeAst == canonicalParameter.typeNode) -> if {
                            parameterReference.typeId => parameterTypeId!
                        }
                        parameterReferenceSearch! + 1 => parameterReferenceSearch!
                    }
                    (parameterTypeId! < 0 and canonicalParameter.typeNode >= 0) -> if {
                        prepared.package.nodes[sourceRange.astStart + canonicalParameter.typeNode] => canonicalParameterTypeAst
                        -1 => canonicalParameterNameToken!
                        canonicalParameterTypeAst.firstToken => canonicalParameterTokenIndex!
                        canonicalParameterTokenIndex! < canonicalParameterTypeAst.firstToken + canonicalParameterTypeAst.tokenCount -> while {
                            prepared.package.tokens[sourceRange.tokenStart + canonicalParameterTokenIndex!] => canonicalParameterToken
                            canonicalParameterToken.kind == grammar.tokenIdIdentifier -> if { canonicalParameterTokenIndex! => canonicalParameterNameToken! }
                            canonicalParameterTokenIndex! + 1 => canonicalParameterTokenIndex!
                        }
                        canonicalParameterNameToken! >= 0 -> if {
                            prepared.package.tokens[sourceRange.tokenStart + canonicalParameterNameToken!] => canonicalParameterName
                            canonicalParameterName.span.length == UIntSize(10) => canonicalParameterIsSourceText!
                            "SourceText" => canonicalParameterSourceTextName
                            UIntSize(0) => canonicalParameterSourceTextByte!
                            (canonicalParameterIsSourceText! and canonicalParameterSourceTextByte! < canonicalParameterName.span.length) -> while {
                                (source -> byte(canonicalParameterName.span.start + canonicalParameterSourceTextByte!)) != (canonicalParameterSourceTextName -> byte(canonicalParameterSourceTextByte!)) -> if { false => canonicalParameterIsSourceText! }
                                canonicalParameterSourceTextByte! + UIntSize(1) => canonicalParameterSourceTextByte!
                            }
                            canonicalParameterIsSourceText! -> if {
                                0 => canonicalParameterSourceTextSearch!
                                canonicalParameterSourceTextSearch! < (recursiveSemanticTypes! -> len) -> while {
                                    recursiveSemanticTypes![canonicalParameterSourceTextSearch!] => canonicalParameterSourceText
                                    (canonicalParameterSourceText.kind == 1 and canonicalParameterSourceText.origin == 1 and canonicalParameterSourceText.symbol == 24) -> if {
                                        canonicalParameterSourceTextSearch! => parameterTypeId!
                                    }
                                    canonicalParameterSourceTextSearch! + 1 => canonicalParameterSourceTextSearch!
                                }
                            }
                        }
                    }
                    parameterTypeId! >= 0 -> if {
                        recursiveSemanticTypes![parameterTypeId!] => canonicalParameterType
                        canonicalParameterType.origin => parameterOrigin!
                        canonicalParameterType.module => parameterModule!
                        canonicalParameterType.symbol => parameterTypeSymbol!
                        canonicalParameterType.kind != 1 -> if {
                            10 + canonicalParameterType.kind => parameterOrigin!
                            canonicalParameterType.first >= 0 -> if {
                                recursiveSemanticTypes![canonicalParameterType.first] => canonicalParameterElement
                                canonicalParameterElement.module => parameterModule!
                                canonicalParameterElement.symbol => parameterTypeSymbol!
                            }
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
                    typeId: functionResultTypeId!
                    typeKind: functionResultTypeKind!
                    typeFlags: functionResultTypeFlags!
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
                    typeId: functionResultTypeId!
                    typeKind: functionResultTypeKind!
                    typeFlags: functionResultTypeFlags!
                    payloadToken: -1
                    opcode: -1
                    operand0: -1
                    operand1: -1
                    nextOperand: -1
                    flags: 0
                })
                parameterIr! >= 0 -> if {
                    prepared.package.symbols[sourceRange.symbolStart + parameterSymbol!] => parameter
                    results! -> push(TypedIrNode {
                        kind: 10
                        parent: functionIr!
                        sourceModule: sourceIndex!
                        astNode: parameter.astNode
                        symbol: parameterSymbol!
                        targetModule: sourceIndex!
                        typeOrigin: parameterOrigin!
                        typeModule: parameterModule!
                        typeSymbol: parameterTypeSymbol!
                        typeId: parameterTypeId!
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
                -1 => firstAdditionalParameterIr!
                -1 => previousAdditionalParameterIr!
                0 => additionalParameterSearch!
                additionalParameterSearch! < sourceRange.symbolCount -> while {
                    prepared.package.symbols[sourceRange.symbolStart + additionalParameterSearch!] => additionalParameter
                    (additionalParameter.kind == 35 and additionalParameter.parent == symbolIndex! and additionalParameter.astNode != function.astNode) -> if {
                        -1 => additionalParameterTypeId!
                        0 => additionalParameterReferenceSearch!
                        additionalParameterReferenceSearch! < (recursiveTypes.references -> len) -> while {
                            recursiveTypes.references[additionalParameterReferenceSearch!] => additionalParameterReference
                            (additionalParameterReference.status == 0 and additionalParameterReference.sourceModule == sourceIndex! and additionalParameterReference.typeAst == additionalParameter.typeNode) -> if {
                                additionalParameterReference.typeId => additionalParameterTypeId!
                            }
                            additionalParameterReferenceSearch! + 1 => additionalParameterReferenceSearch!
                        }
                        -1 => additionalParameterOrigin!
                        -1 => additionalParameterModule!
                        -1 => additionalParameterTypeSymbol!
                        -1 => additionalParameterTypeKind!
                        0 => additionalParameterTypeFlags!
                        additionalParameterTypeId! >= 0 -> if {
                            recursiveSemanticTypes![additionalParameterTypeId!] => additionalParameterType
                            additionalParameterType.origin => additionalParameterOrigin!
                            additionalParameterType.module => additionalParameterModule!
                            additionalParameterType.symbol => additionalParameterTypeSymbol!
                            additionalParameterType.kind => additionalParameterTypeKind!
                            recursiveTypeFlags![additionalParameterTypeId!] => additionalParameterTypeFlags!
                            additionalParameterType.kind != 1 -> if {
                                10 + additionalParameterType.kind => additionalParameterOrigin!
                                additionalParameterType.first >= 0 -> if {
                                    recursiveSemanticTypes![additionalParameterType.first] => additionalParameterElement
                                    additionalParameterElement.module => additionalParameterModule!
                                    additionalParameterElement.symbol => additionalParameterTypeSymbol!
                                }
                            }
                        }
                        additionalParameterOrigin! < 0 -> if {
                            0 => additionalParameterNominalSearch!
                            additionalParameterNominalSearch! < (prepared.nominal -> len) -> while {
                                prepared.nominal[additionalParameterNominalSearch!] => additionalParameterNominal
                                (additionalParameterNominal.sourceModule == sourceIndex! and additionalParameterNominal.typeAst == additionalParameter.typeNode) -> if {
                                    additionalParameterNominal.origin => additionalParameterOrigin!
                                    additionalParameterNominal.targetModule => additionalParameterModule!
                                    additionalParameterNominal.targetSymbol => additionalParameterTypeSymbol!
                                }
                                additionalParameterNominalSearch! + 1 => additionalParameterNominalSearch!
                            }
                        }
                        additionalParameterOrigin! < 0 -> if {
                            0 => additionalParameterCompositeSearch!
                            additionalParameterCompositeSearch! < (prepared.composite -> len) -> while {
                                prepared.composite[additionalParameterCompositeSearch!] => additionalParameterComposite
                                (additionalParameterComposite.sourceModule == sourceIndex! and additionalParameterComposite.typeAst == additionalParameter.typeNode) -> if {
                                    10 + additionalParameterComposite.kind => additionalParameterOrigin!
                                    additionalParameterComposite.elementModule => additionalParameterModule!
                                    additionalParameterComposite.elementSymbol => additionalParameterTypeSymbol!
                                }
                                additionalParameterCompositeSearch! + 1 => additionalParameterCompositeSearch!
                            }
                        }
                        results! -> len => additionalParameterIr
                        firstAdditionalParameterIr! < 0 -> if { additionalParameterIr => firstAdditionalParameterIr! }
                        previousAdditionalParameterIr! >= 0 -> if {
                            results![previousAdditionalParameterIr!] => previousAdditionalParameter!
                            additionalParameterIr => previousAdditionalParameter!.nextOperand
                            previousAdditionalParameter! => results![previousAdditionalParameterIr!]
                        }
                        results! -> push(TypedIrNode {
                            kind: 10
                            parent: functionIr!
                            sourceModule: sourceIndex!
                            astNode: additionalParameter.astNode
                            symbol: additionalParameterSearch!
                            targetModule: sourceIndex!
                            typeOrigin: additionalParameterOrigin!
                            typeModule: additionalParameterModule!
                            typeSymbol: additionalParameterTypeSymbol!
                            typeId: additionalParameterTypeId!
                            typeKind: additionalParameterTypeKind!
                            typeFlags: additionalParameterTypeFlags!
                            payloadToken: additionalParameter.nameToken
                            opcode: -1
                            operand0: -1
                            operand1: -1
                            nextOperand: -1
                            flags: additionalParameter.flags
                        })
                        additionalParameterIr => previousAdditionalParameterIr!
                    }
                    additionalParameterSearch! + 1 => additionalParameterSearch!
                }
                firstAdditionalParameterIr! >= 0 -> if {
                    results![functionIr!] => parameterizedFunction!
                    firstAdditionalParameterIr! => parameterizedFunction!.nextOperand
                    parameterizedFunction! => results![functionIr!]
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
                    prepared.package.nodes[sourceRange.astStart + bindingAstIndex!] => bindingAst
                    (bindingAst.kind == 9 or (bindingAst.kind == 48 and bindingAst.secondaryToken >= 0)) -> if {
                        bindingAst.parent => bindingAncestor!
                        false => bindingBelongsToFunction!
                        false => bindingBlockedByNestedFunction!
                        (bindingAncestor! >= 0 and not bindingBelongsToFunction! and not bindingBlockedByNestedFunction!) -> while {
                            bindingAncestor! == function.astNode -> if { true => bindingBelongsToFunction! } else {
                                (prepared.package.nodes[sourceRange.astStart + bindingAncestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + bindingAncestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + bindingAncestor!].kind == 31) -> if { true => bindingBlockedByNestedFunction! } else { prepared.package.nodes[sourceRange.astStart + bindingAncestor!].parent => bindingAncestor! }
                            }
                        }
                        bindingBelongsToFunction! -> if {
                            -1 => bindingTypeIndex!
                            1000000 => bindingTypeDistance!
                            0 => bindingTypeSearch!
                            bindingTypeSearch! < (inferred! -> len) -> while {
                                inferred![bindingTypeSearch!] => bindingTypeCandidate
                                bindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                    prepared.package.nodes[sourceRange.astStart + bindingTypeCandidate.astNode].parent => bindingTypeAncestor!
                                    1 => bindingDistance!
                                    bindingTypeCandidate.astNode == bindingAstIndex! => belongsToBinding!
                                    belongsToBinding! -> if { 0 => bindingDistance! }
                                    (bindingTypeAncestor! >= 0 and not belongsToBinding!) -> while {
                                        bindingTypeAncestor! == bindingAstIndex! -> if { true => belongsToBinding! } else {
                                            prepared.package.nodes[sourceRange.astStart + bindingTypeAncestor!].parent => bindingTypeAncestor!
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
                            -1 => bindingCanonicalTypeId!
                            1000000 => bindingCanonicalDistance!
                            0 => bindingCanonicalSearch!
                            bindingCanonicalSearch! < (recursiveExpressions! -> len) -> while {
                                recursiveExpressions![bindingCanonicalSearch!] => bindingCanonicalCandidate
                                (bindingCanonicalCandidate.status == 0 and bindingCanonicalCandidate.sourceModule == sourceIndex!) -> if {
                                    0 => bindingCanonicalCandidateDistance!
                                    bindingCanonicalCandidate.astNode == bindingAstIndex! => bindingCanonicalBelongs!
                                    bindingCanonicalBelongs! -> if { 0 => bindingCanonicalCandidateDistance! } else {
                                        prepared.package.nodes[sourceRange.astStart + bindingCanonicalCandidate.astNode].parent => bindingCanonicalAncestor!
                                        1 => bindingCanonicalCandidateDistance!
                                        (bindingCanonicalAncestor! >= 0 and not bindingCanonicalBelongs!) -> while {
                                            bindingCanonicalAncestor! == bindingAstIndex! -> if { true => bindingCanonicalBelongs! } else {
                                                prepared.package.nodes[sourceRange.astStart + bindingCanonicalAncestor!].parent => bindingCanonicalAncestor!
                                                bindingCanonicalCandidateDistance! + 1 => bindingCanonicalCandidateDistance!
                                            }
                                        }
                                    }
                                    (bindingCanonicalBelongs! and bindingCanonicalCandidateDistance! < bindingCanonicalDistance!) -> if {
                                        bindingCanonicalCandidate.typeId => bindingCanonicalTypeId!
                                        bindingCanonicalCandidateDistance! => bindingCanonicalDistance!
                                    }
                                }
                                bindingCanonicalSearch! + 1 => bindingCanonicalSearch!
                            }
                            -1 => bindingSymbol!
                            0 => bindingSymbolSearch!
                            bindingSymbolSearch! < sourceRange.symbolCount -> while {
                                (prepared.package.symbols[sourceRange.symbolStart + bindingSymbolSearch!].kind == 9 and prepared.package.symbols[sourceRange.symbolStart + bindingSymbolSearch!].astNode == bindingAstIndex!) -> if { bindingSymbolSearch! => bindingSymbol! }
                                bindingSymbolSearch! + 1 => bindingSymbolSearch!
                            }
                            bindingSymbol! >= 0 -> if {
                                prepared.package.symbols[sourceRange.symbolStart + bindingSymbol!] => declaredBindingSymbol
                                0 => bindingDeclaredReferenceSearch!
                                bindingDeclaredReferenceSearch! < (prepared.semantic.references -> len) -> while {
                                    prepared.semantic.references[bindingDeclaredReferenceSearch!] => bindingDeclaredReference
                                    (bindingDeclaredReference.sourceModule == sourceIndex! and bindingDeclaredReference.typeAst == declaredBindingSymbol.typeNode and bindingDeclaredReference.status == 0) -> if {
                                        bindingDeclaredReference.typeId => bindingCanonicalTypeId!
                                    }
                                    bindingDeclaredReferenceSearch! + 1 => bindingDeclaredReferenceSearch!
                                }
                            }
                            (bindingTypeIndex! >= 0 or bindingCanonicalTypeId! >= 0) -> if {
                                -1 => bindingOrigin!
                                -1 => bindingModule!
                                -1 => bindingSymbolType!
                                -1 => bindingTypeKind!
                                0 => bindingTypeFlags!
                                bindingTypeIndex! >= 0 -> if {
                                    inferred![bindingTypeIndex!] => bindingType
                                    bindingType.origin => bindingOrigin!
                                    bindingType.targetModule => bindingModule!
                                    bindingType.targetSymbol => bindingSymbolType!
                                }
                                bindingCanonicalTypeId! >= 0 -> if {
                                    recursiveSemanticTypes![bindingCanonicalTypeId!] => bindingCanonicalType
                                    bindingCanonicalType.origin => bindingOrigin!
                                    bindingCanonicalType.module => bindingModule!
                                    bindingCanonicalType.symbol => bindingSymbolType!
                                    bindingCanonicalType.kind => bindingTypeKind!
                                    recursiveTypeFlags![bindingCanonicalTypeId!] => bindingTypeFlags!
                                    (bindingCanonicalType.kind >= 2 and bindingCanonicalType.kind <= 6) -> if { 10 + bindingCanonicalType.kind => bindingOrigin! }
                                    (bindingCanonicalType.kind == 2 or bindingCanonicalType.kind == 3 or bindingCanonicalType.kind == 4 or bindingCanonicalType.kind == 6) -> if {
                                        bindingCanonicalType.first >= 0 -> if {
                                            recursiveSemanticTypes![bindingCanonicalType.first].module => bindingModule!
                                            recursiveSemanticTypes![bindingCanonicalType.first].symbol => bindingSymbolType!
                                        }
                                    }
                                    bindingCanonicalType.kind == 5 -> if {
                                        bindingCanonicalType.first >= 0 -> if { recursiveSemanticTypes![bindingCanonicalType.first].symbol => bindingModule! }
                                        bindingCanonicalType.second >= 0 -> if { recursiveSemanticTypes![bindingCanonicalType.second].symbol => bindingSymbolType! }
                                    }
                                }
                                (bindingOrigin! == 13 and bindingSymbol! >= 0) -> if {
                                    prepared.package.symbols[sourceRange.symbolStart + bindingSymbol!] => declaredBinding
                                    0 => declaredBindingTypeSearch!
                                    declaredBindingTypeSearch! < (prepared.nominal -> len) -> while {
                                        prepared.nominal[declaredBindingTypeSearch!] => declaredBindingType
                                        (declaredBindingType.sourceModule == sourceIndex! and declaredBindingType.typeAst == declaredBinding.typeNode and declaredBindingType.status == 0) -> if {
                                            declaredBindingType.targetModule => bindingModule!
                                            declaredBindingType.targetSymbol => bindingSymbolType!
                                        }
                                        declaredBindingTypeSearch! + 1 => declaredBindingTypeSearch!
                                    }
                                }
                                results! -> len => bindingIr
                                bindingIr => astToIr![bindingAstIndex!]
                                bindingAst.payloadToken => bindingPayloadToken!
                                bindingAst.kind == 48 -> if { bindingAst.secondaryToken => bindingPayloadToken! }
                                results! -> push(TypedIrNode {
                                    kind: 17
                                    parent: returnIr
                                    sourceModule: sourceIndex!
                                    astNode: bindingAstIndex!
                                    symbol: bindingSymbol!
                                    targetModule: sourceIndex!
                                    typeOrigin: bindingOrigin!
                                    typeModule: bindingModule!
                                    typeSymbol: bindingSymbolType!
                                    typeId: bindingCanonicalTypeId!
                                    typeKind: bindingTypeKind!
                                    typeFlags: bindingTypeFlags!
                                    payloadToken: bindingPayloadToken!
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
                    prepared.package.nodes[sourceRange.astStart + expressionAstIndex!] => expression
                    expression.parent => expressionAncestor!
                    false => expressionBelongsToFunction!
                    false => expressionBlockedByNestedFunction!
                    (expressionAncestor! >= 0 and not expressionBelongsToFunction! and not expressionBlockedByNestedFunction!) -> while {
                        expressionAncestor! == function.astNode -> if { true => expressionBelongsToFunction! } else {
                            (prepared.package.nodes[sourceRange.astStart + expressionAncestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + expressionAncestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + expressionAncestor!].kind == 31) -> if { true => expressionBlockedByNestedFunction! } else { prepared.package.nodes[sourceRange.astStart + expressionAncestor!].parent => expressionAncestor! }
                        }
                    }
                    inferredIndexByAst![sourceRange.astStart + expressionAstIndex!] => expressionTypeIndex!
                    recursiveTypeByAst![sourceRange.astStart + expressionAstIndex!] => recursiveExpressionTypeId!
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
                    false => knownPatternReference!
                    -1 => patternTypeAst!
                    expression.kind == 59 -> if { expressionAstIndex! => patternTypeAst! }
                    expression.kind == 15 -> if {
                        0 => patternNameResolutionSearch!
                        patternNameResolutionSearch! < (resolvedNames! -> len) -> while {
                            resolvedNames![patternNameResolutionSearch!] => patternResolvedName
                            patternResolvedName.astNode == expressionAstIndex! -> if {
                                prepared.package.symbols[sourceRange.symbolStart + patternResolvedName.symbol] => patternResolvedSymbol
                                prepared.package.nodes[sourceRange.astStart + patternResolvedSymbol.astNode].kind == 59 -> if { patternResolvedSymbol.astNode => patternTypeAst! }
                            }
                            patternNameResolutionSearch! + 1 => patternNameResolutionSearch!
                        }
                    }
                    patternTypeAst! >= 0 -> if {
                        prepared.package.nodes[sourceRange.astStart + patternTypeAst!] => patternTypeNode
                        prepared.package.nodes[sourceRange.astStart + patternTypeNode.parent].parent => patternMatchAst
                        prepared.package.nodes[sourceRange.astStart + patternMatchAst].parent => patternFlowAst
                        -1 => patternSubjectTypeId!
                        UIntSize(0) => patternSubjectStart!
                        0 => patternSubjectSearch!
                        patternSubjectSearch! < sourceRange.astCount -> while {
                            prepared.package.nodes[sourceRange.astStart + patternSubjectSearch!] => patternSubjectAst
                            recursiveTypeByAst![sourceRange.astStart + patternSubjectSearch!] => patternSubjectCandidateType
                            (patternSubjectAst.parent == patternFlowAst and patternSubjectAst.start < prepared.package.nodes[sourceRange.astStart + patternMatchAst].start and patternSubjectCandidateType >= 0 and recursiveSemanticTypes![patternSubjectCandidateType].kind == 7) -> if {
                                (patternSubjectTypeId! < 0 or patternSubjectAst.start > patternSubjectStart!) -> if {
                                    patternSubjectCandidateType => patternSubjectTypeId!
                                    patternSubjectAst.start => patternSubjectStart!
                                }
                            }
                            patternSubjectSearch! + 1 => patternSubjectSearch!
                        }
                        patternSubjectTypeId! >= 0 -> if {
                            recursiveSemanticTypes![patternSubjectTypeId!] => patternSubjectType
                            prepared.package.tokens[sourceRange.tokenStart + patternTypeNode.payloadToken] => patternVariantToken
                            patternSubjectType.first => recursiveExpressionTypeId!
                            (source -> byte(patternVariantToken.span.start)) == UInt8(69) -> if { patternSubjectType.second => recursiveExpressionTypeId! }
                            recursiveExpressionTypeId! >= 0 -> if {
                                recursiveSemanticTypes![recursiveExpressionTypeId!] => patternPayloadType
                                patternPayloadType.origin => expressionTypeOrigin!
                                patternPayloadType.module => expressionTypeModule!
                                patternPayloadType.symbol => expressionTypeSymbol!
                                true => knownPatternReference!
                            }
                        }
                    }
                    false => knownParameterReference!
                    (expression.kind == 15 and parameterSymbol! >= 0 and parameterTypeId! >= 0) -> if {
                        prepared.package.symbols[sourceRange.symbolStart + parameterSymbol!] => functionParameterSymbol
                        prepared.package.tokens[sourceRange.tokenStart + functionParameterSymbol.nameToken] => functionParameterName
                        prepared.package.tokens[sourceRange.tokenStart + expression.payloadToken] => expressionParameterName
                        functionParameterName.span.length == expressionParameterName.span.length => sameParameterName!
                        UIntSize(0) => parameterNameByte!
                        (sameParameterName! and parameterNameByte! < functionParameterName.span.length) -> while {
                            (source -> byte(functionParameterName.span.start + parameterNameByte!)) != (source -> byte(expressionParameterName.span.start + parameterNameByte!)) -> if { false => sameParameterName! }
                            parameterNameByte! + UIntSize(1) => parameterNameByte!
                        }
                        sameParameterName! -> if {
                            true => knownParameterReference!
                            parameterTypeId! => recursiveExpressionTypeId!
                            recursiveSemanticTypes![parameterTypeId!] => knownParameterType
                            knownParameterType.origin => expressionTypeOrigin!
                            knownParameterType.module => expressionTypeModule!
                            knownParameterType.symbol => expressionTypeSymbol!
                        }
                    }
                    false => knownCallExpression!
                    0 => knownCallSearch!
                    knownCallSearch! < (prepared.calls -> len) -> while {
                        prepared.calls[knownCallSearch!] => knownCall
                        (knownCall.sourceModule == sourceIndex! and knownCall.callAst == expressionAstIndex! and knownCall.status == 0) -> if {
                            true => knownCallExpression!
                            (knownCall.targetSourceModule >= 0 and knownCall.functionSymbol >= 0) -> if {
                                prepared.package.ranges[knownCall.targetSourceModule] => knownCallTargetRange
                                prepared.package.symbols[knownCallTargetRange.symbolStart + knownCall.functionSymbol] => knownCallFunction
                                knownCallFunction.typeNode => knownCallResultAst!
                                knownCallFunction.secondaryTypeNode >= 0 -> if { knownCallFunction.secondaryTypeNode => knownCallResultAst! }
                                0 => knownCallTypeSearch!
                                knownCallTypeSearch! < (prepared.nominal -> len) -> while {
                                    prepared.nominal[knownCallTypeSearch!] => knownCallType
                                    (knownCallType.sourceModule == knownCall.targetSourceModule and knownCallType.typeAst == knownCallResultAst! and knownCallType.status == 0) -> if {
                                        knownCallType.origin => expressionTypeOrigin!
                                        knownCallType.targetModule => expressionTypeModule!
                                        knownCallType.targetSymbol => expressionTypeSymbol!
                                    }
                                    knownCallTypeSearch! + 1 => knownCallTypeSearch!
                                }
                            } else {
                                knownCall.functionSymbol == -115 -> if {
                                    1 => expressionTypeOrigin!
                                    -1 => expressionTypeModule!
                                    2 => expressionTypeSymbol!
                                }
                            }
                        }
                        knownCallSearch! + 1 => knownCallSearch!
                    }
                    expression.kind == 48 -> if {
                        true => knownCallExpression!
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        0 => expressionTypeSymbol!
                    }
                    false => knownNumericExpression!
                    (expression.kind == 20 or expression.kind == 21) -> if {
                        1000000 => knownNumericDistance!
                        0 => knownNumericSearch!
                        knownNumericSearch! < (inferred! -> len) -> while {
                            inferred![knownNumericSearch!] => knownNumericCandidate
                            knownNumericCandidate.sourceModule == sourceIndex! -> if {
                                prepared.package.nodes[sourceRange.astStart + knownNumericCandidate.astNode].parent => knownNumericAncestor!
                                1 => knownNumericCandidateDistance!
                                false => knownNumericBelongs!
                                (knownNumericAncestor! >= 0 and not knownNumericBelongs!) -> while {
                                    knownNumericAncestor! == expressionAstIndex! -> if { true => knownNumericBelongs! } else {
                                        prepared.package.nodes[sourceRange.astStart + knownNumericAncestor!].parent => knownNumericAncestor!
                                        knownNumericCandidateDistance! + 1 => knownNumericCandidateDistance!
                                    }
                                }
                                (knownNumericBelongs! and knownNumericCandidateDistance! < knownNumericDistance!) -> if {
                                    true => knownNumericExpression!
                                    knownNumericCandidateDistance! => knownNumericDistance!
                                    knownNumericCandidate.origin => expressionTypeOrigin!
                                    knownNumericCandidate.targetModule => expressionTypeModule!
                                    knownNumericCandidate.targetSymbol => expressionTypeSymbol!
                                }
                            }
                            knownNumericSearch! + 1 => knownNumericSearch!
                        }
                    }
                    expression.kind == 22 -> if {
                        0 => unaryChildSearch!
                        (unaryChildSearch! < sourceRange.astCount and not knownNumericExpression!) -> while {
                            prepared.package.nodes[sourceRange.astStart + unaryChildSearch!] => unaryChild
                            unaryChild.parent == expressionAstIndex! -> if {
                                recursiveTypeByAst![sourceRange.astStart + unaryChildSearch!] => unaryChildTypeId
                                unaryChildTypeId >= 0 -> if {
                                    unaryChildTypeId => recursiveExpressionTypeId!
                                    recursiveSemanticTypes![unaryChildTypeId] => unaryChildType
                                    unaryChildType.origin => expressionTypeOrigin!
                                    unaryChildType.module => expressionTypeModule!
                                    unaryChildType.symbol => expressionTypeSymbol!
                                    true => knownNumericExpression!
                                }
                            }
                            unaryChildSearch! + 1 => unaryChildSearch!
                        }
                    }
                    (expression.kind == 18 or expression.kind == 19 or expression.kind == 24 or expression.kind == 25 or (expression.kind == 22 and expression.operatorKind == -26)) => knownBooleanExpression!
                    knownBooleanExpression! -> if {
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        23 => expressionTypeSymbol!
                    }
                    false => knownPushExpression!
                    false => knownMapTextExpression!
                    false => knownBorrowTextExpression!
                    expression.kind == 53 => knownIndexAssignment!
                    expression.kind == 54 => knownMemberAssignment!
                    expression.kind == 10 -> if {
                        FlowIntrinsicRequest { source: source, tokens: prepared.package.tokens, tokenStart: sourceRange.tokenStart, node: expression } -> flowIntrinsicOpcode => flowOpcode
                        flowOpcode == -204 -> if { true => knownPushExpression! }
                        flowOpcode == -205 -> if { true => knownMapTextExpression! }
                        flowOpcode == -206 -> if { true => knownBorrowTextExpression! }
                    }
                    knownPushExpression! -> if {
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        0 => expressionTypeSymbol!
                    }
                    knownMapTextExpression! -> if {
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        24 => expressionTypeSymbol!
                    }
                    knownBorrowTextExpression! -> if {
                        1 => expressionTypeOrigin!
                        -1 => expressionTypeModule!
                        24 => expressionTypeSymbol!
                    }
                    expressionBelongsToFunction! -> if {
                        (expression.kind == 42 or expression.kind == 43 or expression.kind == 44 or expression.kind == 45 or expression.kind == 46 or expression.kind == 47 or expression.kind == 58) -> if {
                            results! -> len => controlIr
                            controlIr => astToIr![expressionAstIndex!]
                            18 => controlKind!
                            expression.kind == 43 -> if { 19 => controlKind! }
                            expression.kind == 44 -> if { 20 => controlKind! }
                            expression.kind == 45 -> if { 21 => controlKind! }
                            expression.kind == 46 -> if { 22 => controlKind! }
                            expression.kind == 47 -> if { 23 => controlKind! }
                            expression.kind == 58 -> if { 19 => controlKind! }
                            -1 => controlOpcode!
                            expression.kind == 45 -> if {
                                0 => controlOpcode!
                                (source -> byte(expression.start)) == UInt8(99) -> if { 1 => controlOpcode! }
                            }
                            expression.kind == 46 -> if { expression.operatorKind => controlOpcode! }
                            expression.kind == 58 -> if {
                                0 => patternSearch!
                                patternSearch! < sourceRange.astCount -> while {
                                    prepared.package.nodes[sourceRange.astStart + patternSearch!] => patternCandidate
                                    (patternCandidate.kind == 59 and patternCandidate.parent == expressionAstIndex!) -> if {
                                        prepared.package.tokens[sourceRange.tokenStart + patternCandidate.payloadToken] => patternVariant
                                        (source -> byte(patternVariant.span.start)) == UInt8(69) -> if { 1 => controlOpcode! } else { 0 => controlOpcode! }
                                    }
                                    patternSearch! + 1 => patternSearch!
                                }
                            }
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
                            (expressionTypeIndex! >= 0 or recursiveExpressionTypeId! >= 0 or knownParameterReference! or knownPatternReference! or expression.kind == 41 or expression.kind == 56 or (expression.kind == 15 and expression.parent >= 0 and prepared.package.nodes[sourceRange.astStart + expression.parent].kind == 41) or knownBooleanExpression! or knownCallExpression! or knownNumericExpression! or knownPushExpression! or knownMapTextExpression! or knownBorrowTextExpression! or knownIndexAssignment! or knownMemberAssignment!) -> if {
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
                            expression.kind == 53 -> if { 24 => expressionKind! }
                            expression.kind == 54 -> if { 25 => expressionKind! }
                            expression.kind == 55 -> if { 26 => expressionKind! }
                            expression.kind == 56 -> if { 27 => expressionKind! }
                            expression.kind == 59 -> if { 29 => expressionKind! }
                            0 => propertyCallSearch!
                            propertyCallSearch! < (prepared.calls -> len) -> while {
                                prepared.calls[propertyCallSearch!] => propertyCall
                                (propertyCall.sourceModule == sourceIndex! and propertyCall.callAst == expressionAstIndex! and propertyCall.status == 0) -> if { 6 => expressionKind! }
                                propertyCallSearch! + 1 => propertyCallSearch!
                            }
                            false => transparentExpression!
                            (expression.kind >= 18 and expression.kind <= 21) -> if {
                                0 => transparentChildSearch!
                                transparentChildSearch! < sourceRange.astCount -> while {
                                    prepared.package.nodes[sourceRange.astStart + transparentChildSearch!] => transparentChild
                                    (transparentChild.parent == expressionAstIndex! and transparentChild.start == expression.start and transparentChild.length == expression.length) -> if { true => transparentExpression! }
                                    transparentChildSearch! + 1 => transparentChildSearch!
                                }
                            }
                            not transparentExpression! -> if {
                            results! -> len => expressionIr
                            expressionIr => astToIr![expressionAstIndex!]
                            -1 => expressionSymbol!
                            knownParameterReference! -> if { parameterSymbol! => expressionSymbol! }
                            -1 => expressionTargetModule!
                            expression.flags => expressionFlags!
                            expression.operatorKind => expressionOpcode!
                            expressionKind! == 13 -> if {
                                0 => valuePathSearch!
                                valuePathSearch! < (prepared.qualified -> len) -> while {
                                    prepared.qualified[valuePathSearch!] => valuePath
                                    (valuePath.sourceModule == sourceIndex! and valuePath.pathAst == expressionAstIndex! and valuePath.status == 0) -> if {
                                        prepared.modules[valuePath.targetModule].sourceIndex => valueTargetSource
                                        prepared.package.ranges[valueTargetSource] => valueTargetRange
                                        prepared.package.symbols[valueTargetRange.symbolStart + valuePath.targetSymbol] => valueTarget
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
                                    prepared.package.nodes[sourceRange.astStart + intrinsicChildSearch!] => intrinsicChild
                                    (intrinsicChild.parent == expressionAstIndex! and intrinsicChild.kind == 16) -> if {
                                        IntrinsicNameRequest { source: source, token: prepared.package.tokens[sourceRange.tokenStart + intrinsicChild.payloadToken] } -> intrinsicOpcode => expressionIntrinsicOpcode
                                        expressionIntrinsicOpcode <= -201 -> if { expressionIntrinsicOpcode => expressionOpcode! }
                                    }
                                    intrinsicChildSearch! + 1 => intrinsicChildSearch!
                                }
                                knownPushExpression! -> if { -204 => expressionOpcode! }
                                knownMapTextExpression! -> if { -205 => expressionOpcode! }
                                knownBorrowTextExpression! -> if { -206 => expressionOpcode! }
                            }
                            expression.kind == 48 -> if {
                                expression.firstToken => roleTokenIndex!
                                roleTokenIndex! < expression.firstToken + expression.tokenCount -> while {
                                    prepared.package.tokens[sourceRange.tokenStart + roleTokenIndex!] => roleToken
                                    roleToken.kind == grammar.tokenIdIdentifier -> if {
                                        IntrinsicNameRequest { source: source, token: roleToken } -> intrinsicOpcode => roleOpcode
                                        (roleOpcode == -207 or roleOpcode == -208 or roleOpcode == -209) -> if { roleOpcode => expressionOpcode! }
                                    }
                                    roleTokenIndex! + 1 => roleTokenIndex!
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
                            expressionKind! == 29 -> if {
                                0 => patternBindingSymbolSearch!
                                patternBindingSymbolSearch! < sourceRange.symbolCount -> while {
                                    prepared.package.symbols[sourceRange.symbolStart + patternBindingSymbolSearch!] => patternBindingSymbol
                                    (patternBindingSymbol.kind == 35 and patternBindingSymbol.astNode == expressionAstIndex!) -> if { patternBindingSymbolSearch! => expressionSymbol! }
                                    patternBindingSymbolSearch! + 1 => patternBindingSymbolSearch!
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
                                            prepared.package.ranges[resolvedCall.targetSourceModule] => expressionTargetRange
                                            prepared.package.symbols[expressionTargetRange.symbolStart + resolvedCall.functionSymbol] => expressionTargetFunction
                                            ((expressionTargetFunction.flags / 8) % 2 == 1 and (expressionFlags! / 8) % 2 == 0) -> if {
                                                expressionFlags! + 8 => expressionFlags!
                                            }
                                        } else {
                                            resolvedCall.functionSymbol == -115 -> if {
                                                1 => expressionTypeOrigin!
                                                -1 => expressionTypeModule!
                                                2 => expressionTypeSymbol!
                                            }
                                        }
                                    }
                                    callSearch! + 1 => callSearch!
                                }
                            }
                            -1 => expressionTypeKind!
                            0 => expressionTypeFlags!
                            recursiveExpressionTypeId! >= 0 -> if {
                                recursiveSemanticTypes![recursiveExpressionTypeId!].kind => expressionTypeKind!
                                recursiveTypeFlags![recursiveExpressionTypeId!] => expressionTypeFlags!
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
                                typeKind: expressionTypeKind!
                                typeFlags: expressionTypeFlags!
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
                    }
                    expressionAstIndex! + 1 => expressionAstIndex!
                }

                results! -> len => expressionIrEnd
                expressionIrStart => parentIrIndex!
                parentIrIndex! < expressionIrEnd -> while {
                    results![parentIrIndex!] => expressionIrNode!
                    prepared.package.nodes[sourceRange.astStart + expressionIrNode!.astNode].parent => parentAst!
                    -1 => semanticParentIr!
                    (parentAst! >= 0 and parentAst! != function.astNode and semanticParentIr! < 0) -> while {
                        astToIr![parentAst!] >= 0 -> if { astToIr![parentAst!] => semanticParentIr! } else {
                            prepared.package.nodes[sourceRange.astStart + parentAst!].parent => parentAst!
                        }
                    }
                    semanticParentIr! >= 0 -> if { semanticParentIr! => expressionIrNode!.parent }
                    expressionIrNode! => results![parentIrIndex!]
                    parentIrIndex! + 1 => parentIrIndex!
                }

                expressionIrStart => operandIrIndex!
                operandIrIndex! < expressionIrEnd -> while {
                    results![operandIrIndex!] => operatorIr!
                    (operatorIr!.kind == 6 or operatorIr!.kind == 7 or operatorIr!.kind == 8 or (operatorIr!.kind == 9 and operatorIr!.opcode <= -201 and (operatorIr!.opcode >= -206 or operatorIr!.opcode == -213)) or operatorIr!.kind == 13 or operatorIr!.kind == 15 or operatorIr!.kind == 17 or operatorIr!.kind == 22 or operatorIr!.kind == 23 or operatorIr!.kind == 24 or operatorIr!.kind == 25 or operatorIr!.kind == 26) -> if {
                        -1 => firstOperand!
                        -1 => secondOperand!
                        UIntSize(0) => firstStart!
                        0 => childIrIndex!
                        childIrIndex! < expressionIrEnd -> while {
                            results![childIrIndex!] => childIr
                            childIr.parent == operandIrIndex! -> if {
                                prepared.package.nodes[sourceRange.astStart + childIr.astNode].start => childStart
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
                        (operatorIr!.kind == 6 or operatorIr!.kind == 8 or (operatorIr!.kind == 9 and operatorIr!.opcode <= -201 and (operatorIr!.opcode >= -206 or operatorIr!.opcode == -213)) or operatorIr!.kind == 15 or operatorIr!.kind == 24) -> if { secondOperand! => operatorIr!.operand1 }
                        operatorIr!.kind == 22 -> if {
                            -1 => operatorIr!.operand0
                            firstOperand! => operatorIr!.operand1
                        }
                        operatorIr! => results![operandIrIndex!]
                    }
                    operandIrIndex! + 1 => operandIrIndex!
                }

                expressionIrStart => canonicalIndexResultIr!
                canonicalIndexResultIr! < expressionIrEnd -> while {
                    results![canonicalIndexResultIr!] => canonicalIndexResult!
                    (canonicalIndexResult!.kind == 15 and canonicalIndexResult!.operand1 < 0 and canonicalIndexResult!.operand0 >= expressionIrStart and results![canonicalIndexResult!.operand0].kind == 26) -> if {
                        results![canonicalIndexResult!.operand0] => wrappedConstructor
                        9 => canonicalIndexResult!.kind
                        -1 => canonicalIndexResult!.opcode
                        wrappedConstructor.typeOrigin => canonicalIndexResult!.typeOrigin
                        wrappedConstructor.typeModule => canonicalIndexResult!.typeModule
                        wrappedConstructor.typeSymbol => canonicalIndexResult!.typeSymbol
                        wrappedConstructor.typeId => canonicalIndexResult!.typeId
                        wrappedConstructor.typeKind => canonicalIndexResult!.typeKind
                        wrappedConstructor.typeFlags => canonicalIndexResult!.typeFlags
                        canonicalIndexResult! => results![canonicalIndexResultIr!]
                    }
                    (canonicalIndexResult!.kind == 15 and canonicalIndexResult!.typeId < 0 and canonicalIndexResult!.operand0 >= expressionIrStart) -> if {
                        results![canonicalIndexResult!.operand0] => canonicalIndexedValue
                        canonicalIndexedValue.typeId >= 0 -> if {
                            recursiveSemanticTypes![canonicalIndexedValue.typeId] => canonicalContainerType
                            -1 => canonicalElementTypeId!
                            (canonicalContainerType.kind == 2 or canonicalContainerType.kind == 3 or canonicalContainerType.kind == 4) -> if { canonicalContainerType.first => canonicalElementTypeId! }
                            canonicalContainerType.kind == 5 -> if { canonicalContainerType.second => canonicalElementTypeId! }
                            canonicalElementTypeId! >= 0 -> if {
                                recursiveSemanticTypes![canonicalElementTypeId!] => canonicalElementType
                                canonicalElementTypeId! => canonicalIndexResult!.typeId
                                canonicalElementType.kind => canonicalIndexResult!.typeKind
                                recursiveTypeFlags![canonicalElementTypeId!] => canonicalIndexResult!.typeFlags
                                canonicalElementType.origin => canonicalIndexResult!.typeOrigin
                                canonicalElementType.module => canonicalIndexResult!.typeModule
                                canonicalElementType.symbol => canonicalIndexResult!.typeSymbol
                                (canonicalElementType.kind >= 2 and canonicalElementType.kind <= 6) -> if { 10 + canonicalElementType.kind => canonicalIndexResult!.typeOrigin }
                                (canonicalElementType.kind == 2 or canonicalElementType.kind == 3 or canonicalElementType.kind == 4 or canonicalElementType.kind == 6) -> if {
                                    canonicalElementType.first >= 0 -> if {
                                        recursiveSemanticTypes![canonicalElementType.first].module => canonicalIndexResult!.typeModule
                                        recursiveSemanticTypes![canonicalElementType.first].symbol => canonicalIndexResult!.typeSymbol
                                    }
                                }
                                canonicalElementType.kind == 5 -> if {
                                    canonicalElementType.first >= 0 -> if { recursiveSemanticTypes![canonicalElementType.first].symbol => canonicalIndexResult!.typeModule }
                                    canonicalElementType.second >= 0 -> if { recursiveSemanticTypes![canonicalElementType.second].symbol => canonicalIndexResult!.typeSymbol }
                                }
                                canonicalIndexResult! => results![canonicalIndexResultIr!]
                            }
                        }
                    }
                    canonicalIndexResultIr! + 1 => canonicalIndexResultIr!
                }

                expressionIrStart => siblingIrIndex!
                siblingIrIndex! < expressionIrEnd -> while {
                    results![siblingIrIndex!] => sibling!
                    -1 => nextSibling!
                    UIntSize(0) => nextSiblingStart!
                    expressionIrStart => siblingSearch!
                    siblingSearch! < expressionIrEnd -> while {
                        results![siblingSearch!] => siblingCandidate
                        (siblingCandidate.parent == sibling!.parent and prepared.package.nodes[sourceRange.astStart + siblingCandidate.astNode].start > prepared.package.nodes[sourceRange.astStart + sibling!.astNode].start) -> if {
                            prepared.package.nodes[sourceRange.astStart + siblingCandidate.astNode].start => siblingCandidateStart
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
                                prepared.package.nodes[sourceRange.astStart + results![regionChildSearch!].astNode].start => regionChildStart
                                (firstRegionChild! < 0 or regionChildStart < firstRegionChildStart!) -> if {
                                    regionChildSearch! => firstRegionChild!
                                    regionChildStart => firstRegionChildStart!
                                }
                                (lastRegionChild! < 0 or regionChildStart > lastRegionChildStart! or (regionChildStart == lastRegionChildStart! and (results![regionChildSearch!].kind == 18 or results![regionChildSearch!].kind == 20) and results![lastRegionChild!].kind != 18 and results![lastRegionChild!].kind != 20)) -> if {
                                    regionChildSearch! => lastRegionChild!
                                    regionChildStart => lastRegionChildStart!
                                }
                            }
                            regionChildSearch! + 1 => regionChildSearch!
                        }
                        firstRegionChild! => control!.operand0
                        (lastRegionChild! >= 0 and results![lastRegionChild!].kind == 6 and results![lastRegionChild!].operand1 >= expressionIrStart and results![results![lastRegionChild!].operand1].parent == controlIrIndex!) -> if {
                            results![lastRegionChild!].operand1 => lastRegionChild!
                        }
                        true => nestedRegionUnwrap!
                        (lastRegionChild! >= 0 and nestedRegionUnwrap! and results![lastRegionChild!].kind == 9) -> while {
                            -1 => nestedRegionResult!
                            UIntSize(0) => nestedRegionResultStart!
                            expressionIrStart => nestedResultSearch!
                            nestedResultSearch! < expressionIrEnd -> while {
                                results![nestedResultSearch!].parent == lastRegionChild! -> if {
                                    prepared.package.nodes[sourceRange.astStart + results![nestedResultSearch!].astNode].start => nestedResultStart
                                    (nestedRegionResult! < 0 or nestedResultStart > nestedRegionResultStart!) -> if {
                                        nestedResultSearch! => nestedRegionResult!
                                        nestedResultStart => nestedRegionResultStart!
                                    }
                                }
                                nestedResultSearch! + 1 => nestedResultSearch!
                            }
                            nestedRegionResult! >= 0 -> if { nestedRegionResult! => lastRegionChild! } else { false => nestedRegionUnwrap! }
                        }
                        lastRegionChild! => control!.operand1
                        control! => results![controlIrIndex!]
                    }
                    control!.kind == 27 -> if {
                        prepared.package.nodes[sourceRange.astStart + control!.astNode].parent => matchFlowAst
                        -1 => matchSubject!
                        UIntSize(0) => matchSubjectStart!
                        -1 => matchFirstArm!
                        expressionIrStart => matchSearch!
                        matchSearch! < expressionIrEnd -> while {
                            results![matchSearch!] => matchCandidate
                            prepared.package.nodes[sourceRange.astStart + matchCandidate.astNode] => matchCandidateAst
                            (matchCandidateAst.parent == matchFlowAst and matchCandidateAst.start < prepared.package.nodes[sourceRange.astStart + control!.astNode].start and matchCandidate.typeKind == 7) -> if {
                                (matchSubject! < 0 or matchCandidateAst.start > matchSubjectStart!) -> if {
                                    matchSearch! => matchSubject!
                                    matchCandidateAst.start => matchSubjectStart!
                                }
                            }
                            (matchCandidate.kind == 19 and matchCandidate.parent == controlIrIndex! and matchFirstArm! < 0) -> if { matchSearch! => matchFirstArm! }
                            matchSearch! + 1 => matchSearch!
                        }
                        matchSubject! => control!.operand0
                        matchFirstArm! => control!.operand1
                        control! => results![controlIrIndex!]
                    }
                    (control!.kind == 18 or control!.kind == 20) -> if {
                        prepared.package.nodes[sourceRange.astStart + control!.astNode].parent => controlFlowAst
                        -1 => conditionIr!
                        UIntSize(0) => conditionStart!
                        1000000 => conditionDistance!
                        expressionIrStart => conditionSearch!
                        conditionSearch! < expressionIrEnd -> while {
                            results![conditionSearch!] => conditionCandidate
                            prepared.package.nodes[sourceRange.astStart + conditionCandidate.astNode].parent => conditionAncestor!
                            1 => candidateConditionDistance!
                            false => conditionBelongsToFlow!
                            (conditionAncestor! >= 0 and not conditionBelongsToFlow!) -> while {
                                conditionAncestor! == controlFlowAst -> if { true => conditionBelongsToFlow! } else {
                                    prepared.package.nodes[sourceRange.astStart + conditionAncestor!].parent => conditionAncestor!
                                    candidateConditionDistance! + 1 => candidateConditionDistance!
                                }
                            }
                            (conditionBelongsToFlow! and ((conditionCandidate.typeOrigin == 1 and conditionCandidate.typeSymbol == 23) or (conditionCandidate.typeId >= 0 and recursiveSemanticTypes![conditionCandidate.typeId].origin == 1 and recursiveSemanticTypes![conditionCandidate.typeId].symbol == 23)) and prepared.package.nodes[sourceRange.astStart + conditionCandidate.astNode].start < prepared.package.nodes[sourceRange.astStart + control!.astNode].start) -> if {
                                prepared.package.nodes[sourceRange.astStart + conditionCandidate.astNode].start => candidateStart
                                (conditionIr! < 0 or candidateConditionDistance! < conditionDistance! or (candidateConditionDistance! == conditionDistance! and candidateStart > conditionStart!)) -> if {
                                    conditionSearch! => conditionIr!
                                    candidateStart => conditionStart!
                                    candidateConditionDistance! => conditionDistance!
                                }
                            }
                        conditionSearch! + 1 => conditionSearch!
                    }
                    (conditionIr! < 0 and control!.parent >= expressionIrStart and results![control!.parent].kind != 19 and ((results![control!.parent].typeOrigin == 1 and results![control!.parent].typeSymbol == 23) or (results![control!.parent].typeId >= 0 and recursiveSemanticTypes![results![control!.parent].typeId].origin == 1 and recursiveSemanticTypes![results![control!.parent].typeId].symbol == 23))) -> if {
                        control!.parent => enclosingConditionIr!
                        results![enclosingConditionIr!].parent => control!.parent
                        enclosingConditionIr! => conditionIr!
                    }
                    true => conditionUnwrap!
                    (conditionIr! >= 0 and conditionUnwrap! and results![conditionIr!].kind == 9) -> while {
                        -1 => nestedConditionResult!
                        UIntSize(0) => nestedConditionStart!
                        expressionIrStart => nestedConditionSearch!
                        nestedConditionSearch! < expressionIrEnd -> while {
                            results![nestedConditionSearch!].parent == conditionIr! -> if {
                                prepared.package.nodes[sourceRange.astStart + results![nestedConditionSearch!].astNode].start => nestedCandidateStart
                                (nestedConditionResult! < 0 or nestedCandidateStart > nestedConditionStart!) -> if {
                                    nestedConditionSearch! => nestedConditionResult!
                                    nestedCandidateStart => nestedConditionStart!
                                }
                            }
                            nestedConditionSearch! + 1 => nestedConditionSearch!
                        }
                        nestedConditionResult! >= 0 -> if { nestedConditionResult! => conditionIr! } else { false => conditionUnwrap! }
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

                expressionIrEnd - 1 => controlTypeIndex!
                controlTypeIndex! >= expressionIrStart -> while {
                    results![controlTypeIndex!] => controlTypeNode!
                    (controlTypeNode!.kind == 19 and controlTypeNode!.operand1 >= expressionIrStart) -> if {
                        false => controlRegionRedirected!
                        (results![controlTypeNode!.operand1].kind == 6 and results![controlTypeNode!.operand1].operand1 >= expressionIrStart and results![results![controlTypeNode!.operand1].operand1].parent == controlTypeIndex!) -> if {
                            results![controlTypeNode!.operand1].operand1 => controlTypeNode!.operand1
                            true => controlRegionRedirected!
                        }
                        controlRegionRedirected! -> if {
                            results![controlTypeNode!.operand1] => controlRegionValue
                            controlRegionValue.typeOrigin => controlTypeNode!.typeOrigin
                            controlRegionValue.typeModule => controlTypeNode!.typeModule
                            controlRegionValue.typeSymbol => controlTypeNode!.typeSymbol
                            controlRegionValue.typeId => controlTypeNode!.typeId
                            controlRegionValue.typeKind => controlTypeNode!.typeKind
                            controlRegionValue.typeFlags => controlTypeNode!.typeFlags
                            controlTypeNode! => results![controlTypeIndex!]
                        }
                        results![controlTypeNode!.operand1] => controlRegionResult
                        not (controlRegionResult.typeOrigin == 1 and controlRegionResult.typeSymbol == 0) -> if {
                            controlRegionResult.typeOrigin => controlTypeNode!.typeOrigin
                            controlRegionResult.typeModule => controlTypeNode!.typeModule
                            controlRegionResult.typeSymbol => controlTypeNode!.typeSymbol
                            controlRegionResult.typeId => controlTypeNode!.typeId
                            controlRegionResult.typeKind => controlTypeNode!.typeKind
                            controlRegionResult.typeFlags => controlTypeNode!.typeFlags
                            controlTypeNode! => results![controlTypeIndex!]
                        }
                    }
                    false => controlHasNestedResult!
                    (controlTypeNode!.kind == 18 and controlTypeNode!.operand1 >= expressionIrStart and controlTypeNode!.nextOperand >= expressionIrStart) -> if {
                        results![controlTypeNode!.operand1] => controlThenRegionNode
                        results![controlTypeNode!.nextOperand] => controlElseRegionNode
                        (controlThenRegionNode.operand1 >= expressionIrStart and results![controlThenRegionNode.operand1].kind == 18) -> if { true => controlHasNestedResult! }
                        (controlElseRegionNode.operand1 >= expressionIrStart and results![controlElseRegionNode.operand1].kind == 18) -> if { true => controlHasNestedResult! }
                    }
                    (controlTypeNode!.kind == 18 and controlTypeNode!.typeOrigin == 1 and controlTypeNode!.typeSymbol == 0 and controlTypeNode!.operand1 >= expressionIrStart and controlTypeNode!.nextOperand >= expressionIrStart) -> if {
                        results![controlTypeNode!.operand1] => controlThenType
                        results![controlTypeNode!.nextOperand] => controlElseType
                        (controlThenType.typeOrigin == controlElseType.typeOrigin and controlThenType.typeModule == controlElseType.typeModule and controlThenType.typeSymbol == controlElseType.typeSymbol) -> if {
                            controlThenType.typeOrigin => controlTypeNode!.typeOrigin
                            controlThenType.typeModule => controlTypeNode!.typeModule
                            controlThenType.typeSymbol => controlTypeNode!.typeSymbol
                            controlThenType.typeId => controlTypeNode!.typeId
                            controlThenType.typeKind => controlTypeNode!.typeKind
                            controlThenType.typeFlags => controlTypeNode!.typeFlags
                            controlTypeNode! => results![controlTypeIndex!]
                        }
                    }
                    (controlTypeNode!.kind == 27 and controlTypeNode!.operand1 >= expressionIrStart) -> if {
                        results![controlTypeNode!.operand1] => matchResultArm
                        matchResultArm.typeOrigin => controlTypeNode!.typeOrigin
                        matchResultArm.typeModule => controlTypeNode!.typeModule
                        matchResultArm.typeSymbol => controlTypeNode!.typeSymbol
                        matchResultArm.typeId => controlTypeNode!.typeId
                        matchResultArm.typeKind => controlTypeNode!.typeKind
                        matchResultArm.typeFlags => controlTypeNode!.typeFlags
                        controlTypeNode! => results![controlTypeIndex!]
                    }
                    controlTypeIndex! - 1 => controlTypeIndex!
                }

                expressionIrStart => bindingControlResultIndex!
                bindingControlResultIndex! < expressionIrEnd -> while {
                    results![bindingControlResultIndex!] => bindingControlResult!
                    (bindingControlResult!.kind == 17 and bindingControlResult!.operand0 >= expressionIrStart and results![bindingControlResult!.operand0].kind == 9 and results![bindingControlResult!.operand0].opcode == -1) -> if {
                        bindingControlResult!.operand0 => bindingControlWrapper
                        -1 => bindingWrapperResult!
                        UIntSize(0) => bindingWrapperResultLength!
                        false => bindingWrapperIsControl!
                        expressionIrStart => bindingControlSearch!
                        bindingControlSearch! < expressionIrEnd -> while {
                            (results![bindingControlSearch!].kind == 18 and results![bindingControlSearch!].parent == bindingControlWrapper) -> if {
                                bindingControlSearch! => bindingWrapperResult!
                                true => bindingWrapperIsControl!
                            }
                            (not bindingWrapperIsControl! and results![bindingControlSearch!].parent == bindingControlWrapper and results![bindingControlSearch!].kind != 17 and not (results![bindingControlSearch!].typeOrigin == 1 and results![bindingControlSearch!].typeSymbol == 0)) -> if {
                                prepared.package.nodes[sourceRange.astStart + results![bindingControlSearch!].astNode].length => bindingWrapperCandidateLength
                                (bindingWrapperResult! < 0 or bindingWrapperCandidateLength > bindingWrapperResultLength!) -> if {
                                    bindingControlSearch! => bindingWrapperResult!
                                    bindingWrapperCandidateLength => bindingWrapperResultLength!
                                }
                            }
                            bindingControlSearch! + 1 => bindingControlSearch!
                        }
                        bindingWrapperResult! >= 0 -> if { bindingWrapperResult! => bindingControlResult!.operand0 }
                        bindingControlResult! => results![bindingControlResultIndex!]
                    }
                    bindingControlResultIndex! + 1 => bindingControlResultIndex!
                }

                expressionIrStart => bindingNameIrIndex!
                # A result-producing role owns both the call and its result
                # binding on the same AST node. Equal-AST siblings are not
                # connected by the ordinary child-operand pass.
                expressionIrStart => roleBindingIndex!
                roleBindingIndex! < expressionIrEnd -> while {
                    results![roleBindingIndex!] => roleBinding!
                    (roleBinding!.kind == 17 and roleBinding!.operand0 < 0 and prepared.package.nodes[sourceRange.astStart + roleBinding!.astNode].kind == 48) -> if {
                        expressionIrStart => roleCallSearch!
                        roleCallSearch! < expressionIrEnd -> while {
                            (results![roleCallSearch!].kind == 6 and results![roleCallSearch!].astNode == roleBinding!.astNode and (results![roleCallSearch!].opcode == -207 or results![roleCallSearch!].opcode == -209)) -> if { roleCallSearch! => roleBinding!.operand0 }
                            roleCallSearch! + 1 => roleCallSearch!
                        }
                        roleBinding! => results![roleBindingIndex!]
                    }
                    roleBindingIndex! + 1 => roleBindingIndex!
                }

                expressionIrStart => bindingNameIrIndex!
                bindingNameIrIndex! < expressionIrEnd -> while {
                    results![bindingNameIrIndex!] => bindingName!
                    bindingName!.kind == 5 -> if {
                        expressionIrStart => bindingDefinitionSearch!
                        bindingDefinitionSearch! < expressionIrEnd -> while {
                            ((results![bindingDefinitionSearch!].kind == 17 or results![bindingDefinitionSearch!].kind == 29) and results![bindingDefinitionSearch!].symbol == bindingName!.symbol) -> if {
                                bindingDefinitionSearch! => bindingName!.operand0
                            }
                            bindingDefinitionSearch! + 1 => bindingDefinitionSearch!
                        }
                        bindingName! => results![bindingNameIrIndex!]
                    }
                    bindingNameIrIndex! + 1 => bindingNameIrIndex!
                }

                expressionIrStart => memberAssignmentIrIndex!
                memberAssignmentIrIndex! < expressionIrEnd -> while {
                    results![memberAssignmentIrIndex!] => memberAssignment!
                    memberAssignment!.kind == 25 -> if {
                        prepared.package.nodes[sourceRange.astStart + memberAssignment!.astNode] => memberAssignmentAst
                        prepared.package.tokens[sourceRange.tokenStart + memberAssignmentAst.payloadToken] => memberOwnerName
                        -1 => memberOwnerBinding!
                        UIntSize(0) => memberOwnerStart!
                        expressionIrStart => memberOwnerSearch!
                        memberOwnerSearch! < memberAssignmentIrIndex! -> while {
                            results![memberOwnerSearch!] => memberOwnerCandidate
                            (memberOwnerCandidate.kind == 17 and memberOwnerCandidate.flags == 1 and memberOwnerCandidate.payloadToken >= 0) -> if {
                                prepared.package.tokens[sourceRange.tokenStart + memberOwnerCandidate.payloadToken] => memberCandidateName
                                memberOwnerName.span.length == memberCandidateName.span.length => memberOwnerEqual!
                                UIntSize(0) => memberOwnerByte!
                                (memberOwnerEqual! and memberOwnerByte! < memberOwnerName.span.length) -> while {
                                    (source -> byte(memberOwnerName.span.start + memberOwnerByte!)) != (source -> byte(memberCandidateName.span.start + memberOwnerByte!)) -> if { false => memberOwnerEqual! }
                                    memberOwnerByte! + UIntSize(1) => memberOwnerByte!
                                }
                                memberOwnerEqual! -> if {
                                    prepared.package.nodes[sourceRange.astStart + memberOwnerCandidate.astNode].start => memberCandidateStart
                                    (memberOwnerBinding! < 0 or memberCandidateStart > memberOwnerStart!) -> if {
                                        memberOwnerSearch! => memberOwnerBinding!
                                        memberCandidateStart => memberOwnerStart!
                                    }
                                }
                            }
                            memberOwnerSearch! + 1 => memberOwnerSearch!
                        }
                        memberOwnerBinding! >= 0 -> if {
                            memberOwnerBinding! => memberAssignment!.operand1
                            results![memberOwnerBinding!].symbol => memberAssignment!.symbol
                            memberAssignment! => results![memberAssignmentIrIndex!]
                        }
                    }
                    memberAssignmentIrIndex! + 1 => memberAssignmentIrIndex!
                }

                # A binding's initializer is the authoritative canonical
                # type. The legacy nearest-descendant heuristic can select
                # a loop index type from inside the binding's later use.
                expressionIrStart => canonicalBindingIr!
                canonicalBindingIr! < expressionIrEnd -> while {
                    results![canonicalBindingIr!] => canonicalBinding!
                    (canonicalBinding!.kind == 17 and canonicalBinding!.operand0 >= expressionIrStart) -> if {
                        canonicalBinding!.operand0 => canonicalBindingValueIndex!
                        (results![canonicalBindingValueIndex!].kind == 9 and results![canonicalBindingValueIndex!].opcode == -1) -> if {
                            -1 => canonicalWrapperValue!
                            UIntSize(0) => canonicalWrapperLength!
                            expressionIrStart => canonicalWrapperSearch!
                            canonicalWrapperSearch! < expressionIrEnd -> while {
                                results![canonicalWrapperSearch!] => canonicalWrapperCandidate
                                (canonicalWrapperCandidate.parent == canonicalBindingValueIndex! and canonicalWrapperCandidate.kind != 17 and not (canonicalWrapperCandidate.typeOrigin == 1 and canonicalWrapperCandidate.typeSymbol == 0)) -> if {
                                    prepared.package.nodes[sourceRange.astStart + canonicalWrapperCandidate.astNode].length => canonicalWrapperCandidateLength
                                    (canonicalWrapperValue! < 0 or canonicalWrapperCandidateLength > canonicalWrapperLength!) -> if {
                                        canonicalWrapperSearch! => canonicalWrapperValue!
                                        canonicalWrapperCandidateLength => canonicalWrapperLength!
                                    }
                                }
                                canonicalWrapperSearch! + 1 => canonicalWrapperSearch!
                            }
                            canonicalWrapperValue! >= 0 -> if {
                                canonicalWrapperValue! => canonicalBindingValueIndex!
                                canonicalWrapperValue! => canonicalBinding!.operand0
                            }
                        }
                        results![canonicalBindingValueIndex!] => canonicalBindingValue
                        canonicalBindingValue.typeOrigin >= 0 -> if {
                        canonicalBindingValue.typeOrigin => canonicalBinding!.typeOrigin
                        canonicalBindingValue.typeModule => canonicalBinding!.typeModule
                        canonicalBindingValue.typeSymbol => canonicalBinding!.typeSymbol
                        canonicalBindingValue.typeId => canonicalBinding!.typeId
                        canonicalBindingValue.typeKind => canonicalBinding!.typeKind
                        canonicalBindingValue.typeFlags => canonicalBinding!.typeFlags
                        canonicalBinding!.typeId < 0 -> if {
                            0 => canonicalInitializerTypeSearch!
                            canonicalInitializerTypeSearch! < (recursiveSemanticTypes! -> len) -> while {
                                recursiveSemanticTypes![canonicalInitializerTypeSearch!] => canonicalInitializerType
                                (canonicalInitializerType.status == 0 and canonicalInitializerType.origin == canonicalBinding!.typeOrigin and canonicalInitializerType.module == canonicalBinding!.typeModule and canonicalInitializerType.symbol == canonicalBinding!.typeSymbol) -> if {
                                    canonicalInitializerTypeSearch! => canonicalBinding!.typeId
                                    canonicalInitializerType.kind => canonicalBinding!.typeKind
                                    recursiveTypeFlags![canonicalInitializerTypeSearch!] => canonicalBinding!.typeFlags
                                }
                                canonicalInitializerTypeSearch! + 1 => canonicalInitializerTypeSearch!
                            }
                        }
                        }
                        canonicalBinding! => results![canonicalBindingIr!]
                    }
                    canonicalBindingIr! + 1 => canonicalBindingIr!
                }
                expressionIrStart => canonicalBindingReadIr!
                canonicalBindingReadIr! < expressionIrEnd -> while {
                    results![canonicalBindingReadIr!] => canonicalBindingRead!
                    # Bindings are allocated before expressionIrStart. A read can
                    # therefore point to a valid local binding outside the
                    # expression slice; its initializer-derived type remains
                    # authoritative over a stale nearest-expression inference.
                    (canonicalBindingRead!.kind == 5 and canonicalBindingRead!.operand0 >= 0 and results![canonicalBindingRead!.operand0].kind == 17 and results![canonicalBindingRead!.operand0].typeOrigin >= 0) -> if {
                        results![canonicalBindingRead!.operand0] => canonicalReadBinding
                        canonicalReadBinding.typeOrigin => canonicalBindingRead!.typeOrigin
                        canonicalReadBinding.typeModule => canonicalBindingRead!.typeModule
                        canonicalReadBinding.typeSymbol => canonicalBindingRead!.typeSymbol
                        canonicalReadBinding.typeId => canonicalBindingRead!.typeId
                        canonicalReadBinding.typeKind => canonicalBindingRead!.typeKind
                        canonicalReadBinding.typeFlags => canonicalBindingRead!.typeFlags
                        canonicalBindingRead! => results![canonicalBindingReadIr!]
                    }
                    canonicalBindingReadIr! + 1 => canonicalBindingReadIr!
                }

                expressionIrStart => aggregateIrIndex!
                aggregateIrIndex! < expressionIrEnd -> while {
                    results![aggregateIrIndex!] => aggregate!
                    (aggregate!.kind == 12 or aggregate!.kind == 14 or aggregate!.kind == 16) -> if {
                        -1 => firstFieldOperand!
                        expressionIrStart => fieldOperandSearch!
                        fieldOperandSearch! < expressionIrEnd -> while {
                            results![fieldOperandSearch!].parent == aggregateIrIndex! -> if {
                                (firstFieldOperand! < 0 or prepared.package.nodes[sourceRange.astStart + results![fieldOperandSearch!].astNode].start < prepared.package.nodes[sourceRange.astStart + results![firstFieldOperand!].astNode].start) -> if { fieldOperandSearch! => firstFieldOperand! }
                            }
                            fieldOperandSearch! + 1 => fieldOperandSearch!
                        }
                        firstFieldOperand! => aggregate!.operand0
                        aggregate! => results![aggregateIrIndex!]
                    }
                    aggregateIrIndex! + 1 => aggregateIrIndex!
                }

                results![returnIr] => returnNode!
                astToIr![resultRootAst!] => returnOperandIr!
                returnOperandIr! < 0 -> if {
                    1000000 => returnOperandDistance!
                    expressionIrStart => returnOperandSearch!
                    returnOperandSearch! < expressionIrEnd -> while {
                        prepared.package.nodes[sourceRange.astStart + results![returnOperandSearch!].astNode].parent => returnOperandAncestor!
                        1 => returnCandidateDistance!
                        false => returnCandidateBelongs!
                        (returnOperandAncestor! >= 0 and not returnCandidateBelongs!) -> while {
                            returnOperandAncestor! == resultRootAst! -> if { true => returnCandidateBelongs! } else {
                                prepared.package.nodes[sourceRange.astStart + returnOperandAncestor!].parent => returnOperandAncestor!
                                returnCandidateDistance! + 1 => returnCandidateDistance!
                            }
                        }
                        (returnCandidateBelongs! and returnCandidateDistance! < returnOperandDistance!) -> if {
                            returnOperandSearch! => returnOperandIr!
                            returnCandidateDistance! => returnOperandDistance!
                        }
                        returnOperandSearch! + 1 => returnOperandSearch!
                    }
                }
                (returnOperandIr! >= 0 and results![returnOperandIr!].kind == 9 and results![returnOperandIr!].opcode == -1) -> if {
                    returnOperandIr! => returnWrapperIr
                    -1 => returnWrapperResult!
                    expressionIrStart => returnControlSearch!
                    returnControlSearch! < expressionIrEnd -> while {
                        results![returnControlSearch!] => returnWrapperCandidate
                        (returnWrapperCandidate.parent == returnWrapperIr and returnWrapperCandidate.typeOrigin == functionResultOrigin! and returnWrapperCandidate.typeModule == functionResultModule! and returnWrapperCandidate.typeSymbol == functionResultSymbol!) -> if { returnControlSearch! => returnWrapperResult! }
                        returnControlSearch! + 1 => returnControlSearch!
                    }
                    returnWrapperResult! >= 0 -> if { returnWrapperResult! => returnOperandIr! }
                }
                false => returnOperandMatches!
                returnOperandIr! >= 0 -> if {
                    results![returnOperandIr!] => selectedReturnOperand
                    (selectedReturnOperand.typeOrigin == functionResultOrigin! and selectedReturnOperand.typeModule == functionResultModule! and selectedReturnOperand.typeSymbol == functionResultSymbol!) -> if { true => returnOperandMatches! }
                }
                not returnOperandMatches! -> if {
                    -1 => matchingReturnOperand!
                    UIntSize(0) => matchingReturnStart!
                    UIntSize(0) => matchingReturnLength!
                    expressionIrStart => matchingReturnSearch!
                    matchingReturnSearch! < expressionIrEnd -> while {
                        results![matchingReturnSearch!] => matchingReturnCandidate
                        (matchingReturnCandidate.parent == returnIr and matchingReturnCandidate.typeOrigin == functionResultOrigin! and matchingReturnCandidate.typeModule == functionResultModule! and matchingReturnCandidate.typeSymbol == functionResultSymbol!) -> if {
                            prepared.package.nodes[sourceRange.astStart + matchingReturnCandidate.astNode] => matchingReturnAst
                            (matchingReturnOperand! < 0 or matchingReturnAst.start > matchingReturnStart! or (matchingReturnAst.start == matchingReturnStart! and matchingReturnAst.length > matchingReturnLength!)) -> if {
                                matchingReturnSearch! => matchingReturnOperand!
                                matchingReturnAst.start => matchingReturnStart!
                                matchingReturnAst.length => matchingReturnLength!
                            }
                        }
                        matchingReturnSearch! + 1 => matchingReturnSearch!
                    }
                    matchingReturnOperand! < 0 -> if {
                        expressionIrStart => rootReturnSearch!
                        rootReturnSearch! < expressionIrEnd -> while {
                            results![rootReturnSearch!] => rootReturnCandidate
                            (rootReturnCandidate.parent == returnIr and rootReturnCandidate.kind != 17) -> if {
                                prepared.package.nodes[sourceRange.astStart + rootReturnCandidate.astNode] => rootReturnAst
                                (matchingReturnOperand! < 0 or rootReturnAst.start > matchingReturnStart! or (rootReturnAst.start == matchingReturnStart! and rootReturnAst.length > matchingReturnLength!)) -> if {
                                    rootReturnSearch! => matchingReturnOperand!
                                    rootReturnAst.start => matchingReturnStart!
                                    rootReturnAst.length => matchingReturnLength!
                                }
                            }
                            rootReturnSearch! + 1 => rootReturnSearch!
                        }
                    }
                    matchingReturnOperand! >= 0 -> if { matchingReturnOperand! => returnOperandIr! }
                }
                returnOperandIr! => returnNode!.operand0
                returnNode! => results![returnIr]
            }
        }
        SourceTypedIr { nodes: results! } => functionResult!
        functionResult!
    }

    lowerSource requestedSourceIndex: Int -> SourceTypedIr {
        [TypedIrNode; ~] => results!
        requestedSourceIndex => sourceIndex!
        prepared.package.sources[sourceIndex!] -> len => sourceLength
        prepared.package.sources[sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        prepared.package.ranges[sourceIndex!] => sourceRange
        [resolution.ResolvedName; ~] => resolvedNames!
        0 => sourceNameOffset!
        sourceNameOffset! < sourceRange.nameCount -> while {
            resolvedNames! -> push(prepared.package.names[sourceRange.nameStart + sourceNameOffset!])
            sourceNameOffset! + 1 => sourceNameOffset!
        }
        [FunctionLowerRequest; ~] => functionRequests!
        0 => symbolIndex!
        symbolIndex! < sourceRange.symbolCount -> while {
            prepared.package.symbols[sourceRange.symbolStart + symbolIndex!] => symbol
            symbol.kind == 7 -> if {
                functionRequests! -> push(FunctionLowerRequest {
                    sourceIndex: sourceIndex!
                    symbolIndex: symbolIndex!
                })
            }
            symbolIndex! + 1 => symbolIndex!
        }
        functionRequests! -> parallel functionRequest {
            functionRequest -> lowerFunction
        } => functionResults!
        functionResults! -> each functionResult {
            results! -> len => functionIrOffset
            0 => functionNodeIndex!
            functionNodeIndex! < (functionResult.nodes -> len) -> while {
                functionResult.nodes[functionNodeIndex!] => functionNode!
                functionNode!.parent >= 0 -> if { functionNode!.parent + functionIrOffset => functionNode!.parent }
                functionNode!.operand0 >= 0 -> if { functionNode!.operand0 + functionIrOffset => functionNode!.operand0 }
                functionNode!.operand1 >= 0 -> if { functionNode!.operand1 + functionIrOffset => functionNode!.operand1 }
                functionNode!.nextOperand >= 0 -> if { functionNode!.nextOperand + functionIrOffset => functionNode!.nextOperand }
                results! -> push(functionNode!)
                functionNodeIndex! + 1 => functionNodeIndex!
            }
        }
        0 => entryAstIndex!
        entryAstIndex! < sourceRange.astCount -> while {
            prepared.package.nodes[sourceRange.astStart + entryAstIndex!] => entryAst
            entryAst.kind == 8 -> if {
                -1 => entryResultTypeIndex!
                1000000 => entryResultDistance!
                0 => entryTypeSearch!
                entryTypeSearch! < (inferred! -> len) -> while {
                    inferred![entryTypeSearch!] => entryCandidateType
                    entryCandidateType.sourceModule == sourceIndex! -> if {
                        prepared.package.nodes[sourceRange.astStart + entryCandidateType.astNode].parent => entryAncestor!
                        1 => entryDistance!
                        false => belongsToEntry!
                        false => entryBlockedByNestedFunction!
                        (entryAncestor! >= 0 and not belongsToEntry! and not entryBlockedByNestedFunction!) -> while {
                            entryAncestor! == entryAstIndex! -> if { true => belongsToEntry! } else {
                                (prepared.package.nodes[sourceRange.astStart + entryAncestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + entryAncestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + entryAncestor!].kind == 31) -> if { true => entryBlockedByNestedFunction! } else {
                                    prepared.package.nodes[sourceRange.astStart + entryAncestor!].parent => entryAncestor!
                                    entryDistance! + 1 => entryDistance!
                                }
                            }
                        }
                        (belongsToEntry! and (entryDistance! < entryResultDistance! or (entryDistance! == entryResultDistance! and (entryResultTypeIndex! < 0 or prepared.package.nodes[sourceRange.astStart + entryCandidateType.astNode].start > prepared.package.nodes[sourceRange.astStart + inferred![entryResultTypeIndex!].astNode].start)))) -> if {
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
                        prepared.package.nodes[sourceRange.astStart + entryBindingAstIndex!] => entryBindingAst
                        (entryBindingAst.kind == 9 or (entryBindingAst.kind == 48 and entryBindingAst.secondaryToken >= 0)) -> if {
                            entryBindingAst.parent => entryBindingAncestor!
                            false => entryBindingBelongs!
                            false => entryBindingBlockedByNestedFunction!
                            (entryBindingAncestor! >= 0 and not entryBindingBelongs! and not entryBindingBlockedByNestedFunction!) -> while {
                                entryBindingAncestor! == entryAstIndex! -> if { true => entryBindingBelongs! } else {
                                    (prepared.package.nodes[sourceRange.astStart + entryBindingAncestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + entryBindingAncestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + entryBindingAncestor!].kind == 31) -> if { true => entryBindingBlockedByNestedFunction! } else { prepared.package.nodes[sourceRange.astStart + entryBindingAncestor!].parent => entryBindingAncestor! }
                                }
                            }
                            entryBindingBelongs! -> if {
                                -1 => entryBindingTypeIndex!
                                1000000 => entryBindingTypeDistance!
                                0 => entryBindingTypeSearch!
                                entryBindingTypeSearch! < (inferred! -> len) -> while {
                                    inferred![entryBindingTypeSearch!] => entryBindingTypeCandidate
                                    entryBindingTypeCandidate.sourceModule == sourceIndex! -> if {
                                        prepared.package.nodes[sourceRange.astStart + entryBindingTypeCandidate.astNode].parent => entryBindingTypeAncestor!
                                        1 => entryBindingDistance!
                                        entryBindingTypeCandidate.astNode == entryBindingAstIndex! => entryBelongsToBinding!
                                        entryBelongsToBinding! -> if { 0 => entryBindingDistance! }
                                        (entryBindingTypeAncestor! >= 0 and not entryBelongsToBinding!) -> while {
                                            entryBindingTypeAncestor! == entryBindingAstIndex! -> if { true => entryBelongsToBinding! } else {
                                                prepared.package.nodes[sourceRange.astStart + entryBindingTypeAncestor!].parent => entryBindingTypeAncestor!
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
                                -1 => entryBindingCanonicalTypeId!
                                1000000 => entryBindingCanonicalDistance!
                                0 => entryBindingCanonicalSearch!
                                entryBindingCanonicalSearch! < (recursiveExpressions! -> len) -> while {
                                    recursiveExpressions![entryBindingCanonicalSearch!] => entryBindingCanonicalCandidate
                                    (entryBindingCanonicalCandidate.status == 0 and entryBindingCanonicalCandidate.sourceModule == sourceIndex!) -> if {
                                        0 => entryBindingCanonicalCandidateDistance!
                                        entryBindingCanonicalCandidate.astNode == entryBindingAstIndex! => entryBindingCanonicalBelongs!
                                        entryBindingCanonicalBelongs! -> if { 0 => entryBindingCanonicalCandidateDistance! } else {
                                            prepared.package.nodes[sourceRange.astStart + entryBindingCanonicalCandidate.astNode].parent => entryBindingCanonicalAncestor!
                                            1 => entryBindingCanonicalCandidateDistance!
                                            (entryBindingCanonicalAncestor! >= 0 and not entryBindingCanonicalBelongs!) -> while {
                                                entryBindingCanonicalAncestor! == entryBindingAstIndex! -> if { true => entryBindingCanonicalBelongs! } else {
                                                    prepared.package.nodes[sourceRange.astStart + entryBindingCanonicalAncestor!].parent => entryBindingCanonicalAncestor!
                                                    entryBindingCanonicalCandidateDistance! + 1 => entryBindingCanonicalCandidateDistance!
                                                }
                                            }
                                        }
                                        (entryBindingCanonicalBelongs! and entryBindingCanonicalCandidateDistance! < entryBindingCanonicalDistance!) -> if {
                                            entryBindingCanonicalCandidate.typeId => entryBindingCanonicalTypeId!
                                            entryBindingCanonicalCandidateDistance! => entryBindingCanonicalDistance!
                                        }
                                    }
                                    entryBindingCanonicalSearch! + 1 => entryBindingCanonicalSearch!
                                }
                                -1 => entryBindingSymbol!
                                0 => entryBindingSymbolSearch!
                                entryBindingSymbolSearch! < sourceRange.symbolCount -> while {
                                    (prepared.package.symbols[sourceRange.symbolStart + entryBindingSymbolSearch!].kind == 9 and prepared.package.symbols[sourceRange.symbolStart + entryBindingSymbolSearch!].astNode == entryBindingAstIndex!) -> if { entryBindingSymbolSearch! => entryBindingSymbol! }
                                    entryBindingSymbolSearch! + 1 => entryBindingSymbolSearch!
                                }
                                entryBindingSymbol! >= 0 -> if {
                                    prepared.package.symbols[sourceRange.symbolStart + entryBindingSymbol!] => declaredEntryBindingSymbol
                                    0 => entryBindingDeclaredReferenceSearch!
                                    entryBindingDeclaredReferenceSearch! < (prepared.semantic.references -> len) -> while {
                                        prepared.semantic.references[entryBindingDeclaredReferenceSearch!] => entryBindingDeclaredReference
                                        (entryBindingDeclaredReference.sourceModule == sourceIndex! and entryBindingDeclaredReference.typeAst == declaredEntryBindingSymbol.typeNode and entryBindingDeclaredReference.status == 0) -> if {
                                            entryBindingDeclaredReference.typeId => entryBindingCanonicalTypeId!
                                        }
                                        entryBindingDeclaredReferenceSearch! + 1 => entryBindingDeclaredReferenceSearch!
                                    }
                                }
                                (entryBindingTypeIndex! >= 0 or entryBindingCanonicalTypeId! >= 0) -> if {
                                    -1 => entryBindingOrigin!
                                    -1 => entryBindingModule!
                                    -1 => entryBindingSymbolType!
                                    -1 => entryBindingTypeKind!
                                    0 => entryBindingTypeFlags!
                                    entryBindingTypeIndex! >= 0 -> if {
                                        inferred![entryBindingTypeIndex!] => entryBindingType
                                        entryBindingType.origin => entryBindingOrigin!
                                        entryBindingType.targetModule => entryBindingModule!
                                        entryBindingType.targetSymbol => entryBindingSymbolType!
                                    }
                                    entryBindingCanonicalTypeId! >= 0 -> if {
                                        recursiveSemanticTypes![entryBindingCanonicalTypeId!] => entryBindingCanonicalType
                                        entryBindingCanonicalType.origin => entryBindingOrigin!
                                        entryBindingCanonicalType.module => entryBindingModule!
                                        entryBindingCanonicalType.symbol => entryBindingSymbolType!
                                        entryBindingCanonicalType.kind => entryBindingTypeKind!
                                        recursiveTypeFlags![entryBindingCanonicalTypeId!] => entryBindingTypeFlags!
                                        (entryBindingCanonicalType.kind >= 2 and entryBindingCanonicalType.kind <= 6) -> if { 10 + entryBindingCanonicalType.kind => entryBindingOrigin! }
                                        (entryBindingCanonicalType.kind == 2 or entryBindingCanonicalType.kind == 3 or entryBindingCanonicalType.kind == 4 or entryBindingCanonicalType.kind == 6) -> if {
                                            entryBindingCanonicalType.first >= 0 -> if {
                                                recursiveSemanticTypes![entryBindingCanonicalType.first].module => entryBindingModule!
                                                recursiveSemanticTypes![entryBindingCanonicalType.first].symbol => entryBindingSymbolType!
                                            }
                                        }
                                        entryBindingCanonicalType.kind == 5 -> if {
                                            entryBindingCanonicalType.first >= 0 -> if { recursiveSemanticTypes![entryBindingCanonicalType.first].symbol => entryBindingModule! }
                                            entryBindingCanonicalType.second >= 0 -> if { recursiveSemanticTypes![entryBindingCanonicalType.second].symbol => entryBindingSymbolType! }
                                        }
                                    }
                                    (entryBindingOrigin! == 13 and entryBindingSymbol! >= 0) -> if {
                                        prepared.package.symbols[sourceRange.symbolStart + entryBindingSymbol!] => declaredEntryBinding
                                        0 => declaredEntryBindingTypeSearch!
                                        declaredEntryBindingTypeSearch! < (prepared.nominal -> len) -> while {
                                            prepared.nominal[declaredEntryBindingTypeSearch!] => declaredEntryBindingType
                                            (declaredEntryBindingType.sourceModule == sourceIndex! and declaredEntryBindingType.typeAst == declaredEntryBinding.typeNode and declaredEntryBindingType.status == 0) -> if {
                                                declaredEntryBindingType.targetModule => entryBindingModule!
                                                declaredEntryBindingType.targetSymbol => entryBindingSymbolType!
                                            }
                                            declaredEntryBindingTypeSearch! + 1 => declaredEntryBindingTypeSearch!
                                        }
                                    }
                                    results! -> len => entryBindingIr
                                    entryBindingIr => entryAstToIr![entryBindingAstIndex!]
                                    entryBindingAst.payloadToken => entryBindingPayloadToken!
                                    entryBindingAst.kind == 48 -> if { entryBindingAst.secondaryToken => entryBindingPayloadToken! }
                                    results! -> push(TypedIrNode {
                                        kind: 17
                                        parent: entryIr!
                                        sourceModule: sourceIndex!
                                        astNode: entryBindingAstIndex!
                                        symbol: entryBindingSymbol!
                                        targetModule: sourceIndex!
                                        typeOrigin: entryBindingOrigin!
                                        typeModule: entryBindingModule!
                                        typeSymbol: entryBindingSymbolType!
                                        typeId: entryBindingCanonicalTypeId!
                                        typeKind: entryBindingTypeKind!
                                        typeFlags: entryBindingTypeFlags!
                                        payloadToken: entryBindingPayloadToken!
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
                        prepared.package.nodes[sourceRange.astStart + entryExpressionAst!] => entryExpression
                        entryExpression.parent => entryExpressionAncestor!
                        false => entryExpressionBelongs!
                        false => entryExpressionBlockedByNestedFunction!
                        (entryExpressionAncestor! >= 0 and not entryExpressionBelongs! and not entryExpressionBlockedByNestedFunction!) -> while {
                            entryExpressionAncestor! == entryAstIndex! -> if { true => entryExpressionBelongs! } else {
                                (prepared.package.nodes[sourceRange.astStart + entryExpressionAncestor!].kind == 7 or prepared.package.nodes[sourceRange.astStart + entryExpressionAncestor!].kind == 29 or prepared.package.nodes[sourceRange.astStart + entryExpressionAncestor!].kind == 31) -> if { true => entryExpressionBlockedByNestedFunction! } else { prepared.package.nodes[sourceRange.astStart + entryExpressionAncestor!].parent => entryExpressionAncestor! }
                            }
                        }
                        inferredIndexByAst![sourceRange.astStart + entryExpressionAst!] => entryExpressionTypeIndex!
                        recursiveTypeByAst![sourceRange.astStart + entryExpressionAst!] => recursiveEntryExpressionTypeId!
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
                        false => knownEntryPatternReference!
                        -1 => entryPatternTypeAst!
                        entryExpression.kind == 59 -> if { entryExpressionAst! => entryPatternTypeAst! }
                        entryExpression.kind == 15 -> if {
                            0 => entryPatternNameResolutionSearch!
                            entryPatternNameResolutionSearch! < (resolvedNames! -> len) -> while {
                                resolvedNames![entryPatternNameResolutionSearch!] => entryPatternResolvedName
                                entryPatternResolvedName.astNode == entryExpressionAst! -> if {
                                    prepared.package.symbols[sourceRange.symbolStart + entryPatternResolvedName.symbol] => entryPatternResolvedSymbol
                                    prepared.package.nodes[sourceRange.astStart + entryPatternResolvedSymbol.astNode].kind == 59 -> if { entryPatternResolvedSymbol.astNode => entryPatternTypeAst! }
                                }
                                entryPatternNameResolutionSearch! + 1 => entryPatternNameResolutionSearch!
                            }
                        }
                        entryPatternTypeAst! >= 0 -> if {
                            prepared.package.nodes[sourceRange.astStart + entryPatternTypeAst!] => entryPatternTypeNode
                            prepared.package.nodes[sourceRange.astStart + entryPatternTypeNode.parent].parent => entryPatternMatchAst
                            prepared.package.nodes[sourceRange.astStart + entryPatternMatchAst].parent => entryPatternFlowAst
                            -1 => entryPatternSubjectTypeId!
                            UIntSize(0) => entryPatternSubjectStart!
                            0 => entryPatternSubjectSearch!
                            entryPatternSubjectSearch! < sourceRange.astCount -> while {
                                prepared.package.nodes[sourceRange.astStart + entryPatternSubjectSearch!] => entryPatternSubjectAst
                                recursiveTypeByAst![sourceRange.astStart + entryPatternSubjectSearch!] => entryPatternSubjectCandidateType
                                (entryPatternSubjectAst.parent == entryPatternFlowAst and entryPatternSubjectAst.start < prepared.package.nodes[sourceRange.astStart + entryPatternMatchAst].start and entryPatternSubjectCandidateType >= 0 and recursiveSemanticTypes![entryPatternSubjectCandidateType].kind == 7) -> if {
                                    (entryPatternSubjectTypeId! < 0 or entryPatternSubjectAst.start > entryPatternSubjectStart!) -> if {
                                        entryPatternSubjectCandidateType => entryPatternSubjectTypeId!
                                        entryPatternSubjectAst.start => entryPatternSubjectStart!
                                    }
                                }
                                entryPatternSubjectSearch! + 1 => entryPatternSubjectSearch!
                            }
                            entryPatternSubjectTypeId! >= 0 -> if {
                                recursiveSemanticTypes![entryPatternSubjectTypeId!] => entryPatternSubjectType
                                prepared.package.tokens[sourceRange.tokenStart + entryPatternTypeNode.payloadToken] => entryPatternVariantToken
                                entryPatternSubjectType.first => recursiveEntryExpressionTypeId!
                                (source -> byte(entryPatternVariantToken.span.start)) == UInt8(69) -> if { entryPatternSubjectType.second => recursiveEntryExpressionTypeId! }
                                recursiveEntryExpressionTypeId! >= 0 -> if {
                                    recursiveSemanticTypes![recursiveEntryExpressionTypeId!] => entryPatternPayloadType
                                    entryPatternPayloadType.origin => entryExpressionTypeOrigin!
                                    entryPatternPayloadType.module => entryExpressionTypeModule!
                                    entryPatternPayloadType.symbol => entryExpressionTypeSymbol!
                                    true => knownEntryPatternReference!
                                }
                            }
                        }
                        false => knownEntryCallExpression!
                        0 => knownEntryCallSearch!
                        knownEntryCallSearch! < (prepared.calls -> len) -> while {
                            prepared.calls[knownEntryCallSearch!] => knownEntryCall
                            (knownEntryCall.sourceModule == sourceIndex! and knownEntryCall.callAst == entryExpressionAst! and knownEntryCall.status == 0) -> if {
                                true => knownEntryCallExpression!
                                (knownEntryCall.targetSourceModule >= 0 and knownEntryCall.functionSymbol >= 0) -> if {
                                    prepared.package.ranges[knownEntryCall.targetSourceModule] => knownEntryCallTargetRange
                                    prepared.package.symbols[knownEntryCallTargetRange.symbolStart + knownEntryCall.functionSymbol] => knownEntryCallFunction
                                    knownEntryCallFunction.typeNode => knownEntryCallResultAst!
                                    knownEntryCallFunction.secondaryTypeNode >= 0 -> if { knownEntryCallFunction.secondaryTypeNode => knownEntryCallResultAst! }
                                    0 => knownEntryCallTypeSearch!
                                    knownEntryCallTypeSearch! < (prepared.nominal -> len) -> while {
                                        prepared.nominal[knownEntryCallTypeSearch!] => knownEntryCallType
                                        (knownEntryCallType.sourceModule == knownEntryCall.targetSourceModule and knownEntryCallType.typeAst == knownEntryCallResultAst! and knownEntryCallType.status == 0) -> if {
                                            knownEntryCallType.origin => entryExpressionTypeOrigin!
                                            knownEntryCallType.targetModule => entryExpressionTypeModule!
                                            knownEntryCallType.targetSymbol => entryExpressionTypeSymbol!
                                        }
                                        knownEntryCallTypeSearch! + 1 => knownEntryCallTypeSearch!
                                    }
                                } else {
                                    knownEntryCall.functionSymbol == -115 -> if {
                                        1 => entryExpressionTypeOrigin!
                                        -1 => entryExpressionTypeModule!
                                        2 => entryExpressionTypeSymbol!
                                    }
                                }
                            }
                            knownEntryCallSearch! + 1 => knownEntryCallSearch!
                        }
                        entryExpression.kind == 48 -> if {
                            true => knownEntryCallExpression!
                            1 => entryExpressionTypeOrigin!
                            -1 => entryExpressionTypeModule!
                            0 => entryExpressionTypeSymbol!
                        }
                        false => knownEntryNumericExpression!
                        (entryExpression.kind == 20 or entryExpression.kind == 21) -> if {
                            1000000 => knownEntryNumericDistance!
                            0 => knownEntryNumericSearch!
                            knownEntryNumericSearch! < (inferred! -> len) -> while {
                                inferred![knownEntryNumericSearch!] => knownEntryNumericCandidate
                                knownEntryNumericCandidate.sourceModule == sourceIndex! -> if {
                                    prepared.package.nodes[sourceRange.astStart + knownEntryNumericCandidate.astNode].parent => knownEntryNumericAncestor!
                                    1 => knownEntryNumericCandidateDistance!
                                    false => knownEntryNumericBelongs!
                                    (knownEntryNumericAncestor! >= 0 and not knownEntryNumericBelongs!) -> while {
                                        knownEntryNumericAncestor! == entryExpressionAst! -> if { true => knownEntryNumericBelongs! } else {
                                            prepared.package.nodes[sourceRange.astStart + knownEntryNumericAncestor!].parent => knownEntryNumericAncestor!
                                            knownEntryNumericCandidateDistance! + 1 => knownEntryNumericCandidateDistance!
                                        }
                                    }
                                    (knownEntryNumericBelongs! and knownEntryNumericCandidateDistance! < knownEntryNumericDistance!) -> if {
                                        true => knownEntryNumericExpression!
                                        knownEntryNumericCandidateDistance! => knownEntryNumericDistance!
                                        knownEntryNumericCandidate.origin => entryExpressionTypeOrigin!
                                        knownEntryNumericCandidate.targetModule => entryExpressionTypeModule!
                                        knownEntryNumericCandidate.targetSymbol => entryExpressionTypeSymbol!
                                    }
                                }
                                knownEntryNumericSearch! + 1 => knownEntryNumericSearch!
                            }
                        }
                        entryExpression.kind == 22 -> if {
                            0 => entryUnaryChildSearch!
                            (entryUnaryChildSearch! < sourceRange.astCount and not knownEntryNumericExpression!) -> while {
                                prepared.package.nodes[sourceRange.astStart + entryUnaryChildSearch!] => entryUnaryChild
                                entryUnaryChild.parent == entryExpressionAst! -> if {
                                    recursiveTypeByAst![sourceRange.astStart + entryUnaryChildSearch!] => entryUnaryChildTypeId
                                    entryUnaryChildTypeId >= 0 -> if {
                                        entryUnaryChildTypeId => recursiveEntryExpressionTypeId!
                                        recursiveSemanticTypes![entryUnaryChildTypeId] => entryUnaryChildType
                                        entryUnaryChildType.origin => entryExpressionTypeOrigin!
                                        entryUnaryChildType.module => entryExpressionTypeModule!
                                        entryUnaryChildType.symbol => entryExpressionTypeSymbol!
                                        true => knownEntryNumericExpression!
                                    }
                                }
                                entryUnaryChildSearch! + 1 => entryUnaryChildSearch!
                            }
                        }
                        (entryExpression.kind == 18 or entryExpression.kind == 19 or entryExpression.kind == 24 or entryExpression.kind == 25 or (entryExpression.kind == 22 and entryExpression.operatorKind == -26)) => knownEntryBooleanExpression!
                        knownEntryBooleanExpression! -> if {
                            1 => entryExpressionTypeOrigin!
                            -1 => entryExpressionTypeModule!
                            23 => entryExpressionTypeSymbol!
                        }
                        false => knownEntryPushExpression!
                        false => knownEntryMapTextExpression!
                        false => knownEntryBorrowTextExpression!
                        entryExpression.kind == 53 => knownEntryIndexAssignment!
                        entryExpression.kind == 54 => knownEntryMemberAssignment!
                        entryExpression.kind == 10 -> if {
                            FlowIntrinsicRequest { source: source, tokens: prepared.package.tokens, tokenStart: sourceRange.tokenStart, node: entryExpression } -> flowIntrinsicOpcode => entryFlowOpcode
                            entryFlowOpcode == -204 -> if { true => knownEntryPushExpression! }
                            entryFlowOpcode == -205 -> if { true => knownEntryMapTextExpression! }
                            entryFlowOpcode == -206 -> if { true => knownEntryBorrowTextExpression! }
                        }
                        knownEntryPushExpression! -> if {
                            1 => entryExpressionTypeOrigin!
                            -1 => entryExpressionTypeModule!
                            0 => entryExpressionTypeSymbol!
                        }
                        knownEntryMapTextExpression! -> if {
                            1 => entryExpressionTypeOrigin!
                            -1 => entryExpressionTypeModule!
                            24 => entryExpressionTypeSymbol!
                        }
                        knownEntryBorrowTextExpression! -> if {
                            1 => entryExpressionTypeOrigin!
                            -1 => entryExpressionTypeModule!
                            24 => entryExpressionTypeSymbol!
                        }
                        entryExpressionBelongs! -> if {
                            (entryExpression.kind == 42 or entryExpression.kind == 43 or entryExpression.kind == 44 or entryExpression.kind == 45 or entryExpression.kind == 46 or entryExpression.kind == 47 or entryExpression.kind == 58) -> if {
                                results! -> len => entryControlIr
                                entryControlIr => entryAstToIr![entryExpressionAst!]
                                18 => entryControlKind!
                                entryExpression.kind == 43 -> if { 19 => entryControlKind! }
                                entryExpression.kind == 44 -> if { 20 => entryControlKind! }
                                entryExpression.kind == 45 -> if { 21 => entryControlKind! }
                                entryExpression.kind == 46 -> if { 22 => entryControlKind! }
                                entryExpression.kind == 47 -> if { 23 => entryControlKind! }
                                entryExpression.kind == 58 -> if { 19 => entryControlKind! }
                                -1 => entryControlOpcode!
                                entryExpression.kind == 45 -> if {
                                    0 => entryControlOpcode!
                                    (source -> byte(entryExpression.start)) == UInt8(99) -> if { 1 => entryControlOpcode! }
                                }
                                entryExpression.kind == 46 -> if { entryExpression.operatorKind => entryControlOpcode! }
                                entryExpression.kind == 58 -> if {
                                    0 => entryPatternSearch!
                                    entryPatternSearch! < sourceRange.astCount -> while {
                                        prepared.package.nodes[sourceRange.astStart + entryPatternSearch!] => entryPatternCandidate
                                        (entryPatternCandidate.kind == 59 and entryPatternCandidate.parent == entryExpressionAst!) -> if {
                                            prepared.package.tokens[sourceRange.tokenStart + entryPatternCandidate.payloadToken] => entryPatternVariant
                                            (source -> byte(entryPatternVariant.span.start)) == UInt8(69) -> if { 1 => entryControlOpcode! } else { 0 => entryControlOpcode! }
                                        }
                                        entryPatternSearch! + 1 => entryPatternSearch!
                                    }
                                }
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
                                (entryExpressionTypeIndex! >= 0 or recursiveEntryExpressionTypeId! >= 0 or knownEntryPatternReference! or entryExpression.kind == 41 or entryExpression.kind == 56 or (entryExpression.kind == 15 and entryExpression.parent >= 0 and prepared.package.nodes[sourceRange.astStart + entryExpression.parent].kind == 41) or knownEntryBooleanExpression! or knownEntryCallExpression! or knownEntryNumericExpression! or knownEntryPushExpression! or knownEntryMapTextExpression! or knownEntryBorrowTextExpression! or knownEntryIndexAssignment! or knownEntryMemberAssignment!) -> if {
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
                                entryExpression.kind == 53 -> if { 24 => entryExpressionKind! }
                                entryExpression.kind == 54 -> if { 25 => entryExpressionKind! }
                                entryExpression.kind == 55 -> if { 26 => entryExpressionKind! }
                                entryExpression.kind == 56 -> if { 27 => entryExpressionKind! }
                                entryExpression.kind == 59 -> if { 29 => entryExpressionKind! }
                                0 => entryPropertyCallSearch!
                                entryPropertyCallSearch! < (prepared.calls -> len) -> while {
                                    prepared.calls[entryPropertyCallSearch!] => entryPropertyCall
                                    (entryPropertyCall.sourceModule == sourceIndex! and entryPropertyCall.callAst == entryExpressionAst! and entryPropertyCall.status == 0) -> if { 6 => entryExpressionKind! }
                                    entryPropertyCallSearch! + 1 => entryPropertyCallSearch!
                                }
                                false => transparentEntryExpression!
                                (entryExpression.kind >= 18 and entryExpression.kind <= 21) -> if {
                                    0 => transparentEntryChildSearch!
                                    transparentEntryChildSearch! < sourceRange.astCount -> while {
                                        prepared.package.nodes[sourceRange.astStart + transparentEntryChildSearch!] => transparentEntryChild
                                        (transparentEntryChild.parent == entryExpressionAst! and transparentEntryChild.start == entryExpression.start and transparentEntryChild.length == entryExpression.length) -> if { true => transparentEntryExpression! }
                                        transparentEntryChildSearch! + 1 => transparentEntryChildSearch!
                                    }
                                }
                                not transparentEntryExpression! -> if {
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
                                            prepared.package.ranges[entryValueTargetSource] => entryValueTargetRange
                                            prepared.package.symbols[entryValueTargetRange.symbolStart + entryValuePath.targetSymbol] => entryValueTarget
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
                                        prepared.package.nodes[sourceRange.astStart + entryIntrinsicChildSearch!] => entryIntrinsicChild
                                        (entryIntrinsicChild.parent == entryExpressionAst! and entryIntrinsicChild.kind == 16) -> if {
                                            IntrinsicNameRequest { source: source, token: prepared.package.tokens[sourceRange.tokenStart + entryIntrinsicChild.payloadToken] } -> intrinsicOpcode => entryExpressionIntrinsicOpcode
                                            entryExpressionIntrinsicOpcode <= -201 -> if { entryExpressionIntrinsicOpcode => entryExpressionOpcode! }
                                        }
                                        entryIntrinsicChildSearch! + 1 => entryIntrinsicChildSearch!
                                    }
                                    knownEntryPushExpression! -> if { -204 => entryExpressionOpcode! }
                                    knownEntryMapTextExpression! -> if { -205 => entryExpressionOpcode! }
                                    knownEntryBorrowTextExpression! -> if { -206 => entryExpressionOpcode! }
                                }
                                entryExpression.kind == 48 -> if {
                                    entryExpression.firstToken => entryRoleTokenIndex!
                                    entryRoleTokenIndex! < entryExpression.firstToken + entryExpression.tokenCount -> while {
                                        prepared.package.tokens[sourceRange.tokenStart + entryRoleTokenIndex!] => entryRoleToken
                                        entryRoleToken.kind == grammar.tokenIdIdentifier -> if {
                                            IntrinsicNameRequest { source: source, token: entryRoleToken } -> intrinsicOpcode => entryRoleOpcode
                                            (entryRoleOpcode == -207 or entryRoleOpcode == -208 or entryRoleOpcode == -209) -> if { entryRoleOpcode => entryExpressionOpcode! }
                                        }
                                        entryRoleTokenIndex! + 1 => entryRoleTokenIndex!
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
                                entryExpressionKind! == 29 -> if {
                                    0 => entryPatternBindingSymbolSearch!
                                    entryPatternBindingSymbolSearch! < sourceRange.symbolCount -> while {
                                        prepared.package.symbols[sourceRange.symbolStart + entryPatternBindingSymbolSearch!] => entryPatternBindingSymbol
                                        (entryPatternBindingSymbol.kind == 35 and entryPatternBindingSymbol.astNode == entryExpressionAst!) -> if { entryPatternBindingSymbolSearch! => entryExpressionSymbol! }
                                        entryPatternBindingSymbolSearch! + 1 => entryPatternBindingSymbolSearch!
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
                                                prepared.package.ranges[entryResolvedCall.targetSourceModule] => entryExpressionTargetRange
                                                prepared.package.symbols[entryExpressionTargetRange.symbolStart + entryResolvedCall.functionSymbol] => entryExpressionTargetFunction
                                                ((entryExpressionTargetFunction.flags / 8) % 2 == 1 and (entryExpressionFlags! / 8) % 2 == 0) -> if {
                                                    entryExpressionFlags! + 8 => entryExpressionFlags!
                                                }
                                            } else {
                                                entryResolvedCall.functionSymbol == -115 -> if {
                                                    1 => entryExpressionTypeOrigin!
                                                    -1 => entryExpressionTypeModule!
                                                    2 => entryExpressionTypeSymbol!
                                                }
                                            }
                                        }
                                        entryCallSearch! + 1 => entryCallSearch!
                                    }
                                }
                                -1 => entryExpressionTypeKind!
                                0 => entryExpressionTypeFlags!
                                recursiveEntryExpressionTypeId! >= 0 -> if {
                                    recursiveSemanticTypes![recursiveEntryExpressionTypeId!].kind => entryExpressionTypeKind!
                                    recursiveTypeFlags![recursiveEntryExpressionTypeId!] => entryExpressionTypeFlags!
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
                                    typeKind: entryExpressionTypeKind!
                                    typeFlags: entryExpressionTypeFlags!
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
                        }
                        entryExpressionAst! + 1 => entryExpressionAst!
                    }
                    results! -> len => entryExpressionEnd
                    entryExpressionStart => entryParentIr!
                    entryParentIr! < entryExpressionEnd -> while {
                        results![entryParentIr!] => entryIrNode!
                        prepared.package.nodes[sourceRange.astStart + entryIrNode!.astNode].parent => entryParentAst!
                        -1 => entrySemanticParent!
                        (entryParentAst! >= 0 and entryParentAst! != entryAstIndex! and entrySemanticParent! < 0) -> while {
                            entryAstToIr![entryParentAst!] >= 0 -> if { entryAstToIr![entryParentAst!] => entrySemanticParent! } else { prepared.package.nodes[sourceRange.astStart + entryParentAst!].parent => entryParentAst! }
                        }
                        entrySemanticParent! >= 0 -> if { entrySemanticParent! => entryIrNode!.parent }
                        entryIrNode! => results![entryParentIr!]
                        entryParentIr! + 1 => entryParentIr!
                    }
                    entryExpressionStart => entryOperandIr!
                    entryOperandIr! < entryExpressionEnd -> while {
                        results![entryOperandIr!] => entryOperator!
                        (entryOperator!.kind == 6 or entryOperator!.kind == 7 or entryOperator!.kind == 8 or (entryOperator!.kind == 9 and entryOperator!.opcode <= -201 and (entryOperator!.opcode >= -206 or entryOperator!.opcode == -213)) or entryOperator!.kind == 13 or entryOperator!.kind == 15 or entryOperator!.kind == 17 or entryOperator!.kind == 22 or entryOperator!.kind == 23 or entryOperator!.kind == 24 or entryOperator!.kind == 25 or entryOperator!.kind == 26) -> if {
                            -1 => entryFirstOperand!
                            -1 => entrySecondOperand!
                            UIntSize(0) => entryFirstStart!
                            entryExpressionStart => entryChildIr!
                            entryChildIr! < entryExpressionEnd -> while {
                                results![entryChildIr!] => entryChild
                                entryChild.parent == entryOperandIr! -> if {
                                    prepared.package.nodes[sourceRange.astStart + entryChild.astNode].start => entryChildStart
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
                            (entryOperator!.kind == 6 or entryOperator!.kind == 8 or (entryOperator!.kind == 9 and entryOperator!.opcode <= -201 and (entryOperator!.opcode >= -206 or entryOperator!.opcode == -213)) or entryOperator!.kind == 15 or entryOperator!.kind == 24) -> if { entrySecondOperand! => entryOperator!.operand1 }
                            entryOperator!.kind == 22 -> if {
                                -1 => entryOperator!.operand0
                                entryFirstOperand! => entryOperator!.operand1
                            }
                            entryOperator! => results![entryOperandIr!]
                        }
                        entryOperandIr! + 1 => entryOperandIr!
                    }

                    entryExpressionStart => entryCanonicalIndexResultIr!
                    entryCanonicalIndexResultIr! < entryExpressionEnd -> while {
                        results![entryCanonicalIndexResultIr!] => entryCanonicalIndexResult!
                        (entryCanonicalIndexResult!.kind == 15 and entryCanonicalIndexResult!.operand1 < 0 and entryCanonicalIndexResult!.operand0 >= entryExpressionStart and results![entryCanonicalIndexResult!.operand0].kind == 26) -> if {
                            results![entryCanonicalIndexResult!.operand0] => entryWrappedConstructor
                            9 => entryCanonicalIndexResult!.kind
                            -1 => entryCanonicalIndexResult!.opcode
                            entryWrappedConstructor.typeOrigin => entryCanonicalIndexResult!.typeOrigin
                            entryWrappedConstructor.typeModule => entryCanonicalIndexResult!.typeModule
                            entryWrappedConstructor.typeSymbol => entryCanonicalIndexResult!.typeSymbol
                            entryWrappedConstructor.typeId => entryCanonicalIndexResult!.typeId
                            entryWrappedConstructor.typeKind => entryCanonicalIndexResult!.typeKind
                            entryWrappedConstructor.typeFlags => entryCanonicalIndexResult!.typeFlags
                            entryCanonicalIndexResult! => results![entryCanonicalIndexResultIr!]
                        }
                        (entryCanonicalIndexResult!.kind == 15 and entryCanonicalIndexResult!.typeId < 0 and entryCanonicalIndexResult!.operand0 >= entryExpressionStart) -> if {
                            results![entryCanonicalIndexResult!.operand0] => entryCanonicalIndexedValue
                            entryCanonicalIndexedValue.typeId >= 0 -> if {
                                recursiveSemanticTypes![entryCanonicalIndexedValue.typeId] => entryCanonicalContainerType
                                -1 => entryCanonicalElementTypeId!
                                (entryCanonicalContainerType.kind == 2 or entryCanonicalContainerType.kind == 3 or entryCanonicalContainerType.kind == 4) -> if { entryCanonicalContainerType.first => entryCanonicalElementTypeId! }
                                entryCanonicalContainerType.kind == 5 -> if { entryCanonicalContainerType.second => entryCanonicalElementTypeId! }
                                entryCanonicalElementTypeId! >= 0 -> if {
                                    recursiveSemanticTypes![entryCanonicalElementTypeId!] => entryCanonicalElementType
                                    entryCanonicalElementTypeId! => entryCanonicalIndexResult!.typeId
                                    entryCanonicalElementType.kind => entryCanonicalIndexResult!.typeKind
                                    recursiveTypeFlags![entryCanonicalElementTypeId!] => entryCanonicalIndexResult!.typeFlags
                                    entryCanonicalElementType.origin => entryCanonicalIndexResult!.typeOrigin
                                    entryCanonicalElementType.module => entryCanonicalIndexResult!.typeModule
                                    entryCanonicalElementType.symbol => entryCanonicalIndexResult!.typeSymbol
                                    (entryCanonicalElementType.kind >= 2 and entryCanonicalElementType.kind <= 6) -> if { 10 + entryCanonicalElementType.kind => entryCanonicalIndexResult!.typeOrigin }
                                    (entryCanonicalElementType.kind == 2 or entryCanonicalElementType.kind == 3 or entryCanonicalElementType.kind == 4 or entryCanonicalElementType.kind == 6) -> if {
                                        entryCanonicalElementType.first >= 0 -> if {
                                            recursiveSemanticTypes![entryCanonicalElementType.first].module => entryCanonicalIndexResult!.typeModule
                                            recursiveSemanticTypes![entryCanonicalElementType.first].symbol => entryCanonicalIndexResult!.typeSymbol
                                        }
                                    }
                                    entryCanonicalElementType.kind == 5 -> if {
                                        entryCanonicalElementType.first >= 0 -> if { recursiveSemanticTypes![entryCanonicalElementType.first].symbol => entryCanonicalIndexResult!.typeModule }
                                        entryCanonicalElementType.second >= 0 -> if { recursiveSemanticTypes![entryCanonicalElementType.second].symbol => entryCanonicalIndexResult!.typeSymbol }
                                    }
                                    entryCanonicalIndexResult! => results![entryCanonicalIndexResultIr!]
                                }
                            }
                        }
                        entryCanonicalIndexResultIr! + 1 => entryCanonicalIndexResultIr!
                    }
                    entryExpressionStart => entrySiblingIr!
                    entrySiblingIr! < entryExpressionEnd -> while {
                        results![entrySiblingIr!] => entrySibling!
                        -1 => entryNextSibling!
                        UIntSize(0) => entryNextSiblingStart!
                        entryExpressionStart => entrySiblingSearch!
                        entrySiblingSearch! < entryExpressionEnd -> while {
                            results![entrySiblingSearch!] => entrySiblingCandidate
                            (entrySiblingCandidate.parent == entrySibling!.parent and prepared.package.nodes[sourceRange.astStart + entrySiblingCandidate.astNode].start > prepared.package.nodes[sourceRange.astStart + entrySibling!.astNode].start) -> if {
                                prepared.package.nodes[sourceRange.astStart + entrySiblingCandidate.astNode].start => entrySiblingCandidateStart
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
                                    prepared.package.nodes[sourceRange.astStart + results![entryRegionChildSearch!].astNode].start => entryRegionChildStart
                                    (entryFirstRegionChild! < 0 or entryRegionChildStart < entryFirstRegionChildStart!) -> if {
                                        entryRegionChildSearch! => entryFirstRegionChild!
                                        entryRegionChildStart => entryFirstRegionChildStart!
                                    }
                                    (entryLastRegionChild! < 0 or entryRegionChildStart > entryLastRegionChildStart! or (entryRegionChildStart == entryLastRegionChildStart! and (results![entryRegionChildSearch!].kind == 18 or results![entryRegionChildSearch!].kind == 20) and results![entryLastRegionChild!].kind != 18 and results![entryLastRegionChild!].kind != 20)) -> if {
                                        entryRegionChildSearch! => entryLastRegionChild!
                                        entryRegionChildStart => entryLastRegionChildStart!
                                    }
                                }
                                entryRegionChildSearch! + 1 => entryRegionChildSearch!
                            }
                            entryFirstRegionChild! => entryControl!.operand0
                            (entryLastRegionChild! >= 0 and results![entryLastRegionChild!].kind == 6 and results![entryLastRegionChild!].operand1 >= entryExpressionStart and results![results![entryLastRegionChild!].operand1].parent == entryControlIrIndex!) -> if {
                                results![entryLastRegionChild!].operand1 => entryLastRegionChild!
                            }
                            true => entryNestedRegionUnwrap!
                            (entryLastRegionChild! >= 0 and entryNestedRegionUnwrap! and results![entryLastRegionChild!].kind == 9) -> while {
                                -1 => entryNestedRegionResult!
                                UIntSize(0) => entryNestedRegionResultStart!
                                entryExpressionStart => entryNestedResultSearch!
                                entryNestedResultSearch! < entryExpressionEnd -> while {
                                    results![entryNestedResultSearch!].parent == entryLastRegionChild! -> if {
                                        prepared.package.nodes[sourceRange.astStart + results![entryNestedResultSearch!].astNode].start => entryNestedResultStart
                                        (entryNestedRegionResult! < 0 or entryNestedResultStart > entryNestedRegionResultStart!) -> if {
                                            entryNestedResultSearch! => entryNestedRegionResult!
                                            entryNestedResultStart => entryNestedRegionResultStart!
                                        }
                                    }
                                    entryNestedResultSearch! + 1 => entryNestedResultSearch!
                                }
                                entryNestedRegionResult! >= 0 -> if { entryNestedRegionResult! => entryLastRegionChild! } else { false => entryNestedRegionUnwrap! }
                            }
                            entryLastRegionChild! => entryControl!.operand1
                            entryControl! => results![entryControlIrIndex!]
                        }
                        entryControl!.kind == 27 -> if {
                            prepared.package.nodes[sourceRange.astStart + entryControl!.astNode].parent => entryMatchFlowAst
                            -1 => entryMatchSubject!
                            UIntSize(0) => entryMatchSubjectStart!
                            -1 => entryMatchFirstArm!
                            entryExpressionStart => entryMatchSearch!
                            entryMatchSearch! < entryExpressionEnd -> while {
                                results![entryMatchSearch!] => entryMatchCandidate
                                prepared.package.nodes[sourceRange.astStart + entryMatchCandidate.astNode] => entryMatchCandidateAst
                                (entryMatchCandidateAst.parent == entryMatchFlowAst and entryMatchCandidateAst.start < prepared.package.nodes[sourceRange.astStart + entryControl!.astNode].start and entryMatchCandidate.typeKind == 7) -> if {
                                    (entryMatchSubject! < 0 or entryMatchCandidateAst.start > entryMatchSubjectStart!) -> if {
                                        entryMatchSearch! => entryMatchSubject!
                                        entryMatchCandidateAst.start => entryMatchSubjectStart!
                                    }
                                }
                                (entryMatchCandidate.kind == 19 and entryMatchCandidate.parent == entryControlIrIndex! and entryMatchFirstArm! < 0) -> if { entryMatchSearch! => entryMatchFirstArm! }
                                entryMatchSearch! + 1 => entryMatchSearch!
                            }
                            entryMatchSubject! => entryControl!.operand0
                            entryMatchFirstArm! => entryControl!.operand1
                            entryControl! => results![entryControlIrIndex!]
                        }
                        (entryControl!.kind == 18 or entryControl!.kind == 20) -> if {
                            prepared.package.nodes[sourceRange.astStart + entryControl!.astNode].parent => entryControlFlowAst
                            -1 => entryConditionIr!
                            UIntSize(0) => entryConditionStart!
                            1000000 => entryConditionDistance!
                            entryExpressionStart => entryConditionSearch!
                            entryConditionSearch! < entryExpressionEnd -> while {
                                results![entryConditionSearch!] => entryConditionCandidate
                                prepared.package.nodes[sourceRange.astStart + entryConditionCandidate.astNode].parent => entryConditionAncestor!
                                1 => entryCandidateConditionDistance!
                                false => entryConditionBelongsToFlow!
                                (entryConditionAncestor! >= 0 and not entryConditionBelongsToFlow!) -> while {
                                    entryConditionAncestor! == entryControlFlowAst -> if { true => entryConditionBelongsToFlow! } else {
                                        prepared.package.nodes[sourceRange.astStart + entryConditionAncestor!].parent => entryConditionAncestor!
                                        entryCandidateConditionDistance! + 1 => entryCandidateConditionDistance!
                                    }
                                }
                                (entryConditionBelongsToFlow! and ((entryConditionCandidate.typeOrigin == 1 and entryConditionCandidate.typeSymbol == 23) or (entryConditionCandidate.typeId >= 0 and recursiveSemanticTypes![entryConditionCandidate.typeId].origin == 1 and recursiveSemanticTypes![entryConditionCandidate.typeId].symbol == 23)) and prepared.package.nodes[sourceRange.astStart + entryConditionCandidate.astNode].start < prepared.package.nodes[sourceRange.astStart + entryControl!.astNode].start) -> if {
                                    prepared.package.nodes[sourceRange.astStart + entryConditionCandidate.astNode].start => entryCandidateStart
                                    (entryConditionIr! < 0 or entryCandidateConditionDistance! < entryConditionDistance! or (entryCandidateConditionDistance! == entryConditionDistance! and entryCandidateStart > entryConditionStart!)) -> if {
                                        entryConditionSearch! => entryConditionIr!
                                        entryCandidateStart => entryConditionStart!
                                        entryCandidateConditionDistance! => entryConditionDistance!
                                    }
                                }
                                entryConditionSearch! + 1 => entryConditionSearch!
                            }
                            (entryConditionIr! < 0 and entryControl!.parent >= entryExpressionStart and results![entryControl!.parent].kind != 19 and ((results![entryControl!.parent].typeOrigin == 1 and results![entryControl!.parent].typeSymbol == 23) or (results![entryControl!.parent].typeId >= 0 and recursiveSemanticTypes![results![entryControl!.parent].typeId].origin == 1 and recursiveSemanticTypes![results![entryControl!.parent].typeId].symbol == 23))) -> if {
                                entryControl!.parent => entryEnclosingConditionIr!
                                results![entryEnclosingConditionIr!].parent => entryControl!.parent
                                entryEnclosingConditionIr! => entryConditionIr!
                            }
                            true => entryConditionUnwrap!
                            (entryConditionIr! >= 0 and entryConditionUnwrap! and results![entryConditionIr!].kind == 9) -> while {
                                -1 => entryNestedConditionResult!
                                UIntSize(0) => entryNestedConditionStart!
                                entryExpressionStart => entryNestedConditionSearch!
                                entryNestedConditionSearch! < entryExpressionEnd -> while {
                                    results![entryNestedConditionSearch!].parent == entryConditionIr! -> if {
                                        prepared.package.nodes[sourceRange.astStart + results![entryNestedConditionSearch!].astNode].start => entryNestedCandidateStart
                                        (entryNestedConditionResult! < 0 or entryNestedCandidateStart > entryNestedConditionStart!) -> if {
                                            entryNestedConditionSearch! => entryNestedConditionResult!
                                            entryNestedCandidateStart => entryNestedConditionStart!
                                        }
                                    }
                                    entryNestedConditionSearch! + 1 => entryNestedConditionSearch!
                                }
                                entryNestedConditionResult! >= 0 -> if { entryNestedConditionResult! => entryConditionIr! } else { false => entryConditionUnwrap! }
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

                    entryExpressionEnd - 1 => entryControlTypeIndex!
                    entryControlTypeIndex! >= entryExpressionStart -> while {
                        results![entryControlTypeIndex!] => entryControlTypeNode!
                        (entryControlTypeNode!.kind == 19 and entryControlTypeNode!.operand1 >= entryExpressionStart) -> if {
                            false => entryControlRegionRedirected!
                            (results![entryControlTypeNode!.operand1].kind == 6 and results![entryControlTypeNode!.operand1].operand1 >= entryExpressionStart and results![results![entryControlTypeNode!.operand1].operand1].parent == entryControlTypeIndex!) -> if {
                                results![entryControlTypeNode!.operand1].operand1 => entryControlTypeNode!.operand1
                                true => entryControlRegionRedirected!
                            }
                        entryControlRegionRedirected! -> if {
                                results![entryControlTypeNode!.operand1] => entryControlRegionValue
                                entryControlRegionValue.typeOrigin => entryControlTypeNode!.typeOrigin
                                entryControlRegionValue.typeModule => entryControlTypeNode!.typeModule
                                entryControlRegionValue.typeSymbol => entryControlTypeNode!.typeSymbol
                                entryControlRegionValue.typeId => entryControlTypeNode!.typeId
                                entryControlRegionValue.typeKind => entryControlTypeNode!.typeKind
                                entryControlRegionValue.typeFlags => entryControlTypeNode!.typeFlags
                            entryControlTypeNode! => results![entryControlTypeIndex!]
                        }
                        results![entryControlTypeNode!.operand1] => entryControlRegionResult
                        not (entryControlRegionResult.typeOrigin == 1 and entryControlRegionResult.typeSymbol == 0) -> if {
                            entryControlRegionResult.typeOrigin => entryControlTypeNode!.typeOrigin
                            entryControlRegionResult.typeModule => entryControlTypeNode!.typeModule
                            entryControlRegionResult.typeSymbol => entryControlTypeNode!.typeSymbol
                            entryControlRegionResult.typeId => entryControlTypeNode!.typeId
                            entryControlRegionResult.typeKind => entryControlTypeNode!.typeKind
                            entryControlRegionResult.typeFlags => entryControlTypeNode!.typeFlags
                            entryControlTypeNode! => results![entryControlTypeIndex!]
                        }
                        }
                        (entryControlTypeNode!.kind == 27 and entryControlTypeNode!.operand1 >= entryExpressionStart) -> if {
                            results![entryControlTypeNode!.operand1] => entryMatchResultArm
                            entryMatchResultArm.typeOrigin => entryControlTypeNode!.typeOrigin
                            entryMatchResultArm.typeModule => entryControlTypeNode!.typeModule
                            entryMatchResultArm.typeSymbol => entryControlTypeNode!.typeSymbol
                            entryMatchResultArm.typeId => entryControlTypeNode!.typeId
                            entryMatchResultArm.typeKind => entryControlTypeNode!.typeKind
                            entryMatchResultArm.typeFlags => entryControlTypeNode!.typeFlags
                            entryControlTypeNode! => results![entryControlTypeIndex!]
                        }
                        false => entryControlHasNestedResult!
                        (entryControlTypeNode!.kind == 18 and entryControlTypeNode!.operand1 >= entryExpressionStart and entryControlTypeNode!.nextOperand >= entryExpressionStart) -> if {
                            results![entryControlTypeNode!.operand1] => entryControlThenRegionNode
                            results![entryControlTypeNode!.nextOperand] => entryControlElseRegionNode
                            (entryControlThenRegionNode.operand1 >= entryExpressionStart and results![entryControlThenRegionNode.operand1].kind == 18) -> if { true => entryControlHasNestedResult! }
                            (entryControlElseRegionNode.operand1 >= entryExpressionStart and results![entryControlElseRegionNode.operand1].kind == 18) -> if { true => entryControlHasNestedResult! }
                        }
                        (entryControlTypeNode!.kind == 18 and entryControlTypeNode!.typeOrigin == 1 and entryControlTypeNode!.typeSymbol == 0 and entryControlTypeNode!.operand1 >= entryExpressionStart and entryControlTypeNode!.nextOperand >= entryExpressionStart) -> if {
                            results![entryControlTypeNode!.operand1] => entryControlThenType
                            results![entryControlTypeNode!.nextOperand] => entryControlElseType
                            (entryControlThenType.typeOrigin == entryControlElseType.typeOrigin and entryControlThenType.typeModule == entryControlElseType.typeModule and entryControlThenType.typeSymbol == entryControlElseType.typeSymbol) -> if {
                                entryControlThenType.typeOrigin => entryControlTypeNode!.typeOrigin
                                entryControlThenType.typeModule => entryControlTypeNode!.typeModule
                                entryControlThenType.typeSymbol => entryControlTypeNode!.typeSymbol
                                entryControlThenType.typeId => entryControlTypeNode!.typeId
                                entryControlThenType.typeKind => entryControlTypeNode!.typeKind
                                entryControlThenType.typeFlags => entryControlTypeNode!.typeFlags
                                entryControlTypeNode! => results![entryControlTypeIndex!]
                            }
                        }
                        entryControlTypeIndex! - 1 => entryControlTypeIndex!
                    }

                    entryExpressionStart => entryBindingControlResultIndex!
                    entryBindingControlResultIndex! < entryExpressionEnd -> while {
                        results![entryBindingControlResultIndex!] => entryBindingControlResult!
                        (entryBindingControlResult!.kind == 17 and entryBindingControlResult!.operand0 >= entryExpressionStart and results![entryBindingControlResult!.operand0].kind == 9) -> if {
                            entryBindingControlResult!.operand0 => entryBindingControlWrapper
                            entryExpressionStart => entryBindingControlSearch!
                            entryBindingControlSearch! < entryExpressionEnd -> while {
                                (results![entryBindingControlSearch!].kind == 18 and results![entryBindingControlSearch!].parent == entryBindingControlWrapper) -> if {
                                    entryBindingControlSearch! => entryBindingControlResult!.operand0
                                }
                                entryBindingControlSearch! + 1 => entryBindingControlSearch!
                            }
                            entryBindingControlResult! => results![entryBindingControlResultIndex!]
                        }
                        entryBindingControlResultIndex! + 1 => entryBindingControlResultIndex!
                    }

                    # Entry bodies need the same role-result edge repair as
                    # ordinary functions. The role call and its `=>` binding
                    # share one AST node, so the generic child pass cannot
                    # discover this operand.
                    entryExpressionStart => entryRoleBindingIndex!
                    entryRoleBindingIndex! < entryExpressionEnd -> while {
                        results![entryRoleBindingIndex!] => entryRoleBinding!
                        (entryRoleBinding!.kind == 17 and entryRoleBinding!.operand0 < 0 and prepared.package.nodes[sourceRange.astStart + entryRoleBinding!.astNode].kind == 48) -> if {
                            entryExpressionStart => entryRoleCallSearch!
                            entryRoleCallSearch! < entryExpressionEnd -> while {
                                (results![entryRoleCallSearch!].kind == 6 and results![entryRoleCallSearch!].astNode == entryRoleBinding!.astNode and (results![entryRoleCallSearch!].opcode == -207 or results![entryRoleCallSearch!].opcode == -209)) -> if { entryRoleCallSearch! => entryRoleBinding!.operand0 }
                                entryRoleCallSearch! + 1 => entryRoleCallSearch!
                            }
                            entryRoleBinding! => results![entryRoleBindingIndex!]
                        }
                        entryRoleBindingIndex! + 1 => entryRoleBindingIndex!
                    }

                    entryExpressionStart => entryBindingNameIr!
                    entryBindingNameIr! < entryExpressionEnd -> while {
                        results![entryBindingNameIr!] => entryBindingName!
                        entryBindingName!.kind == 5 -> if {
                            entryExpressionStart => entryBindingDefinitionSearch!
                            entryBindingDefinitionSearch! < entryExpressionEnd -> while {
                                ((results![entryBindingDefinitionSearch!].kind == 17 or results![entryBindingDefinitionSearch!].kind == 29) and results![entryBindingDefinitionSearch!].symbol == entryBindingName!.symbol) -> if {
                                    entryBindingDefinitionSearch! => entryBindingName!.operand0
                                }
                                entryBindingDefinitionSearch! + 1 => entryBindingDefinitionSearch!
                            }
                            entryBindingName! => results![entryBindingNameIr!]
                        }
                        entryBindingNameIr! + 1 => entryBindingNameIr!
                    }

                    entryExpressionStart => entryMemberAssignmentIr!
                    entryMemberAssignmentIr! < entryExpressionEnd -> while {
                        results![entryMemberAssignmentIr!] => entryMemberAssignment!
                        entryMemberAssignment!.kind == 25 -> if {
                            prepared.package.nodes[sourceRange.astStart + entryMemberAssignment!.astNode] => entryMemberAssignmentAst
                            prepared.package.tokens[sourceRange.tokenStart + entryMemberAssignmentAst.payloadToken] => entryMemberOwnerName
                            -1 => entryMemberOwnerBinding!
                            UIntSize(0) => entryMemberOwnerStart!
                            entryExpressionStart => entryMemberOwnerSearch!
                            entryMemberOwnerSearch! < entryMemberAssignmentIr! -> while {
                                results![entryMemberOwnerSearch!] => entryMemberOwnerCandidate
                                (entryMemberOwnerCandidate.kind == 17 and entryMemberOwnerCandidate.flags == 1 and entryMemberOwnerCandidate.payloadToken >= 0) -> if {
                                    prepared.package.tokens[sourceRange.tokenStart + entryMemberOwnerCandidate.payloadToken] => entryMemberCandidateName
                                    entryMemberOwnerName.span.length == entryMemberCandidateName.span.length => entryMemberOwnerEqual!
                                    UIntSize(0) => entryMemberOwnerByte!
                                    (entryMemberOwnerEqual! and entryMemberOwnerByte! < entryMemberOwnerName.span.length) -> while {
                                        (source -> byte(entryMemberOwnerName.span.start + entryMemberOwnerByte!)) != (source -> byte(entryMemberCandidateName.span.start + entryMemberOwnerByte!)) -> if { false => entryMemberOwnerEqual! }
                                        entryMemberOwnerByte! + UIntSize(1) => entryMemberOwnerByte!
                                    }
                                    entryMemberOwnerEqual! -> if {
                                        prepared.package.nodes[sourceRange.astStart + entryMemberOwnerCandidate.astNode].start => entryMemberCandidateStart
                                        (entryMemberOwnerBinding! < 0 or entryMemberCandidateStart > entryMemberOwnerStart!) -> if {
                                            entryMemberOwnerSearch! => entryMemberOwnerBinding!
                                            entryMemberCandidateStart => entryMemberOwnerStart!
                                        }
                                    }
                                }
                                entryMemberOwnerSearch! + 1 => entryMemberOwnerSearch!
                            }
                            entryMemberOwnerBinding! >= 0 -> if {
                                entryMemberOwnerBinding! => entryMemberAssignment!.operand1
                                results![entryMemberOwnerBinding!].symbol => entryMemberAssignment!.symbol
                                entryMemberAssignment! => results![entryMemberAssignmentIr!]
                            }
                        }
                        entryMemberAssignmentIr! + 1 => entryMemberAssignmentIr!
                    }

                    entryExpressionStart => entryCanonicalBindingIr!
                    entryCanonicalBindingIr! < entryExpressionEnd -> while {
                        results![entryCanonicalBindingIr!] => entryCanonicalBinding!
                        (entryCanonicalBinding!.kind == 17 and entryCanonicalBinding!.operand0 >= entryExpressionStart and results![entryCanonicalBinding!.operand0].kind == 9 and results![entryCanonicalBinding!.operand0].opcode == -1 and results![entryCanonicalBinding!.operand0].operand0 >= entryExpressionStart) -> if {
                            results![entryCanonicalBinding!.operand0].operand0 => entryCanonicalBinding!.operand0
                        }
                        (entryCanonicalBinding!.kind == 17 and entryCanonicalBinding!.operand0 >= entryExpressionStart and results![entryCanonicalBinding!.operand0].typeId >= 0) -> if {
                            results![entryCanonicalBinding!.operand0] => entryCanonicalBindingValue
                            entryCanonicalBindingValue.typeOrigin => entryCanonicalBinding!.typeOrigin
                            entryCanonicalBindingValue.typeModule => entryCanonicalBinding!.typeModule
                            entryCanonicalBindingValue.typeSymbol => entryCanonicalBinding!.typeSymbol
                            entryCanonicalBindingValue.typeId => entryCanonicalBinding!.typeId
                            entryCanonicalBindingValue.typeKind => entryCanonicalBinding!.typeKind
                            entryCanonicalBindingValue.typeFlags => entryCanonicalBinding!.typeFlags
                            entryCanonicalBinding! => results![entryCanonicalBindingIr!]
                        }
                        entryCanonicalBindingIr! + 1 => entryCanonicalBindingIr!
                    }
                    entryExpressionStart => entryCanonicalBindingReadIr!
                    entryCanonicalBindingReadIr! < entryExpressionEnd -> while {
                        results![entryCanonicalBindingReadIr!] => entryCanonicalBindingRead!
                        (entryCanonicalBindingRead!.kind == 5 and entryCanonicalBindingRead!.operand0 >= entryExpressionStart and results![entryCanonicalBindingRead!.operand0].typeId >= 0) -> if {
                            results![entryCanonicalBindingRead!.operand0] => entryCanonicalReadBinding
                            entryCanonicalReadBinding.typeOrigin => entryCanonicalBindingRead!.typeOrigin
                            entryCanonicalReadBinding.typeModule => entryCanonicalBindingRead!.typeModule
                            entryCanonicalReadBinding.typeSymbol => entryCanonicalBindingRead!.typeSymbol
                            entryCanonicalReadBinding.typeId => entryCanonicalBindingRead!.typeId
                            entryCanonicalReadBinding.typeKind => entryCanonicalBindingRead!.typeKind
                            entryCanonicalReadBinding.typeFlags => entryCanonicalBindingRead!.typeFlags
                            entryCanonicalBindingRead! => results![entryCanonicalBindingReadIr!]
                        }
                        entryCanonicalBindingReadIr! + 1 => entryCanonicalBindingReadIr!
                    }
                    entryExpressionStart => entryAggregateIrIndex!
                    entryAggregateIrIndex! < entryExpressionEnd -> while {
                        results![entryAggregateIrIndex!] => entryAggregate!
                        (entryAggregate!.kind == 12 or entryAggregate!.kind == 14 or entryAggregate!.kind == 16) -> if {
                            -1 => entryFirstFieldOperand!
                            entryExpressionStart => entryFieldOperandSearch!
                            entryFieldOperandSearch! < entryExpressionEnd -> while {
                                results![entryFieldOperandSearch!].parent == entryAggregateIrIndex! -> if {
                                    (entryFirstFieldOperand! < 0 or prepared.package.nodes[sourceRange.astStart + results![entryFieldOperandSearch!].astNode].start < prepared.package.nodes[sourceRange.astStart + results![entryFirstFieldOperand!].astNode].start) -> if { entryFieldOperandSearch! => entryFirstFieldOperand! }
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
                    entryResultIr! < 0 -> if {
                        1000000 => entryResultOperandDistance!
                        entryExpressionStart => entryResultOperandSearch!
                        entryResultOperandSearch! < entryExpressionEnd -> while {
                            prepared.package.nodes[sourceRange.astStart + results![entryResultOperandSearch!].astNode].parent => entryResultOperandAncestor!
                            1 => entryResultCandidateDistance!
                            false => entryResultCandidateBelongs!
                            (entryResultOperandAncestor! >= 0 and not entryResultCandidateBelongs!) -> while {
                                entryResultOperandAncestor! == inferred![entryResultTypeIndex!].astNode -> if { true => entryResultCandidateBelongs! } else {
                                    prepared.package.nodes[sourceRange.astStart + entryResultOperandAncestor!].parent => entryResultOperandAncestor!
                                    entryResultCandidateDistance! + 1 => entryResultCandidateDistance!
                                }
                            }
                            (entryResultCandidateBelongs! and entryResultCandidateDistance! < entryResultOperandDistance!) -> if {
                                entryResultOperandSearch! => entryResultIr!
                                entryResultCandidateDistance! => entryResultOperandDistance!
                            }
                            entryResultOperandSearch! + 1 => entryResultOperandSearch!
                        }
                    }
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
        SourceTypedIr { nodes: results! } => sourceResult!
        sourceResult!
    }

    prepared -> expressionTypeIds.resolveContext => recursiveTypes
    [typeIds.SemanticType; ~] => recursiveSemanticTypes!
    0 => recursiveSemanticTypeCopyIndex!
    recursiveSemanticTypeCopyIndex! < (recursiveTypes.types -> len) -> while {
        recursiveTypes.types[recursiveSemanticTypeCopyIndex!] => recursiveSemanticType
        recursiveSemanticTypes! -> push(recursiveSemanticType)
        recursiveSemanticTypeCopyIndex! + 1 => recursiveSemanticTypeCopyIndex!
    }
    [typeIds.SemanticType; ~] => classificationTypes!
    0 => classificationTypeCopyIndex152!
    classificationTypeCopyIndex152! < (recursiveSemanticTypes! -> len) -> while {
        recursiveSemanticTypes![classificationTypeCopyIndex152!] => classificationType
        classificationTypes! -> push(classificationType)
        classificationTypeCopyIndex152! + 1 => classificationTypeCopyIndex152!
    }
    [typeIds.NominalField; ~] => classificationFields!
    0 => classificationFieldCopyIndex154!
    classificationFieldCopyIndex154! < (recursiveTypes.fields -> len) -> while {
        recursiveTypes.fields[classificationFieldCopyIndex154!] => classificationField
        classificationFields! -> push(classificationField)
        classificationFieldCopyIndex154! + 1 => classificationFieldCopyIndex154!
    }
    typeIds.TypeClassificationRequest { types: classificationTypes!, fields: classificationFields! } => classificationRequest!
    classificationRequest! -> typeIds.classify => recursiveTypeFlags!
    [typeIds.TypeReference; ~] => recursiveReferences!
    0 => recursiveReferenceCopyIndex!
    recursiveReferenceCopyIndex! < (recursiveTypes.references -> len) -> while {
        recursiveTypes.references[recursiveReferenceCopyIndex!] => recursiveReference
        recursiveReferences! -> push(recursiveReference)
        recursiveReferenceCopyIndex! + 1 => recursiveReferenceCopyIndex!
    }
    [expressionTypeIds.ExpressionTypeId; ~] => recursiveExpressions!
    0 => recursiveExpressionCopyIndex!
    recursiveExpressionCopyIndex! < (recursiveTypes.expressions -> len) -> while {
        recursiveTypes.expressions[recursiveExpressionCopyIndex!] => recursiveExpression
        recursiveExpressions! -> push(recursiveExpression)
        recursiveExpressionCopyIndex! + 1 => recursiveExpressionCopyIndex!
    }
    [Int; ~] => recursiveTypeByAst!
    0 => recursiveTypeSeed!
    recursiveTypeSeed! < (prepared.package.nodes -> len) -> while {
        recursiveTypeByAst! -> push(-1)
        recursiveTypeSeed! + 1 => recursiveTypeSeed!
    }
    0 => recursiveTypeMapIndex!
    recursiveTypeMapIndex! < (recursiveExpressions! -> len) -> while {
        recursiveExpressions![recursiveTypeMapIndex!] => mappedExpression
        (mappedExpression.status == 0 and mappedExpression.sourceModule >= 0 and mappedExpression.sourceModule < (prepared.package.ranges -> len)) -> if {
            prepared.package.ranges[mappedExpression.sourceModule] => mappedRange
            (mappedExpression.astNode >= 0 and mappedExpression.astNode < mappedRange.astCount) -> if {
                mappedExpression.typeId => recursiveTypeByAst![mappedRange.astStart + mappedExpression.astNode]
            }
        }
        recursiveTypeMapIndex! + 1 => recursiveTypeMapIndex!
    }
    recursiveTypes -> legacyTypes => inferred!
    [Int; ~] => inferredIndexByAst!
    0 => inferredIndexSeed!
    inferredIndexSeed! < (prepared.package.nodes -> len) -> while {
        inferredIndexByAst! -> push(-1)
        inferredIndexSeed! + 1 => inferredIndexSeed!
    }
    0 => inferredMapIndex!
    inferredMapIndex! < (inferred! -> len) -> while {
        inferred![inferredMapIndex!] => mappedLegacy
        (mappedLegacy.sourceModule >= 0 and mappedLegacy.sourceModule < (prepared.package.ranges -> len)) -> if {
            prepared.package.ranges[mappedLegacy.sourceModule] => mappedLegacyRange
            (mappedLegacy.astNode >= 0 and mappedLegacy.astNode < mappedLegacyRange.astCount) -> if {
                inferredMapIndex! => inferredIndexByAst![mappedLegacyRange.astStart + mappedLegacy.astNode]
            }
        }
        inferredMapIndex! + 1 => inferredMapIndex!
    }
    [Int; ~] => sourceIndices!
    0 => sourceIndex!
    sourceIndex! < (prepared.package.sources -> len) -> while {
        sourceIndices! -> push(sourceIndex!)
        sourceIndex! + 1 => sourceIndex!
    }
    # Function lowering owns the active parallel region. Keeping source
    # lowering parallel here would nest the function pool inside a source
    # worker, causing the runtime to serialize the inner work for safety.
    [TypedIrNode; ~] => results!
    0 => sourceLowerIndex!
    sourceLowerIndex! < (sourceIndices! -> len) -> while {
        sourceIndices![sourceLowerIndex!] -> lowerSource => sourceResult
        results! -> len => sourceIrOffset
        0 => sourceNodeIndex!
        sourceNodeIndex! < (sourceResult.nodes -> len) -> while {
            sourceResult.nodes[sourceNodeIndex!] => sourceNode!
            sourceNode!.parent >= 0 -> if { sourceNode!.parent + sourceIrOffset => sourceNode!.parent }
            sourceNode!.operand0 >= 0 -> if { sourceNode!.operand0 + sourceIrOffset => sourceNode!.operand0 }
            sourceNode!.operand1 >= 0 -> if { sourceNode!.operand1 + sourceIrOffset => sourceNode!.operand1 }
            sourceNode!.nextOperand >= 0 -> if { sourceNode!.nextOperand + sourceIrOffset => sourceNode!.nextOperand }
            results! -> push(sourceNode!)
            sourceNodeIndex! + 1 => sourceNodeIndex!
        }
        sourceLowerIndex! + 1 => sourceLowerIndex!
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
            prepared.package.ranges[member!.sourceModule] => memberSourceRange
            prepared.package.nodes[memberSourceRange.astStart + member!.astNode] => memberAst
            # The operand already represents the complete typed base. Resolve
            # only the trailing path, so an identifier inside values[index]
            # cannot be mistaken for a field in values[index].field.
            1 => memberIdentifierOrdinal!
            prepared.package.nodes[memberSourceRange.astStart + memberBase.astNode] => memberBaseAst
            memberBaseAst.start + memberBaseAst.length => memberBaseEnd
            memberAst.firstToken => memberTokenIndex!
            true => beforeMemberBaseEnd!
            (beforeMemberBaseEnd! and memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount) -> while {
                prepared.package.tokens[memberSourceRange.tokenStart + memberTokenIndex!].span.start < memberBaseEnd -> if {
                    memberTokenIndex! + 1 => memberTokenIndex!
                } else {
                    false => beforeMemberBaseEnd!
                }
            }
            memberTokenIndex! < memberAst.firstToken + memberAst.tokenCount -> while {
                prepared.package.tokens[memberSourceRange.tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                    memberIdentifierOrdinal! > 0 -> if {
                        memberCurrentModule! => memberOwnerSource!
                        memberCurrentOrigin! == 2 -> if { prepared.modules[memberCurrentModule!].sourceIndex => memberOwnerSource! }
                        (memberOwnerSource! >= 0 and memberOwnerSource! < (prepared.package.ranges -> len)) -> if {
                        prepared.package.ranges[memberOwnerSource!] => memberOwnerRange
                        0 => memberFieldOrdinal!
                        0 => memberFieldIndex!
                        memberFieldIndex! < memberOwnerRange.symbolCount -> while {
                            prepared.package.symbols[memberOwnerRange.symbolStart + memberFieldIndex!] => memberField
                            (memberField.kind == 26 and memberField.parent == memberCurrentSymbol!) -> if {
                                prepared.package.tokens[memberSourceRange.tokenStart + memberTokenIndex!] => memberName
                                prepared.package.tokens[memberOwnerRange.tokenStart + memberField.nameToken] => memberFieldName
                                memberName.span.length == memberFieldName.span.length => memberEqual!
                                UIntSize(0) => memberNameByte!
                                (memberEqual! and memberNameByte! < memberName.span.length) -> while {
                                    prepared.package.sources[member!.sourceModule] -> byte(memberName.span.start + memberNameByte!) => memberByte
                                    prepared.package.sources[memberOwnerSource!] -> byte(memberFieldName.span.start + memberNameByte!) => memberFieldByte
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
        0 => recursiveExpressionIndex!
        recursiveExpressionIndex! < (recursiveExpressions! -> len) -> while {
            recursiveExpressions![recursiveExpressionIndex!] => recursiveExpression
            (recursiveExpression.status == 0 and recursiveExpression.sourceModule == recursiveIr!.sourceModule and recursiveExpression.astNode == recursiveIr!.astNode and recursiveIr!.kind != 0 and recursiveIr!.kind != 1 and recursiveIr!.kind != 10 and recursiveIr!.kind != 11) -> if {
                recursiveExpression.typeId => recursiveIr!.typeId
                recursiveSemanticTypes![recursiveExpression.typeId] => recursiveExpressionType
                recursiveExpressionType.kind => recursiveIr!.typeKind
                recursiveExpressionType.origin => recursiveIr!.typeOrigin
                recursiveExpressionType.module => recursiveIr!.typeModule
                recursiveExpressionType.symbol => recursiveIr!.typeSymbol
                recursiveTypeFlags![recursiveExpression.typeId] => recursiveIr!.typeFlags
            }
            recursiveExpressionIndex! + 1 => recursiveExpressionIndex!
        }
        recursiveIr! => results![recursiveIrIndex!]
        recursiveIrIndex! + 1 => recursiveIrIndex!
    }
    # Typed empty owners are runtime expressions rather than type-use nodes.
    # Their shallow projection therefore knows the imported declaration but
    # may have no recursive ID. Recover the intrinsic SourceText identity once
    # at the IR boundary before binding/reference/index propagation.
    -1 => intrinsicSourceTextModule!
    -1 => intrinsicSourceTextSymbol!
    0 => intrinsicSourceTextModuleSearch!
    intrinsicSourceTextModuleSearch! < (prepared.modules -> len) -> while {
        prepared.modules[intrinsicSourceTextModuleSearch!] => intrinsicSourceTextIdentity
        prepared.package.sources[intrinsicSourceTextIdentity.sourceIndex] -> len => intrinsicSourceTextLength
        prepared.package.sources[intrinsicSourceTextIdentity.sourceIndex] -> slice(UIntSize(0), intrinsicSourceTextLength) => intrinsicSourceTextSource
        intrinsicSourceTextIdentity.pathLength == UIntSize(8) => intrinsicSourceTextNamespaceEqual!
        "sys.file" => intrinsicSourceTextNamespace
        UIntSize(0) => intrinsicSourceTextNamespaceByte!
        (intrinsicSourceTextNamespaceEqual! and intrinsicSourceTextNamespaceByte! < intrinsicSourceTextIdentity.pathLength) -> while {
            (intrinsicSourceTextSource -> byte(intrinsicSourceTextIdentity.pathStart + intrinsicSourceTextNamespaceByte!)) != (intrinsicSourceTextNamespace -> byte(intrinsicSourceTextNamespaceByte!)) -> if { false => intrinsicSourceTextNamespaceEqual! }
            intrinsicSourceTextNamespaceByte! + UIntSize(1) => intrinsicSourceTextNamespaceByte!
        }
        intrinsicSourceTextNamespaceEqual! -> if {
            prepared.package.ranges[intrinsicSourceTextIdentity.sourceIndex] => intrinsicSourceTextRange
            0 => intrinsicSourceTextSymbolSearch!
            intrinsicSourceTextSymbolSearch! < intrinsicSourceTextRange.symbolCount -> while {
                prepared.package.symbols[intrinsicSourceTextRange.symbolStart + intrinsicSourceTextSymbolSearch!] => intrinsicSourceTextCandidate
                (intrinsicSourceTextCandidate.kind == 3 and intrinsicSourceTextCandidate.parent < 0) -> if {
                    prepared.package.tokens[intrinsicSourceTextRange.tokenStart + intrinsicSourceTextCandidate.nameToken] => intrinsicSourceTextNameToken
                    intrinsicSourceTextNameToken.span.length == UIntSize(10) => intrinsicSourceTextNameEqual!
                    "SourceText" => intrinsicSourceTextName
                    UIntSize(0) => intrinsicSourceTextNameByte!
                    (intrinsicSourceTextNameEqual! and intrinsicSourceTextNameByte! < intrinsicSourceTextNameToken.span.length) -> while {
                        (intrinsicSourceTextSource -> byte(intrinsicSourceTextNameToken.span.start + intrinsicSourceTextNameByte!)) != (intrinsicSourceTextName -> byte(intrinsicSourceTextNameByte!)) -> if { false => intrinsicSourceTextNameEqual! }
                        intrinsicSourceTextNameByte! + UIntSize(1) => intrinsicSourceTextNameByte!
                    }
                    intrinsicSourceTextNameEqual! -> if {
                        intrinsicSourceTextIdentity.sourceIndex => intrinsicSourceTextModule!
                        intrinsicSourceTextSymbolSearch! => intrinsicSourceTextSymbol!
                    }
                }
                intrinsicSourceTextSymbolSearch! + 1 => intrinsicSourceTextSymbolSearch!
            }
        }
        intrinsicSourceTextModuleSearch! + 1 => intrinsicSourceTextModuleSearch!
    }
    -1 => intrinsicSourceTextTypeId!
    0 => intrinsicSourceTextTypeSearch!
    intrinsicSourceTextTypeSearch! < (recursiveSemanticTypes! -> len) -> while {
        recursiveSemanticTypes![intrinsicSourceTextTypeSearch!] => intrinsicSourceTextTypeCandidate
        (intrinsicSourceTextTypeCandidate.kind == 1 and intrinsicSourceTextTypeCandidate.origin == 1 and intrinsicSourceTextTypeCandidate.symbol == 24) -> if {
            intrinsicSourceTextTypeSearch! => intrinsicSourceTextTypeId!
        }
        intrinsicSourceTextTypeSearch! + 1 => intrinsicSourceTextTypeSearch!
    }
    -1 => intrinsicSourceTextArrayTypeId!
    0 => intrinsicSourceTextArraySearch!
    intrinsicSourceTextArraySearch! < (recursiveSemanticTypes! -> len) -> while {
        recursiveSemanticTypes![intrinsicSourceTextArraySearch!] => intrinsicSourceTextArrayCandidate
        (intrinsicSourceTextArrayCandidate.kind == 3 and intrinsicSourceTextArrayCandidate.first == intrinsicSourceTextTypeId!) -> if {
            intrinsicSourceTextArraySearch! => intrinsicSourceTextArrayTypeId!
        }
        intrinsicSourceTextArraySearch! + 1 => intrinsicSourceTextArraySearch!
    }
    0 => intrinsicSourceTextIrIndex!
    intrinsicSourceTextIrIndex! < (results! -> len) -> while {
        results![intrinsicSourceTextIrIndex!] => intrinsicSourceTextIr!
        (intrinsicSourceTextIr!.typeId < 0 and intrinsicSourceTextIr!.typeModule == intrinsicSourceTextModule! and intrinsicSourceTextIr!.typeSymbol == intrinsicSourceTextSymbol!) -> if {
            (intrinsicSourceTextIr!.typeOrigin == 0 or intrinsicSourceTextIr!.typeOrigin == 2) -> if {
                intrinsicSourceTextTypeId! => intrinsicSourceTextIr!.typeId
                1 => intrinsicSourceTextIr!.typeOrigin
                -1 => intrinsicSourceTextIr!.typeModule
                24 => intrinsicSourceTextIr!.typeSymbol
                1 => intrinsicSourceTextIr!.typeKind
            }
            intrinsicSourceTextIr!.typeOrigin == 13 -> if {
                intrinsicSourceTextArrayTypeId! => intrinsicSourceTextIr!.typeId
                -1 => intrinsicSourceTextIr!.typeModule
                24 => intrinsicSourceTextIr!.typeSymbol
                3 => intrinsicSourceTextIr!.typeKind
            }
            intrinsicSourceTextIr! => results![intrinsicSourceTextIrIndex!]
        }
        intrinsicSourceTextIrIndex! + 1 => intrinsicSourceTextIrIndex!
    }
    0 => canonicalBindingIndex!
    canonicalBindingIndex! < (results! -> len) -> while {
        results![canonicalBindingIndex!] => canonicalBinding!
        (canonicalBinding!.kind == 17 and canonicalBinding!.operand0 >= 0) -> if {
            canonicalBinding!.operand0 => canonicalGlobalValueIndex!
            (results![canonicalGlobalValueIndex!].kind == 9 and results![canonicalGlobalValueIndex!].opcode == -1) -> if {
                -1 => canonicalGlobalWrapperValue!
                UIntSize(0) => canonicalGlobalWrapperLength!
                0 => canonicalGlobalWrapperSearch!
                canonicalGlobalWrapperSearch! < (results! -> len) -> while {
                    results![canonicalGlobalWrapperSearch!] => canonicalGlobalWrapperCandidate
                    (canonicalGlobalWrapperCandidate.parent == canonicalGlobalValueIndex! and canonicalGlobalWrapperCandidate.kind != 17 and not (canonicalGlobalWrapperCandidate.typeOrigin == 1 and canonicalGlobalWrapperCandidate.typeSymbol == 0)) -> if {
                        prepared.package.ranges[canonicalGlobalWrapperCandidate.sourceModule] => canonicalGlobalWrapperRange
                        prepared.package.nodes[canonicalGlobalWrapperRange.astStart + canonicalGlobalWrapperCandidate.astNode].length => canonicalGlobalWrapperCandidateLength
                        (canonicalGlobalWrapperValue! < 0 or canonicalGlobalWrapperCandidateLength > canonicalGlobalWrapperLength!) -> if {
                            canonicalGlobalWrapperSearch! => canonicalGlobalWrapperValue!
                            canonicalGlobalWrapperCandidateLength => canonicalGlobalWrapperLength!
                        }
                    }
                    canonicalGlobalWrapperSearch! + 1 => canonicalGlobalWrapperSearch!
                }
                canonicalGlobalWrapperValue! >= 0 -> if {
                    canonicalGlobalWrapperValue! => canonicalGlobalValueIndex!
                    canonicalGlobalWrapperValue! => canonicalBinding!.operand0
                }
            }
            results![canonicalGlobalValueIndex!].typeId => canonicalBinding!.typeId
            results![canonicalGlobalValueIndex!].typeKind => canonicalBinding!.typeKind
            results![canonicalGlobalValueIndex!].typeOrigin => canonicalBinding!.typeOrigin
            results![canonicalGlobalValueIndex!].typeModule => canonicalBinding!.typeModule
            results![canonicalGlobalValueIndex!].typeSymbol => canonicalBinding!.typeSymbol
            results![canonicalGlobalValueIndex!].typeFlags => canonicalBinding!.typeFlags
            canonicalBinding! => results![canonicalBindingIndex!]
        }
        canonicalBindingIndex! + 1 => canonicalBindingIndex!
    }
    0 => canonicalReferenceIndex!
    canonicalReferenceIndex! < (results! -> len) -> while {
        results![canonicalReferenceIndex!] => canonicalReference!
        false => canonicalDirectBindingResolved!
        (canonicalReference!.kind == 5 and canonicalReference!.operand0 >= 0 and results![canonicalReference!.operand0].kind == 17) -> if {
            results![canonicalReference!.operand0] => canonicalReferenceBinding
            canonicalReference!.parent => canonicalDirectScope!
            (canonicalDirectScope! >= 0 and results![canonicalDirectScope!].kind != 0 and results![canonicalDirectScope!].kind != 1 and results![canonicalDirectScope!].kind != 11 and results![canonicalDirectScope!].kind != 19) -> while {
                results![canonicalDirectScope!].parent => canonicalDirectScope!
            }
            false => canonicalDirectBindingVisible!
            canonicalDirectScope! >= 0 -> while {
                canonicalDirectScope! == canonicalReferenceBinding.parent -> if { true => canonicalDirectBindingVisible! }
                results![canonicalDirectScope!].parent => canonicalDirectScope!
                (canonicalDirectScope! >= 0 and results![canonicalDirectScope!].kind != 0 and results![canonicalDirectScope!].kind != 1 and results![canonicalDirectScope!].kind != 11 and results![canonicalDirectScope!].kind != 19) -> while {
                    results![canonicalDirectScope!].parent => canonicalDirectScope!
                }
            }
            canonicalDirectBindingVisible! -> if {
                canonicalReferenceBinding.typeId => canonicalReference!.typeId
                canonicalReferenceBinding.typeKind => canonicalReference!.typeKind
                canonicalReferenceBinding.typeOrigin => canonicalReference!.typeOrigin
                canonicalReferenceBinding.typeModule => canonicalReference!.typeModule
                canonicalReferenceBinding.typeSymbol => canonicalReference!.typeSymbol
                canonicalReferenceBinding.typeFlags => canonicalReference!.typeFlags
                canonicalReference! => results![canonicalReferenceIndex!]
                true => canonicalDirectBindingResolved!
            }
        }
        # Region-local mutable references do not always retain a direct
        # operand edge to their binding. Recover from the nearest preceding
        # binding with the same name that is visible through lexical regions.
        (canonicalReference!.kind == 5 and canonicalReference!.payloadToken >= 0 and not canonicalDirectBindingResolved!) -> if {
            canonicalReference!.parent => canonicalReferenceOwner!
            (canonicalReferenceOwner! >= 0 and results![canonicalReferenceOwner!].kind != 0 and results![canonicalReferenceOwner!].kind != 1 and results![canonicalReferenceOwner!].kind != 11) -> while {
                results![canonicalReferenceOwner!].parent => canonicalReferenceOwner!
            }
            prepared.package.ranges[canonicalReference!.sourceModule] => canonicalReferenceRange
            prepared.package.tokens[canonicalReferenceRange.tokenStart + canonicalReference!.payloadToken] => canonicalReferenceName
            canonicalReference!.parent => canonicalReferenceScope!
            (canonicalReferenceScope! >= 0 and results![canonicalReferenceScope!].kind != 0 and results![canonicalReferenceScope!].kind != 1 and results![canonicalReferenceScope!].kind != 11 and results![canonicalReferenceScope!].kind != 19) -> while {
                results![canonicalReferenceScope!].parent => canonicalReferenceScope!
            }
            -1 => canonicalReferenceBindingSearch!
            0 => canonicalReferenceCandidateIndex!
            canonicalReferenceCandidateIndex! < (results! -> len) -> while {
                results![canonicalReferenceCandidateIndex!] => canonicalReferenceCandidate
                (canonicalReferenceCandidateIndex! < canonicalReferenceIndex! and canonicalReferenceCandidate.kind == 17 and canonicalReferenceCandidate.sourceModule == canonicalReference!.sourceModule and canonicalReferenceCandidate.payloadToken >= 0) -> if {
                    canonicalReferenceCandidate.parent => canonicalReferenceCandidateOwner!
                    (canonicalReferenceCandidateOwner! >= 0 and results![canonicalReferenceCandidateOwner!].kind != 0 and results![canonicalReferenceCandidateOwner!].kind != 1 and results![canonicalReferenceCandidateOwner!].kind != 11) -> while {
                        results![canonicalReferenceCandidateOwner!].parent => canonicalReferenceCandidateOwner!
                    }
                    canonicalReferenceCandidateOwner! == canonicalReferenceOwner! -> if {
                        prepared.package.tokens[canonicalReferenceRange.tokenStart + canonicalReferenceCandidate.payloadToken] => canonicalReferenceCandidateName
                        canonicalReferenceName.span.length == canonicalReferenceCandidateName.span.length => canonicalReferenceNameEqual!
                        UIntSize(0) => canonicalReferenceNameByte!
                        (canonicalReferenceNameEqual! and canonicalReferenceNameByte! < canonicalReferenceName.span.length) -> while {
                            (prepared.package.sources[canonicalReference!.sourceModule] -> byte(canonicalReferenceName.span.start + canonicalReferenceNameByte!)) != (prepared.package.sources[canonicalReference!.sourceModule] -> byte(canonicalReferenceCandidateName.span.start + canonicalReferenceNameByte!)) -> if { false => canonicalReferenceNameEqual! }
                            canonicalReferenceNameByte! + UIntSize(1) => canonicalReferenceNameByte!
                        }
                        false => canonicalReferenceCandidateVisible!
                        canonicalReferenceScope! => canonicalReferenceVisibleScope!
                        canonicalReferenceVisibleScope! >= 0 -> while {
                            canonicalReferenceVisibleScope! == canonicalReferenceCandidate.parent -> if { true => canonicalReferenceCandidateVisible! }
                            results![canonicalReferenceVisibleScope!].parent => canonicalReferenceVisibleScope!
                            (canonicalReferenceVisibleScope! >= 0 and results![canonicalReferenceVisibleScope!].kind != 0 and results![canonicalReferenceVisibleScope!].kind != 1 and results![canonicalReferenceVisibleScope!].kind != 11 and results![canonicalReferenceVisibleScope!].kind != 19) -> while {
                                results![canonicalReferenceVisibleScope!].parent => canonicalReferenceVisibleScope!
                            }
                        }
                        (canonicalReferenceNameEqual! and canonicalReferenceCandidateVisible!) -> if { canonicalReferenceCandidateIndex! => canonicalReferenceBindingSearch! }
                    }
                }
                canonicalReferenceCandidateIndex! + 1 => canonicalReferenceCandidateIndex!
            }
            canonicalReferenceBindingSearch! >= 0 -> if {
                results![canonicalReferenceBindingSearch!] => recoveredReferenceBinding
                canonicalReferenceBindingSearch! => canonicalReference!.operand0
                recoveredReferenceBinding.typeId => canonicalReference!.typeId
                recoveredReferenceBinding.typeKind => canonicalReference!.typeKind
                recoveredReferenceBinding.typeOrigin => canonicalReference!.typeOrigin
                recoveredReferenceBinding.typeModule => canonicalReference!.typeModule
                recoveredReferenceBinding.typeSymbol => canonicalReference!.typeSymbol
                recoveredReferenceBinding.typeFlags => canonicalReference!.typeFlags
                canonicalReference! => results![canonicalReferenceIndex!]
            }
        }
        canonicalReferenceIndex! + 1 => canonicalReferenceIndex!
    }
    # Name recovery can finalize the type of a reference used directly as a
    # binding initializer. Refresh bindings from those finalized operands, then
    # refresh every direct binding read so no stale pre-recovery type survives.
    0 => finalizedBindingIndex!
    finalizedBindingIndex! < (results! -> len) -> while {
        results![finalizedBindingIndex!] => finalizedBinding!
        (finalizedBinding!.kind == 17 and finalizedBinding!.operand0 >= 0) -> if {
            results![finalizedBinding!.operand0] => finalizedBindingValue
            finalizedBindingValue.typeId => finalizedBinding!.typeId
            finalizedBindingValue.typeKind => finalizedBinding!.typeKind
            finalizedBindingValue.typeOrigin => finalizedBinding!.typeOrigin
            finalizedBindingValue.typeModule => finalizedBinding!.typeModule
            finalizedBindingValue.typeSymbol => finalizedBinding!.typeSymbol
            finalizedBindingValue.typeFlags => finalizedBinding!.typeFlags
            finalizedBinding! => results![finalizedBindingIndex!]
        }
        finalizedBindingIndex! + 1 => finalizedBindingIndex!
    }
    0 => finalizedReferenceIndex!
    finalizedReferenceIndex! < (results! -> len) -> while {
        results![finalizedReferenceIndex!] => finalizedReference!
        (finalizedReference!.kind == 5 and finalizedReference!.operand0 >= 0 and results![finalizedReference!.operand0].kind == 17) -> if {
            results![finalizedReference!.operand0] => finalizedReferenceBinding
            finalizedReferenceBinding.typeId => finalizedReference!.typeId
            finalizedReferenceBinding.typeKind => finalizedReference!.typeKind
            finalizedReferenceBinding.typeOrigin => finalizedReference!.typeOrigin
            finalizedReferenceBinding.typeModule => finalizedReference!.typeModule
            finalizedReferenceBinding.typeSymbol => finalizedReference!.typeSymbol
            finalizedReferenceBinding.typeFlags => finalizedReference!.typeFlags
            finalizedReference! => results![finalizedReferenceIndex!]
        }
        finalizedReferenceIndex! + 1 => finalizedReferenceIndex!
    }
    # Control conditions are first linked before the final binding/reference
    # canonicalization above. Re-run only unresolved controls now that mutable
    # Bool reads have their stable type, preserving the original AST-distance
    # and source-order selection rules.
    0 => recoveredControlIndex!
    recoveredControlIndex! < (results! -> len) -> while {
        results![recoveredControlIndex!] => recoveredControl!
        ((recoveredControl!.kind == 18 or recoveredControl!.kind == 20) and recoveredControl!.operand0 < 0 and recoveredControl!.sourceModule >= 0) -> if {
            prepared.package.ranges[recoveredControl!.sourceModule] => recoveredControlRange
            prepared.package.nodes[recoveredControlRange.astStart + recoveredControl!.astNode] => recoveredControlAst
            recoveredControlAst.parent => recoveredFlowAst!
            -1 => recoveredConditionIr!
            UIntSize(0) => recoveredConditionStart!
            1000000 => recoveredConditionDistance!
            0 => recoveredConditionSearch!
            recoveredConditionSearch! < (results! -> len) -> while {
                results![recoveredConditionSearch!] => recoveredConditionCandidate
                (recoveredConditionCandidate.sourceModule == recoveredControl!.sourceModule and recoveredConditionCandidate.astNode >= 0 and recoveredConditionCandidate.typeOrigin == 1 and recoveredConditionCandidate.typeSymbol == 23) -> if {
                    prepared.package.nodes[recoveredControlRange.astStart + recoveredConditionCandidate.astNode] => recoveredConditionAst
                    recoveredConditionAst.parent => recoveredConditionAncestor!
                    1 => recoveredCandidateDistance!
                    false => recoveredConditionBelongs!
                    (recoveredConditionAncestor! >= 0 and not recoveredConditionBelongs!) -> while {
                        recoveredConditionAncestor! == recoveredFlowAst! -> if { true => recoveredConditionBelongs! } else {
                            prepared.package.nodes[recoveredControlRange.astStart + recoveredConditionAncestor!].parent => recoveredConditionAncestor!
                            recoveredCandidateDistance! + 1 => recoveredCandidateDistance!
                        }
                    }
                    (recoveredConditionBelongs! and recoveredConditionAst.start < recoveredControlAst.start) -> if {
                        (recoveredConditionIr! < 0 or recoveredCandidateDistance! < recoveredConditionDistance! or (recoveredCandidateDistance! == recoveredConditionDistance! and recoveredConditionAst.start > recoveredConditionStart!)) -> if {
                            recoveredConditionSearch! => recoveredConditionIr!
                            recoveredConditionAst.start => recoveredConditionStart!
                            recoveredCandidateDistance! => recoveredConditionDistance!
                        }
                    }
                }
                recoveredConditionSearch! + 1 => recoveredConditionSearch!
            }
            recoveredConditionIr! >= 0 -> if {
                recoveredConditionIr! => recoveredControl!.operand0
                recoveredControl! => results![recoveredControlIndex!]
                recoveredControl!.kind == 20 -> if {
                    results![recoveredConditionIr!] => recoveredLoopCondition!
                    recoveredControlIndex! => recoveredLoopCondition!.parent
                    recoveredLoopCondition! => results![recoveredConditionIr!]
                }
            }
        }
        recoveredControlIndex! + 1 => recoveredControlIndex!
    }
    # Indexing is canonical across region and expression boundaries. A loop
    # body commonly indexes a binding declared in its enclosing function, so
    # restricting element recovery to one expression slice leaves the result
    # untyped even though the container already has a complete recursive id.
    0 => globalCanonicalIndex!
    globalCanonicalIndex! < (results! -> len) -> while {
        results![globalCanonicalIndex!] => globalIndexNode!
        (globalIndexNode!.kind == 15 and globalIndexNode!.operand0 >= 0) -> if {
            results![globalIndexNode!.operand0] => globalIndexContainer
            globalIndexContainer.typeId >= 0 -> if {
                recursiveSemanticTypes![globalIndexContainer.typeId] => globalContainerType
                -1 => globalElementTypeId!
                (globalContainerType.kind == 2 or globalContainerType.kind == 3 or globalContainerType.kind == 4) -> if { globalContainerType.first => globalElementTypeId! }
                globalContainerType.kind == 5 -> if { globalContainerType.second => globalElementTypeId! }
                globalElementTypeId! >= 0 -> if { globalElementTypeId! => globalIndexNode!.typeId }
            }
            globalIndexNode! => results![globalCanonicalIndex!]
        }
        globalCanonicalIndex! + 1 => globalCanonicalIndex!
    }
    # Global index recovery can establish an element type after the earlier
    # binding/reference passes. Refresh the direct dependency chain in order so
    # an index initializer cannot leave its binding and reads with stale types.
    0 => postIndexBindingIndex!
    postIndexBindingIndex! < (results! -> len) -> while {
        results![postIndexBindingIndex!] => postIndexBinding!
        (postIndexBinding!.kind == 17 and postIndexBinding!.operand0 >= 0) -> if {
            results![postIndexBinding!.operand0] => postIndexBindingValue
            postIndexBindingValue.typeId => postIndexBinding!.typeId
            postIndexBindingValue.typeKind => postIndexBinding!.typeKind
            postIndexBindingValue.typeOrigin => postIndexBinding!.typeOrigin
            postIndexBindingValue.typeModule => postIndexBinding!.typeModule
            postIndexBindingValue.typeSymbol => postIndexBinding!.typeSymbol
            postIndexBindingValue.typeFlags => postIndexBinding!.typeFlags
            postIndexBinding! => results![postIndexBindingIndex!]
        }
        postIndexBindingIndex! + 1 => postIndexBindingIndex!
    }
    0 => postIndexReferenceIndex!
    postIndexReferenceIndex! < (results! -> len) -> while {
        results![postIndexReferenceIndex!] => postIndexReference!
        (postIndexReference!.kind == 5 and postIndexReference!.operand0 >= 0 and results![postIndexReference!.operand0].kind == 17) -> if {
            results![postIndexReference!.operand0] => postIndexReferenceBinding
            postIndexReferenceBinding.typeId => postIndexReference!.typeId
            postIndexReferenceBinding.typeKind => postIndexReference!.typeKind
            postIndexReferenceBinding.typeOrigin => postIndexReference!.typeOrigin
            postIndexReferenceBinding.typeModule => postIndexReference!.typeModule
            postIndexReferenceBinding.typeSymbol => postIndexReference!.typeSymbol
            postIndexReferenceBinding.typeFlags => postIndexReference!.typeFlags
            postIndexReference! => results![postIndexReferenceIndex!]
        }
        postIndexReferenceIndex! + 1 => postIndexReferenceIndex!
    }
    # A recovered scalar reference can retain an unrelated nominal type id
    # even after lexical binding recovery has established its built-in shape.
    # Reconcile that stale id before the authoritative projection below;
    # otherwise an Int value can incorrectly acquire struct drop glue.
    0 => canonicalScalarIndex!
    canonicalScalarIndex! < (results! -> len) -> while {
        results![canonicalScalarIndex!] => canonicalScalar!
        (canonicalScalar!.typeKind == 1 and canonicalScalar!.typeOrigin == 1 and canonicalScalar!.typeSymbol >= 0 and canonicalScalar!.typeSymbol <= 23) -> if {
            true => canonicalScalarIdMismatch!
            canonicalScalar!.typeId >= 0 -> if {
                recursiveSemanticTypes![canonicalScalar!.typeId] => canonicalScalarIdType
                (canonicalScalarIdType.kind == 1 and canonicalScalarIdType.origin == 1 and canonicalScalarIdType.symbol == canonicalScalar!.typeSymbol) -> if { false => canonicalScalarIdMismatch! }
            }
            canonicalScalarIdMismatch! -> if {
                -1 => canonicalScalarTypeId!
                0 => canonicalScalarTypeSearch!
                canonicalScalarTypeSearch! < (recursiveSemanticTypes! -> len) -> while {
                    recursiveSemanticTypes![canonicalScalarTypeSearch!] => canonicalScalarTypeCandidate
                    (canonicalScalarTypeCandidate.kind == 1 and canonicalScalarTypeCandidate.origin == 1 and canonicalScalarTypeCandidate.symbol == canonicalScalar!.typeSymbol) -> if {
                        canonicalScalarTypeSearch! => canonicalScalarTypeId!
                    }
                    canonicalScalarTypeSearch! + 1 => canonicalScalarTypeSearch!
                }
                canonicalScalarTypeId! >= 0 -> if {
                    canonicalScalarTypeId! => canonicalScalar!.typeId
                    canonicalScalar! => results![canonicalScalarIndex!]
                }
            }
        }
        canonicalScalarIndex! + 1 => canonicalScalarIndex!
    }
    # Imported process intrinsics are resolved by module and symbol identity,
    # not by the surface token on the call expression. Preserve that identity
    # as stable backend opcodes after all per-source IR has been merged.
    -1 => processModuleSource!
    0 => processModuleSearch!
    processModuleSearch! < (prepared.modules -> len) -> while {
        prepared.modules[processModuleSearch!] => processModuleIdentity
        processModuleIdentity.pathHash == UInt64(15528987723426532771) -> if { processModuleIdentity.sourceIndex => processModuleSource! }
        processModuleSearch! + 1 => processModuleSearch!
    }
    -1 => processArgumentsSymbol!
    -1 => processRunSymbol!
    -1 => processRunToFileSymbol!
    processModuleSource! >= 0 -> if {
        prepared.package.ranges[processModuleSource!] => processSourceRange
        prepared.package.sources[processModuleSource!] -> len => processSourceLength
        prepared.package.sources[processModuleSource!] -> slice(UIntSize(0), processSourceLength) => processSource
        0 => processSymbolSearch!
        processSymbolSearch! < processSourceRange.symbolCount -> while {
            prepared.package.symbols[processSourceRange.symbolStart + processSymbolSearch!] => processSymbol
            (processSymbol.kind == 7 and processSymbol.parent < 0) -> if {
                prepared.package.tokens[processSourceRange.tokenStart + processSymbol.nameToken] => processNameToken
                processNameToken.span.length == UIntSize(3) -> if {
                    ((processSource -> byte(processNameToken.span.start)) == UInt8(114) and (processSource -> byte(processNameToken.span.start + UIntSize(1))) == UInt8(117) and (processSource -> byte(processNameToken.span.start + UIntSize(2))) == UInt8(110)) -> if { processSymbolSearch! => processRunSymbol! }
                }
                processNameToken.span.length == UIntSize(9) -> if {
                    ((processSource -> byte(processNameToken.span.start)) == UInt8(97) and (processSource -> byte(processNameToken.span.start + UIntSize(1))) == UInt8(114) and (processSource -> byte(processNameToken.span.start + UIntSize(2))) == UInt8(103) and (processSource -> byte(processNameToken.span.start + UIntSize(3))) == UInt8(117) and (processSource -> byte(processNameToken.span.start + UIntSize(4))) == UInt8(109) and (processSource -> byte(processNameToken.span.start + UIntSize(5))) == UInt8(101) and (processSource -> byte(processNameToken.span.start + UIntSize(6))) == UInt8(110) and (processSource -> byte(processNameToken.span.start + UIntSize(7))) == UInt8(116) and (processSource -> byte(processNameToken.span.start + UIntSize(8))) == UInt8(115)) -> if { processSymbolSearch! => processArgumentsSymbol! }
                    ((processSource -> byte(processNameToken.span.start)) == UInt8(114) and (processSource -> byte(processNameToken.span.start + UIntSize(1))) == UInt8(117) and (processSource -> byte(processNameToken.span.start + UIntSize(2))) == UInt8(110) and (processSource -> byte(processNameToken.span.start + UIntSize(3))) == UInt8(84) and (processSource -> byte(processNameToken.span.start + UIntSize(4))) == UInt8(111) and (processSource -> byte(processNameToken.span.start + UIntSize(5))) == UInt8(70) and (processSource -> byte(processNameToken.span.start + UIntSize(6))) == UInt8(105) and (processSource -> byte(processNameToken.span.start + UIntSize(7))) == UInt8(108) and (processSource -> byte(processNameToken.span.start + UIntSize(8))) == UInt8(101)) -> if { processSymbolSearch! => processRunToFileSymbol! }
                }
            }
            processSymbolSearch! + 1 => processSymbolSearch!
        }
        0 => processCallIndex!
        processCallIndex! < (results! -> len) -> while {
            results![processCallIndex!] => processCall!
            (processCall!.kind == 6 and processCall!.targetModule == processModuleSource!) -> if {
                processCall!.symbol == processArgumentsSymbol! -> if { -212 => processCall!.opcode }
                processCall!.symbol == processRunSymbol! -> if { -210 => processCall!.opcode }
                processCall!.symbol == processRunToFileSymbol! -> if { -211 => processCall!.opcode }
                processCall! => results![processCallIndex!]
            }
            processCallIndex! + 1 => processCallIndex!
        }
    }
    # Collection literals returned directly from a function are contextually
    # typed by that function's declared result. Perform this after source IR
    # merging, when the final function -> return -> literal edges are stable.
    0 => contextualCollectionFunctionIndex!
    contextualCollectionFunctionIndex! < (results! -> len) -> while {
        results![contextualCollectionFunctionIndex!] => contextualCollectionFunction
        (contextualCollectionFunction.kind == 0 and contextualCollectionFunction.typeId >= 0 and contextualCollectionFunction.operand0 >= 0) -> if {
            results![contextualCollectionFunction.operand0] => contextualCollectionReturn
            contextualCollectionReturn.kind == 1 -> if {
                contextualCollectionReturn.operand0 => contextualCollectionLiteralIndex!
                contextualCollectionLiteralIndex! >= 0 -> if {
                    results![contextualCollectionLiteralIndex!] => contextualCollectionLiteral!
                    ((contextualCollectionFunction.typeKind == 3 and contextualCollectionLiteral!.kind == 14) or (contextualCollectionFunction.typeKind == 5 and contextualCollectionLiteral!.kind == 16)) -> if {
                        contextualCollectionFunction.typeId => contextualCollectionLiteral!.typeId
                        contextualCollectionFunction.typeKind => contextualCollectionLiteral!.typeKind
                        contextualCollectionFunction.typeOrigin => contextualCollectionLiteral!.typeOrigin
                        contextualCollectionFunction.typeModule => contextualCollectionLiteral!.typeModule
                        contextualCollectionFunction.typeSymbol => contextualCollectionLiteral!.typeSymbol
                        contextualCollectionFunction.typeFlags => contextualCollectionLiteral!.typeFlags
                        contextualCollectionLiteral! => results![contextualCollectionLiteralIndex!]
                    }
                }
            }
        }
        contextualCollectionFunctionIndex! + 1 => contextualCollectionFunctionIndex!
    }
    # A canonical type id is authoritative. Earlier shallow inference can
    # discover a useful id before its legacy origin/module/symbol projection is
    # updated (notably array indexing through imported intrinsic nominals).
    # Synchronize every node once at the IR boundary so emitters never observe
    # a canonical id paired with stale structural fields.
    0 => canonicalTypeProjectionIndex!
    canonicalTypeProjectionIndex! < (results! -> len) -> while {
        results![canonicalTypeProjectionIndex!] => canonicalTypeProjection!
        canonicalTypeProjection!.typeId >= 0 -> if {
            recursiveSemanticTypes![canonicalTypeProjection!.typeId] => canonicalProjectedType
            canonicalProjectedType.kind => canonicalTypeProjection!.typeKind
            canonicalProjectedType.origin => canonicalTypeProjection!.typeOrigin
            canonicalProjectedType.module => canonicalTypeProjection!.typeModule
            canonicalProjectedType.symbol => canonicalTypeProjection!.typeSymbol
            recursiveTypeFlags![canonicalTypeProjection!.typeId] => canonicalTypeProjection!.typeFlags
            (canonicalProjectedType.kind >= 2 and canonicalProjectedType.kind <= 6) -> if {
                10 + canonicalProjectedType.kind => canonicalTypeProjection!.typeOrigin
                canonicalProjectedType.kind == 5 -> if {
                    recursiveSemanticTypes![canonicalProjectedType.first].symbol => canonicalTypeProjection!.typeModule
                    recursiveSemanticTypes![canonicalProjectedType.second].symbol => canonicalTypeProjection!.typeSymbol
                } else {
                    recursiveSemanticTypes![canonicalProjectedType.first].module => canonicalTypeProjection!.typeModule
                    recursiveSemanticTypes![canonicalProjectedType.first].symbol => canonicalTypeProjection!.typeSymbol
                }
            }
            canonicalTypeProjection! => results![canonicalTypeProjectionIndex!]
        }
        canonicalTypeProjectionIndex! + 1 => canonicalTypeProjectionIndex!
    }
    results!
}

public lowerPrepared request: move TypedIrRequest -> [TypedIrNode; ~] {
    [file.SourceText; ~] => sources!
    0 => sourceIndex!
    sourceIndex! < (request.sources -> len) -> while {
        request.sources[sourceIndex!] => source
        source -> file.borrowText => ownedSource
        sources! -> push(ownedSource)
        sourceIndex! + 1 => sourceIndex!
    }
    [typeIds.SemanticType; ~] => types!
    0 => semanticTypeCopyIndex2198!
    semanticTypeCopyIndex2198! < (request.types -> len) -> while {
        request.types[semanticTypeCopyIndex2198!] => semanticType
        types! -> push(semanticType)
        semanticTypeCopyIndex2198! + 1 => semanticTypeCopyIndex2198!
    }
    [typeIds.TypeReference; ~] => references!
    0 => referenceCopyIndex2200!
    referenceCopyIndex2200! < (request.references -> len) -> while {
        request.references[referenceCopyIndex2200!] => reference
        references! -> push(reference)
        referenceCopyIndex2200! + 1 => referenceCopyIndex2200!
    }
    [typeIds.NominalField; ~] => fields!
    0 => fieldCopyIndex2202!
    fieldCopyIndex2202! < (request.fields -> len) -> while {
        request.fields[fieldCopyIndex2202!] => field
        fields! -> push(field)
        fieldCopyIndex2202! + 1 => fieldCopyIndex2202!
    }
    [nominalTypes.NominalType; ~] => nominal!
    0 => nominalTypeCopyIndex2204!
    nominalTypeCopyIndex2204! < (request.nominal -> len) -> while {
        request.nominal[nominalTypeCopyIndex2204!] => nominalType
        nominal! -> push(nominalType)
        nominalTypeCopyIndex2204! + 1 => nominalTypeCopyIndex2204!
    }
    [compositeTypes.CompositeType; ~] => composite!
    0 => compositeTypeCopyIndex2206!
    compositeTypeCopyIndex2206! < (request.composite -> len) -> while {
        request.composite[compositeTypeCopyIndex2206!] => compositeType
        composite! -> push(compositeType)
        compositeTypeCopyIndex2206! + 1 => compositeTypeCopyIndex2206!
    }
    [modules.ModuleIdentity; ~] => moduleIdentities!
    0 => moduleIdentityCopyIndex2208!
    moduleIdentityCopyIndex2208! < (request.modules -> len) -> while {
        request.modules[moduleIdentityCopyIndex2208!] => moduleIdentity
        moduleIdentities! -> push(moduleIdentity)
        moduleIdentityCopyIndex2208! + 1 => moduleIdentityCopyIndex2208!
    }
    [qualified.QualifiedResolution; ~] => qualifiedResults!
    0 => qualifiedResultCopyIndex2210!
    qualifiedResultCopyIndex2210! < (request.qualified -> len) -> while {
        request.qualified[qualifiedResultCopyIndex2210!] => qualifiedResult
        qualifiedResults! -> push(qualifiedResult)
        qualifiedResultCopyIndex2210! + 1 => qualifiedResultCopyIndex2210!
    }
    [calls.ModuleCallResolution; ~] => moduleCalls!
    0 => moduleCallCopyIndex2212!
    moduleCallCopyIndex2212! < (request.calls -> len) -> while {
        request.calls[moduleCallCopyIndex2212!] => moduleCall
        moduleCalls! -> push(moduleCall)
        moduleCallCopyIndex2212! + 1 => moduleCallCopyIndex2212!
    }
    [analysis.SourceAnalysisRange; ~] => ranges!
    0 => sourceRangeCopyIndex2214!
    sourceRangeCopyIndex2214! < (request.analysisRanges -> len) -> while {
        request.analysisRanges[sourceRangeCopyIndex2214!] => sourceRange
        ranges! -> push(sourceRange)
        sourceRangeCopyIndex2214! + 1 => sourceRangeCopyIndex2214!
    }
    [ast.AstNode; ~] => nodes!
    0 => nodeCopyIndex2216!
    nodeCopyIndex2216! < (request.analysisNodes -> len) -> while {
        request.analysisNodes[nodeCopyIndex2216!] => node
        nodes! -> push(node)
        nodeCopyIndex2216! + 1 => nodeCopyIndex2216!
    }
    [syntax.SyntaxToken; ~] => tokens!
    0 => tokenCopyIndex2218!
    tokenCopyIndex2218! < (request.analysisTokens -> len) -> while {
        request.analysisTokens[tokenCopyIndex2218!] => token
        tokens! -> push(token)
        tokenCopyIndex2218! + 1 => tokenCopyIndex2218!
    }
    [symbols.Symbol; ~] => symbolTable!
    0 => symbolCopyIndex2220!
    symbolCopyIndex2220! < (request.analysisSymbols -> len) -> while {
        request.analysisSymbols[symbolCopyIndex2220!] => symbol
        symbolTable! -> push(symbol)
        symbolCopyIndex2220! + 1 => symbolCopyIndex2220!
    }
    [resolution.ResolvedName; ~] => names!
    0 => nameCopyIndex2222!
    nameCopyIndex2222! < (request.analysisNames -> len) -> while {
        request.analysisNames[nameCopyIndex2222!] => name
        names! -> push(name)
        nameCopyIndex2222! + 1 => nameCopyIndex2222!
    }
    [typeTerms.TypeTerm; ~] => terms!
    [semanticTypes.TypeUse; ~] => typeUses!
    typeIds.SemanticTypeSet { types: types!, references: references!, fields: fields! } => contextSemantic!
    analysis.PackageAnalysis {
        sources: sources!
        ranges: ranges!
        nodes: nodes!
        tokens: tokens!
        symbols: symbolTable!
        names: names!
        terms: terms!
        typeUses: typeUses!
    } => contextPackage!
    semanticContext.SemanticSnapshot {
        semantic: contextSemantic!
        package: contextPackage!
        nominal: nominal!
        composite: composite!
        modules: moduleIdentities!
        imports: [modules.ImportEdge; ~]
        resolvedImports: [moduleResolve.ResolvedImport; ~]
        qualified: qualifiedResults!
        calls: moduleCalls!
    } => prepared!
    prepared! -> semanticContext.freeze => snapshot!
    snapshot! -> lowerContext => result!
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
        # An owned value placed into a struct, array, or dictionary literal
        # transfers into that aggregate. Treat direct names and owned member
        # paths exactly like consuming-call arguments so the former binding is
        # not dropped after the aggregate has taken ownership.
        ((site.kind == 5 or site.kind == 13) and (site.typeOrigin == 13 or site.typeOrigin == 15 or site.typeFlags % 2 == 1)) -> if {
            site.parent => aggregateOwner!
            false => aggregateMemberPath!
            (aggregateOwner! >= 0 and ir[aggregateOwner!].kind != 0 and ir[aggregateOwner!].kind != 1 and ir[aggregateOwner!].kind != 11 and ir[aggregateOwner!].kind != 12 and ir[aggregateOwner!].kind != 14 and ir[aggregateOwner!].kind != 16 and ir[aggregateOwner!].kind != 19 and ir[aggregateOwner!].kind != 20) -> while {
                ir[aggregateOwner!].kind == 13 -> if { true => aggregateMemberPath! }
                ir[aggregateOwner!].parent => aggregateOwner!
            }
            (not aggregateMemberPath! and aggregateOwner! >= 0 and (ir[aggregateOwner!].kind == 12 or ir[aggregateOwner!].kind == 14 or ir[aggregateOwner!].kind == 16)) -> if {
                siteIndex! => movedValueIr!
            }
        }
        movedValueIr! >= 0 -> if {
            movedValueIr! => moveRootIr!
            -1 => moveMemberIr!
            ir[moveRootIr!].kind == 13 -> if { moveRootIr! => moveMemberIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 13) -> while { ir[moveRootIr!].operand0 => moveRootIr! }
            (moveRootIr! >= 0 and ir[moveRootIr!].kind == 5 and ir[moveRootIr!].symbol >= 0) -> if {
                site.parent => moveRegion!
                (moveRegion! >= 0 and ir[moveRegion!].kind != 1 and ir[moveRegion!].kind != 11 and ir[moveRegion!].kind != 19 and ir[moveRegion!].kind != 20) -> while {
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

public coroutinePlanContext prepared: semanticContext.SemanticSnapshot -> CoroutinePlan {
    prepared -> lowerContext => ir!
    [CoroutineSuspendPoint; ~] => points!
    0 => sourceIndex!
    sourceIndex! < (prepared.package.sources -> len) -> while {
        prepared.package.sources[sourceIndex!] -> len => sourceLength
        prepared.package.sources[sourceIndex!] -> slice(UIntSize(0), sourceLength) => source
        prepared.package.ranges[sourceIndex!] => sourceRange
        0 => awaitAstIndex!
        awaitAstIndex! < sourceRange.astCount -> while {
            prepared.package.nodes[sourceRange.astStart + awaitAstIndex!] => awaitAst
            false => isAwait!
            false => isYield!
            (awaitAst.kind == 16 and awaitAst.parent >= 0 and prepared.package.nodes[sourceRange.astStart + awaitAst.parent].kind == 10 and awaitAst.payloadToken >= 0) -> if {
                prepared.package.tokens[sourceRange.tokenStart + awaitAst.payloadToken] => awaitToken
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
                prepared.package.tokens[sourceRange.tokenStart + awaitAst.payloadToken] => yieldToken
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
                (functionAstIndex! >= 0 and prepared.package.nodes[sourceRange.astStart + functionAstIndex!].kind != 7) -> while {
                    prepared.package.nodes[sourceRange.astStart + functionAstIndex!].parent => functionAstIndex!
                }
                (functionAstIndex! >= 0 and (prepared.package.nodes[sourceRange.astStart + functionAstIndex!].flags / 8) % 2 == 1) -> if {
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
                        0 => suspendKind!
                        isYield! -> if { 1 => suspendKind! }
                        points! -> push(CoroutineSuspendPoint {
                            functionIr: functionIr!
                            sourceModule: sourceIndex!
                            awaitAst: awaitAstIndex!
                            awaitIr: awaitIr!
                            state: state!
                            kind: suspendKind!
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
        prepared.package.ranges[point.sourceModule] => sourceRange
        ir![point.functionIr].astNode => functionAst
        prepared.package.nodes[sourceRange.astStart + point.awaitAst].start => awaitStart
        point.awaitAst => awaitBindingAst!
        (awaitBindingAst! >= 0 and prepared.package.nodes[sourceRange.astStart + awaitBindingAst!].kind != 9) -> while {
            prepared.package.nodes[sourceRange.astStart + awaitBindingAst!].parent => awaitBindingAst!
        }
        0 => definitionIndex!
        definitionIndex! < (ir! -> len) -> while {
            ir![definitionIndex!] => definition
            false => belongsToFunction!
            definition.astNode => definitionAncestor!
            (definitionAncestor! >= 0 and not belongsToFunction!) -> while {
                definitionAncestor! == functionAst -> if { true => belongsToFunction! } else {
                    prepared.package.nodes[sourceRange.astStart + definitionAncestor!].parent => definitionAncestor!
                }
            }
            (definition.kind == 17 and definition.sourceModule == point.sourceModule and definition.symbol >= 0 and definition.astNode != awaitBindingAst! and belongsToFunction! and prepared.package.nodes[sourceRange.astStart + definition.astNode].start < awaitStart) -> if {
                false => usedAfterAwait!
                0 => useIndex!
                (useIndex! < (ir! -> len) and not usedAfterAwait!) -> while {
                    ir![useIndex!] => use
                    (use.kind == 5 and use.sourceModule == point.sourceModule and use.symbol == definition.symbol and use.astNode >= 0 and prepared.package.nodes[sourceRange.astStart + use.astNode].start > awaitStart) -> if {
                        false => useBelongsToFunction!
                        use.astNode => useAncestor!
                        (useAncestor! >= 0 and not useBelongsToFunction!) -> while {
                            useAncestor! == functionAst -> if { true => useBelongsToFunction! } else {
                                prepared.package.nodes[sourceRange.astStart + useAncestor!].parent => useAncestor!
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
                                    prepared.package.nodes[sourceRange.astStart + taskAncestor!].parent => taskAncestor!
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
    0 => pointCopyIndex2504!
    pointCopyIndex2504! < (plan.points -> len) -> while {
        plan.points[pointCopyIndex2504!] => point
        points! -> push(point)
        pointCopyIndex2504! + 1 => pointCopyIndex2504!
    }
    points!
}

public frameSlots sources: [Text; ~] -> [CoroutineFrameSlot; ~] {
    sources -> coroutinePlan => plan
    [CoroutineFrameSlot; ~] => slots!
    0 => slotCopyIndex2511!
    slotCopyIndex2511! < (plan.slots -> len) -> while {
        plan.slots[slotCopyIndex2511!] => slot
        slots! -> push(slot)
        slotCopyIndex2511! + 1 => slotCopyIndex2511!
    }
    slots!
}

# Destruction order is explicit and deterministic. Each state starts with a
# synthetic active-child entry (symbol -1, Task bit set), followed by initialized
# owned frame slots in reverse definition order. Scalar-only slots need no drop.
public destroySlots sources: [Text; ~] -> [CoroutineFrameSlot; ~] {
    sources -> coroutinePlan => plan
    [CoroutineFrameSlot; ~] => destroys!
    0 => slotCopyIndex2521!
    slotCopyIndex2521! < (plan.destroys -> len) -> while {
        plan.destroys[slotCopyIndex2521!] => slot
        destroys! -> push(slot)
        slotCopyIndex2521! + 1 => slotCopyIndex2521!
    }
    destroys!
}
