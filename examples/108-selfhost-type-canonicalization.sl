import smalllang.compiler.semantic.types as types

main {
    "first values: [ Int ; ~ ] -> [Int;~] { values } main { }" -> types.canonicalize => uses!
    uses! -> each use {
        "type = $(use.canonical),$(use.kind),$(use.elementCanonical)" -> println
    }
}
