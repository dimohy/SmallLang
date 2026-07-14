namespace smalllang.compiler.semantic.module_resolve

import smalllang.compiler.lexer
import smalllang.compiler.semantic.modules as modules

public struct ResolvedImport {
    edge: Int
    sourceModule: Int
    targetModule: Int
    status: Int
}

# Status: 0 resolved, 1 missing target, 2 duplicate target identity,
# 3 duplicate import alias in one source module.
public resolve sources: [Text; ~] -> [ResolvedImport; ~] {
    sources -> modules.identities => identities!
    sources -> modules.imports => imports!
    [ResolvedImport; ~] => resolved!
    identities! -> len => moduleCount
    imports! -> len => importCount
    0 => edgeIndex!
    edgeIndex! < importCount -> while {
        imports![edgeIndex!] => edge
        sources[edge.sourceModule] => edgeSource
        edgeSource -> lexer.lex => edgeTokens!
        edgeTokens![edge.aliasToken] => edgeAlias
        false => duplicateAlias!
        0 => priorEdgeIndex!
        priorEdgeIndex! < edgeIndex! -> while {
            imports![priorEdgeIndex!] => priorEdge
            priorEdge.sourceModule == edge.sourceModule -> if {
                edgeTokens![priorEdge.aliasToken] => priorAlias
                edgeAlias.span.length == priorAlias.span.length => aliasEqual!
                UIntSize(0) => aliasByte!
                (aliasEqual! and aliasByte! < edgeAlias.span.length) -> while {
                    edgeSource -> byte(edgeAlias.span.start + aliasByte!) => edgeByte
                    edgeSource -> byte(priorAlias.span.start + aliasByte!) => priorByte
                    edgeByte != priorByte -> if { false => aliasEqual! }
                    aliasByte! + UIntSize(1) => aliasByte!
                }
                aliasEqual! -> if { true => duplicateAlias! }
            }
            priorEdgeIndex! + 1 => priorEdgeIndex!
        }
        -1 => targetModule!
        0 => matches!
        0 => moduleIndex!
        moduleIndex! < moduleCount -> while {
            identities![moduleIndex!].pathHash == edge.targetHash -> if {
                moduleIndex! => targetModule!
                matches! + 1 => matches!
            }
            moduleIndex! + 1 => moduleIndex!
        }
        1 => status!
        matches! == 1 -> if { 0 => status! }
        matches! > 1 -> if { 2 => status! }
        duplicateAlias! -> if { 3 => status! }
        ResolvedImport {
            edge: edgeIndex!
            sourceModule: edge.sourceModule
            targetModule: targetModule!
            status: status!
        } => result
        resolved! -> push(result)
        edgeIndex! + 1 => edgeIndex!
    }
    resolved!
}
