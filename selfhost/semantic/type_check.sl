namespace smalllang.compiler.semantic.type_check

import smalllang.compiler.ast as ast
import smalllang.compiler.lexer as lexer
import smalllang.compiler.semantic.calls as calls
import smalllang.compiler.semantic.expression_types as expressionTypes
import smalllang.compiler.semantic.nominal_types as nominalTypes
import smalllang.compiler.semantic.symbols as symbols
import smalllang.compiler.syntax as syntax
import syntax.generated.smalllang as grammar

public struct TypeCheckDiagnostic {
    code: Int
    sourceModule: Int
    functionSymbol: Int
    expectedOrigin: Int
    expectedModule: Int
    expectedSymbol: Int
    actualOrigin: Int
    actualModule: Int
    actualSymbol: Int
    actualBuiltin: Int
    span: syntax.SourceSpan
}

# Code 5 identifies a mismatching function return expression. Code 6 identifies
# a call argument that does not match the local function input type. Code 7
# identifies an unresolved local call target. Code 8 identifies a binary
# operator whose typed operands are incompatible. Code 9 identifies a
# non-public imported call target. Code 10 identifies missing or extra call
# arguments for the current zero-or-one-input function surface.
public analyze sources: [Text; ~] -> [TypeCheckDiagnostic; ~] {
    sources -> nominalTypes.resolve => nominal!
    sources -> expressionTypes.infer => expressionTypeTable!
    sources -> calls.resolveModules => moduleCalls!
    [TypeCheckDiagnostic; ~] => diagnostics!
    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        source -> symbols.collect => table!
        0 => symbolIndex!
        symbolIndex! < (table! -> len) -> while {
            table![symbolIndex!] => function
            function.kind == 7 -> if {
                function.secondaryTypeNode >= 0 -> if {
                    function.secondaryTypeNode
                } else {
                    function.typeNode
                } => returnTypeAst
                -1 => expectedIndex!
                0 => nominalIndex!
                nominalIndex! < (nominal! -> len) -> while {
                    nominal![nominalIndex!] => candidateType
                    (candidateType.sourceModule == sourceIndex! and candidateType.typeAst == returnTypeAst) -> if {
                        nominalIndex! => expectedIndex!
                    }
                    nominalIndex! + 1 => nominalIndex!
                }
                -1 => returnExpressionAst!
                -1 => returnExpressionType!
                1000000 => returnDistance!
                0 => expressionTypeIndex!
                expressionTypeIndex! < (expressionTypeTable! -> len) -> while {
                    expressionTypeTable![expressionTypeIndex!] => candidateType
                    candidateType.sourceModule == sourceIndex! -> if {
                        nodes![candidateType.astNode].parent => ancestor!
                        1 => distance!
                        false => belongsToFunction!
                        (ancestor! >= 0 and not belongsToFunction!) -> while {
                            ancestor! == function.astNode -> if {
                                true => belongsToFunction!
                            } else {
                                nodes![ancestor!].parent => ancestor!
                                distance! + 1 => distance!
                            }
                        }
                        (belongsToFunction! and distance! < returnDistance!) -> if {
                            candidateType.astNode => returnExpressionAst!
                            expressionTypeIndex! => returnExpressionType!
                            distance! => returnDistance!
                        }
                    }
                    expressionTypeIndex! + 1 => expressionTypeIndex!
                }
                (expectedIndex! >= 0 and returnExpressionType! >= 0) -> if {
                    nominal![expectedIndex!] => expected
                    nodes![returnExpressionAst!] => returnExpression
                    expressionTypeTable![returnExpressionType!] => actualType
                    (expected.origin != actualType.origin or expected.targetModule != actualType.targetModule or expected.targetSymbol != actualType.targetSymbol) -> if {
                        TypeCheckDiagnostic {
                            code: 5
                            sourceModule: sourceIndex!
                            functionSymbol: symbolIndex!
                            expectedOrigin: expected.origin
                            expectedModule: expected.targetModule
                            expectedSymbol: expected.targetSymbol
                            actualOrigin: actualType.origin
                            actualModule: actualType.targetModule
                            actualSymbol: actualType.targetSymbol
                            actualBuiltin: actualType.origin == 1 -> if { actualType.targetSymbol } else { -1 }
                            span: syntax.SourceSpan {
                                fileId: sourceIndex!
                                start: returnExpression.start
                                length: returnExpression.length
                            }
                        } => diagnostic
                        diagnostics! -> push(diagnostic)
                    }
                }
            }
            symbolIndex! + 1 => symbolIndex!
        }

        0 => callIndex!
        callIndex! < (moduleCalls! -> len) -> while {
            moduleCalls![callIndex!] => call
            (call.sourceModule == sourceIndex! and call.status == 0) -> if {
                sources[call.targetSourceModule] -> symbols.collect => targetTable!
                targetTable![call.functionSymbol] => targetFunction
                nodes![call.callAst] => callNode
                false => afterLeftParen!
                false => hasArgument!
                callNode.firstToken => callTokenIndex!
                callTokenIndex! < callNode.firstToken + callNode.tokenCount -> while {
                    tokens![callTokenIndex!].kind == grammar.tokenIdLeftParen -> if {
                        true => afterLeftParen!
                    } else {
                        (afterLeftParen! and tokens![callTokenIndex!].kind != grammar.tokenIdRightParen and tokens![callTokenIndex!].kind != grammar.triviaIdWhitespace and tokens![callTokenIndex!].kind != grammar.triviaIdComment) -> if {
                            true => hasArgument!
                        }
                    }
                    callTokenIndex! + 1 => callTokenIndex!
                }
                targetFunction.secondaryTypeNode >= 0 -> if {
                    -1 => expectedInputIndex!
                    0 => inputSearch!
                    inputSearch! < (nominal! -> len) -> while {
                        nominal![inputSearch!] => inputType
                        (inputType.sourceModule == call.targetSourceModule and inputType.typeAst == targetFunction.typeNode) -> if {
                            inputSearch! => expectedInputIndex!
                        }
                        inputSearch! + 1 => inputSearch!
                    }
                    -1 => actualArgumentIndex!
                    1000000 => argumentDistance!
                    0 => argumentSearch!
                    argumentSearch! < (expressionTypeTable! -> len) -> while {
                        expressionTypeTable![argumentSearch!] => argumentType
                        argumentType.sourceModule == sourceIndex! -> if {
                            nodes![argumentType.astNode].parent => argumentAncestor!
                            1 => distance!
                            false => belongsToCall!
                            (argumentAncestor! >= 0 and not belongsToCall!) -> while {
                                argumentAncestor! == call.callAst -> if {
                                    true => belongsToCall!
                                } else {
                                    nodes![argumentAncestor!].parent => argumentAncestor!
                                    distance! + 1 => distance!
                                }
                            }
                            (belongsToCall! and distance! < argumentDistance!) -> if {
                                argumentSearch! => actualArgumentIndex!
                                distance! => argumentDistance!
                            }
                        }
                        argumentSearch! + 1 => argumentSearch!
                    }
                    (expectedInputIndex! >= 0 and actualArgumentIndex! >= 0) -> if {
                        nominal![expectedInputIndex!] => expected
                        expressionTypeTable![actualArgumentIndex!] => actual
                        (expected.origin != 3 and (expected.origin != actual.origin or expected.targetModule != actual.targetModule or expected.targetSymbol != actual.targetSymbol)) -> if {
                            nodes![actual.astNode] => argumentExpression
                            diagnostics! -> push(TypeCheckDiagnostic {
                                code: 6
                                sourceModule: sourceIndex!
                                functionSymbol: call.functionSymbol
                                expectedOrigin: expected.origin
                                expectedModule: expected.targetModule
                                expectedSymbol: expected.targetSymbol
                                actualOrigin: actual.origin
                                actualModule: actual.targetModule
                                actualSymbol: actual.targetSymbol
                                actualBuiltin: actual.origin == 1 -> if { actual.targetSymbol } else { -1 }
                                span: syntax.SourceSpan {
                                    fileId: sourceIndex!
                                    start: argumentExpression.start
                                    length: argumentExpression.length
                                }
                            })
                        }
                    }
                    not hasArgument! -> if {
                        diagnostics! -> push(TypeCheckDiagnostic {
                            code: 10
                            sourceModule: sourceIndex!
                            functionSymbol: call.functionSymbol
                            expectedOrigin: -1
                            expectedModule: call.targetModule
                            expectedSymbol: -1
                            actualOrigin: -1
                            actualModule: -1
                            actualSymbol: -1
                            actualBuiltin: -1
                            span: syntax.SourceSpan { fileId: sourceIndex!, start: callNode.start, length: callNode.length }
                        })
                    }
                } else {
                    true -> if {
                        diagnostics! -> push(TypeCheckDiagnostic {
                            code: 10
                            sourceModule: sourceIndex!
                            functionSymbol: call.functionSymbol
                            expectedOrigin: -1
                            expectedModule: call.targetModule
                            expectedSymbol: -1
                            actualOrigin: -1
                            actualModule: -1
                            actualSymbol: -1
                            actualBuiltin: -1
                            span: syntax.SourceSpan { fileId: sourceIndex!, start: callNode.start, length: callNode.length }
                        })
                    }
                }
            } else {
                call.sourceModule == sourceIndex! -> if {
                nodes![call.callAst] => unresolvedCall
                diagnostics! -> push(TypeCheckDiagnostic {
                    code: call.status == 3 -> if { 9 } else { 7 }
                    sourceModule: sourceIndex!
                    functionSymbol: -1
                    expectedOrigin: -1
                    expectedModule: -1
                    expectedSymbol: -1
                    actualOrigin: -1
                    actualModule: -1
                    actualSymbol: -1
                    actualBuiltin: -1
                    span: syntax.SourceSpan {
                        fileId: sourceIndex!
                        start: unresolvedCall.start
                        length: unresolvedCall.length
                    }
                })
                }
            }
            callIndex! + 1 => callIndex!
        }

        0 => operatorIndex!
        operatorIndex! < (nodes! -> len) -> while {
            nodes![operatorIndex!] => operator
            ((operator.kind >= 18 and operator.kind <= 22) or operator.kind == 24 or operator.kind == 25) -> if {
                false => operatorInferred!
                -1 => leftTypeIndex!
                -1 => rightTypeIndex!
                1000000 => leftDistance!
                1000000 => rightDistance!
                0 => inferredIndex!
                inferredIndex! < (expressionTypeTable! -> len) -> while {
                    expressionTypeTable![inferredIndex!] => inferredType
                    inferredType.sourceModule == sourceIndex! -> if {
                        inferredType.astNode == operatorIndex! -> if { true => operatorInferred! }
                        (nodes![inferredType.astNode].start == operator.start and nodes![inferredType.astNode].length == operator.length) -> if { true => operatorInferred! }
                        inferredType.astNode != operatorIndex! -> if {
                            nodes![inferredType.astNode].parent => operandAncestor!
                            1 => operandDistance!
                            false => belongsToOperator!
                            (operandAncestor! >= 0 and not belongsToOperator!) -> while {
                                operandAncestor! == operatorIndex! -> if {
                                    true => belongsToOperator!
                                } else {
                                    nodes![operandAncestor!].parent => operandAncestor!
                                    operandDistance! + 1 => operandDistance!
                                }
                            }
                            belongsToOperator! -> if {
                                operandDistance! < leftDistance! -> if {
                                    leftTypeIndex! => rightTypeIndex!
                                    leftDistance! => rightDistance!
                                    inferredIndex! => leftTypeIndex!
                                    operandDistance! => leftDistance!
                                } else {
                                    operandDistance! < rightDistance! -> if {
                                        inferredIndex! => rightTypeIndex!
                                        operandDistance! => rightDistance!
                                    }
                                }
                            }
                        }
                    }
                    inferredIndex! + 1 => inferredIndex!
                }
                (operator.kind == 22 and not operatorInferred! and leftTypeIndex! >= 0) -> if {
                    expressionTypeTable![leftTypeIndex!] => operandType
                    operator.operatorKind == -26 -> if { 23 } else { 2 } => expectedUnaryBuiltin
                    diagnostics! -> push(TypeCheckDiagnostic {
                        code: 8
                        sourceModule: sourceIndex!
                        functionSymbol: -1
                        expectedOrigin: 1
                        expectedModule: -1
                        expectedSymbol: expectedUnaryBuiltin
                        actualOrigin: operandType.origin
                        actualModule: operandType.targetModule
                        actualSymbol: operandType.targetSymbol
                        actualBuiltin: operandType.origin == 1 -> if { operandType.targetSymbol } else { -1 }
                        span: syntax.SourceSpan { fileId: sourceIndex!, start: operator.start, length: operator.length }
                    })
                }
                (operator.kind != 22 and not operatorInferred! and leftTypeIndex! >= 0 and rightTypeIndex! >= 0) -> if {
                    expressionTypeTable![leftTypeIndex!] => leftType
                    expressionTypeTable![rightTypeIndex!] => rightType
                    false => duplicateOperatorDiagnostic!
                    0 => diagnosticIndex!
                    diagnosticIndex! < (diagnostics! -> len) -> while {
                        diagnostics![diagnosticIndex!] => existingDiagnostic
                        (existingDiagnostic.code == 8 and existingDiagnostic.span.fileId == sourceIndex! and existingDiagnostic.span.start == operator.start and existingDiagnostic.span.length == operator.length) -> if {
                            true => duplicateOperatorDiagnostic!
                        }
                        diagnosticIndex! + 1 => diagnosticIndex!
                    }
                    not duplicateOperatorDiagnostic! -> if {
                        diagnostics! -> push(TypeCheckDiagnostic {
                        code: 8
                        sourceModule: sourceIndex!
                        functionSymbol: -1
                        expectedOrigin: leftType.origin
                        expectedModule: leftType.targetModule
                        expectedSymbol: leftType.targetSymbol
                        actualOrigin: rightType.origin
                        actualModule: rightType.targetModule
                        actualSymbol: rightType.targetSymbol
                        actualBuiltin: rightType.origin == 1 -> if { rightType.targetSymbol } else { -1 }
                        span: syntax.SourceSpan {
                            fileId: sourceIndex!
                            start: operator.start
                            length: operator.length
                        }
                        })
                    }
                }
            }
            operatorIndex! + 1 => operatorIndex!
        }
        sourceIndex! + 1 => sourceIndex!
    }
    diagnostics!
}
