namespace smalllang.compiler.semantic.type_ids

import smalllang.compiler.ast
import smalllang.compiler.lexer
import smalllang.compiler.semantic.modules
import smalllang.compiler.semantic.qualified
import smalllang.compiler.semantic.symbols
import smalllang.compiler.semantic.type_terms as typeTerms
import syntax.generated.smalllang as grammar

# Kinds match type_terms: 1 nominal, 2 slice, 3 dynamic array, 4 fixed
# array, 5 dictionary, 6 box, 7 nominal application.
# Nominal origins: 0 local declaration, 1 builtin, 2 imported declaration,
# 3 generic parameter, 4 intrinsic type constructor.
public struct SemanticType {
    kind: Int
    origin: Int
    module: Int
    symbol: Int
    first: Int
    second: Int
    lengthHash: UInt64
    containsParameter: Bool
    status: Int
}

public struct TypeReference {
    sourceModule: Int
    typeAst: Int
    typeId: Int
    status: Int
}

public struct SemanticTypeSet {
    types: [SemanticType; ~]
    references: [TypeReference; ~]
}

public struct SpecializationRequest {
    types: [SemanticType; ~]
    inputTemplate: Int
    actualInput: Int
    resultTemplate: Int
}

public struct SpecializationResult {
    types: [SemanticType; ~]
    root: Int
    status: Int
}

# Canonical ownership traits derived from the recursive type arena.
# Bit 0 means the value owns destruction responsibility. Bit 1 means that
# responsibility reaches heap-backed storage. Child IDs always precede their
# parents, so fixed arrays and nominal applications are classified bottom-up.
public classify types: [SemanticType; ~] -> [Int; ~] {
    [Int; ~] => traits!
    types -> each current {
        false => owns!
        false => reachesHeap!
        (current.kind == 3 or current.kind == 5 or current.kind == 6) -> if {
            true => owns!
            true => reachesHeap!
        }
        (current.kind == 1 and (current.origin == 0 or current.origin == 2)) -> if {
            true => owns!
        }
        (current.kind == 4 or current.kind == 7) -> if {
            current.first >= 0 -> if {
                traits![current.first] % 2 == 1 -> if { true => owns! }
                (traits![current.first] / 2) % 2 == 1 -> if { true => reachesHeap! }
            }
            current.second >= 0 -> if {
                traits![current.second] % 2 == 1 -> if { true => owns! }
                (traits![current.second] / 2) % 2 == 1 -> if { true => reachesHeap! }
            }
        }
        0 => value!
        owns! -> if { value! + 1 => value! }
        reachesHeap! -> if { value! + 2 => value! }
        traits! -> push(value!)
    }
    traits!
}

# Resolves and globally interns recursive annotation types across source files.
# Nominal equality uses declaration identity, not source spelling.
public resolve sources: [Text; ~] -> SemanticTypeSet {
    ["Unit", "Text", "Int", "Int8", "Int16", "Int32", "Int64", "Long", "UInt8", "UInt16", "UInt32", "UInt64", "Size", "UIntSize", "CodePoint", "Arena", "Arguments", "MappedBytes", "MutableMappedBytes", "Float", "Float32", "Float64", "Double", "Bool", ~] => builtinNames!
    sources -> modules.identities => identities!
    sources -> qualified.resolve => qualifiedResults!
    [SemanticType; ~] => semanticTypes!
    [TypeReference; ~] => references!

    # Seed builtins in their stable nominal-symbol order. Expression inference
    # can therefore use the same id without an adapter or encounter-order map.
    0 => builtinSeed!
    builtinSeed! < (builtinNames! -> len) -> while {
        semanticTypes! -> push(SemanticType {
            kind: 1
            origin: 1
            module: -1
            symbol: builtinSeed!
            first: -1
            second: -1
            lengthHash: UInt64(0)
            containsParameter: false
            status: 0
        })
        builtinSeed! + 1 => builtinSeed!
    }

    0 => sourceIndex!
    sourceIndex! < (sources -> len) -> while {
        sources[sourceIndex!] => source
        source -> ast.lower => nodes!
        source -> lexer.lex => tokens!
        source -> symbols.collect => table!
        source -> typeTerms.lower => terms!
        [Int; ~] => mapped!
        0 => mapSeed!
        mapSeed! < (terms! -> len) -> while {
            mapped! -> push(-1)
            mapSeed! + 1 => mapSeed!
        }

        0 => completed!
        true => changed!
        (completed! < (terms! -> len) and changed!) -> while {
            false => changed!
            0 => termIndex!
            termIndex! < (terms! -> len) -> while {
                mapped![termIndex!] < 0 -> if {
                    terms![termIndex!] => term
                    (term.firstArgument < 0 or mapped![term.firstArgument] >= 0) => firstReady
                    (term.secondArgument < 0 or mapped![term.secondArgument] >= 0) => secondReady
                    (firstReady and secondReady) -> if {
                        -1 => origin!
                        -1 => targetModule!
                        -1 => targetSymbol!
                        0 => status!
                        term.kind == 1 -> if {
                            -1 => qualifiedIndex!
                            0 => qualifiedSearch!
                            qualifiedSearch! < (qualifiedResults! -> len) -> while {
                                qualifiedResults![qualifiedSearch!] => candidate
                                candidate.sourceModule == sourceIndex! -> if {
                                    candidate.pathAst => ancestor!
                                    false => belongsToTerm!
                                    (ancestor! >= 0 and not belongsToTerm!) -> while {
                                        ancestor! == term.astNode -> if {
                                            true => belongsToTerm!
                                        } else {
                                            nodes![ancestor!].parent => ancestor!
                                        }
                                    }
                                    belongsToTerm! -> if { qualifiedSearch! => qualifiedIndex! }
                                }
                                qualifiedSearch! + 1 => qualifiedSearch!
                            }
                            qualifiedIndex! >= 0 -> if {
                                qualifiedResults![qualifiedIndex!] => imported
                                2 => origin!
                                identities![imported.targetModule].sourceIndex => targetModule!
                                imported.targetSymbol => targetSymbol!
                                imported.status => status!
                            } else {
                                tokens![term.nameToken] => name
                                -1 => builtinIndex!
                                0 => builtinSearch!
                                (builtinSearch! < (builtinNames! -> len) and builtinIndex! < 0) -> while {
                                    builtinNames![builtinSearch!] => builtinName
                                    name.span.length == (builtinName -> len) => equal!
                                    UIntSize(0) => nameByte!
                                    (equal! and nameByte! < name.span.length) -> while {
                                        source -> byte(name.span.start + nameByte!) => leftByte
                                        builtinName -> byte(nameByte!) => rightByte
                                        leftByte != rightByte -> if { false => equal! }
                                        nameByte! + UIntSize(1) => nameByte!
                                    }
                                    equal! -> if { builtinSearch! => builtinIndex! }
                                    builtinSearch! + 1 => builtinSearch!
                                }
                                builtinIndex! >= 0 -> if {
                                    1 => origin!
                                    -1 => targetModule!
                                    builtinIndex! => targetSymbol!
                                } else {
                                    -1 => ownerFunction!
                                    nodes![term.astNode].parent => ownerAst!
                                    (ownerAst! >= 0 and ownerFunction! < 0) -> while {
                                        0 => ownerSymbolIndex!
                                        ownerSymbolIndex! < (table! -> len) -> while {
                                            table![ownerSymbolIndex!] => ownerCandidate
                                            (ownerCandidate.kind == 7 and ownerCandidate.astNode == ownerAst!) -> if {
                                                ownerSymbolIndex! => ownerFunction!
                                            }
                                            ownerSymbolIndex! + 1 => ownerSymbolIndex!
                                        }
                                        ownerFunction! < 0 -> if { nodes![ownerAst!].parent => ownerAst! }
                                    }
                                    -1 => localSymbol!
                                    0 => symbolIndex!
                                    (symbolIndex! < (table! -> len) and localSymbol! < 0) -> while {
                                        table![symbolIndex!] => candidate
                                        ((candidate.parent < 0 and (candidate.kind == 3 or candidate.kind == 4)) or (candidate.kind == 32 and candidate.parent == ownerFunction!)) -> if {
                                            tokens![candidate.nameToken] => candidateName
                                            name.span.length == candidateName.span.length => equal!
                                            UIntSize(0) => localByte!
                                            (equal! and localByte! < name.span.length) -> while {
                                                source -> byte(name.span.start + localByte!) => leftByte
                                                source -> byte(candidateName.span.start + localByte!) => rightByte
                                                leftByte != rightByte -> if { false => equal! }
                                                localByte! + UIntSize(1) => localByte!
                                            }
                                            equal! -> if { symbolIndex! => localSymbol! }
                                        }
                                        symbolIndex! + 1 => symbolIndex!
                                    }
                                    localSymbol! >= 0 -> if {
                                        table![localSymbol!].kind == 32 -> if { 3 => origin! } else { 0 => origin! }
                                        sourceIndex! => targetModule!
                                        localSymbol! => targetSymbol!
                                    } else {
                                        2 => status!
                                    }
                                }
                            }
                        }
                        term.kind == 7 -> if {
                            4 => origin!
                            -1 => targetModule!
                            tokens![term.nameToken] => constructorName
                            constructorName.span.length == UIntSize(6) -> if {
                                source -> byte(constructorName.span.start) => byte0
                                source -> byte(constructorName.span.start + UIntSize(1)) => byte1
                                source -> byte(constructorName.span.start + UIntSize(2)) => byte2
                                source -> byte(constructorName.span.start + UIntSize(3)) => byte3
                                source -> byte(constructorName.span.start + UIntSize(4)) => byte4
                                source -> byte(constructorName.span.start + UIntSize(5)) => byte5
                                (byte0 == UInt8(82) and byte1 == UInt8(101) and byte2 == UInt8(115) and byte3 == UInt8(117) and byte4 == UInt8(108) and byte5 == UInt8(116)) -> if { 1 => targetSymbol! }
                                (byte0 == UInt8(79) and byte1 == UInt8(112) and byte2 == UInt8(116) and byte3 == UInt8(105) and byte4 == UInt8(111) and byte5 == UInt8(110)) -> if { 0 => targetSymbol! }
                            }
                            constructorName.span.length == UIntSize(4) -> if {
                                source -> byte(constructorName.span.start) => byte0
                                source -> byte(constructorName.span.start + UIntSize(1)) => byte1
                                source -> byte(constructorName.span.start + UIntSize(2)) => byte2
                                source -> byte(constructorName.span.start + UIntSize(3)) => byte3
                                (byte0 == UInt8(84) and byte1 == UInt8(97) and byte2 == UInt8(115) and byte3 == UInt8(107)) -> if { 2 => targetSymbol! }
                            }
                            targetSymbol! < 0 -> if { 2 => status! }
                        }

                        term.firstArgument < 0 -> if { -1 } else { mapped![term.firstArgument] } => firstType
                        term.secondArgument < 0 -> if { -1 } else { mapped![term.secondArgument] } => secondType
                        origin! == 3 => containsParameter!
                        firstType >= 0 -> if {
                            semanticTypes![firstType].containsParameter -> if { true => containsParameter! }
                        }
                        secondType >= 0 -> if {
                            semanticTypes![secondType].containsParameter -> if { true => containsParameter! }
                        }
                        UInt64(0) => lengthHash!
                        term.lengthToken >= 0 -> if {
                            UInt64(1469598103934665603) => lengthHash!
                            tokens![term.lengthToken] => length
                            UIntSize(0) => lengthByte!
                            lengthByte! < length.span.length -> while {
                                source -> byte(length.span.start + lengthByte!) => value
                                lengthHash! * UInt64(1099511628211) + UInt64(value) => lengthHash!
                                lengthByte! + UIntSize(1) => lengthByte!
                            }
                        }
                        -1 => existing!
                        0 => semanticIndex!
                        (semanticIndex! < (semanticTypes! -> len) and existing! < 0) -> while {
                            semanticTypes![semanticIndex!] => known
                            known.origin == origin! => sameOrigin!
                            (term.kind == 1 and ((known.origin == 0 and origin! == 2) or (known.origin == 2 and origin! == 0))) -> if { true => sameOrigin! }
                            (known.kind == term.kind and sameOrigin! and known.module == targetModule! and known.symbol == targetSymbol! and known.first == firstType and known.second == secondType and known.lengthHash == lengthHash! and known.status == status!) -> if {
                                semanticIndex! => existing!
                            }
                            semanticIndex! + 1 => semanticIndex!
                        }
                        existing! < 0 -> if {
                            semanticTypes! -> len => existing!
                            semanticTypes! -> push(SemanticType {
                                kind: term.kind
                                origin: origin!
                                module: targetModule!
                                symbol: targetSymbol!
                                first: firstType
                                second: secondType
                                lengthHash: lengthHash!
                                containsParameter: containsParameter!
                                status: status!
                            })
                        }
                        existing! => mapped![termIndex!]
                        references! -> push(TypeReference {
                            sourceModule: sourceIndex!
                            typeAst: term.astNode
                            typeId: existing!
                            status: status!
                        })
                        completed! + 1 => completed!
                        true => changed!
                    }
                }
                termIndex! + 1 => termIndex!
            }
        }
        sourceIndex! + 1 => sourceIndex!
    }
    SemanticTypeSet { types: semanticTypes!, references: references! } => result!
    result!
}

# Structurally unifies a generic input template with one concrete argument,
# then rebuilds and interns the complete result template in the same arena.
public specialize request: SpecializationRequest -> SpecializationResult {
    request.inputTemplate => inputTemplate
    request.actualInput => actualInput
    request.resultTemplate => resultTemplate
    [SemanticType; ~] => types!
    request.types -> each semanticType {
        types! -> push(semanticType)
    }
    [Int; ~] => templateStack!
    [Int; ~] => actualStack!
    [Int; ~] => parameterModules!
    [Int; ~] => parameterSymbols!
    [Int; ~] => replacements!
    0 => status!
    (inputTemplate < 0 or inputTemplate >= (types! -> len) or actualInput < 0 or actualInput >= (types! -> len) or resultTemplate < 0 or resultTemplate >= (types! -> len)) -> if {
        2 => status!
    } else {
        templateStack! -> push(inputTemplate)
        actualStack! -> push(actualInput)
    }

    0 => workIndex!
    (status! == 0 and workIndex! < (templateStack! -> len)) -> while {
        templateStack![workIndex!] => templateId
        actualStack![workIndex!] => actualId
        types![templateId] => template
        types![actualId] => actual
        (template.kind == 1 and template.origin == 3) -> if {
            -1 => parameterIndex!
            0 => parameterSearch!
            (parameterSearch! < (parameterSymbols! -> len) and parameterIndex! < 0) -> while {
                (parameterModules![parameterSearch!] == template.module and parameterSymbols![parameterSearch!] == template.symbol) -> if {
                    parameterSearch! => parameterIndex!
                }
                parameterSearch! + 1 => parameterSearch!
            }
            parameterIndex! >= 0 -> if {
                replacements![parameterIndex!] != actualId -> if { 2 => status! }
            } else {
                parameterModules! -> push(template.module)
                parameterSymbols! -> push(template.symbol)
                replacements! -> push(actualId)
            }
        } else {
            template.kind != actual.kind -> if { 2 => status! }
            (status! == 0 and template.kind == 1 and templateId != actualId) -> if { 2 => status! }
            (status! == 0 and template.kind == 7 and (template.origin != actual.origin or template.module != actual.module or template.symbol != actual.symbol)) -> if { 2 => status! }
            (status! == 0 and template.kind == 4 and template.lengthHash != actual.lengthHash) -> if { 2 => status! }
            status! == 0 -> if {
                (template.first < 0 and actual.first >= 0) -> if { 2 => status! }
                (template.first >= 0 and actual.first < 0) -> if { 2 => status! }
                (template.second < 0 and actual.second >= 0) -> if { 2 => status! }
                (template.second >= 0 and actual.second < 0) -> if { 2 => status! }
            }
            (status! == 0 and template.first >= 0) -> if {
                templateStack! -> push(template.first)
                actualStack! -> push(actual.first)
            }
            (status! == 0 and template.second >= 0) -> if {
                templateStack! -> push(template.second)
                actualStack! -> push(actual.second)
            }
        }
        workIndex! + 1 => workIndex!
    }

    -1 => specializedRoot!
    status! == 0 -> if {
        types! -> len => originalCount
        [Int; ~] => substituted!
        0 => typeIndex!
        typeIndex! < originalCount -> while {
            types![typeIndex!] => current
            -1 => replacementType!
            (current.kind == 1 and current.origin == 3) -> if {
                0 => replacementSearch!
                (replacementSearch! < (parameterSymbols! -> len) and replacementType! < 0) -> while {
                    (parameterModules![replacementSearch!] == current.module and parameterSymbols![replacementSearch!] == current.symbol) -> if {
                        replacements![replacementSearch!] => replacementType!
                    }
                    replacementSearch! + 1 => replacementSearch!
                }
            }
            replacementType! < 0 -> if {
                current.first < 0 -> if { -1 } else { substituted![current.first] } => first
                current.second < 0 -> if { -1 } else { substituted![current.second] } => second
                (first == current.first and second == current.second) -> if {
                    typeIndex! => replacementType!
                } else {
                    current.origin == 3 => containsParameter!
                    first >= 0 -> if { types![first].containsParameter -> if { true => containsParameter! } }
                    second >= 0 -> if { types![second].containsParameter -> if { true => containsParameter! } }
                    -1 => existing!
                    0 => existingSearch!
                    (existingSearch! < (types! -> len) and existing! < 0) -> while {
                        types![existingSearch!] => known
                        (known.kind == current.kind and known.origin == current.origin and known.module == current.module and known.symbol == current.symbol and known.first == first and known.second == second and known.lengthHash == current.lengthHash and known.status == current.status) -> if {
                            existingSearch! => existing!
                        }
                        existingSearch! + 1 => existingSearch!
                    }
                    existing! < 0 -> if {
                        types! -> len => existing!
                        types! -> push(SemanticType {
                            kind: current.kind
                            origin: current.origin
                            module: current.module
                            symbol: current.symbol
                            first: first
                            second: second
                            lengthHash: current.lengthHash
                            containsParameter: containsParameter!
                            status: current.status
                        })
                    }
                    existing! => replacementType!
                }
            }
            substituted! -> push(replacementType!)
            typeIndex! + 1 => typeIndex!
        }
        substituted![resultTemplate] => specializedRoot!
    }

    SpecializationResult { types: types!, root: specializedRoot!, status: status! } => result!
    result!
}
