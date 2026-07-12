namespace smalllang.compiler.semantic.module_resolve

import smalllang.compiler.semantic.modules as modules

public struct ResolvedImport {
    edge: Int
    sourceModule: Int
    targetModule: Int
    status: Int
}

# Status: 0 resolved, 1 missing target, 2 duplicate target identity.
public resolve sources: [Text; ~] -> [ResolvedImport; ~] {
    sources -> modules.identities => identities!
    sources -> modules.imports => imports!
    [ResolvedImport; ~] => resolved!
    identities! -> len => moduleCount
    imports! -> len => importCount
    0 => edgeIndex!
    edgeIndex! < importCount -> while {
        imports![edgeIndex!] => edge
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
