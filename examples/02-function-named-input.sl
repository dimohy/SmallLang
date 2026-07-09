getName: -> Text {
    "dimohy"
}

square n: Int -> Int {
    n * n
}

main {
    getName() => name
    7 -> square => num
    "Hello, $name. square = $num" -> print
}
