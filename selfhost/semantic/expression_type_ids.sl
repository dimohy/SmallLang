namespace smalllang.compiler.semantic.expression_type_ids

import smalllang.compiler.ast
import smalllang.compiler.lexer
import smalllang.compiler.semantic.analysis
import smalllang.compiler.semantic.calls
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.context as semanticContext
import smalllang.compiler.semantic.modules
import smalllang.compiler.semantic.module_resolve as moduleResolve
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.qualified
import smalllang.compiler.semantic.resolve as resolution
import smalllang.compiler.semantic.symbols
import smalllang.compiler.semantic.type_ids as typeIds
import smalllang.compiler.semantic.type_terms as typeTerms
import smalllang.compiler.semantic.types as semanticTypes
import smalllang.compiler.syntax
import syntax.generated.smalllang as grammar

public struct ExpressionTypeId {
    sourceModule: Int
    astNode: Int
    typeId: Int
    status: Int
}

public struct ExpressionTypeIdSet {
    types: [typeIds.SemanticType; ~]
    references: [typeIds.TypeReference; ~]
    fields: [typeIds.NominalField; ~]
    expressions: [ExpressionTypeId; ~]
}

public struct ExpressionTypeIdRequest {
    sources: [Text; ~]
    types: [typeIds.SemanticType; ~]
    references: [typeIds.TypeReference; ~]
    fields: [typeIds.NominalField; ~]
    modules: [modules.ModuleIdentity; ~]
    qualified: [qualified.QualifiedResolution; ~]
    calls: [calls.ModuleCallResolution; ~]
    analysisRanges: [analysis.SourceAnalysisRange; ~]
    analysisNodes: [ast.AstNode; ~]
    analysisTokens: [syntax.SyntaxToken; ~]
    analysisSymbols: [symbols.Symbol; ~]
    analysisNames: [resolution.ResolvedName; ~]
}

# Bridges the existing shallow expression pass into the canonical recursive
# type arena. Annotation-backed names and call results retain their full type;
# builtin expressions use the stable builtin id directly.
public resolveContext prepared: semanticContext.CompilationContext -> ExpressionTypeIdSet {
    [typeIds.SemanticType; ~] => types!
    prepared.types -> each semanticType { types! -> push(semanticType) }
    [typeIds.TypeReference; ~] => references!
    prepared.references -> each reference { references! -> push(reference) }
    [typeIds.NominalField; ~] => fields!
    prepared.fields -> each field { fields! -> push(field) }
    [ExpressionTypeId; ~] => expressions!

    0 => sourceIndex!
    sourceIndex! < (prepared.sources -> len) -> while {
        prepared.sources[sourceIndex!] => source
        prepared.ranges[sourceIndex!] => sourceRange
        [resolution.ResolvedName; ~] => resolvedNames!
        0 => sourceNameOffset!
        sourceNameOffset! < sourceRange.nameCount -> while {
            resolvedNames! -> push(prepared.names[sourceRange.nameStart + sourceNameOffset!])
            sourceNameOffset! + 1 => sourceNameOffset!
        }

        0 => astIndex!
        astIndex! < sourceRange.astCount -> while {
            prepared.nodes[sourceRange.astStart + astIndex!] => node
            -1 => builtinTypeId!
            node.kind == 13 -> if { 1 => builtinTypeId! }
            node.kind == 14 -> if { 2 => builtinTypeId! }
            (node.kind >= 44 and node.kind <= 47) -> if { 0 => builtinTypeId! }
            node.kind == 15 -> if {
                prepared.tokens[sourceRange.tokenStart + node.payloadToken] => name
                name.span.length == UIntSize(4) -> if {
                    source -> byte(name.span.start) => byte0
                    source -> byte(name.span.start + UIntSize(1)) => byte1
                    source -> byte(name.span.start + UIntSize(2)) => byte2
                    source -> byte(name.span.start + UIntSize(3)) => byte3
                    (byte0 == UInt8(116) and byte1 == UInt8(114) and byte2 == UInt8(117) and byte3 == UInt8(101)) -> if { 23 => builtinTypeId! }
                }
                name.span.length == UIntSize(5) -> if {
                    source -> byte(name.span.start) => byte0
                    source -> byte(name.span.start + UIntSize(1)) => byte1
                    source -> byte(name.span.start + UIntSize(2)) => byte2
                    source -> byte(name.span.start + UIntSize(3)) => byte3
                    source -> byte(name.span.start + UIntSize(4)) => byte4
                    (byte0 == UInt8(102) and byte1 == UInt8(97) and byte2 == UInt8(108) and byte3 == UInt8(115) and byte4 == UInt8(101)) -> if { 23 => builtinTypeId! }
                }
            }
            node.kind == 39 -> if {
                -1 => typeNameToken!
                node.firstToken => literalTokenIndex!
                (literalTokenIndex! < node.firstToken + node.tokenCount and typeNameToken! < 0) -> while {
                    prepared.tokens[sourceRange.tokenStart + literalTokenIndex!].kind == grammar.tokenIdIdentifier -> if { literalTokenIndex! => typeNameToken! }
                    literalTokenIndex! + 1 => literalTokenIndex!
                }
                -1 => literalTargetModule!
                -1 => literalTargetSymbol!
                0 => localStructSearch!
                (localStructSearch! < sourceRange.symbolCount and literalTargetSymbol! < 0) -> while {
                    prepared.symbols[sourceRange.symbolStart + localStructSearch!] => candidateStruct
                    (candidateStruct.kind == 3 and candidateStruct.parent < 0) -> if {
                        prepared.tokens[sourceRange.tokenStart + typeNameToken!] => literalName
                        prepared.tokens[sourceRange.tokenStart + candidateStruct.nameToken] => declarationName
                        literalName.span.length == declarationName.span.length => equal!
                        UIntSize(0) => nameByte!
                        (equal! and nameByte! < literalName.span.length) -> while {
                            source -> byte(literalName.span.start + nameByte!) => leftByte
                            source -> byte(declarationName.span.start + nameByte!) => rightByte
                            leftByte != rightByte -> if { false => equal! }
                            nameByte! + UIntSize(1) => nameByte!
                        }
                        equal! -> if {
                            sourceIndex! => literalTargetModule!
                            localStructSearch! => literalTargetSymbol!
                        }
                    }
                    localStructSearch! + 1 => localStructSearch!
                }
                literalTargetSymbol! < 0 -> if {
                    0 => qualifiedSearch!
                    qualifiedSearch! < (prepared.qualified -> len) -> while {
                        prepared.qualified[qualifiedSearch!] => importedCandidate
                        (importedCandidate.sourceModule == sourceIndex! and importedCandidate.status == 0) -> if {
                            importedCandidate.pathAst => importedAncestor!
                            false => belongsToLiteral!
                            (importedAncestor! >= 0 and not belongsToLiteral!) -> while {
                                importedAncestor! == astIndex! -> if { true => belongsToLiteral! } else {
                                    prepared.nodes[sourceRange.astStart + importedAncestor!].parent => importedAncestor!
                                }
                            }
                            belongsToLiteral! -> if {
                                prepared.modules[importedCandidate.targetModule].sourceIndex => literalTargetModule!
                                importedCandidate.targetSymbol => literalTargetSymbol!
                            }
                        }
                        qualifiedSearch! + 1 => qualifiedSearch!
                    }
                }
                literalTargetSymbol! >= 0 -> if {
                    -1 => nominalTypeId!
                    0 => nominalTypeSearch!
                    (nominalTypeSearch! < (types! -> len) and nominalTypeId! < 0) -> while {
                        types![nominalTypeSearch!] => known
                        (known.kind == 1 and (known.origin == 0 or known.origin == 2) and known.module == literalTargetModule! and known.symbol == literalTargetSymbol!) -> if {
                            nominalTypeSearch! => nominalTypeId!
                        }
                        nominalTypeSearch! + 1 => nominalTypeSearch!
                    }
                    nominalTypeId! < 0 -> if {
                        types! -> len => nominalTypeId!
                        types! -> push(typeIds.SemanticType {
                            kind: 1
                            origin: literalTargetModule! == sourceIndex! -> if { 0 } else { 2 }
                            module: literalTargetModule!
                            symbol: literalTargetSymbol!
                            first: -1
                            second: -1
                            length: -1
                            lengthHash: UInt64(0)
                            containsParameter: false
                            status: 0
                        })
                    }
                    nominalTypeId! => builtinTypeId!
                }
            }
            builtinTypeId! >= 0 -> if {
                expressions! -> push(ExpressionTypeId {
                    sourceModule: sourceIndex!
                    astNode: astIndex!
                    typeId: builtinTypeId!
                    status: 0
                })
            }
            astIndex! + 1 => astIndex!
        }

        resolvedNames! -> each resolvedName {
            prepared.symbols[sourceRange.symbolStart + resolvedName.symbol] => valueSymbol
            valueSymbol.typeNode >= 0 -> if {
                -1 => referenceIndex!
                0 => referenceSearch!
                (referenceSearch! < (references! -> len) and referenceIndex! < 0) -> while {
                    references![referenceSearch!] => candidate
                    (candidate.sourceModule == sourceIndex! and candidate.typeAst == valueSymbol.typeNode) -> if {
                        referenceSearch! => referenceIndex!
                    }
                    referenceSearch! + 1 => referenceSearch!
                }
                referenceIndex! >= 0 -> if {
                    references![referenceIndex!] => reference
                    -1 => existingIndex!
                    0 => existingSearch!
                    (existingSearch! < (expressions! -> len) and existingIndex! < 0) -> while {
                        (expressions![existingSearch!].sourceModule == sourceIndex! and expressions![existingSearch!].astNode == resolvedName.astNode) -> if {
                            existingSearch! => existingIndex!
                        }
                        existingSearch! + 1 => existingSearch!
                    }
                    ExpressionTypeId {
                        sourceModule: sourceIndex!
                        astNode: resolvedName.astNode
                        typeId: reference.typeId
                        status: reference.status
                    } => exactType
                    existingIndex! >= 0 -> if {
                        exactType => expressions![existingIndex!]
                    } else {
                        expressions! -> push(exactType)
                    }
                }
            }
        }
        sourceIndex! + 1 => sourceIndex!
    }

    # Build recursive IDs for composite literals bottom-up. This makes literal
    # arguments available to the same generic unifier as annotated names.
    true => compositeChanged!
    compositeChanged! -> while {
        false => compositeChanged!
        0 => compositeSourceIndex!
        compositeSourceIndex! < (prepared.sources -> len) -> while {
            prepared.sources[compositeSourceIndex!] => compositeSource
            compositeSource -> ast.lower => compositeNodes!
            compositeSource -> lexer.lex => compositeTokens!
            0 => compositeAstIndex!
            compositeAstIndex! < (compositeNodes! -> len) -> while {
                compositeNodes![compositeAstIndex!] => compositeNode
                false => alreadyTyped!
                0 => existingExpressionSearch!
                existingExpressionSearch! < (expressions! -> len) -> while {
                    (expressions![existingExpressionSearch!].sourceModule == compositeSourceIndex! and expressions![existingExpressionSearch!].astNode == compositeAstIndex!) -> if {
                        true => alreadyTyped!
                    }
                    existingExpressionSearch! + 1 => existingExpressionSearch!
                }
                ((compositeNode.kind == 23 or compositeNode.kind == 37 or compositeNode.kind == 38) and not alreadyTyped!) -> if {
                    1000000 => childDistance!
                    0 => childDistanceSearch!
                    childDistanceSearch! < (expressions! -> len) -> while {
                        expressions![childDistanceSearch!] => childCandidate
                        childCandidate.sourceModule == compositeSourceIndex! -> if {
                            compositeNodes![childCandidate.astNode].parent => childAncestor!
                            1 => distance!
                            false => belongsToComposite!
                            (childAncestor! >= 0 and not belongsToComposite!) -> while {
                                childAncestor! == compositeAstIndex! -> if { true => belongsToComposite! } else {
                                    compositeNodes![childAncestor!].parent => childAncestor!
                                    distance! + 1 => distance!
                                }
                            }
                            (belongsToComposite! and distance! < childDistance!) -> if { distance! => childDistance! }
                        }
                        childDistanceSearch! + 1 => childDistanceSearch!
                    }
                    -1 => firstChildType!
                    -1 => secondChildType!
                    0 => childPosition!
                    true => homogeneousComposite!
                    0 => childSearch!
                    childSearch! < (expressions! -> len) -> while {
                        expressions![childSearch!] => childCandidate
                        childCandidate.sourceModule == compositeSourceIndex! -> if {
                            compositeNodes![childCandidate.astNode].parent => childAncestor!
                            1 => distance!
                            false => belongsToComposite!
                            (childAncestor! >= 0 and not belongsToComposite!) -> while {
                                childAncestor! == compositeAstIndex! -> if { true => belongsToComposite! } else {
                                    compositeNodes![childAncestor!].parent => childAncestor!
                                    distance! + 1 => distance!
                                }
                            }
                            (belongsToComposite! and distance! == childDistance!) -> if {
                                compositeNode.kind == 38 -> if {
                                    childPosition! % 2 == 0 -> if {
                                        firstChildType! < 0 -> if { childCandidate.typeId => firstChildType! } else {
                                            childCandidate.typeId != firstChildType! -> if { false => homogeneousComposite! }
                                        }
                                    } else {
                                        secondChildType! < 0 -> if { childCandidate.typeId => secondChildType! } else {
                                            childCandidate.typeId != secondChildType! -> if { false => homogeneousComposite! }
                                        }
                                    }
                                } else {
                                    firstChildType! < 0 -> if { childCandidate.typeId => firstChildType! } else {
                                        childCandidate.typeId != firstChildType! -> if { false => homogeneousComposite! }
                                    }
                                }
                                childPosition! + 1 => childPosition!
                            }
                        }
                        childSearch! + 1 => childSearch!
                    }
                    false => dynamicArray!
                    compositeNode.kind == 37 -> if {
                        compositeNode.firstToken => compositeTokenIndex!
                        compositeTokenIndex! < compositeNode.firstToken + compositeNode.tokenCount -> while {
                            compositeTokens![compositeTokenIndex!].kind == grammar.tokenIdTilde -> if { true => dynamicArray! }
                            compositeTokenIndex! + 1 => compositeTokenIndex!
                        }
                    }
                    false => canBuildComposite!
                    -1 => compositeKind!
                    compositeNode.kind == 23 -> if {
                        firstChildType! >= 0 -> if {
                            6 => compositeKind!
                            true => canBuildComposite!
                        }
                    }
                    (compositeNode.kind == 37 and dynamicArray! and firstChildType! >= 0 and homogeneousComposite!) -> if {
                        3 => compositeKind!
                        true => canBuildComposite!
                    }
                    (compositeNode.kind == 38 and childPosition! > 0 and childPosition! % 2 == 0 and firstChildType! >= 0 and secondChildType! >= 0 and homogeneousComposite!) -> if {
                        5 => compositeKind!
                        true => canBuildComposite!
                    }
                    canBuildComposite! -> if {
                        -1 => existingType!
                        0 => typeSearch!
                        (typeSearch! < (types! -> len) and existingType! < 0) -> while {
                            types![typeSearch!] => known
                            (known.kind == compositeKind! and known.origin == -1 and known.module == -1 and known.symbol == -1 and known.first == firstChildType! and known.second == secondChildType! and known.lengthHash == UInt64(0) and known.status == 0) -> if {
                                typeSearch! => existingType!
                            }
                            typeSearch! + 1 => typeSearch!
                        }
                        existingType! < 0 -> if {
                            types! -> len => existingType!
                            firstChildType! >= 0 -> if { types![firstChildType!].containsParameter } else { false } => containsParameter
                            secondChildType! >= 0 -> if { types![secondChildType!].containsParameter } else { false } => secondContainsParameter
                            types! -> push(typeIds.SemanticType {
                                kind: compositeKind!
                                origin: -1
                                module: -1
                                symbol: -1
                                first: firstChildType!
                                second: secondChildType!
                                length: -1
                                lengthHash: UInt64(0)
                                containsParameter: containsParameter or secondContainsParameter
                                status: 0
                            })
                        }
                        expressions! -> push(ExpressionTypeId {
                            sourceModule: compositeSourceIndex!
                            astNode: compositeAstIndex!
                            typeId: existingType!
                            status: 0
                        })
                        true => compositeChanged!
                    }
                }
                compositeAstIndex! + 1 => compositeAstIndex!
            }
            compositeSourceIndex! + 1 => compositeSourceIndex!
        }
    }

    prepared.calls -> each call {
        (call.status == 0 and call.targetSourceModule >= 0) -> if {
            prepared.sources[call.targetSourceModule] -> symbols.collect => targetTable!
            targetTable![call.functionSymbol] => function
            function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode } else { function.typeNode } => returnTypeAst
            returnTypeAst >= 0 -> if {
                -1 => returnReference!
                0 => returnSearch!
                (returnSearch! < (references! -> len) and returnReference! < 0) -> while {
                    references![returnSearch!] => candidate
                    (candidate.sourceModule == call.targetSourceModule and candidate.typeAst == returnTypeAst) -> if {
                        returnSearch! => returnReference!
                    }
                    returnSearch! + 1 => returnSearch!
                }
                returnReference! >= 0 -> if {
                    references![returnReference!] => reference
                    reference.typeId => resultTypeId!
                    reference.status => resultStatus!
                    types![reference.typeId] => returnTemplateType
                    returnTemplateType.containsParameter => resultContainsParameter
                    resultContainsParameter -> if {
                        -1 => inputReference!
                        0 => inputReferenceSearch!
                        (inputReferenceSearch! < (references! -> len) and inputReference! < 0) -> while {
                            references![inputReferenceSearch!] => inputCandidate
                            (inputCandidate.sourceModule == call.targetSourceModule and inputCandidate.typeAst == function.typeNode) -> if {
                                inputReferenceSearch! => inputReference!
                            }
                            inputReferenceSearch! + 1 => inputReferenceSearch!
                        }
                        -1 => argumentExpression!
                        1000000 => argumentDistance!
                        prepared.sources[call.sourceModule] -> ast.lower => callNodes!
                        prepared.sources[call.sourceModule] -> lexer.lex => callTokens!
                        callNodes![call.callAst] => callNode
                        0 => argumentSearch!
                        argumentSearch! < (expressions! -> len) -> while {
                            expressions![argumentSearch!] => argumentCandidate
                            argumentCandidate.sourceModule == call.sourceModule -> if {
                                true => beforeRoleTarget!
                                callNode.kind == 48 -> if {
                                    callNodes![argumentCandidate.astNode] => argumentNode
                                    argumentNode.start + argumentNode.length > callTokens![callNode.payloadToken].span.start -> if { false => beforeRoleTarget! }
                                }
                                callNodes![argumentCandidate.astNode].parent => ancestor!
                                1 => distance!
                                false => belongsToCall!
                                (ancestor! >= 0 and not belongsToCall!) -> while {
                                    ancestor! == call.callAst -> if { true => belongsToCall! } else {
                                        callNodes![ancestor!].parent => ancestor!
                                        distance! + 1 => distance!
                                    }
                                }
                                (belongsToCall! and beforeRoleTarget! and distance! < argumentDistance!) -> if {
                                    argumentSearch! => argumentExpression!
                                    distance! => argumentDistance!
                                }
                            }
                            argumentSearch! + 1 => argumentSearch!
                        }
                        false => concreteArgument!
                        argumentExpression! >= 0 -> if {
                            expressions![argumentExpression!] => argumentTypeReference
                            types![argumentTypeReference.typeId] => argumentSemanticType
                            (argumentTypeReference.status == 0 and not argumentSemanticType.containsParameter) -> if { true => concreteArgument! }
                        }
                        (inputReference! >= 0 and concreteArgument!) -> if {
                            [typeIds.SemanticType; ~] => requestTypes!
                            types! -> each currentType {
                                requestTypes! -> push(currentType)
                            }
                            references![inputReference!].typeId => inputTemplateTypeId
                            expressions![argumentExpression!].typeId => actualInputTypeId
                            reference.typeId => resultTemplateTypeId
                            typeIds.SpecializationRequest {
                                types: requestTypes!
                                inputTemplate: inputTemplateTypeId
                                actualInput: actualInputTypeId
                                resultTemplate: resultTemplateTypeId
                            } => specializationRequest!
                            specializationRequest! -> typeIds.specialize => specialization
                            specialization.status == 0 -> if {
                                types! -> len => previousTypeCount
                                previousTypeCount => specializedTypeIndex!
                                specializedTypeIndex! < (specialization.types -> len) -> while {
                                    types! -> push(specialization.types[specializedTypeIndex!])
                                    specializedTypeIndex! + 1 => specializedTypeIndex!
                                }
                                specialization.root => resultTypeId!
                                0 => resultStatus!
                            }
                        }
                    }
                    expressions! -> push(ExpressionTypeId {
                        sourceModule: call.sourceModule
                        astNode: call.callAst
                        typeId: resultTypeId!
                        status: resultStatus!
                    })
                }
            }
        }
    }

    ExpressionTypeIdSet { types: types!, references: references!, fields: fields!, expressions: expressions! } => result!
    result!
}

public resolvePrepared request: move ExpressionTypeIdRequest -> ExpressionTypeIdSet {
    [Text; ~] => sources!
    request.sources -> each source { sources! -> push(source) }
    [typeIds.SemanticType; ~] => types!
    request.types -> each semanticType { types! -> push(semanticType) }
    [typeIds.TypeReference; ~] => references!
    request.references -> each reference { references! -> push(reference) }
    [typeIds.NominalField; ~] => fields!
    request.fields -> each field { fields! -> push(field) }
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
    [nominalTypes.NominalType; ~] => nominal!
    [compositeTypes.CompositeType; ~] => composite!
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
    prepared! -> resolveContext => result!
    result!
}

public resolve sources: [Text; ~] -> ExpressionTypeIdSet {
    sources -> semanticContext.prepare => prepared!
    prepared! -> resolveContext => result!
    result!
}
