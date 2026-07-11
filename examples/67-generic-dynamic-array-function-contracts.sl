readSecond values: [Text; ~] -> Text {
    values[1]
}

addBackend values: mut [Text; ~] -> Unit {
    values -> push("llvm")
}

forwardValues values: move [Text; ~] -> [Text; ~] => values

main {
    ["lexer", "parser", ~] => values!
    values! -> readSecond => second
    values! -> addBackend
    values![2] => third
    values! -> forwardValues => values!
    values! -> len => count
    "array contract second = $second" -> println
    "array contract third = $third" -> println
    "array contract count = $count" -> println
}
