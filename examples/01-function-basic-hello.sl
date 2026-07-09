getName: -> Text {
    "dimohy"
}

square: Int -> Int {
    it * it
}

main {
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
