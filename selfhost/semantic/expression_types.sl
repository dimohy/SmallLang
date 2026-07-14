namespace smalllang.compiler.semantic.expression_types

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.analysis as analysis
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.composite_types as compositeTypes
import smalllang.compiler.semantic.context as semanticContext
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

public struct ExpressionType {
    sourceModule: Int
    astNode: Int
    origin: Int
    targetModule: Int
    targetSymbol: Int
    keyOrigin: Int
    keyModule: Int
    valueOrigin: Int
    valueModule: Int
}

public struct ExpressionTypeRequest {
    sources: [Text; ~]
    nominal: [nominalTypes.NominalType; ~]
    composite: [compositeTypes.CompositeType; ~]
    qualified: [qualified.QualifiedResolution; ~]
    modules: [modules.ModuleIdentity; ~]
    calls: [calls.ModuleCallResolution; ~]
    analysisRanges: [analysis.SourceAnalysisRange; ~]
    analysisNodes: [ast.AstNode; ~]
    analysisTokens: [syntax.SyntaxToken; ~]
    analysisSymbols: [symbols.Symbol; ~]
    analysisNames: [resolution.ResolvedName; ~]
}

# Bottom-up expression inference over the flat AST. Builtin ids use the stable
# nominal table: Text 1, Int 2, Bool 23.
public inferContext prepared: semanticContext.CompilationContext -> [ExpressionType; ~] {
    [ExpressionType; ~] => inferred!
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
            node.kind == 13 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 1, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
            }
            node.kind == 14 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 2, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
            }
            node.kind == 44 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 0, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
            }
            node.kind == 45 -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 0, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
            }
            (node.kind == 46 or node.kind == 47) -> if {
                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 0, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
            }
            node.kind == 15 -> if {
                prepared.tokens[sourceRange.tokenStart + node.payloadToken] => nameToken
                false => booleanLiteral!
                nameToken.span.length == UIntSize(4) -> if {
                    source -> byte(nameToken.span.start) => boolByte0
                    source -> byte(nameToken.span.start + UIntSize(1)) => boolByte1
                    source -> byte(nameToken.span.start + UIntSize(2)) => boolByte2
                    source -> byte(nameToken.span.start + UIntSize(3)) => boolByte3
                    (boolByte0 == UInt8(116) and boolByte1 == UInt8(114) and boolByte2 == UInt8(117) and boolByte3 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                nameToken.span.length == UIntSize(5) -> if {
                    source -> byte(nameToken.span.start) => falseByte0
                    source -> byte(nameToken.span.start + UIntSize(1)) => falseByte1
                    source -> byte(nameToken.span.start + UIntSize(2)) => falseByte2
                    source -> byte(nameToken.span.start + UIntSize(3)) => falseByte3
                    source -> byte(nameToken.span.start + UIntSize(4)) => falseByte4
                    (falseByte0 == UInt8(102) and falseByte1 == UInt8(97) and falseByte2 == UInt8(108) and falseByte3 == UInt8(115) and falseByte4 == UInt8(101)) -> if { true => booleanLiteral! }
                }
                booleanLiteral! -> if {
                    inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: astIndex!, origin: 1, targetModule: -1, targetSymbol: 23, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
                }
                -1 => resolvedNameIndex!
                0 => nameSearch!
                nameSearch! < (resolvedNames! -> len) -> while {
                    resolvedNames![nameSearch!].astNode == astIndex! -> if { nameSearch! => resolvedNameIndex! }
                    nameSearch! + 1 => nameSearch!
                }
                (not booleanLiteral! and resolvedNameIndex! >= 0) -> if {
                    prepared.symbols[sourceRange.symbolStart + resolvedNames![resolvedNameIndex!].symbol] => valueSymbol
                    false => blockValueInferred!
                    (valueSymbol.kind == 35 and prepared.nodes[sourceRange.astStart + valueSymbol.astNode].kind == 48) -> if {
                        -1 => blockCallResolution!
                        0 => blockCallSearch!
                        blockCallSearch! < (prepared.calls -> len) -> while {
                            prepared.calls[blockCallSearch!] => blockCallCandidate
                            (blockCallCandidate.sourceModule == sourceIndex! and blockCallCandidate.callAst == valueSymbol.astNode and blockCallCandidate.status == 0) -> if {
                                blockCallSearch! => blockCallResolution!
                            }
                            blockCallSearch! + 1 => blockCallSearch!
                        }
                        blockCallResolution! >= 0 -> if {
                            prepared.calls[blockCallResolution!] => blockCall
                            prepared.sources[blockCall.targetSourceModule] -> symbols.collect => blockTargetTable!
                            blockTargetTable![blockCall.functionSymbol] => blockTargetFunction
                            blockTargetFunction.blockTypeNode >= 0 -> if {
                                -1 => blockNominalIndex!
                                0 => blockNominalSearch!
                                blockNominalSearch! < (prepared.nominal -> len) -> while {
                                    prepared.nominal[blockNominalSearch!] => blockNominalCandidate
                                    (blockNominalCandidate.sourceModule == blockCall.targetSourceModule and blockNominalCandidate.typeAst == blockTargetFunction.blockTypeNode) -> if {
                                        blockNominalSearch! => blockNominalIndex!
                                    }
                                    blockNominalSearch! + 1 => blockNominalSearch!
                                }
                                blockNominalIndex! >= 0 -> if {
                                    prepared.nominal[blockNominalIndex!] => blockValueType
                                    inferred! -> push(ExpressionType {
                                        sourceModule: sourceIndex!
                                        astNode: astIndex!
                                        origin: blockValueType.origin
                                        targetModule: blockValueType.targetModule
                                        targetSymbol: blockValueType.targetSymbol
                                        keyOrigin: -1
                                        keyModule: -1
                                        valueOrigin: -1
                                        valueModule: -1
                                    })
                                    true => blockValueInferred!
                                }
                                not blockValueInferred! -> if {
                                    0 => blockCompositeSearch!
                                    blockCompositeSearch! < (prepared.composite -> len) -> while {
                                        prepared.composite[blockCompositeSearch!] => blockComposite
                                        (blockComposite.sourceModule == blockCall.targetSourceModule and blockComposite.typeAst == blockTargetFunction.blockTypeNode) -> if {
                                            inferred! -> push(ExpressionType {
                                                sourceModule: sourceIndex!
                                                astNode: astIndex!
                                                origin: 10 + blockComposite.kind
                                                targetModule: blockComposite.kind == 5 -> if { blockComposite.keySymbol } else { blockComposite.elementModule }
                                                targetSymbol: blockComposite.kind == 5 -> if { blockComposite.valueSymbol } else { blockComposite.elementSymbol }
                                                keyOrigin: blockComposite.kind == 5 -> if { blockComposite.keyOrigin } else { -1 }
                                                keyModule: blockComposite.kind == 5 -> if { blockComposite.keyModule } else { -1 }
                                                valueOrigin: blockComposite.kind == 5 -> if { blockComposite.valueOrigin } else { -1 }
                                                valueModule: blockComposite.kind == 5 -> if { blockComposite.valueModule } else { -1 }
                                            })
                                            true => blockValueInferred!
                                        }
                                        blockCompositeSearch! + 1 => blockCompositeSearch!
                                    }
                                }
                            }
                        }
                    }
                    not blockValueInferred! -> if {
                    -1 => nominalIndex!
                    0 => typeSearch!
                    typeSearch! < (prepared.nominal -> len) -> while {
                        prepared.nominal[typeSearch!] => candidateType
                        (candidateType.sourceModule == sourceIndex! and candidateType.typeAst == valueSymbol.typeNode) -> if {
                            typeSearch! => nominalIndex!
                        }
                        typeSearch! + 1 => typeSearch!
                    }
                    nominalIndex! >= 0 -> if {
                        prepared.nominal[nominalIndex!] => valueType
                        inferred! -> push(ExpressionType {
                            sourceModule: sourceIndex!
                            astNode: astIndex!
                            origin: valueType.origin
                            targetModule: valueType.targetModule
                            targetSymbol: valueType.targetSymbol
                            keyOrigin: -1
                            keyModule: -1
                            valueOrigin: -1
                            valueModule: -1
                        })
                    } else {
                        -1 => valueCompositeIndex!
                        0 => valueCompositeSearch!
                        valueCompositeSearch! < (prepared.composite -> len) -> while {
                            prepared.composite[valueCompositeSearch!] => valueCompositeCandidate
                            (valueCompositeCandidate.sourceModule == sourceIndex! and valueCompositeCandidate.typeAst == valueSymbol.typeNode) -> if { valueCompositeSearch! => valueCompositeIndex! }
                            valueCompositeSearch! + 1 => valueCompositeSearch!
                        }
                        valueCompositeIndex! >= 0 -> if {
                            prepared.composite[valueCompositeIndex!] => valueComposite
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: astIndex!
                                origin: 10 + valueComposite.kind
                                targetModule: valueComposite.kind == 5 -> if { valueComposite.keySymbol } else { valueComposite.elementModule }
                                targetSymbol: valueComposite.kind == 5 -> if { valueComposite.valueSymbol } else { valueComposite.elementSymbol }
                                keyOrigin: valueComposite.kind == 5 -> if { valueComposite.keyOrigin } else { -1 }
                                keyModule: valueComposite.kind == 5 -> if { valueComposite.keyModule } else { -1 }
                                valueOrigin: valueComposite.kind == 5 -> if { valueComposite.valueOrigin } else { -1 }
                                valueModule: valueComposite.kind == 5 -> if { valueComposite.valueModule } else { -1 }
                            })
                        }
                    }
                    }
                }
            }
            node.kind == 39 -> if {
                -1 => typeNameToken!
                node.firstToken => structTokenIndex!
                (structTokenIndex! < node.firstToken + node.tokenCount and typeNameToken! < 0) -> while {
                    prepared.tokens[sourceRange.tokenStart + structTokenIndex!].kind == grammar.tokenIdIdentifier -> if { structTokenIndex! => typeNameToken! }
                    structTokenIndex! + 1 => structTokenIndex!
                }
                -1 => structSymbol!
                0 => structSearch!
                (structSearch! < sourceRange.symbolCount and structSymbol! < 0) -> while {
                    prepared.symbols[sourceRange.symbolStart + structSearch!] => candidateStruct
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
                        equal! -> if { structSearch! => structSymbol! }
                    }
                    structSearch! + 1 => structSearch!
                }
                structSymbol! >= 0 -> if {
                    inferred! -> push(ExpressionType {
                        sourceModule: sourceIndex!
                        astNode: astIndex!
                        origin: 0
                        targetModule: sourceIndex!
                        targetSymbol: structSymbol!
                        keyOrigin: -1
                        keyModule: -1
                        valueOrigin: -1
                        valueModule: -1
                    })
                } else {
                    -1 => importedStructIndex!
                    0 => qualifiedSearch!
                    qualifiedSearch! < (prepared.qualified -> len) -> while {
                        prepared.qualified[qualifiedSearch!] => importedCandidate
                        importedCandidate.sourceModule == sourceIndex! -> if {
                            importedCandidate.pathAst => importedAncestor!
                            false => belongsToStructLiteral!
                            (importedAncestor! >= 0 and not belongsToStructLiteral!) -> while {
                                importedAncestor! == astIndex! -> if { true => belongsToStructLiteral! } else {
                                    prepared.nodes[sourceRange.astStart + importedAncestor!].parent => importedAncestor!
                                }
                            }
                            (belongsToStructLiteral! and importedCandidate.status == 0) -> if { qualifiedSearch! => importedStructIndex! }
                        }
                        qualifiedSearch! + 1 => qualifiedSearch!
                    }
                    importedStructIndex! >= 0 -> if {
                        prepared.qualified[importedStructIndex!] => importedStruct
                        inferred! -> push(ExpressionType {
                            sourceModule: sourceIndex!
                            astNode: astIndex!
                            origin: 2
                            targetModule: importedStruct.targetModule
                            targetSymbol: importedStruct.targetSymbol
                            keyOrigin: -1
                            keyModule: -1
                            valueOrigin: -1
                            valueModule: -1
                        })
                    }
                }
            }
            astIndex! + 1 => astIndex!
        }

        true => changed!
        changed! -> while {
            false => changed!
            0 => callIndex!
            callIndex! < (prepared.calls -> len) -> while {
                prepared.calls[callIndex!] => call
                (call.sourceModule == sourceIndex! and call.status == 0) -> if {
                    false => callInferred!
                    0 => callExistingIndex!
                    callExistingIndex! < (inferred! -> len) -> while {
                        inferred![callExistingIndex!] => callExisting
                        (callExisting.sourceModule == sourceIndex! and callExisting.astNode == call.callAst) -> if { true => callInferred! }
                        callExistingIndex! + 1 => callExistingIndex!
                    }
                    not callInferred! -> if {
                        call.origin == 2 -> if {
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: call.callAst
                                origin: 1
                                targetModule: -1
                                targetSymbol: 0
                                keyOrigin: -1
                                keyModule: -1
                                valueOrigin: -1
                                valueModule: -1
                            })
                            true => changed!
                        } else {
                        prepared.sources[call.targetSourceModule] -> symbols.collect => targetTable!
                        targetTable![call.functionSymbol] => function
                        function.secondaryTypeNode >= 0 -> if { function.secondaryTypeNode } else { function.typeNode } => returnTypeAst
                        -1 => returnNominalIndex!
                        0 => returnSearch!
                        returnSearch! < (prepared.nominal -> len) -> while {
                            prepared.nominal[returnSearch!] => candidateReturn
                            (candidateReturn.sourceModule == call.targetSourceModule and candidateReturn.typeAst == returnTypeAst) -> if {
                                returnSearch! => returnNominalIndex!
                            }
                            returnSearch! + 1 => returnSearch!
                        }
                        returnNominalIndex! >= 0 -> if {
                            prepared.nominal[returnNominalIndex!] => returnType
                            returnType.origin => resultOrigin!
                            returnType.targetModule => resultModule!
                            returnType.targetSymbol => resultSymbol!
                            returnType.origin != 3 => canInferCall!
                            returnType.origin == 3 -> if {
                                function.secondaryTypeNode >= 0 -> if {
                                    -1 => inputNominalIndex!
                                    0 => inputSearch!
                                    inputSearch! < (prepared.nominal -> len) -> while {
                                        prepared.nominal[inputSearch!] => candidateInput
                                        (candidateInput.sourceModule == call.targetSourceModule and candidateInput.typeAst == function.typeNode) -> if {
                                            inputSearch! => inputNominalIndex!
                                        }
                                        inputSearch! + 1 => inputSearch!
                                    }
                                    inputNominalIndex! >= 0 -> if {
                                        prepared.nominal[inputNominalIndex!] => inputType
                                        (inputType.origin == 3 and inputType.targetSymbol == returnType.targetSymbol) -> if {
                                            -1 => argumentTypeIndex!
                                            1000000 => argumentDistance!
                                            0 => argumentSearch!
                                            argumentSearch! < (inferred! -> len) -> while {
                                                inferred![argumentSearch!] => argumentType
                                                argumentType.sourceModule == sourceIndex! -> if {
                                                    true => beforeRoleTarget!
                                                    prepared.nodes[sourceRange.astStart + call.callAst] => genericCallNode
                                                    genericCallNode.kind == 48 -> if {
                                                        prepared.nodes[sourceRange.astStart + argumentType.astNode] => genericArgumentNode
                                                        genericArgumentNode.start + genericArgumentNode.length > prepared.tokens[sourceRange.tokenStart + genericCallNode.payloadToken].span.start -> if {
                                                            false => beforeRoleTarget!
                                                        }
                                                    }
                                                    prepared.nodes[sourceRange.astStart + argumentType.astNode].parent => argumentAncestor!
                                                    1 => distance!
                                                    false => belongsToCall!
                                                    (argumentAncestor! >= 0 and not belongsToCall!) -> while {
                                                        argumentAncestor! == call.callAst -> if { true => belongsToCall! } else {
                                                            prepared.nodes[sourceRange.astStart + argumentAncestor!].parent => argumentAncestor!
                                                            distance! + 1 => distance!
                                                        }
                                                    }
                                                    (belongsToCall! and beforeRoleTarget! and distance! < argumentDistance!) -> if {
                                                        argumentSearch! => argumentTypeIndex!
                                                        distance! => argumentDistance!
                                                    }
                                                }
                                                argumentSearch! + 1 => argumentSearch!
                                            }
                                            argumentTypeIndex! >= 0 -> if {
                                                inferred![argumentTypeIndex!] => specialized
                                                specialized.origin => resultOrigin!
                                                specialized.targetModule => resultModule!
                                                specialized.targetSymbol => resultSymbol!
                                                true => canInferCall!
                                            }
                                        }
                                    }
                                }
                            }
                            canInferCall! -> if {
                                inferred! -> push(ExpressionType {
                                    sourceModule: sourceIndex!
                                    astNode: call.callAst
                                    origin: resultOrigin!
                                    targetModule: resultModule!
                                    targetSymbol: resultSymbol!
                                    keyOrigin: -1
                                    keyModule: -1
                                    valueOrigin: -1
                                    valueModule: -1
                                })
                                true => changed!
                            }
                        } else {
                            -1 => returnCompositeIndex!
                            -1 => inputCompositeIndex!
                            0 => compositeSearch!
                            compositeSearch! < (prepared.composite -> len) -> while {
                                prepared.composite[compositeSearch!] => candidateComposite
                                (candidateComposite.sourceModule == call.targetSourceModule and candidateComposite.typeAst == returnTypeAst) -> if {
                                    compositeSearch! => returnCompositeIndex!
                                }
                                (function.secondaryTypeNode >= 0 and candidateComposite.sourceModule == call.targetSourceModule and candidateComposite.typeAst == function.typeNode) -> if {
                                    compositeSearch! => inputCompositeIndex!
                                }
                                compositeSearch! + 1 => compositeSearch!
                            }
                            (returnCompositeIndex! >= 0 and inputCompositeIndex! >= 0) -> if {
                                prepared.composite[returnCompositeIndex!] => returnComposite
                                prepared.composite[inputCompositeIndex!] => inputComposite
                                false => sameCompositeGenerics!
                                returnComposite.kind == inputComposite.kind -> if {
                                    returnComposite.kind == 5 -> if {
                                        (returnComposite.keyOrigin == 3 and inputComposite.keyOrigin == 3 and returnComposite.valueOrigin == 3 and inputComposite.valueOrigin == 3 and returnComposite.keySymbol == inputComposite.keySymbol and returnComposite.valueSymbol == inputComposite.valueSymbol) -> if { true => sameCompositeGenerics! }
                                    } else {
                                        (returnComposite.elementOrigin == 3 and inputComposite.elementOrigin == 3 and returnComposite.elementSymbol == inputComposite.elementSymbol) -> if { true => sameCompositeGenerics! }
                                    }
                                }
                                sameCompositeGenerics! -> if {
                                    -1 => compositeArgumentIndex!
                                    1000000 => compositeArgumentDistance!
                                    0 => compositeArgumentSearch!
                                    compositeArgumentSearch! < (inferred! -> len) -> while {
                                        inferred![compositeArgumentSearch!] => argumentType
                                        argumentType.sourceModule == sourceIndex! -> if {
                                            true => compositeBeforeRoleTarget!
                                            prepared.nodes[sourceRange.astStart + call.callAst] => compositeCallNode
                                            compositeCallNode.kind == 48 -> if {
                                                prepared.nodes[sourceRange.astStart + argumentType.astNode] => compositeArgumentNode
                                                compositeArgumentNode.start + compositeArgumentNode.length > prepared.tokens[sourceRange.tokenStart + compositeCallNode.payloadToken].span.start -> if {
                                                    false => compositeBeforeRoleTarget!
                                                }
                                            }
                                            prepared.nodes[sourceRange.astStart + argumentType.astNode].parent => argumentAncestor!
                                            1 => distance!
                                            false => belongsToCall!
                                            (argumentAncestor! >= 0 and not belongsToCall!) -> while {
                                                argumentAncestor! == call.callAst -> if { true => belongsToCall! } else {
                                                    prepared.nodes[sourceRange.astStart + argumentAncestor!].parent => argumentAncestor!
                                                    distance! + 1 => distance!
                                                }
                                            }
                                            (belongsToCall! and compositeBeforeRoleTarget! and distance! < compositeArgumentDistance!) -> if {
                                                compositeArgumentSearch! => compositeArgumentIndex!
                                                distance! => compositeArgumentDistance!
                                            }
                                        }
                                        compositeArgumentSearch! + 1 => compositeArgumentSearch!
                                    }
                                    compositeArgumentIndex! >= 0 -> if {
                                        inferred![compositeArgumentIndex!] => specializedComposite
                                        specializedComposite.origin == 10 + inputComposite.kind -> if {
                                            inferred! -> push(ExpressionType {
                                                sourceModule: sourceIndex!
                                                astNode: call.callAst
                                                origin: specializedComposite.origin
                                                targetModule: specializedComposite.targetModule
                                                targetSymbol: specializedComposite.targetSymbol
                                                keyOrigin: specializedComposite.keyOrigin
                                                keyModule: specializedComposite.keyModule
                                                valueOrigin: specializedComposite.valueOrigin
                                                valueModule: specializedComposite.valueModule
                                            })
                                            true => changed!
                                        }
                                    }
                                }
                            }
                        }
                        }
                    }
                }
                callIndex! + 1 => callIndex!
            }
            # A generic role item is fixed from the source before the caller
            # body is checked. This is deliberately outside-in: body uses do
            # not participate in selecting a type argument.
            0 => roleReferenceIndex!
            roleReferenceIndex! < (resolvedNames! -> len) -> while {
                resolvedNames![roleReferenceIndex!] => roleReference
                prepared.symbols[sourceRange.symbolStart + roleReference.symbol] => roleValueSymbol
                (roleValueSymbol.kind == 35 and prepared.nodes[sourceRange.astStart + roleValueSymbol.astNode].kind == 48) -> if {
                    -1 => roleValueTypeIndex!
                    0 => roleValueTypeSearch!
                    roleValueTypeSearch! < (inferred! -> len) -> while {
                        inferred![roleValueTypeSearch!] => roleValueTypeCandidate
                        (roleValueTypeCandidate.sourceModule == sourceIndex! and roleValueTypeCandidate.astNode == roleReference.astNode) -> if {
                            roleValueTypeSearch! => roleValueTypeIndex!
                        }
                        roleValueTypeSearch! + 1 => roleValueTypeSearch!
                    }
                    -1 => roleCallIndex!
                    0 => roleCallSearch!
                    roleCallSearch! < (prepared.calls -> len) -> while {
                        prepared.calls[roleCallSearch!] => roleCallCandidate
                        (roleCallCandidate.sourceModule == sourceIndex! and roleCallCandidate.callAst == roleValueSymbol.astNode and roleCallCandidate.status == 0 and roleCallCandidate.targetSourceModule >= 0) -> if {
                            roleCallSearch! => roleCallIndex!
                        }
                        roleCallSearch! + 1 => roleCallSearch!
                    }
                    (roleValueTypeIndex! >= 0 and roleCallIndex! >= 0) -> if {
                        prepared.calls[roleCallIndex!] => roleCall
                        prepared.sources[roleCall.targetSourceModule] -> symbols.collect => roleTargetTable!
                        roleTargetTable![roleCall.functionSymbol] => roleFunction
                        (roleFunction.secondaryTypeNode >= 0 and roleFunction.blockTypeNode >= 0) -> if {
                            -1 => roleSourceTypeIndex!
                            1000000 => roleSourceDistance!
                            0 => roleSourceSearch!
                            roleSourceSearch! < (inferred! -> len) -> while {
                                inferred![roleSourceSearch!] => roleSourceTypeCandidate
                                roleSourceTypeCandidate.sourceModule == sourceIndex! -> if {
                                    prepared.nodes[sourceRange.astStart + roleSourceTypeCandidate.astNode] => roleSourceNode
                                    roleSourceNode.start + roleSourceNode.length <= prepared.tokens[sourceRange.tokenStart + prepared.nodes[sourceRange.astStart + roleCall.callAst].payloadToken].span.start -> if {
                                        roleSourceNode.parent => roleSourceAncestor!
                                        1 => roleDistance!
                                        false => belongsToRole!
                                        (roleSourceAncestor! >= 0 and not belongsToRole!) -> while {
                                            roleSourceAncestor! == roleCall.callAst -> if {
                                                true => belongsToRole!
                                            } else {
                                                prepared.nodes[sourceRange.astStart + roleSourceAncestor!].parent => roleSourceAncestor!
                                                roleDistance! + 1 => roleDistance!
                                            }
                                        }
                                        (belongsToRole! and roleDistance! < roleSourceDistance!) -> if {
                                            roleSourceSearch! => roleSourceTypeIndex!
                                            roleDistance! => roleSourceDistance!
                                        }
                                    }
                                }
                                roleSourceSearch! + 1 => roleSourceSearch!
                            }
                            roleSourceTypeIndex! >= 0 -> if {
                                inferred![roleSourceTypeIndex!] => roleSourceType
                                -1 => roleInputNominalIndex!
                                -1 => roleBlockNominalIndex!
                                0 => roleNominalSearch!
                                roleNominalSearch! < (prepared.nominal -> len) -> while {
                                    prepared.nominal[roleNominalSearch!] => roleNominal
                                    (roleNominal.sourceModule == roleCall.targetSourceModule and roleNominal.typeAst == roleFunction.typeNode) -> if { roleNominalSearch! => roleInputNominalIndex! }
                                    (roleNominal.sourceModule == roleCall.targetSourceModule and roleNominal.typeAst == roleFunction.blockTypeNode) -> if { roleNominalSearch! => roleBlockNominalIndex! }
                                    roleNominalSearch! + 1 => roleNominalSearch!
                                }
                                -1 => roleInputCompositeIndex!
                                -1 => roleBlockCompositeIndex!
                                0 => roleCompositeSearch!
                                roleCompositeSearch! < (prepared.composite -> len) -> while {
                                    prepared.composite[roleCompositeSearch!] => roleComposite
                                    (roleComposite.sourceModule == roleCall.targetSourceModule and roleComposite.typeAst == roleFunction.typeNode) -> if { roleCompositeSearch! => roleInputCompositeIndex! }
                                    (roleComposite.sourceModule == roleCall.targetSourceModule and roleComposite.typeAst == roleFunction.blockTypeNode) -> if { roleCompositeSearch! => roleBlockCompositeIndex! }
                                    roleCompositeSearch! + 1 => roleCompositeSearch!
                                }

                                -1 => specializedRoleOrigin!
                                -1 => specializedRoleModule!
                                -1 => specializedRoleSymbol!
                                -1 => specializedRoleKeyOrigin!
                                -1 => specializedRoleKeyModule!
                                -1 => specializedRoleValueOrigin!
                                -1 => specializedRoleValueModule!

                                roleBlockNominalIndex! >= 0 -> if {
                                    prepared.nominal[roleBlockNominalIndex!] => roleBlockNominal
                                    roleBlockNominal.origin == 3 -> if {
                                        roleInputNominalIndex! >= 0 -> if {
                                            prepared.nominal[roleInputNominalIndex!] => roleInputNominal
                                            (roleInputNominal.origin == 3 and roleInputNominal.targetSymbol == roleBlockNominal.targetSymbol) -> if {
                                                roleSourceType.origin => specializedRoleOrigin!
                                                roleSourceType.targetModule => specializedRoleModule!
                                                roleSourceType.targetSymbol => specializedRoleSymbol!
                                                roleSourceType.keyOrigin => specializedRoleKeyOrigin!
                                                roleSourceType.keyModule => specializedRoleKeyModule!
                                                roleSourceType.valueOrigin => specializedRoleValueOrigin!
                                                roleSourceType.valueModule => specializedRoleValueModule!
                                            }
                                        }
                                        roleInputCompositeIndex! >= 0 -> if {
                                            prepared.composite[roleInputCompositeIndex!] => roleInputComposite
                                            roleInputComposite.kind == 5 -> if {
                                                (roleInputComposite.keyOrigin == 3 and roleInputComposite.keySymbol == roleBlockNominal.targetSymbol and roleSourceType.origin == 15) -> if {
                                                    roleSourceType.keyOrigin => specializedRoleOrigin!
                                                    roleSourceType.keyModule => specializedRoleModule!
                                                    roleSourceType.targetModule => specializedRoleSymbol!
                                                }
                                                (roleInputComposite.valueOrigin == 3 and roleInputComposite.valueSymbol == roleBlockNominal.targetSymbol and roleSourceType.origin == 15) -> if {
                                                    roleSourceType.valueOrigin => specializedRoleOrigin!
                                                    roleSourceType.valueModule => specializedRoleModule!
                                                    roleSourceType.targetSymbol => specializedRoleSymbol!
                                                }
                                            } else {
                                                (roleInputComposite.elementOrigin == 3 and roleInputComposite.elementSymbol == roleBlockNominal.targetSymbol and roleSourceType.origin == 10 + roleInputComposite.kind) -> if {
                                                    roleSourceType.targetModule == -1 -> if { 1 } else {
                                                        roleSourceType.targetModule == sourceIndex! -> if { 0 } else { 2 }
                                                    } => specializedRoleOrigin!
                                                    roleSourceType.targetModule => specializedRoleModule!
                                                    roleSourceType.targetSymbol => specializedRoleSymbol!
                                                }
                                            }
                                        }
                                    }
                                }
                                (specializedRoleOrigin! < 0 and roleInputNominalIndex! >= 0 and roleBlockCompositeIndex! >= 0) -> if {
                                    prepared.nominal[roleInputNominalIndex!] => roleInputNominal
                                    prepared.composite[roleBlockCompositeIndex!] => roleBlockComposite
                                    (roleInputNominal.origin == 3 and roleBlockComposite.kind != 5 and roleBlockComposite.elementOrigin == 3 and roleInputNominal.targetSymbol == roleBlockComposite.elementSymbol) -> if {
                                        10 + roleBlockComposite.kind => specializedRoleOrigin!
                                        roleSourceType.targetModule => specializedRoleModule!
                                        roleSourceType.targetSymbol => specializedRoleSymbol!
                                    }
                                }
                                (specializedRoleOrigin! < 0 and roleBlockCompositeIndex! >= 0 and roleInputCompositeIndex! >= 0) -> if {
                                    prepared.composite[roleBlockCompositeIndex!] => roleBlockComposite
                                    prepared.composite[roleInputCompositeIndex!] => roleInputComposite
                                    false => sameRoleComposite!
                                    (roleBlockComposite.kind == roleInputComposite.kind and roleSourceType.origin == 10 + roleInputComposite.kind) -> if {
                                        roleInputComposite.kind == 5 -> if {
                                            (roleInputComposite.keyOrigin == 3 and roleBlockComposite.keyOrigin == 3 and roleInputComposite.keySymbol == roleBlockComposite.keySymbol and roleInputComposite.valueOrigin == 3 and roleBlockComposite.valueOrigin == 3 and roleInputComposite.valueSymbol == roleBlockComposite.valueSymbol) -> if {
                                                true => sameRoleComposite!
                                            }
                                        } else {
                                            (roleInputComposite.elementOrigin == 3 and roleBlockComposite.elementOrigin == 3 and roleInputComposite.elementSymbol == roleBlockComposite.elementSymbol) -> if {
                                                true => sameRoleComposite!
                                            }
                                        }
                                    }
                                    sameRoleComposite! -> if {
                                        roleSourceType.origin => specializedRoleOrigin!
                                        roleSourceType.targetModule => specializedRoleModule!
                                        roleSourceType.targetSymbol => specializedRoleSymbol!
                                        roleSourceType.keyOrigin => specializedRoleKeyOrigin!
                                        roleSourceType.keyModule => specializedRoleKeyModule!
                                        roleSourceType.valueOrigin => specializedRoleValueOrigin!
                                        roleSourceType.valueModule => specializedRoleValueModule!
                                    }
                                }
                                specializedRoleOrigin! >= 0 -> if {
                                    inferred![roleValueTypeIndex!] => currentRoleType!
                                    (currentRoleType!.origin != specializedRoleOrigin! or currentRoleType!.targetModule != specializedRoleModule! or currentRoleType!.targetSymbol != specializedRoleSymbol! or currentRoleType!.keyOrigin != specializedRoleKeyOrigin! or currentRoleType!.keyModule != specializedRoleKeyModule! or currentRoleType!.valueOrigin != specializedRoleValueOrigin! or currentRoleType!.valueModule != specializedRoleValueModule!) -> if {
                                        specializedRoleOrigin! => currentRoleType!.origin
                                        specializedRoleModule! => currentRoleType!.targetModule
                                        specializedRoleSymbol! => currentRoleType!.targetSymbol
                                        specializedRoleKeyOrigin! => currentRoleType!.keyOrigin
                                        specializedRoleKeyModule! => currentRoleType!.keyModule
                                        specializedRoleValueOrigin! => currentRoleType!.valueOrigin
                                        specializedRoleValueModule! => currentRoleType!.valueModule
                                        currentRoleType! => inferred![roleValueTypeIndex!]
                                        true => changed!
                                    }
                                }
                            }
                        }
                    }
                }
                roleReferenceIndex! + 1 => roleReferenceIndex!
            }
            0 => bindingSymbolIndex!
            bindingSymbolIndex! < sourceRange.symbolCount -> while {
                prepared.symbols[sourceRange.symbolStart + bindingSymbolIndex!] => bindingSymbol
                bindingSymbol.kind == 9 -> if {
                    -1 => bindingValueIndex!
                    1000000 => bindingDistance!
                    0 => valueSearch!
                    valueSearch! < (inferred! -> len) -> while {
                        inferred![valueSearch!] => valueType
                        valueType.sourceModule == sourceIndex! -> if {
                            prepared.nodes[sourceRange.astStart + valueType.astNode].parent => ancestor!
                            1 => distance!
                            valueType.astNode == bindingSymbol.astNode => belongsToBinding!
                            belongsToBinding! -> if { 0 => distance! }
                            (ancestor! >= 0 and not belongsToBinding!) -> while {
                                ancestor! == bindingSymbol.astNode -> if {
                                    true => belongsToBinding!
                                } else {
                                    prepared.nodes[sourceRange.astStart + ancestor!].parent => ancestor!
                                    distance! + 1 => distance!
                                }
                            }
                            (belongsToBinding! and distance! < bindingDistance!) -> if {
                                valueSearch! => bindingValueIndex!
                                distance! => bindingDistance!
                            }
                        }
                        valueSearch! + 1 => valueSearch!
                    }
                    bindingValueIndex! >= 0 -> if {
                        inferred![bindingValueIndex!] => bindingType
                        0 => referenceIndex!
                        referenceIndex! < (resolvedNames! -> len) -> while {
                            resolvedNames![referenceIndex!] => reference
                            reference.symbol == bindingSymbolIndex! -> if {
                                false => referenceInferred!
                                -1 => referenceInferredIndex!
                                0 => inferredSearch!
                                inferredSearch! < (inferred! -> len) -> while {
                                    inferred![inferredSearch!] => existing
                                    (existing.sourceModule == sourceIndex! and existing.astNode == reference.astNode) -> if {
                                        true => referenceInferred!
                                        inferredSearch! => referenceInferredIndex!
                                    }
                                    inferredSearch! + 1 => inferredSearch!
                                }
                                not referenceInferred! -> if {
                                    inferred! -> push(ExpressionType {
                                        sourceModule: sourceIndex!
                                        astNode: reference.astNode
                                        origin: bindingType.origin
                                        targetModule: bindingType.targetModule
                                        targetSymbol: bindingType.targetSymbol
                                        keyOrigin: bindingType.keyOrigin
                                        keyModule: bindingType.keyModule
                                        valueOrigin: bindingType.valueOrigin
                                        valueModule: bindingType.valueModule
                                    })
                                    true => changed!
                                } else {
                                    inferred![referenceInferredIndex!] => existingReference!
                                    (existingReference!.origin != bindingType.origin or existingReference!.targetModule != bindingType.targetModule or existingReference!.targetSymbol != bindingType.targetSymbol or existingReference!.keyOrigin != bindingType.keyOrigin or existingReference!.keyModule != bindingType.keyModule or existingReference!.valueOrigin != bindingType.valueOrigin or existingReference!.valueModule != bindingType.valueModule) -> if {
                                        bindingType.origin => existingReference!.origin
                                        bindingType.targetModule => existingReference!.targetModule
                                        bindingType.targetSymbol => existingReference!.targetSymbol
                                        bindingType.keyOrigin => existingReference!.keyOrigin
                                        bindingType.keyModule => existingReference!.keyModule
                                        bindingType.valueOrigin => existingReference!.valueOrigin
                                        bindingType.valueModule => existingReference!.valueModule
                                        existingReference! => inferred![referenceInferredIndex!]
                                        true => changed!
                                    }
                                }
                            }
                            referenceIndex! + 1 => referenceIndex!
                        }
                    }
                }
                bindingSymbolIndex! + 1 => bindingSymbolIndex!
            }
            0 => memberIndex!
            memberIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + memberIndex!] => member
                member.kind == 36 -> if {
                    false => memberInferred!
                    0 => memberExistingIndex!
                    memberExistingIndex! < (inferred! -> len) -> while {
                        inferred![memberExistingIndex!] => existingMember
                        (existingMember.sourceModule == sourceIndex! and existingMember.astNode == memberIndex!) -> if { true => memberInferred! }
                        memberExistingIndex! + 1 => memberExistingIndex!
                    }
                    not memberInferred! -> if {
                        -1 => baseTypeIndex!
                        1000000 => baseDistance!
                        0 => baseSearch!
                        baseSearch! < (inferred! -> len) -> while {
                            inferred![baseSearch!] => baseType
                            baseType.sourceModule == sourceIndex! -> if {
                                prepared.nodes[sourceRange.astStart + baseType.astNode].parent => baseAncestor!
                                1 => distance!
                                false => belongsToMember!
                                (baseAncestor! >= 0 and not belongsToMember!) -> while {
                                    baseAncestor! == memberIndex! -> if { true => belongsToMember! } else {
                                        prepared.nodes[sourceRange.astStart + baseAncestor!].parent => baseAncestor!
                                        distance! + 1 => distance!
                                    }
                                }
                                (belongsToMember! and distance! < baseDistance!) -> if {
                                    baseSearch! => baseTypeIndex!
                                    distance! => baseDistance!
                                }
                            }
                            baseSearch! + 1 => baseSearch!
                        }
                        baseTypeIndex! >= 0 -> if {
                            inferred![baseTypeIndex!] => baseType
                            ((baseType.origin == 0 or baseType.origin == 2) and prepared.nodes[sourceRange.astStart + baseType.astNode].kind != 39) -> if {
                                baseType.origin => memberCurrentOrigin!
                                baseType.targetModule => memberCurrentModule!
                                baseType.targetSymbol => memberCurrentSymbol!
                                baseType.keyOrigin => memberCurrentKeyOrigin!
                                baseType.keyModule => memberCurrentKeyModule!
                                baseType.valueOrigin => memberCurrentValueOrigin!
                                baseType.valueModule => memberCurrentValueModule!
                                true => memberPathValid!
                                0 => memberIdentifierOrdinal!
                                member.firstToken => memberTokenIndex!
                                (memberPathValid! and memberTokenIndex! < member.firstToken + member.tokenCount) -> while {
                                    prepared.tokens[sourceRange.tokenStart + memberTokenIndex!].kind == grammar.tokenIdIdentifier -> if {
                                        memberIdentifierOrdinal! > 0 -> if {
                                            memberCurrentModule! => targetSourceModule!
                                            memberCurrentOrigin! == 2 -> if { prepared.modules[memberCurrentModule!].sourceIndex => targetSourceModule! }
                                            prepared.sources[targetSourceModule!] -> symbols.collect => targetTable!
                                            prepared.sources[targetSourceModule!] -> lexer.lex => targetTokens!
                                            -1 => fieldSymbol!
                                            0 => fieldSearch!
                                            (fieldSearch! < (targetTable! -> len) and fieldSymbol! < 0) -> while {
                                                targetTable![fieldSearch!] => field
                                                (field.kind == 26 and field.parent == memberCurrentSymbol!) -> if {
                                                    prepared.tokens[sourceRange.tokenStart + memberTokenIndex!] => memberName
                                                    targetTokens![field.nameToken] => fieldName
                                                    memberName.span.length == fieldName.span.length => equal!
                                                    UIntSize(0) => fieldByte!
                                                    (equal! and fieldByte! < memberName.span.length) -> while {
                                                        source -> byte(memberName.span.start + fieldByte!) => leftByte
                                                        prepared.sources[targetSourceModule!] -> byte(fieldName.span.start + fieldByte!) => rightByte
                                                        leftByte != rightByte -> if { false => equal! }
                                                        fieldByte! + UIntSize(1) => fieldByte!
                                                    }
                                                    equal! -> if { fieldSearch! => fieldSymbol! }
                                                }
                                                fieldSearch! + 1 => fieldSearch!
                                            }
                                            fieldSymbol! >= 0 -> if {
                                                targetTable![fieldSymbol!] => field
                                                -1 => fieldNominalIndex!
                                                -1 => fieldCompositeIndex!
                                                0 => fieldTypeSearch!
                                                fieldTypeSearch! < (prepared.nominal -> len) -> while {
                                                    prepared.nominal[fieldTypeSearch!] => fieldType
                                                    (fieldType.sourceModule == targetSourceModule! and fieldType.typeAst == field.typeNode) -> if { fieldTypeSearch! => fieldNominalIndex! }
                                                    fieldTypeSearch! + 1 => fieldTypeSearch!
                                                }
                                                0 => fieldCompositeSearch!
                                                fieldCompositeSearch! < (prepared.composite -> len) -> while {
                                                    prepared.composite[fieldCompositeSearch!] => fieldCompositeCandidate
                                                    (fieldCompositeCandidate.sourceModule == targetSourceModule! and fieldCompositeCandidate.typeAst == field.typeNode) -> if { fieldCompositeSearch! => fieldCompositeIndex! }
                                                    fieldCompositeSearch! + 1 => fieldCompositeSearch!
                                                }
                                                fieldNominalIndex! >= 0 -> if {
                                                    prepared.nominal[fieldNominalIndex!] => fieldType
                                                    fieldType.origin => memberCurrentOrigin!
                                                    fieldType.targetModule => memberCurrentModule!
                                                    fieldType.targetSymbol => memberCurrentSymbol!
                                                    -1 => memberCurrentKeyOrigin!
                                                    -1 => memberCurrentKeyModule!
                                                    -1 => memberCurrentValueOrigin!
                                                    -1 => memberCurrentValueModule!
                                                } else {
                                                    fieldCompositeIndex! >= 0 -> if {
                                                        prepared.composite[fieldCompositeIndex!] => fieldType
                                                        10 + fieldType.kind => memberCurrentOrigin!
                                                        fieldType.kind == 5 -> if { fieldType.keySymbol => memberCurrentModule! } else { fieldType.elementModule => memberCurrentModule! }
                                                        fieldType.kind == 5 -> if { fieldType.valueSymbol => memberCurrentSymbol! } else { fieldType.elementSymbol => memberCurrentSymbol! }
                                                        fieldType.kind == 5 -> if { fieldType.keyOrigin => memberCurrentKeyOrigin! } else { -1 => memberCurrentKeyOrigin! }
                                                        fieldType.kind == 5 -> if { fieldType.keyModule => memberCurrentKeyModule! } else { -1 => memberCurrentKeyModule! }
                                                        fieldType.kind == 5 -> if { fieldType.valueOrigin => memberCurrentValueOrigin! } else { -1 => memberCurrentValueOrigin! }
                                                        fieldType.kind == 5 -> if { fieldType.valueModule => memberCurrentValueModule! } else { -1 => memberCurrentValueModule! }
                                                    } else { false => memberPathValid! }
                                                }
                                            } else { false => memberPathValid! }
                                        }
                                        memberIdentifierOrdinal! + 1 => memberIdentifierOrdinal!
                                    }
                                    memberTokenIndex! + 1 => memberTokenIndex!
                                }
                                (memberPathValid! and memberIdentifierOrdinal! > 1) -> if {
                                    inferred! -> push(ExpressionType {
                                        sourceModule: sourceIndex!
                                        astNode: memberIndex!
                                        origin: memberCurrentOrigin!
                                        targetModule: memberCurrentModule!
                                        targetSymbol: memberCurrentSymbol!
                                        keyOrigin: memberCurrentKeyOrigin!
                                        keyModule: memberCurrentKeyModule!
                                        valueOrigin: memberCurrentValueOrigin!
                                        valueModule: memberCurrentValueModule!
                                    })
                                    true => changed!
                                }
                            }
                        }
                    }
                }
                memberIndex! + 1 => memberIndex!
            }

            0 => indexIndex!
            indexIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + indexIndex!] => indexAccess
                indexAccess.kind == 41 -> if {
                    false => indexInferred!
                    -1 => indexedTypeIndex!
                    1000000 => indexedDistance!
                    0 => indexTypeSearch!
                    indexTypeSearch! < (inferred! -> len) -> while {
                        inferred![indexTypeSearch!] => indexType
                        (indexType.sourceModule == sourceIndex! and indexType.astNode == indexIndex!) -> if { true => indexInferred! }
                        (indexType.sourceModule == sourceIndex! and indexType.origin >= 12 and indexType.origin <= 15) -> if {
                            prepared.nodes[sourceRange.astStart + indexType.astNode].parent => indexAncestor!
                            1 => indexDistance!
                            false => belongsToIndex!
                            (indexAncestor! >= 0 and not belongsToIndex!) -> while {
                                indexAncestor! == indexIndex! -> if { true => belongsToIndex! } else {
                                    prepared.nodes[sourceRange.astStart + indexAncestor!].parent => indexAncestor!
                                    indexDistance! + 1 => indexDistance!
                                }
                            }
                            (belongsToIndex! and indexDistance! < indexedDistance!) -> if {
                                indexTypeSearch! => indexedTypeIndex!
                                indexDistance! => indexedDistance!
                            }
                        }
                        indexTypeSearch! + 1 => indexTypeSearch!
                    }
                    (not indexInferred! and indexedTypeIndex! >= 0) -> if {
                        inferred![indexedTypeIndex!] => indexedType
                        0 => elementOrigin!
                        indexedType.targetModule => elementModule!
                        indexedType.origin == 15 -> if {
                            indexedType.valueOrigin => elementOrigin!
                            indexedType.valueModule => elementModule!
                        } else {
                            indexedType.targetModule == -1 -> if { 1 => elementOrigin! } else {
                                indexedType.targetModule == sourceIndex! -> if { 0 => elementOrigin! } else { 2 => elementOrigin! }
                            }
                        }
                        inferred! -> push(ExpressionType {
                            sourceModule: sourceIndex!
                            astNode: indexIndex!
                            origin: elementOrigin!
                            targetModule: elementModule!
                            targetSymbol: indexedType.targetSymbol
                            keyOrigin: -1
                            keyModule: -1
                            valueOrigin: -1
                            valueModule: -1
                        })
                        true => changed!
                    }
                }
                indexIndex! + 1 => indexIndex!
            }

            0 => operatorIndex!
            operatorIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + operatorIndex!] => operator
                (operator.kind >= 18 and operator.kind <= 25) -> if {
                    false => alreadyInferred!
                    0 => existingIndex!
                    existingIndex! < (inferred! -> len) -> while {
                        inferred![existingIndex!] => existing
                        (existing.sourceModule == sourceIndex! and existing.astNode == operatorIndex!) -> if { true => alreadyInferred! }
                        (existing.sourceModule == sourceIndex! and prepared.nodes[sourceRange.astStart + existing.astNode].start == operator.start and prepared.nodes[sourceRange.astStart + existing.astNode].length == operator.length) -> if { true => alreadyInferred! }
                        existingIndex! + 1 => existingIndex!
                    }
                    not alreadyInferred! -> if {
                        -1 => firstChild!
                        -1 => secondChild!
                        0 => childSearch!
                        childSearch! < (inferred! -> len) -> while {
                            inferred![childSearch!] => child
                            (child.sourceModule == sourceIndex! and prepared.nodes[sourceRange.astStart + child.astNode].parent == operatorIndex!) -> if {
                                firstChild! < 0 -> if { childSearch! => firstChild! } else { childSearch! => secondChild! }
                            }
                            childSearch! + 1 => childSearch!
                        }
                        false => canInfer!
                        1 => resultOrigin!
                        -1 => resultModule!
                        -1 => resultSymbol!
                        (operator.kind == 18 or operator.kind == 19) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == right.origin and left.targetModule == right.targetModule and left.targetSymbol == right.targetSymbol) -> if {
                                    true => canInfer!
                                    23 => resultSymbol!
                                }
                            }
                        }
                        (operator.kind == 20 or operator.kind == 21) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == 1 and left.targetSymbol == 2 and right.origin == 1 and right.targetSymbol == 2) -> if {
                                    true => canInfer!
                                    2 => resultSymbol!
                                }
                            }
                        }
                        operator.kind == 22 -> if {
                            firstChild! >= 0 -> if {
                                inferred![firstChild!] => operand
                                (operator.operatorKind == -26 and operand.origin == 1 and operand.targetSymbol == 23) -> if {
                                    true => canInfer!
                                    23 => resultSymbol!
                                }
                                (operator.operatorKind == grammar.tokenIdMinus and operand.origin == 1 and operand.targetSymbol == 2) -> if {
                                    true => canInfer!
                                    2 => resultSymbol!
                                }
                            }
                        }
                        operator.kind == 23 -> if {
                            firstChild! >= 0 -> if {
                                inferred![firstChild!] => operand
                                true => canInfer!
                                16 => resultOrigin!
                                operand.targetModule => resultModule!
                                operand.targetSymbol => resultSymbol!
                            }
                        }
                        (operator.kind == 24 or operator.kind == 25) -> if {
                            (firstChild! >= 0 and secondChild! >= 0) -> if {
                                inferred![firstChild!] => left
                                inferred![secondChild!] => right
                                (left.origin == 1 and left.targetSymbol == 23 and right.origin == 1 and right.targetSymbol == 23) -> if {
                                    true => canInfer!
                                    23 => resultSymbol!
                                }
                            }
                        }
                        canInfer! -> if {
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: operatorIndex!
                                origin: resultOrigin!
                                targetModule: resultModule!
                                targetSymbol: resultSymbol!
                                keyOrigin: -1
                                keyModule: -1
                                valueOrigin: -1
                                valueModule: -1
                            })
                            true => changed!
                        }
                    }
                }
                operatorIndex! + 1 => operatorIndex!
            }
            0 => arrayIndex!
            arrayIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + arrayIndex!] => arrayNode
                arrayNode.kind == 37 -> if {
                    false => arrayInferred!
                    0 => arrayExistingIndex!
                    arrayExistingIndex! < (inferred! -> len) -> while {
                        inferred![arrayExistingIndex!] => existingArray
                        (existingArray.sourceModule == sourceIndex! and existingArray.astNode == arrayIndex!) -> if { true => arrayInferred! }
                        arrayExistingIndex! + 1 => arrayExistingIndex!
                    }
                    not arrayInferred! -> if {
                        1000000 => elementDistance!
                        -1 => elementOrigin!
                        -1 => elementModule!
                        -1 => elementSymbol!
                        true => homogeneous!
                        0 => elementSearch!
                        elementSearch! < (inferred! -> len) -> while {
                            inferred![elementSearch!] => elementType
                            elementType.sourceModule == sourceIndex! -> if {
                                prepared.nodes[sourceRange.astStart + elementType.astNode].parent => elementAncestor!
                                1 => distance!
                                false => belongsToArray!
                                (elementAncestor! >= 0 and not belongsToArray!) -> while {
                                    elementAncestor! == arrayIndex! -> if { true => belongsToArray! } else {
                                        prepared.nodes[sourceRange.astStart + elementAncestor!].parent => elementAncestor!
                                        distance! + 1 => distance!
                                    }
                                }
                                belongsToArray! -> if {
                                    distance! < elementDistance! -> if {
                                        distance! => elementDistance!
                                        elementType.origin => elementOrigin!
                                        elementType.targetModule => elementModule!
                                        elementType.targetSymbol => elementSymbol!
                                        true => homogeneous!
                                    } else {
                                        distance! == elementDistance! -> if {
                                            (elementType.origin != elementOrigin! or elementType.targetModule != elementModule! or elementType.targetSymbol != elementSymbol!) -> if { false => homogeneous! }
                                        }
                                    }
                                }
                            }
                            elementSearch! + 1 => elementSearch!
                        }
                        (elementOrigin! >= 0 and homogeneous!) -> if {
                            false => dynamicArray!
                            arrayNode.firstToken => arrayTokenIndex!
                            arrayTokenIndex! < arrayNode.firstToken + arrayNode.tokenCount -> while {
                                prepared.tokens[sourceRange.tokenStart + arrayTokenIndex!].kind == grammar.tokenIdTilde -> if { true => dynamicArray! }
                                arrayTokenIndex! + 1 => arrayTokenIndex!
                            }
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: arrayIndex!
                                origin: dynamicArray! -> if { 13 } else { 14 }
                                targetModule: elementModule!
                                targetSymbol: elementSymbol!
                                keyOrigin: -1
                                keyModule: -1
                                valueOrigin: -1
                                valueModule: -1
                            })
                            true => changed!
                        }
                    }
                }
                arrayIndex! + 1 => arrayIndex!
            }
            0 => dictionaryIndex!
            dictionaryIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + dictionaryIndex!] => dictionaryNode
                dictionaryNode.kind == 38 -> if {
                    false => dictionaryInferred!
                    0 => dictionaryExistingIndex!
                    dictionaryExistingIndex! < (inferred! -> len) -> while {
                        inferred![dictionaryExistingIndex!] => existingDictionary
                        (existingDictionary.sourceModule == sourceIndex! and existingDictionary.astNode == dictionaryIndex!) -> if { true => dictionaryInferred! }
                        dictionaryExistingIndex! + 1 => dictionaryExistingIndex!
                    }
                    not dictionaryInferred! -> if {
                        1000000 => entryDistance!
                        0 => entryDistanceSearch!
                        entryDistanceSearch! < (inferred! -> len) -> while {
                            inferred![entryDistanceSearch!] => entryType
                            entryType.sourceModule == sourceIndex! -> if {
                                prepared.nodes[sourceRange.astStart + entryType.astNode].parent => entryAncestor!
                                1 => distance!
                                false => belongsToDictionary!
                                (entryAncestor! >= 0 and not belongsToDictionary!) -> while {
                                    entryAncestor! == dictionaryIndex! -> if { true => belongsToDictionary! } else {
                                        prepared.nodes[sourceRange.astStart + entryAncestor!].parent => entryAncestor!
                                        distance! + 1 => distance!
                                    }
                                }
                                (belongsToDictionary! and distance! < entryDistance!) -> if { distance! => entryDistance! }
                            }
                            entryDistanceSearch! + 1 => entryDistanceSearch!
                        }
                        -1 => keySymbol!
                        -1 => valueSymbol!
                        -1 => keyOrigin!
                        -1 => keyModule!
                        -1 => valueOrigin!
                        -1 => valueModule!
                        0 => entryPosition!
                        true => homogeneousDictionary!
                        0 => entryTypeSearch!
                        entryTypeSearch! < (inferred! -> len) -> while {
                            inferred![entryTypeSearch!] => entryType
                            entryType.sourceModule == sourceIndex! -> if {
                                prepared.nodes[sourceRange.astStart + entryType.astNode].parent => entryAncestor!
                                1 => distance!
                                false => belongsToDictionary!
                                (entryAncestor! >= 0 and not belongsToDictionary!) -> while {
                                    entryAncestor! == dictionaryIndex! -> if { true => belongsToDictionary! } else {
                                        prepared.nodes[sourceRange.astStart + entryAncestor!].parent => entryAncestor!
                                        distance! + 1 => distance!
                                    }
                                }
                                (belongsToDictionary! and distance! == entryDistance!) -> if {
                                    entryPosition! % 2 == 0 -> if {
                                        keySymbol! < 0 -> if {
                                            entryType.targetSymbol => keySymbol!
                                            entryType.origin => keyOrigin!
                                            entryType.targetModule => keyModule!
                                        } else {
                                            (entryType.origin != keyOrigin! or entryType.targetModule != keyModule! or entryType.targetSymbol != keySymbol!) -> if { false => homogeneousDictionary! }
                                        }
                                    } else {
                                        valueSymbol! < 0 -> if {
                                            entryType.targetSymbol => valueSymbol!
                                            entryType.origin => valueOrigin!
                                            entryType.targetModule => valueModule!
                                        } else {
                                            (entryType.origin != valueOrigin! or entryType.targetModule != valueModule! or entryType.targetSymbol != valueSymbol!) -> if { false => homogeneousDictionary! }
                                        }
                                    }
                                    entryPosition! + 1 => entryPosition!
                                }
                            }
                            entryTypeSearch! + 1 => entryTypeSearch!
                        }
                        (entryPosition! > 0 and entryPosition! % 2 == 0 and homogeneousDictionary!) -> if {
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: dictionaryIndex!
                                origin: 15
                                targetModule: keySymbol!
                                targetSymbol: valueSymbol!
                                keyOrigin: keyOrigin!
                                keyModule: keyModule!
                                valueOrigin: valueOrigin!
                                valueModule: valueModule!
                            })
                            true => changed!
                        }
                    }
                }
                dictionaryIndex! + 1 => dictionaryIndex!
            }

            0 => regionIndex!
            regionIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + regionIndex!].kind == 43 -> if {
                    false => regionInferred!
                    0 => regionExistingSearch!
                    regionExistingSearch! < (inferred! -> len) -> while {
                        (inferred![regionExistingSearch!].sourceModule == sourceIndex! and inferred![regionExistingSearch!].astNode == regionIndex!) -> if { true => regionInferred! }
                        regionExistingSearch! + 1 => regionExistingSearch!
                    }
                    not regionInferred! -> if {
                        -1 => regionResultIndex!
                        0 => regionResultSearch!
                        regionResultSearch! < (inferred! -> len) -> while {
                            inferred![regionResultSearch!] => regionCandidate
                            (regionCandidate.sourceModule == sourceIndex! and prepared.nodes[sourceRange.astStart + regionCandidate.astNode].parent == regionIndex!) -> if {
                                (regionResultIndex! < 0 or prepared.nodes[sourceRange.astStart + regionCandidate.astNode].start > prepared.nodes[sourceRange.astStart + inferred![regionResultIndex!].astNode].start) -> if {
                                    regionResultSearch! => regionResultIndex!
                                }
                            }
                            regionResultSearch! + 1 => regionResultSearch!
                        }
                        regionResultIndex! >= 0 -> if {
                            inferred![regionResultIndex!] => regionResult
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: regionIndex!
                                origin: regionResult.origin
                                targetModule: regionResult.targetModule
                                targetSymbol: regionResult.targetSymbol
                                keyOrigin: regionResult.keyOrigin
                                keyModule: regionResult.keyModule
                                valueOrigin: regionResult.valueOrigin
                                valueModule: regionResult.valueModule
                            })
                            true => changed!
                        }
                    }
                }
                regionIndex! + 1 => regionIndex!
            }

            0 => controlIndex!
            controlIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + controlIndex!].kind == 42 -> if {
                    false => controlInferred!
                    0 => controlExistingSearch!
                    controlExistingSearch! < (inferred! -> len) -> while {
                        (inferred![controlExistingSearch!].sourceModule == sourceIndex! and inferred![controlExistingSearch!].astNode == controlIndex!) -> if { true => controlInferred! }
                        controlExistingSearch! + 1 => controlExistingSearch!
                    }
                    not controlInferred! -> if {
                        0 => controlRegionCount!
                        0 => inferredRegionCount!
                        -1 => firstRegionTypeIndex!
                        true => homogeneousRegions!
                        0 => controlRegionSearch!
                        controlRegionSearch! < sourceRange.astCount -> while {
                            (prepared.nodes[sourceRange.astStart + controlRegionSearch!].parent == controlIndex! and prepared.nodes[sourceRange.astStart + controlRegionSearch!].kind == 43) -> if {
                                controlRegionCount! + 1 => controlRegionCount!
                                -1 => controlRegionTypeIndex!
                                0 => controlRegionTypeSearch!
                                controlRegionTypeSearch! < (inferred! -> len) -> while {
                                    (inferred![controlRegionTypeSearch!].sourceModule == sourceIndex! and inferred![controlRegionTypeSearch!].astNode == controlRegionSearch!) -> if { controlRegionTypeSearch! => controlRegionTypeIndex! }
                                    controlRegionTypeSearch! + 1 => controlRegionTypeSearch!
                                }
                                controlRegionTypeIndex! >= 0 -> if {
                                    inferredRegionCount! + 1 => inferredRegionCount!
                                    firstRegionTypeIndex! < 0 -> if { controlRegionTypeIndex! => firstRegionTypeIndex! } else {
                                        inferred![firstRegionTypeIndex!] => firstRegionType
                                        inferred![controlRegionTypeIndex!] => otherRegionType
                                        (firstRegionType.origin != otherRegionType.origin or firstRegionType.targetModule != otherRegionType.targetModule or firstRegionType.targetSymbol != otherRegionType.targetSymbol or firstRegionType.keyOrigin != otherRegionType.keyOrigin or firstRegionType.keyModule != otherRegionType.keyModule or firstRegionType.valueOrigin != otherRegionType.valueOrigin or firstRegionType.valueModule != otherRegionType.valueModule) -> if { false => homogeneousRegions! }
                                    }
                                }
                            }
                            controlRegionSearch! + 1 => controlRegionSearch!
                        }
                        (controlRegionCount! > 0 and inferredRegionCount! == controlRegionCount! and homogeneousRegions!) -> if {
                            controlRegionCount! == 1 -> if {
                                inferred! -> push(ExpressionType { sourceModule: sourceIndex!, astNode: controlIndex!, origin: 1, targetModule: -1, targetSymbol: 0, keyOrigin: -1, keyModule: -1, valueOrigin: -1, valueModule: -1 })
                            } else {
                                inferred![firstRegionTypeIndex!] => controlResult
                                inferred! -> push(ExpressionType {
                                    sourceModule: sourceIndex!
                                    astNode: controlIndex!
                                    origin: controlResult.origin
                                    targetModule: controlResult.targetModule
                                    targetSymbol: controlResult.targetSymbol
                                    keyOrigin: controlResult.keyOrigin
                                    keyModule: controlResult.keyModule
                                    valueOrigin: controlResult.valueOrigin
                                    valueModule: controlResult.valueModule
                                })
                            }
                            true => changed!
                        }
                    }
                }
                controlIndex! + 1 => controlIndex!
            }

            0 => controlFlowIndex!
            controlFlowIndex! < sourceRange.astCount -> while {
                prepared.nodes[sourceRange.astStart + controlFlowIndex!].kind == 10 -> if {
                    false => controlFlowInferred!
                    0 => controlFlowExistingSearch!
                    controlFlowExistingSearch! < (inferred! -> len) -> while {
                        (inferred![controlFlowExistingSearch!].sourceModule == sourceIndex! and inferred![controlFlowExistingSearch!].astNode == controlFlowIndex!) -> if { true => controlFlowInferred! }
                        controlFlowExistingSearch! + 1 => controlFlowExistingSearch!
                    }
                    not controlFlowInferred! -> if {
                        -1 => controlTargetTypeIndex!
                        -1 => directControlAst!
                        0 => directControlSearch!
                        directControlSearch! < sourceRange.astCount -> while {
                            (prepared.nodes[sourceRange.astStart + directControlSearch!].parent == controlFlowIndex! and (prepared.nodes[sourceRange.astStart + directControlSearch!].kind == 42 or prepared.nodes[sourceRange.astStart + directControlSearch!].kind == 44)) -> if { directControlSearch! => directControlAst! }
                            directControlSearch! + 1 => directControlSearch!
                        }
                        0 => controlTargetTypeSearch!
                        controlTargetTypeSearch! < (inferred! -> len) -> while {
                            inferred![controlTargetTypeSearch!] => flowChildType
                            (directControlAst! >= 0 and flowChildType.sourceModule == sourceIndex! and flowChildType.astNode == directControlAst!) -> if { controlTargetTypeSearch! => controlTargetTypeIndex! }
                            (directControlAst! < 0 and prepared.nodes[sourceRange.astStart + controlFlowIndex!].parent >= 0 and prepared.nodes[sourceRange.astStart + prepared.nodes[sourceRange.astStart + controlFlowIndex!].parent].kind == 43 and flowChildType.sourceModule == sourceIndex! and prepared.nodes[sourceRange.astStart + flowChildType.astNode].parent == controlFlowIndex!) -> if {
                                (controlTargetTypeIndex! < 0 or prepared.nodes[sourceRange.astStart + flowChildType.astNode].start > prepared.nodes[sourceRange.astStart + inferred![controlTargetTypeIndex!].astNode].start) -> if { controlTargetTypeSearch! => controlTargetTypeIndex! }
                            }
                            controlTargetTypeSearch! + 1 => controlTargetTypeSearch!
                        }
                        controlTargetTypeIndex! >= 0 -> if {
                            inferred![controlTargetTypeIndex!] => controlFlowResult
                            inferred! -> push(ExpressionType {
                                sourceModule: sourceIndex!
                                astNode: controlFlowIndex!
                                origin: controlFlowResult.origin
                                targetModule: controlFlowResult.targetModule
                                targetSymbol: controlFlowResult.targetSymbol
                                keyOrigin: controlFlowResult.keyOrigin
                                keyModule: controlFlowResult.keyModule
                                valueOrigin: controlFlowResult.valueOrigin
                                valueModule: controlFlowResult.valueModule
                            })
                            true => changed!
                        }
                    }
                }
                controlFlowIndex! + 1 => controlFlowIndex!
            }
        }
        sourceIndex! + 1 => sourceIndex!
    }
    inferred!
}

public inferPrepared request: move ExpressionTypeRequest -> [ExpressionType; ~] {
    [Text; ~] => sources!
    request.sources -> each source { sources! -> push(source) }
    [nominalTypes.NominalType; ~] => nominal!
    request.nominal -> each nominalType { nominal! -> push(nominalType) }
    [compositeTypes.CompositeType; ~] => composite!
    request.composite -> each compositeType { composite! -> push(compositeType) }
    [qualified.QualifiedResolution; ~] => qualifiedResults!
    request.qualified -> each qualifiedResult { qualifiedResults! -> push(qualifiedResult) }
    [modules.ModuleIdentity; ~] => moduleIdentities!
    request.modules -> each moduleIdentity { moduleIdentities! -> push(moduleIdentity) }
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
    [typeIds.SemanticType; ~] => types!
    [typeIds.TypeReference; ~] => references!
    [typeIds.NominalField; ~] => fields!
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
    prepared! -> inferContext => inferred!
    inferred!
}

public infer sources: [Text; ~] -> [ExpressionType; ~] {
    sources -> semanticContext.prepare => prepared!
    prepared! -> inferContext => inferred!
    inferred!
}
