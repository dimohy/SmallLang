title: -> Text {
    "SmallLang WebAssembly"
}

square: Int -> Int {
    it * it
}

main {
    title() => runtimeName
    8 -> square => value
    1..5 -> fold 0 sum, i {
        sum + i
    } => total

    "Hello from $runtimeName" -> println
    "8 squared = $value" -> println
    "1..5 sum = $total" -> println
}
