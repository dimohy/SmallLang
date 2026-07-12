import smalllang.compiler.semantic.types as types

main {
    ["boxed value: box Int -> box Int { value } main { }", "mapped value: {Text: Int} -> {Text:Int} { value } main { }", "fixed<N: Int> value: [Int; N] -> [Int;N] { value } main { }"] -> each source {
        source -> types.canonicalize => uses!
        uses! -> each use {
            "component = $(use.canonical),$(use.kind),$(use.elementCanonical),$(use.keyCanonical),$(use.valueCanonical),$(use.lengthToken)" -> println
        }
    }
}
